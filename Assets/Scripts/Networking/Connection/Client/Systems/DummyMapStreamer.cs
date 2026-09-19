#nullable enable

using Kern;
using Kern.Core;
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.World;
using Kern.World.Streaming;
using UnityEngine;

namespace MinesServer.Networking.Connection.Client;

internal static class DummyMapStreamer
{
    private static readonly StreamingGovernor _governor =
        new(StreamingPolicy.Default);

    public static bool NeedsStreaming(
        IWorldLayer<CellType>? worldLayer,
        StreamingWindow currentWindow,
        ushort serverX,
        ushort serverY)
    {
        if (worldLayer == null)
        {
            return false;
        }

        StreamingWindow targetWindow = SelectTargetWindow(
            currentWindow,
            serverX,
            serverY,
            worldLayer.WidthChunks * worldLayer.ChunkSize,
            worldLayer.HeightChunks * worldLayer.ChunkSize);
        return targetWindow != currentWindow;
    }

    public static async UniTask<StreamingWindow> SendMapChunksAroundAsync(
        IWorldLayer<CellType>? worldLayer,
        HashSet<int> sentMapChunks,
        StreamingWindow currentWindow,
        ushort serverX,
        ushort serverY,
        Action<ServerPacket> sendPacket,
        CancellationToken cancellationToken = default)
    {
        if (worldLayer == null)
        {
            return currentWindow;
        }

        StreamingWindow targetWindow = SelectTargetWindow(
            currentWindow,
            serverX,
            serverY,
            worldLayer.WidthChunks * worldLayer.ChunkSize,
            worldLayer.HeightChunks * worldLayer.ChunkSize);
        StreamingPlan plan = _governor.Plan(
            currentWindow,
            targetWindow.Origin,
            targetWindow.Size,
            dimensionsChanged: false);
        StreamingWindow window = plan.Target;

        await SendMapWindowAsync(worldLayer, sentMapChunks, window, sendPacket, cancellationToken);
        return window;
    }

    public static async UniTask SendMapWindowAsync(
        IWorldLayer<CellType> worldLayer,
        HashSet<int> sentMapChunks,
        StreamingWindow window,
        Action<ServerPacket> sendPacket,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int minimumChunkX = Math.Max(0, window.Origin.x / worldLayer.ChunkSize);
        int maximumChunkX = Math.Min(
            worldLayer.WidthChunks - 1,
            (window.Origin.x + window.Size.x - 1) / worldLayer.ChunkSize);
        int minimumChunkY = Math.Max(0, window.Origin.y / worldLayer.ChunkSize);
        int maximumChunkY = Math.Min(
            worldLayer.HeightChunks - 1,
            (window.Origin.y + window.Size.y - 1) / worldLayer.ChunkSize);
        var pendingRegions = new List<IHBPacket>();
        var pendingChunkIndices = new List<int>();

        // Start every missing disk read before awaiting any one of them.
        // The old loop paid at least one Update per cold chunk, then another
        // Update per four prepared payloads, with movement awaiting the batch.
        for (int chunkX = minimumChunkX; chunkX <= maximumChunkX; chunkX++)
        {
            for (int chunkY = minimumChunkY; chunkY <= maximumChunkY; chunkY++)
            {
                int chunkIndex = chunkY + chunkX * worldLayer.HeightChunks;
                if (!sentMapChunks.Contains(chunkIndex))
                {
                    worldLayer.ReadChunk(chunkIndex, touchLru: true);
                }
            }
        }

        for (int chunkX = minimumChunkX; chunkX <= maximumChunkX; chunkX++)
        {
            for (int chunkY = minimumChunkY; chunkY <= maximumChunkY; chunkY++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int chunkIndex = chunkY + (chunkX * worldLayer.HeightChunks);
                if (sentMapChunks.Contains(chunkIndex))
                {
                    continue;
                }

                ChunkReadResult<CellType> result = worldLayer.ReadChunk(chunkIndex, touchLru: true);
                while (result.Status == ChunkReadStatus.Loading)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                    result = worldLayer.ReadChunk(chunkIndex, touchLru: true);
                }

                if (result.Status == ChunkReadStatus.Failed)
                {
                    throw new InvalidOperationException($"Failed to load map chunk {chunkIndex}.", result.Error);
                }

                int chunkSize = worldLayer.ChunkSize;
                CellType[] source = result.Status == ChunkReadStatus.Available && result.Data != null
                    ? result.Data
                    : new CellType[chunkSize * chunkSize];
                pendingRegions.Add(
                    new MapRegionPacket(
                        (ushort)(chunkX * chunkSize),
                        (ushort)(chunkY * chunkSize),
                        (byte)(chunkSize - 1),
                        (byte)(chunkSize - 1),
                        CreatePayload(source, chunkSize)));
                pendingChunkIndices.Add(chunkIndex);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (pendingRegions.Count > 0)
        {
            sendPacket(new ServerPacket(new HBPacket(pendingRegions.ToArray())));
            for (int index = 0; index < pendingChunkIndices.Count; index++)
            {
                sentMapChunks.Add(pendingChunkIndices[index]);
            }
        }
    }

    private static StreamingWindow SelectTargetWindow(
        StreamingWindow currentWindow,
        int serverX,
        int serverY,
        int worldWidth,
        int worldHeight)
    {
        StreamingPolicy policy = _governor.Policy;
        int requestedDimension = StreamingPolicy.DefaultMapWindowDimensionCells;
        int windowDimension = policy.QuantizeDimensionWithHeadroom(requestedDimension);
        windowDimension = Math.Min(windowDimension, Math.Min(worldWidth, worldHeight));
        windowDimension = Math.Max(StreamingPolicy.DefaultMinimumWindowDimension, windowDimension);

        Vector2Int player = new(serverX, serverY);
        Vector2Int centeredOrigin = new(
            serverX - (windowDimension / 2),
            serverY - (windowDimension / 2));
        if (!currentWindow.IsValid)
        {
            return new StreamingWindow(
                policy.AlignOrigin(centeredOrigin),
                new Vector2Int(windowDimension, windowDimension));
        }

        Vector2Int targetOrigin = _governor.SelectTargetOrigin(
            currentWindow.Origin,
            centeredOrigin,
            player,
            Vector2Int.one,
            currentWindow.Size,
            dimensionsChanged: false,
            reanchorMarginCells: policy.ResolvePrefetchMarginCells(currentWindow.Size.x));
        return new StreamingWindow(
            targetOrigin,
            new Vector2Int(windowDimension, windowDimension));
    }

    private static CellType[] CreatePayload(CellType[] source, int chunkSize)
    {
        var payload = new CellType[source.Length];
        for (int lx = 0; lx < chunkSize; lx++)
        {
            for (int ly = 0; ly < chunkSize; ly++)
            {
                payload[(ly * chunkSize) + lx] = source[ly + (lx * chunkSize)];
            }
        }

        return payload;
    }

}

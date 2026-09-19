#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern;
using Kern.World.Streaming;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using UnityEngine;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyWorldStreamingCoordinator
{
    private readonly HashSet<int> _sentMapChunks = new();
    private StreamingWindow _residentWindow = new(UnityEngine.Vector2Int.one * int.MinValue, UnityEngine.Vector2Int.zero);
    private CancellationTokenSource? _activeStreamingRequest;
    private CancellationTokenSource? _terrainRequestCancellation;
    private RectInt? _requestedTerrainRegion;

    public void Reset()
    {
        DummyRequestCoordinator.CancelRequest(ref _terrainRequestCancellation);
        _requestedTerrainRegion = null;
        DummyRequestCoordinator.CancelRequest(ref _activeStreamingRequest);
        _sentMapChunks.Clear();
        _residentWindow = new StreamingWindow(UnityEngine.Vector2Int.one * int.MinValue, UnityEngine.Vector2Int.zero);
    }

    public void ResetWindow()
    {
        _sentMapChunks.Clear();
        _residentWindow = new StreamingWindow(UnityEngine.Vector2Int.one * int.MinValue, UnityEngine.Vector2Int.zero);
    }

    public void CancelTerrainRequest()
    {
        DummyRequestCoordinator.CancelRequest(ref _terrainRequestCancellation);
        _requestedTerrainRegion = null;
    }

    public bool ShouldQueueTerrainRegion(string currentWorldCodeName, string targetWorldCodeName, RectInt region)
    {
        return currentWorldCodeName == targetWorldCodeName &&
            region.width > 0 && region.height > 0 &&
            _requestedTerrainRegion != region;
    }

    public CancellationTokenSource BeginTerrainRequest(RectInt region)
    {
        var request = new CancellationTokenSource();
        DummyRequestCoordinator.ReplaceRequest(ref _terrainRequestCancellation, request);
        _requestedTerrainRegion = region;
        return request;
    }

    public void FinishTerrainRequest(CancellationTokenSource request, bool completed)
    {
        if (DummyRequestCoordinator.TryCompleteRequest(ref _terrainRequestCancellation, request) && !completed)
        {
            _requestedTerrainRegion = null;
        }
    }

    public async UniTask SendTerrainRegionAsync(
        LayerLease lease,
        string currentWorldCodeName,
        string worldCodeName,
        RectInt region,
        Action<ServerPacket> sendPacket,
        CancellationToken cancellationToken)
    {
        if (currentWorldCodeName != worldCodeName)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await DummyMapStreamer.SendMapWindowAsync(
            lease.Layer,
            _sentMapChunks,
            new StreamingWindow(region.position, region.size),
            sendPacket,
            cancellationToken);
    }

    public bool NeedsStreaming(LayerLease lease, ushort playerX, ushort playerY) =>
        DummyMapStreamer.NeedsStreaming(lease.Layer, _residentWindow, playerX, playerY);

    public CancellationTokenSource BeginStreamingRequest(CancellationToken parentToken)
    {
        CancellationTokenSource requestCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        DummyRequestCoordinator.ReplaceRequest(ref _activeStreamingRequest, requestCancellation);
        return requestCancellation;
    }

    public void FinishStreamingRequest(CancellationTokenSource requestCancellation)
    {
        DummyRequestCoordinator.TryCompleteRequest(ref _activeStreamingRequest, requestCancellation);
    }

    public async UniTask SendChunksAroundAsync(
        LayerLease lease,
        string currentWorldCodeName,
        string worldCodeName,
        ushort playerX,
        ushort playerY,
        Action<ServerPacket> sendPacket,
        CancellationToken cancellationToken)
    {
        if (currentWorldCodeName != worldCodeName)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        _residentWindow = await DummyMapStreamer.SendMapChunksAroundAsync(
            lease.Layer,
            _sentMapChunks,
            _residentWindow,
            playerX,
            playerY,
            sendPacket,
            cancellationToken);
    }

    public async UniTask<bool> EnsureCellAvailableAsync(
        LayerLease lease,
        ushort serverX,
        ushort serverY,
        Func<bool> isDisposed,
        CancellationToken cancellationToken)
    {
        IWorldLayer<CellType> layer = lease.Layer;
        int chunkIndex = (serverY / layer.ChunkSize) + ((serverX / layer.ChunkSize) * layer.HeightChunks);
        ChunkReadResult<CellType> result = layer.ReadChunk(chunkIndex, touchLru: true);
        while (result.Status == ChunkReadStatus.Loading)
        {
            if (isDisposed())
            {
                return false;
            }

            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            result = layer.ReadChunk(chunkIndex, touchLru: true);
        }

        return result.Status == ChunkReadStatus.Available && !isDisposed();
    }
}

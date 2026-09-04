#nullable enable

using Fodinae;
using Fodinae.Core;
using System;
using System.Collections.Generic;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.World;

namespace MinesServer.Networking.Connection.Client;

internal static class DummyMapStreamer
{
    public static void SendMapChunksAround(IWorldLayer<CellType>? worldLayer, HashSet<int> sentMapChunks, ushort serverX, ushort serverY, Action<ServerPacket> sendPacket)
    {
        const int StreamingRadiusChunks = 4;
        if (worldLayer == null)
        {
            throw new InvalidOperationException(
                "Cannot stream map chunks before the DummyConnection world layer is initialized.");
        }

        int centerChunkX = serverX / ProjectRuntimeContracts.World.ChunkSize;
        int centerChunkY = serverY / ProjectRuntimeContracts.World.ChunkSize;
        int minimumChunkX = Math.Max(0, centerChunkX - StreamingRadiusChunks);
        int maximumChunkX = Math.Min(
            worldLayer.WidthChunks - 1,
            centerChunkX + StreamingRadiusChunks);
        int minimumChunkY = Math.Max(0, centerChunkY - StreamingRadiusChunks);
        int maximumChunkY = Math.Min(
            worldLayer.HeightChunks - 1,
            centerChunkY + StreamingRadiusChunks);
        for (int chunkX = minimumChunkX; chunkX <= maximumChunkX; chunkX++)
        {
            for (int chunkY = minimumChunkY; chunkY <= maximumChunkY; chunkY++)
            {
                int chunkIndex = chunkY + (chunkX * worldLayer.HeightChunks);
                if (sentMapChunks.Contains(chunkIndex))
                {
                    continue;
                }

                CellType[] source = worldLayer.GetOrCreateChunk(chunkIndex, touchLru: true);

                CellType[] payload = CreatePayload(source);
                sendPacket(new ServerPacket(new HBPacket(new IHBPacket[]
                {
                    new MapRegionPacket(
                        (ushort)(chunkX * ProjectRuntimeContracts.World.ChunkSize),
                        (ushort)(chunkY * ProjectRuntimeContracts.World.ChunkSize),
                        ProjectRuntimeContracts.World.ChunkSize - 1,
                        ProjectRuntimeContracts.World.ChunkSize - 1,
                        payload),
                })));

                sentMapChunks.Add(chunkIndex);
            }
        }
    }

    private static CellType[] CreatePayload(CellType[] source)
    {
        var payload = new CellType[source.Length];
        for (int lx = 0; lx < ProjectRuntimeContracts.World.ChunkSize; lx++)
        {
            for (int ly = 0; ly < ProjectRuntimeContracts.World.ChunkSize; ly++)
            {
                payload[(ly * ProjectRuntimeContracts.World.ChunkSize) + lx] =
                    source[ly + (lx * ProjectRuntimeContracts.World.ChunkSize)];
            }
        }

        return payload;
    }
}

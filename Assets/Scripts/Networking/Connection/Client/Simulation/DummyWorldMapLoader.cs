#nullable enable

using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern;
using Kern.Core;
using Kern.Persistence;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;

namespace MinesServer.Networking.Connection.Client;

internal static class DummyWorldMapLoader
{
    internal static async UniTask<(WorldLayer<CellType> Layer, DummyWorldDescriptor Descriptor)> LoadWorldLayerAsync(
        string mapPath,
        IAsyncOperationSupervisor operations,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        (int worldWidth, int worldHeight) =
            await DummyWorldMapArchive.ReadDimensionsWithRetryAsync(mapPath);

        cancellationToken.ThrowIfCancellationRequested();

        if (worldWidth <= 0 || worldHeight <= 0)
        {
            throw new InvalidDataException(
                $"Prebaked map file '{mapPath}' has invalid dimensions ({worldWidth}x{worldHeight}).");
        }

        int widthChunks = (worldWidth + ProjectRuntimeContracts.World.ChunkSize - 1) /
            ProjectRuntimeContracts.World.ChunkSize;
        int heightChunks = (worldHeight + ProjectRuntimeContracts.World.ChunkSize - 1) /
            ProjectRuntimeContracts.World.ChunkSize;

        WorldLayer<CellType>? newLayer = null;
        try
        {
            newLayer = new WorldLayer<CellType>(
                mapPath,
                widthChunks,
                heightChunks,
                operations,
                ProjectRuntimeContracts.World.ChunkSize,
                maxRamChunks: ProjectRuntimeContracts.World.ResidentChunkCacheCapacity);

            cancellationToken.ThrowIfCancellationRequested();

            CellConfigurationPacket[] cellConfigs =
                DummyCellConfigurationUtilities.CreateCellConfigurations();

            var descriptor = new DummyWorldDescriptor(worldWidth, worldHeight, cellConfigs);
            var layer = newLayer;
            newLayer = null;
            return (layer, descriptor);
        }
        finally
        {
            newLayer?.Dispose();
        }
    }
}

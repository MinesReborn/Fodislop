#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using Fodinae;
using Fodinae.Core;
using Fodinae.Persistence;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.World;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyWorldSimulationState(IAsyncOperationSupervisor operations) : IDisposable
{
    private readonly IAsyncOperationSupervisor _operations = operations ??
        throw new ArgumentNullException(nameof(operations));
    private readonly HashSet<int> _sentMapChunks = new();
    private UniTaskCompletionSource? _initializationInFlight;
    private bool _initialized;

    public WorldLayer<CellType>? Layer { get; private set; }

    public CellConfigurationPacket[]? CellConfigurations { get; private set; }

    public bool HasLayer => Layer != null;

    public async UniTask EnsureInitializedAsync(Func<UniTask> initialize)
    {
        if (initialize == null)
        {
            throw new ArgumentNullException(nameof(initialize));
        }

        UniTaskCompletionSource? inFlight = _initializationInFlight;
        if (inFlight != null)
        {
            await inFlight.Task;
            return;
        }

        if (_initialized)
        {
            return;
        }

        var gate = new UniTaskCompletionSource();
        _initializationInFlight = gate;
        try
        {
            await initialize();
            _initialized = true;
            gate.TrySetResult();
        }
        catch (Exception exception)
        {
            _initialized = false;
            gate.TrySetException(exception);
            throw;
        }
        finally
        {
            _initializationInFlight = null;
        }
    }

    public async UniTask<DummyWorldDescriptor> OpenAsync(string worldCodeName)
    {
        CellConfigurations = DummyCellConfigurationUtilities.CreateCellConfigurations();
        DisposeLayer();

        string mapPath = await DummyWorldMapArchive.ResolveMapFileAsync(worldCodeName);
        (int worldWidth, int worldHeight) =
            await DummyWorldMapArchive.ReadDimensionsWithRetryAsync(mapPath);
        if (worldWidth <= 0 || worldHeight <= 0)
        {
            throw new InvalidDataException(
                $"Prebaked map file '{mapPath}' has invalid dimensions ({worldWidth}x{worldHeight}).");
        }

        int widthChunks = (worldWidth + ProjectRuntimeContracts.World.ChunkSize - 1) /
            ProjectRuntimeContracts.World.ChunkSize;
        int heightChunks = (worldHeight + ProjectRuntimeContracts.World.ChunkSize - 1) /
            ProjectRuntimeContracts.World.ChunkSize;
        Layer = new WorldLayer<CellType>(
            mapPath,
            widthChunks,
            heightChunks,
            _operations,
            ProjectRuntimeContracts.World.ChunkSize,
            36);
        _sentMapChunks.Clear();
        return new DummyWorldDescriptor(worldWidth, worldHeight, CellConfigurations);
    }

    public CellType GetCell(ushort serverX, ushort serverY)
    {
        return Layer?.GetCellSync(serverX, serverY) ?? CellType.Unloaded;
    }

    public CellConfigurationPacket? GetCellConfig(CellType type)
    {
        int index = (int)type;
        if (CellConfigurations == null || index < 0 || index >= CellConfigurations.Length)
        {
            return null;
        }

        return CellConfigurations[index];
    }

    public void SetCell(ushort serverX, ushort serverY, CellType type)
    {
        if (Layer != null)
        {
            Layer[serverX, serverY] = type;
        }
    }

    public void SendChunksAround(
        ushort playerX,
        ushort playerY,
        Action<ServerPacket> sendPacket)
    {
        DummyMapStreamer.SendMapChunksAround(
            Layer,
            _sentMapChunks,
            playerX,
            playerY,
            sendPacket);
    }

    public void Reset()
    {
        _initialized = false;
        DisposeLayer();
        CellConfigurations = null;
        _sentMapChunks.Clear();
    }

    public void Dispose()
    {
        Reset();
    }

    private void DisposeLayer()
    {
        Layer?.Dispose();
        Layer = null;
    }
}

internal readonly record struct DummyWorldDescriptor(
    int Width,
    int Height,
    CellConfigurationPacket[] CellConfigurations);

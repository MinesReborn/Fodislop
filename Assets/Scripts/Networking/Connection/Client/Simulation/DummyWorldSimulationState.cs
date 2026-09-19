#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern;
using Kern.Core;
using Kern.Persistence;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;
using Kern.World.Streaming;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyWorldSimulationState(
    IAsyncOperationSupervisor operations,
    IDummyWorldMapSource worldMaps) : IDisposable
{
    private readonly IDummyWorldMapSource _worldMaps = worldMaps ??
        throw new ArgumentNullException(nameof(worldMaps));
    private readonly IAsyncOperationSupervisor _operations = operations ??
        throw new ArgumentNullException(nameof(operations));
    private readonly SemaphoreSlim _streamingGate = new(1, 1);
    private readonly DummyWorldStreamingCoordinator _streaming = new();
    private CancellationTokenSource? _activeOpenCancellation;
    private LayerLease? _layerLease;
    private string? _worldCodeName;
    private UniTaskCompletionSource? _initializationInFlight;
    private bool _initialized;
    private bool _disposed;
    private int _inFlightOperations;
    private int _resourcesDisposed;

    public WorldLayer<CellType>? Layer => _layerLease?.Layer;

    public CellConfigurationPacket[]? CellConfigurations { get; private set; }

    public bool HasLayer => _layerLease != null;

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

    public async UniTask<DummyWorldDescriptor> OpenAsync(
        string worldCodeName,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DummyWorldSimulationState));
        }

        _streaming.CancelTerrainRequest();
        _worldCodeName = null;

        using CancellationTokenSource openCts = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : new CancellationTokenSource();
        DummyRequestCoordinator.ReplaceRequest(ref _activeOpenCancellation, openCts);
        Interlocked.Increment(ref _inFlightOperations);

        try
        {
            using (await AcquireStreamingGateAsync(openCts.Token))
            {
                openCts.Token.ThrowIfCancellationRequested();
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(DummyWorldSimulationState));
                }

                DisposeLayer();

                string mapPath = await _worldMaps.GetMapFileAsync(worldCodeName, openCts.Token);

                openCts.Token.ThrowIfCancellationRequested();
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(DummyWorldSimulationState));
                }

                (WorldLayer<CellType> loadedLayer, DummyWorldDescriptor descriptor) =
                    await DummyWorldMapLoader.LoadWorldLayerAsync(mapPath, _operations, openCts.Token);

                if (_disposed)
                {
                    loadedLayer.Dispose();
                    throw new ObjectDisposedException(nameof(DummyWorldSimulationState));
                }

                CellConfigurations = descriptor.CellConfigurations;
                _layerLease = new LayerLease(loadedLayer);
                _streaming.ResetWindow();
                _worldCodeName = worldCodeName;
                return descriptor;
            }
        }
        finally
        {
            DummyRequestCoordinator.TryCompleteRequest(ref _activeOpenCancellation, openCts);
            OnOperationCompleted();
        }
    }

    public void QueueTerrainRegion(string worldCodeName, RectInt region, Action<ServerPacket> sendPacket)
    {
        if (_disposed || !_streaming.ShouldQueueTerrainRegion(_worldCodeName ?? string.Empty, worldCodeName, region))
        {
            return;
        }

        LayerLease? lease = AcquireLayerLease();
        if (lease == null)
        {
            return;
        }

        CancellationTokenSource request = _streaming.BeginTerrainRequest(region);
        Interlocked.Increment(ref _inFlightOperations);
        try
        {
            _operations.Run(
                "dummy_stream_terrain",
                token => SendTerrainRegionAsync(lease, worldCodeName, region, sendPacket, request, token));
        }
        catch
        {
            lease.Release();
            _streaming.FinishTerrainRequest(request, false);
            request.Dispose();
            OnOperationCompleted();
            throw;
        }
    }

    private async UniTask SendTerrainRegionAsync(
        LayerLease lease,
        string worldCodeName,
        RectInt region,
        Action<ServerPacket> sendPacket,
        CancellationTokenSource request,
        CancellationToken supervisorToken)
    {
        try
        {
            using (request)
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(request.Token, supervisorToken))
            {
                bool completed = false;
                try
                {
                    using (await AcquireStreamingGateAsync(linked.Token))
                    {
                        if (_disposed)
                        {
                            return;
                        }

                        await _streaming.SendTerrainRegionAsync(
                            lease,
                            _worldCodeName ?? string.Empty,
                            worldCodeName,
                            region,
                            sendPacket,
                            linked.Token);
                        completed = true;
                    }
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                    // Replaced viewport or world shutdown; no partial packet was published.
                }
                catch (ObjectDisposedException) when (_disposed || linked.IsCancellationRequested)
                {
                    // Gate was disposed during shutdown or cancellation.
                }
                finally
                {
                    _streaming.FinishTerrainRequest(request, completed);
                }
            }
        }
        finally
        {
            lease.Release();
            OnOperationCompleted();
        }
    }

    public CellType GetCell(ushort serverX, ushort serverY) =>
        TryGetCell(serverX, serverY, out CellType cellType) ? cellType : CellType.Unloaded;

    public bool TryGetCell(ushort serverX, ushort serverY, out CellType cellType)
    {
        WorldLayer<CellType>? layer = Layer;
        if (layer != null && layer.TryGetCell(serverX, serverY, out cellType))
        {
            return true;
        }

        cellType = CellType.Unloaded;
        return false;
    }

    public UniTask<bool> EnsureCellAvailableAsync(
        ushort serverX,
        ushort serverY,
        CancellationToken cancellationToken = default)
    {
        LayerLease? lease = AcquireLayerLease();
        if (lease == null)
        {
            return UniTask.FromResult(false);
        }

        return _streaming.EnsureCellAvailableAsync(lease, serverX, serverY, () => _disposed, cancellationToken);
    }

    public CellConfigurationPacket? GetCellConfig(CellType type)
    {
        int index = (int)type;
        CellConfigurationPacket[]? configs = CellConfigurations;
        if (configs == null || index < 0 || index >= configs.Length)
        {
            return null;
        }

        return configs[index];
    }

    public void SetCell(ushort serverX, ushort serverY, CellType type)
    {
        if (Layer is { } layer)
        {
            layer[serverX, serverY] = type;
        }
    }

    public async UniTask SendChunksAroundAsync(
        ushort playerX,
        ushort playerY,
        Action<ServerPacket> sendPacket,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        string? worldCodeName = _worldCodeName;
        if (worldCodeName == null)
        {
            return;
        }

        LayerLease? lease = AcquireLayerLease();
        if (lease == null)
        {
            return;
        }

        try
        {
            if (_disposed || _worldCodeName != worldCodeName)
            {
                return;
            }

            if (!_streaming.NeedsStreaming(lease, playerX, playerY))
            {
                return;
            }

            using CancellationTokenSource requestCancellation =
                _streaming.BeginStreamingRequest(cancellationToken);
            Interlocked.Increment(ref _inFlightOperations);

            try
            {
                using (await AcquireStreamingGateAsync(requestCancellation.Token))
                {
                    if (_disposed)
                    {
                        return;
                    }

                    await _streaming.SendChunksAroundAsync(
                        lease,
                        _worldCodeName ?? string.Empty,
                        worldCodeName,
                        playerX,
                        playerY,
                        sendPacket,
                        requestCancellation.Token);
                }
            }
            catch (ObjectDisposedException) when (_disposed || requestCancellation.IsCancellationRequested)
            {
                // Gate was disposed during shutdown or cancellation.
            }
            finally
            {
                _streaming.FinishStreamingRequest(requestCancellation);
                OnOperationCompleted();
            }
        }
        finally
        {
            lease.Release();
        }
    }

    public void QueueChunksAround(
        ushort playerX,
        ushort playerY,
        Action<ServerPacket> sendPacket)
    {
        _operations.Run(
            "dummy_stream_chunks",
            cancellationToken => QueuedStreamAsync(playerX, playerY, sendPacket, cancellationToken));
    }

    // Новый запрос подкачки отменяет предыдущий: при частых телепортах это
    // штатно, и отменённая подкачка не должна уходить в лог как ошибка.
    private async UniTask QueuedStreamAsync(
        ushort playerX,
        ushort playerY,
        Action<ServerPacket> sendPacket,
        CancellationToken cancellationToken)
    {
        try
        {
            await SendChunksAroundAsync(playerX, playerY, sendPacket, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
    }

    public void Reset()
    {
        _streaming.Reset();
        _worldCodeName = null;
        DummyRequestCoordinator.CancelRequest(ref _activeOpenCancellation);
        _initialized = false;
        DisposeLayer();
        CellConfigurations = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Reset();
        if (Volatile.Read(ref _inFlightOperations) == 0)
        {
            DisposeResources();
        }
    }

    private void OnOperationCompleted()
    {
        if (Interlocked.Decrement(ref _inFlightOperations) == 0 && _disposed)
        {
            DisposeResources();
        }
    }

    private void DisposeResources()
    {
        if (Interlocked.Exchange(ref _resourcesDisposed, 1) != 0)
        {
            return;
        }

        try
        {
            _streamingGate.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

        DisposeLayer();
    }

    private LayerLease? AcquireLayerLease() =>
        Volatile.Read(ref _layerLease) is { } lease && lease.TryAddRef() ? lease : null;

    private void DisposeLayer() =>
        Interlocked.Exchange(ref _layerLease, null)?.Release();

    private async UniTask<StreamingGateLock> AcquireStreamingGateAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DummyWorldSimulationState));
        }

        cancellationToken.ThrowIfCancellationRequested();
        await _streamingGate.WaitAsync(cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            try
            {
                _streamingGate.Release();
            }
            catch (ObjectDisposedException)
            {
            }

            throw new OperationCanceledException(cancellationToken);
        }

        if (_disposed)
        {
            try
            {
                _streamingGate.Release();
            }
            catch (ObjectDisposedException)
            {
            }

            throw new ObjectDisposedException(nameof(DummyWorldSimulationState));
        }

        return new StreamingGateLock(this);
    }

    private readonly struct StreamingGateLock(DummyWorldSimulationState owner) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                owner._streamingGate.Release();
            }
            catch (ObjectDisposedException)
            {
                // Gate was disposed concurrently during release.
            }
        }
    }
}

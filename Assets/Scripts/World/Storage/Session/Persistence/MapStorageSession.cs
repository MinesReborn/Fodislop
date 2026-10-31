#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Persistence;
using Kern.World;
using Kern.World.Terrain;
using MinesServer.Data;
using UnityEngine;
using VContainer;

namespace Kern.World;

internal sealed class MapStorageSession
{
    private readonly Func<string> _dataRoot;
    private readonly MapStorageRegionBatcher _regionBatcher;
    private readonly string _backupMapSuffix;
    private string DataRoot => _dataRoot();

    internal MapStorageSession(Func<string> dataRoot, MapStorageRegionBatcher regionBatcher, string backupMapSuffix)
    {
        _dataRoot = dataRoot;
        _regionBatcher = regionBatcher;
        _backupMapSuffix = backupMapSuffix;
    }

    internal WorldLayer<CellType>? Layer { get => _cellLayer; set => _cellLayer = value; }
    internal bool Initialized { get => _isInitialized; set => _isInitialized = value; }
    internal string WorldCodeName { get => _worldCodeName; set => _worldCodeName = value; }
    internal int Width { get => _worldWidth; set => _worldWidth = value; }
    internal int Height { get => _worldHeight; set => _worldHeight = value; }
    internal string? MapPath { get => _mapFilePath; set => _mapFilePath = value; }

    internal WorldLayer<CellType>? _cellLayer;
    internal string? _mapFilePath;
    internal readonly MapPersistenceGate _persistenceGate = new();

    internal bool _isInitialized;
    internal bool _disposeAsyncInProgress;
    internal string _worldCodeName = string.Empty;
    internal int _worldWidth;
    internal int _worldHeight;

    public string MapFilePath => _mapFilePath ?? throw new InvalidOperationException("[MapStorage] Map file path is not initialized");

    public string BackupMapFilePath => _isInitialized
        ? Path.Combine(DataRoot, _worldCodeName + _backupMapSuffix)
        : throw new InvalidOperationException("[MapStorage] Map file path is not initialized");

    public long Revision { get; internal set; }

    public bool IsDisposed { get; internal set; }

    public void Flush()
    {
        // A separate overload rather than an optional parameter: an
        // optional parameter does not match IWorldDataStorage.Flush(),
        // which declares none, so the type stops implementing the
        // interface.
        Flush(durable: true);
    }

    /// <param name="durable">
    /// Whether to force the bytes all the way onto the physical drive.
    /// <para>
    /// This is <c>FileStream.Flush(true)</c>, which on macOS issues
    /// F_FULLFSYNC and blocks until the drive acknowledges the write - tens
    /// of milliseconds, routinely. It belongs to quit, pause and low-memory,
    /// where the process is about to stop and the cost is paid once.
    /// </para>
    /// <para>
    /// It must not be on the five-second autosave from MapManager.Update,
    /// which is on the main thread: that turns a periodic save into a
    /// periodic stall, visible as an evenly spaced comb of spikes through
    /// an otherwise flat frame graph. Passing false still flushes the
    /// managed buffers to the OS, so the data survives a process crash -
    /// only an OS crash or power loss can lose it, and the next durable
    /// flush on quit closes that window.
    /// </para>
    /// </param>
    public void Flush(bool durable)
    {
        _persistenceGate.Run(() => FlushCore(durable));
    }

    public async UniTask FlushAsync(
        bool durable,
        CancellationToken cancellationToken = default)
    {
        WorldLayer<CellType>? layer = null;
        List<(int Index, CellType[] Chunk)>? snapshot = null;
        try
        {
            await _persistenceGate.RunAsync(
                () =>
                {
                    if (_cellLayer == null || !_isInitialized || IsDisposed)
                    {
                        return null;
                    }

                    layer = _cellLayer;
                    snapshot = layer.TakeDirtySnapshot();
                    WorldLayer<CellType> writtenLayer = layer;
                    List<(int Index, CellType[] Chunk)> writtenSnapshot = snapshot;
                    string mapFilePath = MapFilePath;
                    string backupMapFilePath = BackupMapFilePath;

                    // В пул уходит только файл. Кэш чанков меняется на главном
                    // потоке каждый кадр, поэтому учёт записанного — ниже,
                    // когда запись уже дождались на главном.
                    return () => MapStorageDiskWriter.WriteSnapshot(
                        writtenLayer,
                        writtenSnapshot,
                        durable,
                        mapFilePath,
                        backupMapFilePath);
                },
                cancellationToken);
        }
        catch
        {
            // Ни чанка не потерять: отметки возвращаются, и следующее
            // сохранение повторит запись.
            if (layer != null && snapshot != null && !IsDisposed)
            {
                layer.RestoreDirty(snapshot);
            }

            throw;
        }

        if (layer != null && snapshot != null && !IsDisposed)
        {
            layer.CompleteDirty(snapshot);
        }
    }

    internal void FlushCore(bool durable)
    {
        if (_cellLayer == null || !_isInitialized || IsDisposed)
        {
            return;
        }

        WorldLayer<CellType> layer = _cellLayer;
        var snapshot = layer.TakeDirtySnapshot();
        try
        {
            MapStorageDiskWriter.WriteSnapshot(layer, snapshot, durable, MapFilePath, BackupMapFilePath);
        }
        catch
        {
            layer.RestoreDirty(snapshot);
            throw;
        }

        layer.CompleteDirty(snapshot);
    }

    public void Dispose()
    {
        _persistenceGate.Run(() =>
        {
            if (_disposeAsyncInProgress)
            {
                throw new InvalidOperationException(
                    "[MapStorage] Synchronous dispose cannot run while asynchronous dispose is in progress.");
            }

            DisposeCore();
        });
    }

    public async UniTask DisposeAsync(CancellationToken cancellationToken = default)
    {
        WorldLayer<CellType>? writtenLayer = null;
        List<(int Index, CellType[] Chunk)>? writtenSnapshot = null;
        try
        {
            await _persistenceGate.RunAsync(
                () =>
                {
                    // Тот же снимок, что во FlushAsync: WorldLayer.Dispose иначе
                    // перебирал бы грязные чанки в пуле потоков. Снимок снимается
                    // на главном потоке, в пул уходит только запись снимка; кэш
                    // очищается после возврата сюда.
                    WorldLayer<CellType>? layer =
                        _isInitialized && !IsDisposed && !_disposeAsyncInProgress ? _cellLayer : null;
                    var snapshot = layer?.TakeDirtySnapshot(includeDetachedSnapshots: true);
                    writtenLayer = layer;
                    writtenSnapshot = snapshot;
                    string? mapFilePath = layer != null ? MapFilePath : null;
                    string? backupMapFilePath = layer != null ? BackupMapFilePath : null;
                    if (layer != null)
                    {
                        _disposeAsyncInProgress = true;
                        _cellLayer = null;
                        _isInitialized = false;
                        IsDisposed = true;
                        Revision++;
                    }

                    return () =>
                    {
                        if (layer != null && snapshot != null)
                        {
                            MapStorageDiskWriter.WriteSnapshot(
                                layer,
                                snapshot,
                                durable: true,
                                mapFilePath: mapFilePath!,
                                backupMapFilePath: backupMapFilePath!);
                        }
                    };
                },
                cancellationToken);
        }
        catch
        {
            if (writtenLayer != null && writtenSnapshot != null)
            {
                writtenLayer.RestoreDirty(writtenSnapshot);
                _cellLayer = writtenLayer;
                _isInitialized = true;
                IsDisposed = false;
                _disposeAsyncInProgress = false;
                Revision++;
            }

            throw;
        }

        if (writtenLayer != null && writtenSnapshot != null)
        {
            writtenLayer.CompleteDirty(writtenSnapshot);
            try
            {
                _regionBatcher.Reset();
                writtenLayer.DisposeAfterDurableSnapshot();
            }
            finally
            {
                _cellLayer = null;
                _isInitialized = false;
                _worldCodeName = string.Empty;
                _worldWidth = 0;
                _worldHeight = 0;
                _mapFilePath = null;
                IsDisposed = true;
                _disposeAsyncInProgress = false;
                Revision++;
            }
        }
    }

    internal void DisposeCore()
    {
        Exception? disposeFailure = null;
        try
        {
            _regionBatcher.Reset();
            _cellLayer?.Dispose();
        }
        catch (Exception ex) when (
            ex is IOException ||
            ex is UnauthorizedAccessException ||
            ex is ObjectDisposedException)
        {
            disposeFailure = ex;
        }
        finally
        {
            _cellLayer = null;
            _isInitialized = false;
            _worldCodeName = string.Empty;
            _worldWidth = 0;
            _worldHeight = 0;
            _mapFilePath = null;
            IsDisposed = true;
            Revision++;
        }

        if (disposeFailure != null)
        {
            throw new IOException(
                "[MapStorage] Failed to close the persistent world map after flushing.",
                disposeFailure);
        }
    }

}

#nullable enable

using System;
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
public class MapStorage : IWorldDataStorage, IWorldPersistence, IRegionBatchStorage
{
    private WorldLayer<CellType>? _cellLayer;
    private string? _mapFilePath;
    private readonly SemaphoreSlim _persistenceGate = new(1, 1);
    private readonly IAsyncOperationSupervisor _operations;

    private const string MapExtension = ".map";
    private const string BackupMapSuffix = ".backup.map";

    private readonly string? _dataRoot;
    private readonly Func<string, Stream> _openMapFile = WorldLayer<CellType>.OpenMapFile;

    [Inject]
    public MapStorage(IAsyncOperationSupervisor operations)
    {
        _operations = operations;
    }

    // Корень данных задаётся явно в тестах установки и обновления; в игре это
    // Application.persistentDataPath.
    internal MapStorage(
        IAsyncOperationSupervisor operations,
        string dataRoot,
        Func<string, Stream>? openMapFile = null)
        : this(operations)
    {
        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            throw new ArgumentException("Data root is required.", nameof(dataRoot));
        }

        _dataRoot = dataRoot;
        _openMapFile = openMapFile ?? WorldLayer<CellType>.OpenMapFile;
    }

    private string _DataRoot => _dataRoot ?? Application.persistentDataPath;

    private bool _isInitialized;
    private string _worldCodeName = string.Empty;
    private int _worldWidth;
    private int _worldHeight;
    private readonly MapStorageRegionBatcher _regionBatcher = new();

    public IWorldLayer<CellType>? CellLayer => _cellLayer;

    public string MapFilePath => _mapFilePath ?? throw new InvalidOperationException("[MapStorage] Map file path is not initialized");

    public string BackupMapFilePath => _isInitialized
        ? Path.Combine(_DataRoot, _worldCodeName + BackupMapSuffix)
        : throw new InvalidOperationException("[MapStorage] Map file path is not initialized");

    public bool IsReady => _isInitialized && _cellLayer != null;
    public bool HasDirtyChunks => _cellLayer?.HasDirtyChunks == true;

    public long Revision { get; private set; }

    public bool IsDisposed { get; private set; }

    public event Action<int, int>? CellChanged;
    public event Action<int, int, int, int>? RegionChanged;

    public void BeginRegionBatch() => _regionBatcher.BeginBatch(_cellLayer);

    public void EndRegionBatch() => _regionBatcher.EndBatch(_cellLayer, RegionChanged);

    public void EnsureEditorInitialized()
    {
#if UNITY_EDITOR
        if (_isInitialized || Application.isPlaying)
        {
            return;
        }

        InitWorld("EditorPreview", 128, 128);
#else
        throw new InvalidOperationException(
            "[MapStorage] EnsureEditorInitialized is available only in the Unity Editor.");
#endif
    }

    public void InitWorld(string worldCodeName, int width, int height)
    {
        Dispose();

        if (string.IsNullOrEmpty(worldCodeName))
        {
            throw new ArgumentException("[MapStorage] World code name cannot be null or empty", nameof(worldCodeName));
        }

        worldCodeName = MapStorageDiskWriter.SanitizeWorldCodeName(worldCodeName);

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException($"[MapStorage] Invalid world dimensions: {width}x{height}");
        }

        _worldCodeName = worldCodeName;
        _worldWidth = width;
        _worldHeight = height;
        int widthChunks = (width + ProjectRuntimeContracts.World.ChunkSize - 1) /
            ProjectRuntimeContracts.World.ChunkSize;
        int heightChunks = (height + ProjectRuntimeContracts.World.ChunkSize - 1) /
            ProjectRuntimeContracts.World.ChunkSize;

        if (widthChunks <= 0 || heightChunks <= 0)
        {
            throw new ArgumentOutOfRangeException($"[MapStorage] Invalid chunk calculation: {widthChunks}x{heightChunks}");
        }

        string path = Path.Combine(_DataRoot, worldCodeName + MapExtension);
        string backupPath = Path.Combine(_DataRoot, worldCodeName + BackupMapSuffix);
        try
        {
            _cellLayer = MapStorageDiskWriter.OpenWorldLayer(
                path,
                widthChunks,
                heightChunks,
                _operations,
                _openMapFile,
                backupPath);
            _mapFilePath = path;
            _isInitialized = true;
            IsDisposed = false;
            Revision++;
        }
        catch
        {
            _cellLayer = null;
            _mapFilePath = null;
            _isInitialized = false;
            throw;
        }
    }

    public bool IsInitialized() => _isInitialized;

    public string GetWorldCodeName() => _worldCodeName;

    public CellType GetCell(int x, int y)
    {
        if (!_isInitialized || _cellLayer == null)
        {
            throw new InvalidOperationException("[MapStorage] GetCell called before world initialization");
        }

        return _cellLayer.GetCell(x, y, touchLru: true);
    }

    public bool TryGetCell(int x, int y, out CellType cellType)
    {
        cellType = CellType.Unloaded;
        return _isInitialized &&
            _cellLayer != null &&
            _cellLayer.TryGetCell(x, y, out cellType);
    }

    public void SetCell(int x, int y, CellType type)
    {
        if (!_isInitialized || _cellLayer == null)
        {
            throw new InvalidOperationException(
                $"[MapStorage] SetCell called before world initialization: ({x},{y}).");
        }

        if (_cellLayer.TryGetCell(x, y, out CellType current) && current == type)
        {
            return;
        }

        _cellLayer[x, y] = type;
        Revision++;
        CellChanged?.Invoke(x, y);
    }

    public void SetRegion(
        int startX,
        int startY,
        int width,
        int height,
        CellType[] cells)
    {
        if (cells == null)
        {
            throw new ArgumentNullException(nameof(cells));
        }

        SetRegion(startX, startY, width, height, cells.AsSpan());
    }

    public void SetRegion(
        int startX,
        int startY,
        int width,
        int height,
        ReadOnlySpan<CellType> cells)
    {
        if (!_isInitialized || _cellLayer == null)
        {
            throw new InvalidOperationException(
                $"[MapStorage] SetRegion called before world initialization: " +
                $"({startX},{startY}) {width}x{height}.");
        }

        _regionBatcher.ValidateRegionParameters(
            startX,
            startY,
            width,
            height,
            cells.Length,
            _worldWidth,
            _worldHeight);

        (int appliedWidth, int appliedHeight) = _regionBatcher.ClipRegionBounds(
            startX,
            startY,
            width,
            height,
            _worldWidth,
            _worldHeight);

        // Bulk write: WorldLayer.SetRegion applies the payload chunk-by-chunk
        // with one LRU touch per chunk instead of per cell (a 32x32 region used
        // to issue ~2048 LRU/Dictionary operations through GetCellSync+SetCell,
        // costing several milliseconds per region and stretching the initial
        // world burst across dozens of frames under the packet-drain budget).
        int changedCells = _cellLayer.SetRegion(
            startX,
            startY,
            width,
            height,
            cells,
            0);

        if (changedCells > 0)
        {
            Revision++;
            bool onlyMaterializedNewChunks =
                _cellLayer.LastSetRegionOnlyMaterializedNewChunks;
            if (_regionBatcher.Depth > 0 && !onlyMaterializedNewChunks)
            {
                _regionBatcher.RecordRegionChange(startX, startY, appliedWidth, appliedHeight);
            }
            else if (!onlyMaterializedNewChunks)
            {
                RegionChanged?.Invoke(startX, startY, width, height);
            }
        }

        // SetRegion emits ChunkLoaded only for chunks that were missing before
        // this packet. Repeated packets stay on the RegionChanged path and do
        // not invalidate terrain and static lighting as if a new chunk arrived.
    }

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
        _persistenceGate.Wait();
        try
        {
            FlushCore(durable);
        }
        finally
        {
            _persistenceGate.Release();
        }
    }

    public async UniTask FlushAsync(
        bool durable,
        CancellationToken cancellationToken = default)
    {
        await AcquireGateOnMainThreadAsync(cancellationToken);
        bool releasedInPool = false;
        try
        {
            if (_cellLayer == null || !_isInitialized || IsDisposed)
            {
                return;
            }

            // Снимок снимается на главном потоке, там же, где меняется кэш.
            // При отказе записи WorldLayer.WriteSnapshot сам возвращает
            // отметки грязных чанков.
            WorldLayer<CellType> layer = _cellLayer;
            var snapshot = layer.TakeDirtySnapshot();
            Exception? failure = null;
            await UniTask.RunOnThreadPool(
                () =>
                {
                    try
                    {
                        MapStorageDiskWriter.WriteSnapshot(layer, snapshot, durable, MapFilePath);
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                    }
                    finally
                    {
                        releasedInPool = true;
                        _persistenceGate.Release();
                    }
                },
                configureAwait: false);

            await UniTask.SwitchToMainThread();
            if (failure != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
        finally
        {
            if (!releasedInPool)
            {
                _persistenceGate.Release();
            }
        }
    }

    // Семафор записи берётся только синхронно на главном потоке, а асинхронная
    // запись отпускает его в пуле потоков, не возвращаясь на главный. Иначе
    // синхронный Flush на выходе из игры блокировал главный поток в ожидании
    // семафора, который держала запись, ждущая этот же главный поток.
    private async UniTask AcquireGateOnMainThreadAsync(CancellationToken cancellationToken)
    {
        await UniTask.SwitchToMainThread(cancellationToken);
        while (!_persistenceGate.Wait(0))
        {
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
        }
    }

    private void FlushCore(bool durable)
    {
        if (_cellLayer == null || !_isInitialized || IsDisposed)
        {
            return;
        }

        WorldLayer<CellType> layer = _cellLayer;
        var snapshot = layer.TakeDirtySnapshot();
        try
        {
            MapStorageDiskWriter.WriteSnapshot(layer, snapshot, durable, MapFilePath);
        }
        catch
        {
            layer.RestoreDirty(snapshot);
            throw;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "SonarAnalyzer.CSharp",
        "S3877",
        Justification = "Persistent map close failures must propagate instead of becoming silent data loss.")]
    public void Dispose()
    {
        _persistenceGate.Wait();
        try
        {
            DisposeCore();
        }
        finally
        {
            _persistenceGate.Release();
        }
    }

    public async UniTask DisposeAsync(CancellationToken cancellationToken = default)
    {
        await AcquireGateOnMainThreadAsync(cancellationToken);
        bool releasedInPool = false;
        try
        {
            // Тот же снимок, что во FlushAsync: WorldLayer.Dispose иначе
            // перебирал бы грязные чанки в пуле потоков. Снимок снимается на
            // главном потоке, в пул уходят только его запись и закрытие файла.
            WorldLayer<CellType>? layer = _isInitialized && !IsDisposed ? _cellLayer : null;
            var snapshot = layer?.TakeDirtySnapshot();
            Exception? failure = null;
            await UniTask.RunOnThreadPool(
                () =>
                {
                    try
                    {
                        if (layer != null && snapshot != null)
                        {
                            MapStorageDiskWriter.WriteSnapshot(layer, snapshot, durable: true, MapFilePath);
                        }

                        DisposeCore();
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                    }
                    finally
                    {
                        releasedInPool = true;
                        _persistenceGate.Release();
                    }
                },
                configureAwait: false);

            await UniTask.SwitchToMainThread();
            if (failure != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
        finally
        {
            if (!releasedInPool)
            {
                _persistenceGate.Release();
            }
        }
    }

    private void DisposeCore()
    {
        Exception? disposeFailure = null;
        try
        {
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

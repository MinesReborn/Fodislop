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
public class MapStorage : IWorldDataStorage, IWorldPersistence, IRegionBatchStorage
{
    private readonly IAsyncOperationSupervisor _operations;
    private readonly MapStorageSession _session;

    private const string MapExtension = ".map";
    private const string BackupMapSuffix = ".backup.map";

    private readonly string? _dataRoot;
    private readonly Func<string, Stream> _openMapFile = WorldLayer<CellType>.OpenMapFile;

    [Inject]
    public MapStorage(IAsyncOperationSupervisor operations)
    {
        _operations = operations;
        _session = new MapStorageSession(() => DataRoot, _regionBatcher, BackupMapSuffix);
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

    private string DataRoot => _dataRoot ?? Application.persistentDataPath;
    private readonly MapStorageRegionBatcher _regionBatcher = new();

    public IWorldLayer<CellType>? CellLayer => _session.Initialized ? _session.Layer : null;

    public static async UniTask PrepareWorldCacheAsync(
        string worldCodeName,
        int widthChunks,
        int heightChunks,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(worldCodeName))
        {
            throw new ArgumentException("World code name is required.", nameof(worldCodeName));
        }

        if (widthChunks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(widthChunks));
        }

        if (heightChunks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(heightChunks));
        }

        string safeWorldCodeName = MapStorageDiskWriter.SanitizeWorldCodeName(worldCodeName);
        string mapPath = Path.Combine(Application.persistentDataPath, safeWorldCodeName + MapExtension);
        string backupPath = Path.Combine(Application.persistentDataPath, safeWorldCodeName + BackupMapSuffix);
        await MapStorageDiskWriter.PrepareWorldLayerFileAsync(
            mapPath,
            widthChunks,
            heightChunks,
            WorldGridDefaults.ChunkSize,
            backupPath,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public static UniTask PrepareExistingWorldCachesAsync(CancellationToken cancellationToken = default)
    {
#if UNITY_ANDROID || UNITY_IOS
        string dataRoot = Application.persistentDataPath;
        return UniTask.RunOnThreadPool(() =>
        {
            if (!Directory.Exists(dataRoot))
            {
                return;
            }

            foreach (string mapPath in Directory.EnumerateFiles(dataRoot, "*" + MapExtension, SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string fileName = Path.GetFileName(mapPath);
                if (fileName.EndsWith(BackupMapSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string backupPath = Path.Combine(dataRoot, Path.GetFileNameWithoutExtension(mapPath) + BackupMapSuffix);
                try
                {
                    int widthChunks;
                    int heightChunks;
                    int chunkSize;
                    int formatVersion;
                    using (var stream = new FileStream(mapPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var reader = new BinaryReader(stream))
                    {
                        widthChunks = reader.ReadInt32();
                        heightChunks = reader.ReadInt32();
                        chunkSize = reader.ReadInt32();
                        formatVersion = reader.ReadInt32();
                    }

                    if (widthChunks <= 0 || heightChunks <= 0 ||
                        chunkSize != WorldGridDefaults.ChunkSize ||
                        formatVersion < 0 || formatVersion > WorldLayerFileHeader.CurrentFormatVersion)
                    {
                        UnityEngine.Debug.LogWarning(
                            $"[MapStorage] Skipping mobile map preflight for '{mapPath}': " +
                            $"header {widthChunks}x{heightChunks}, chunkSize={chunkSize}, version={formatVersion}.");
                        continue;
                    }

                    MapStorageDiskWriter.PrepareWorldLayerFile(
                        mapPath,
                        widthChunks,
                        heightChunks,
                        chunkSize,
                        backupPath);
                }
                catch (Exception exception) when (
                    exception is IOException or InvalidDataException or UnauthorizedAccessException or OverflowException)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[MapStorage] Could not preflight map '{mapPath}'; " +
                        $"its storage error will be reported if that world is opened: {exception.Message}");
                }
            }
        }, cancellationToken: cancellationToken);
#else
        return UniTask.CompletedTask;
#endif
    }

    public bool IsReady => _session.Initialized && _session.Layer != null;
    public bool HasDirtyChunks => _session.Layer?.HasDirtyChunks == true;

    public event Action<int, int>? CellChanged;
    public event Action<int, int, int, int>? RegionChanged;

    public void BeginRegionBatch()
    {
        _regionBatcher.BeginBatch();
        if (_regionBatcher.Depth == 1)
        {
            _session.Layer?.BeginChunkLoadBatch();
        }
    }

    public void EndRegionBatch()
    {
        _regionBatcher.EndBatch();
        if (_regionBatcher.Depth == 0)
        {
            _session.Layer?.EndChunkLoadBatch();
            if (_session.Layer != null && _regionBatcher.TryConsumeDirtyRegion(out int x, out int y, out int w, out int h))
            {
                RegionChanged?.Invoke(x, y, w, h);
            }
        }
    }

    public void EnsureEditorInitialized()
    {
#if UNITY_EDITOR
        if (_session.Initialized || Application.isPlaying)
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

        _session.WorldCodeName = worldCodeName;
        _session.Width = width;
        _session.Height = height;
        int widthChunks = (width + WorldGridDefaults.ChunkSize - 1) /
            WorldGridDefaults.ChunkSize;
        int heightChunks = (height + WorldGridDefaults.ChunkSize - 1) /
            WorldGridDefaults.ChunkSize;

        if (widthChunks <= 0 || heightChunks <= 0)
        {
            throw new ArgumentOutOfRangeException($"[MapStorage] Invalid chunk calculation: {widthChunks}x{heightChunks}");
        }

        string path = Path.Combine(DataRoot, worldCodeName + MapExtension);
        string backupPath = Path.Combine(DataRoot, worldCodeName + BackupMapSuffix);
        Stopwatch openTimer = Stopwatch.StartNew();
        try
        {
            _session.Layer = MapStorageDiskWriter.OpenWorldLayer(
                path,
                widthChunks,
                heightChunks,
                _operations,
                _openMapFile,
                backupPath);
            if (_regionBatcher.Depth > 0)
            {
                _session.Layer.BeginChunkLoadBatch();
            }

            _session.MapPath = path;
            _session.Initialized = true;
            _session.IsDisposed = false;
            _session.Revision++;
            UnityEngine.Debug.Log(
                $"[MapStorage] Opened world '{worldCodeName}' ({width}x{height}; " +
                $"{widthChunks}x{heightChunks} chunks) at '{path}' in {openTimer.ElapsedMilliseconds} ms.");
        }
        catch (Exception exception)
        {
            _session.Layer = null;
            _session.MapPath = null;
            _session.Initialized = false;
            UnityEngine.Debug.LogError(
                $"[MapStorage] Failed to initialize world '{worldCodeName}' " +
                $"({width}x{height}) at '{path}' after {openTimer.ElapsedMilliseconds} ms: {exception}");
            throw;
        }
    }

    public bool IsInitialized() => _session.Initialized;

    public string GetWorldCodeName() => _session.WorldCodeName;

    public CellType GetCell(int x, int y)
    {
        if (!_session.Initialized || _session.Layer == null)
        {
            throw new InvalidOperationException("[MapStorage] GetCell called before world initialization");
        }

        if (!IsInsideWorld(x, y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                $"Cell coordinate ({x}, {y}) is outside the world bounds {_session.Width}x{_session.Height}.");
        }

        return _session.Layer.GetCell(x, y, touchLRU: true);
    }

    public bool TryGetCell(int x, int y, out CellType cellType)
    {
        cellType = CellType.Unloaded;
        return _session.Initialized &&
            _session.Layer != null &&
            IsInsideWorld(x, y) &&
            _session.Layer.TryGetCell(x, y, out cellType);
    }

    public void SetCell(int x, int y, CellType type)
    {
        if (!_session.Initialized || _session.Layer == null)
        {
            throw new InvalidOperationException(
                $"[MapStorage] SetCell called before world initialization: ({x},{y}).");
        }

        if (!IsInsideWorld(x, y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                $"Cell coordinate ({x}, {y}) is outside the world bounds {_session.Width}x{_session.Height}.");
        }

        if (_session.Layer.TryGetCell(x, y, out CellType current) && current == type)
        {
            return;
        }

        _session.Layer[x, y] = type;
        _session.Revision++;
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
        if (!_session.Initialized || _session.Layer == null)
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
            _session.Width,
            _session.Height);

        (int appliedWidth, int appliedHeight) = _regionBatcher.ClipRegionBounds(
            startX,
            startY,
            width,
            height,
            _session.Width,
            _session.Height);

        // Bulk write: WorldLayer.SetRegion applies the payload chunk-by-chunk
        // with one LRU touch per chunk instead of per cell (a 32x32 region used
        // to issue ~2048 LRU/Dictionary operations through GetCellSync+SetCell,
        // costing several milliseconds per region and stretching the initial
        // world burst across dozens of frames under the packet-drain budget).
        ReadOnlySpan<CellType> appliedCells = cells;
        CellType[]? clippedCells = null;
        if (appliedWidth != width || appliedHeight != height)
        {
            clippedCells = new CellType[checked(appliedWidth * appliedHeight)];
            for (int row = 0; row < appliedHeight; row++)
            {
                cells.Slice(row * width, appliedWidth).CopyTo(
                    clippedCells.AsSpan(row * appliedWidth, appliedWidth));
            }

            appliedCells = clippedCells;
        }

        int changedCells = _session.Layer.SetRegion(
            startX,
            startY,
            appliedWidth,
            appliedHeight,
            appliedCells,
            0);

        if (changedCells > 0)
        {
            _session.Revision++;
            bool onlyMaterializedNewChunks =
                _session.Layer.LastSetRegionOnlyMaterializedNewChunks;
            if (_regionBatcher.Depth > 0 && !onlyMaterializedNewChunks)
            {
                _regionBatcher.RecordRegionChange(startX, startY, appliedWidth, appliedHeight);
            }
            else if (!onlyMaterializedNewChunks)
            {
                RegionChanged?.Invoke(startX, startY, appliedWidth, appliedHeight);
            }
        }

        // SetRegion emits ChunkLoaded only for chunks that were missing before
        // this packet. Repeated packets stay on the RegionChanged path and do
        // not invalidate terrain and static lighting as if a new chunk arrived.
    }

    public string MapFilePath => _session.MapFilePath;
    public string BackupMapFilePath => _session.BackupMapFilePath;
    public long Revision => _session.Revision;
    public bool IsDisposed => _session.IsDisposed;
    public void Flush() => _session.Flush();
    public void Flush(bool durable) => _session.Flush(durable);
    public UniTask FlushAsync(bool durable = false, CancellationToken cancellationToken = default) =>
        _session.FlushAsync(durable, cancellationToken);
    public void Dispose() => _session.Dispose();
    public UniTask DisposeAsync(CancellationToken cancellationToken = default) => _session.DisposeAsync(cancellationToken);

    private bool IsInsideWorld(int x, int y) =>
        x >= 0 && y >= 0 && x < _session.Width && y < _session.Height;
}

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kern;
using Kern.Core;
using UnityEngine;

[assembly: InternalsVisibleTo("Kern.Tests.Editor")]
[assembly: InternalsVisibleTo("Kern.Tests.PlayMode")]
[assembly: InternalsVisibleTo("Kern.World")]

namespace Kern.Persistence;
public sealed class WorldLayer<T> : IWorldLayer<T>
    where T : unmanaged
{
    private readonly int _chunkSize;
    private readonly int _chunkArea;
    private readonly int _widthChunks;
    private readonly int _heightChunks;
    private readonly int _maxChunksInMemory;
    private readonly string _filePath;
    private readonly object _ioLock = new object();

    // The Look-Up Table (FAT). Stores file offset for each chunk.
    private readonly long[] _chunkOffsets;

    // --- Memory Cache (LRU) ---
    private readonly ChunkLruCache<T> _cache;
    private readonly HashSet<int> _loadingChunks;
    private readonly Dictionary<int, Exception> _failedChunkLoads = new();
    private readonly object _loadingLock = new object();
    private readonly IAsyncOperationSupervisor _operations;
    private bool _disposed;
    private int _chunkLoadBatchDepth;
    private readonly List<RectInt> _batchedChunkRegions = new(8);

    // A failing disk would otherwise warn once per chunk per streaming
    // pass and flood the console within seconds.
    private const int MAX_LOGGED_CHUNK_DISK_FAILURES = 8;
    private readonly HashSet<int> _loggedChunkLoadFailures = [];
    private bool _chunkDiskFailureCapLogged;

    private Stream? _fileStream;
    private readonly Func<string, Stream> _openFile;

    // One reader for the layer's lifetime, used under _ioLock. A reader per
    // chunk load allocated its buffers and UTF8 decoder on every load.
    private BinaryReader? _reader;

    public WorldLayer(
        string filePath,
        int WIDTH_CHUNKS,
        int HEIGHT_CHUNKS,
        IAsyncOperationSupervisor operations,
        int CHUNK_SIZE = ProjectRuntimeContracts.World.ChunkSize,
        int maxRamChunks = 1000)
        : this(filePath, WIDTH_CHUNKS, HEIGHT_CHUNKS, operations, OpenMapFile, CHUNK_SIZE, maxRamChunks)
    {
    }

    // openFile подменяется в тестах отказов диска; в игре это OpenMapFile.
    internal WorldLayer(
        string filePath,
        int WIDTH_CHUNKS,
        int HEIGHT_CHUNKS,
        IAsyncOperationSupervisor operations,
        Func<string, Stream> openFile,
        int CHUNK_SIZE = ProjectRuntimeContracts.World.ChunkSize,
        int maxRamChunks = 1000)
    {
        _openFile = openFile ?? throw new ArgumentNullException(nameof(openFile));

        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("World layer file path is required.", nameof(filePath));
        }

        if (WIDTH_CHUNKS <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(WIDTH_CHUNKS),
                WIDTH_CHUNKS,
                "World layer width must be positive.");
        }

        if (HEIGHT_CHUNKS <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(HEIGHT_CHUNKS),
                HEIGHT_CHUNKS,
                "World layer height must be positive.");
        }

        _operations = operations ?? throw new ArgumentNullException(nameof(operations));

        if (CHUNK_SIZE <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CHUNK_SIZE),
                CHUNK_SIZE,
                "World layer chunk size must be positive.");
        }

        if (maxRamChunks <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxRamChunks),
                maxRamChunks,
                "World layer RAM cache size must be positive.");
        }

        _filePath = filePath;
        _widthChunks = WIDTH_CHUNKS;
        _heightChunks = HEIGHT_CHUNKS;
        _chunkSize = CHUNK_SIZE;
        _chunkArea = CHUNK_SIZE * CHUNK_SIZE;
        _maxChunksInMemory = maxRamChunks;

        int totalChunks = WIDTH_CHUNKS * HEIGHT_CHUNKS;
        _chunkOffsets = new long[totalChunks];
        Array.Fill(_chunkOffsets, -1);

        _cache = new ChunkLruCache<T>(
            maxRamChunks,
            allowDirtyEviction: false);
        _loadingChunks = new HashSet<int>();

        InitializeFile();
    }

    public int ChunkSize => _chunkSize;

    public int WidthChunks => _widthChunks;

    public int HeightChunks => _heightChunks;

    public int MaxChunksInMemory => _maxChunksInMemory;

    public event Action<int, int, int, int>? ChunkLoaded;

    public bool LastSetRegionOnlyMaterializedNewChunks { get; private set; }

    public void BeginChunkLoadBatch()
    {
        _chunkLoadBatchDepth++;
    }

    public void EndChunkLoadBatch()
    {
        if (_chunkLoadBatchDepth <= 0)
        {
            throw new InvalidOperationException("[WorldLayer] Chunk load batch is not active.");
        }

        _chunkLoadBatchDepth--;
        if (_chunkLoadBatchDepth != 0 || _batchedChunkRegions.Count == 0)
        {
            return;
        }

        foreach (RectInt region in _batchedChunkRegions)
        {
            ChunkLoaded?.Invoke(region.x, region.y, region.width, region.height);
        }

        _batchedChunkRegions.Clear();
    }

    public void NotifyRegionLoaded(int startX, int startY, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                $"Loaded region must be positive: ({startX},{startY}) {width}x{height}.");
        }

        int worldWidth = checked(_widthChunks * _chunkSize);
        int worldHeight = checked(_heightChunks * _chunkSize);
        long endX = (long)startX + width;
        long endY = (long)startY + height;
        if (startX < 0 || startY < 0 || endX > worldWidth || endY > worldHeight)
        {
            string message = $"Loaded region ({startX},{startY}) {width}x{height} is outside " +
                $"the layer of {worldWidth}x{worldHeight} cells.";
            throw new ArgumentOutOfRangeException(
                nameof(startX),
                message);
        }

        int firstChunkX = startX / _chunkSize;
        int firstChunkY = startY / _chunkSize;
        int lastChunkX = ((int)endX - 1) / _chunkSize;
        int lastChunkY = ((int)endY - 1) / _chunkSize;
        for (int chunkX = firstChunkX; chunkX <= lastChunkX; chunkX++)
        {
            for (int chunkY = firstChunkY; chunkY <= lastChunkY; chunkY++)
            {
                ChunkLoaded?.Invoke(
                    chunkX * _chunkSize,
                    chunkY * _chunkSize,
                    _chunkSize,
                    _chunkSize);
            }
        }
    }

    public T this[int x, int y]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => GetCell(x, y, touchLru: true);
        set => SetCell(x, y, value);
    }

    // --- Debug Access ---
    [ExcludeFromCodeCoverage]
    internal long[] GetChunkOffsets() => _chunkOffsets;

    public IEnumerable<int> GetLoadedChunkIndices()
    {
        return _cache.LoadedIndices;
    }

    public int GetLoadedCount()
    {
        return _cache.LoadedCount;
    }

    public int GetDirtyCount()
    {
        return _cache.DirtyCount;
    }

    public bool HasDirtyChunks => _cache.HasDirtyChunks;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetCell(int x, int y, bool touchLru = true)
    {
        if (!GetChunkIndexAndLocal(x, y, out int chunkIndex, out int localIndex))
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                $"Cell coordinate ({x}, {y}) is outside the world layer bounds.");
        }

        T[] chunk = GetOrCreateChunk(chunkIndex, touchLru);

        return chunk[localIndex];
    }

    public T GetCellSync(int x, int y, bool touchLru = true)
    {
        if (!GetChunkIndexAndLocal(x, y, out int chunkIndex, out int localIndex))
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                $"Cell coordinate ({x}, {y}) is outside the world layer bounds.");
        }

        T[] chunk = GetOrCreateChunk(chunkIndex, touchLru);

        return chunk[localIndex];
    }

    public bool TryGetCell(int x, int y, out T value)
    {
        value = default;
        if (!GetChunkIndexAndLocal(x, y, out int chunkIndex, out int localIndex))
        {
            return false;
        }

        if (_cache.TryGet(chunkIndex, out T[]? chunk) && chunk != null)
        {
            value = chunk[localIndex];
            return true;
        }

        return false;
    }

    public void SetCell(int x, int y, T value)
    {
        if (!GetChunkIndexAndLocal(x, y, out int chunkIndex, out int localIndex))
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                $"Cell coordinate ({x}, {y}) is outside the world layer bounds.");
        }

        T[] chunk = GetOrCreateChunk(chunkIndex, touchLru: true);

        if (!EqualityComparer<T>.Default.Equals(chunk[localIndex], value))
        {
            chunk = _cache.PrepareForWrite(chunkIndex, chunk);
            chunk[localIndex] = value;
            MarkDirty(chunkIndex);
        }
    }

    /// <param name="startX">Region origin X in world cells.</param>
    /// <param name="startY">Region origin Y in world cells.</param>
    /// <param name="width">Region width in world cells.</param>
    /// <param name="height">Region height in world cells.</param>
    /// <param name="cells">Payload in row-major order (y outer, x inner).</param>
    /// <param name="cellsOffset">Index of the first payload cell.</param>
    /// <returns>Number of cells that actually changed.</returns>
    public int SetRegion(
        int startX,
        int startY,
        int width,
        int height,
        T[] cells,
        int cellsOffset = 0)
    {
        if (cells == null)
        {
            throw new ArgumentNullException(nameof(cells));
        }

        return SetRegion(startX, startY, width, height, cells.AsSpan(), cellsOffset);
    }

    public int SetRegion(
        int startX,
        int startY,
        int width,
        int height,
        ReadOnlySpan<T> cells,
        int cellsOffset = 0)
    {
        LastSetRegionOnlyMaterializedNewChunks = false;
        int worldWidth = _widthChunks * _chunkSize;
        int worldHeight = _heightChunks * _chunkSize;
        if (startX < 0 || startY < 0 || startX >= worldWidth || startY >= worldHeight)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startX),
                $"Region origin ({startX}, {startY}) is outside the world layer bounds {worldWidth}x{worldHeight}.");
        }

        long requiredCells = (long)cellsOffset + ((long)width * height);
        if (width <= 0 || height <= 0 || cells.Length < requiredCells)
        {
            throw new ArgumentException(
                $"Region payload too small: {cells.Length} cells at offset {cellsOffset}, " +
                $"needs at least {requiredCells} for {width}x{height}.",
                nameof(cells));
        }

        int endX = Math.Min(startX + width, worldWidth);
        int endY = Math.Min(startY + height, worldHeight);
        int firstChunkX = startX / _chunkSize;
        int lastChunkX = (endX - 1) / _chunkSize;
        int firstChunkY = startY / _chunkSize;
        int lastChunkY = (endY - 1) / _chunkSize;

        int changedCount = 0;
        int touchedChunkCount = 0;
        int newChunkCount = 0;
        for (int chunkX = firstChunkX; chunkX <= lastChunkX; chunkX++)
        {
            for (int chunkY = firstChunkY; chunkY <= lastChunkY; chunkY++)
            {
                int chunkIndex = chunkY + (chunkX * _heightChunks);
                bool wasLoaded = _cache.Contains(chunkIndex);
                touchedChunkCount++;
                if (!wasLoaded)
                {
                    newChunkCount++;
                }

                int regionX0 = Math.Max(startX, chunkX * _chunkSize);
                int regionX1 = Math.Min(endX, (chunkX + 1) * _chunkSize);
                int regionY0 = Math.Max(startY, chunkY * _chunkSize);
                int regionY1 = Math.Min(endY, (chunkY + 1) * _chunkSize);
                bool overwritesWholeChunk =
                    regionX0 == chunkX * _chunkSize &&
                    regionX1 == (chunkX + 1) * _chunkSize &&
                    regionY0 == chunkY * _chunkSize &&
                    regionY1 == (chunkY + 1) * _chunkSize;

                // A full network chunk is authoritative for every cell in the
                // chunk. Loading the old RLE payload first only adds a
                // synchronous disk decode to the packet path and can stall
                // the frame exactly when the player crosses a stream boundary.
                // Partial regions still use GetOrCreateChunk so cells outside
                // the packet remain intact.
                T[] chunk;
                if (!wasLoaded && overwritesWholeChunk)
                {
                    chunk = new T[_chunkArea];
                    AddToCache(chunkIndex, chunk);
                }
                else
                {
                    chunk = GetOrCreateChunk(chunkIndex, touchLru: true);
                }

                bool chunkChanged = false;
                for (int x = regionX0; x < regionX1; x++)
                {
                    int localX = x - (chunkX * _chunkSize);
                    for (int y = regionY0; y < regionY1; y++)
                    {
                        int payloadIndex = cellsOffset + ((y - startY) * width) + (x - startX);
                        T value = cells[payloadIndex];
                        int localIndex = (y - (chunkY * _chunkSize)) + (localX * _chunkSize);
                        if ((overwritesWholeChunk && !wasLoaded) ||
                            !EqualityComparer<T>.Default.Equals(chunk[localIndex], value))
                        {
                            if (wasLoaded)
                            {
                                chunk = _cache.PrepareForWrite(chunkIndex, chunk);
                            }

                            chunk[localIndex] = value;
                            chunkChanged = true;
                            if (!overwritesWholeChunk || wasLoaded)
                            {
                                changedCount++;
                            }
                        }
                    }
                }

                if (chunkChanged)
                {
                    MarkDirty(chunkIndex);
                }

                // SetRegion is also the network streaming path. Notify only
                // when this call actually materialized a previously missing
                // chunk; repeated packets for an already loaded chunk are
                // ordinary data changes and are covered by RegionChanged.
                if (!wasLoaded)
                {
                    int loadedMinX = chunkX * _chunkSize;
                    int loadedMinY = chunkY * _chunkSize;
                    int loadedMaxX = loadedMinX + _chunkSize;
                    int loadedMaxY = loadedMinY + _chunkSize;
                    if (_chunkLoadBatchDepth > 0)
                    {
                        _batchedChunkRegions.Add(
                            new RectInt(loadedMinX, loadedMinY, _chunkSize, _chunkSize));
                    }
                    else
                    {
                        ChunkLoaded?.Invoke(loadedMinX, loadedMinY, _chunkSize, _chunkSize);
                    }
                }
            }
        }

        LastSetRegionOnlyMaterializedNewChunks =
            touchedChunkCount > 0 && touchedChunkCount == newChunkCount;
        return changedCount;
    }

    // --- Core Paging Logic ---
    public T[] GetOrCreateChunk(int chunkIndex, bool touchLru = true)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WorldLayer<T>));
        }

        if (chunkIndex < 0 || chunkIndex >= _chunkOffsets.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkIndex), chunkIndex, "Chunk index is outside the world layer.");
        }

        if (_cache.TryGet(chunkIndex, out T[]? chunk) && chunk != null)
        {
            if (touchLru)
            {
                _cache.Touch(chunkIndex);
            }

            return chunk;
        }

        try
        {
            chunk = LoadChunkFromDisk(chunkIndex);
            if (chunk == null)
            {
                // Sparse layers are expected while the server streams
                // regions. A synchronous write materializes the chunk;
                // read-only streaming keeps missing chunks distinct.
                chunk = new T[_chunkArea];
            }

            AddToCache(chunkIndex, chunk);
            return chunk;
        }
        catch (IOException ioEx)
        {
            throw new IOException($"[WorldLayer] Could not load/create chunk {chunkIndex}: {ioEx.Message}", ioEx);
        }
        catch (UnauthorizedAccessException authEx)
        {
            throw new UnauthorizedAccessException($"[WorldLayer] Access denied for chunk {chunkIndex}: {authEx.Message}", authEx);
        }
        catch (OutOfMemoryException)
        {
            throw;
        }
    }

    public ChunkReadResult<T> ReadChunk(int chunkIndex, bool touchLru = true)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WorldLayer<T>));
        }

        if (chunkIndex < 0 || chunkIndex >= _chunkOffsets.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkIndex), chunkIndex, "Chunk index is outside the world layer.");
        }

        if (_cache.TryGet(chunkIndex, out T[]? chunk) && chunk != null)
        {
            if (touchLru)
            {
                _cache.Touch(chunkIndex);
            }

            return new ChunkReadResult<T>(ChunkReadStatus.Available, chunk, null);
        }

        lock (_loadingLock)
        {
            if (_failedChunkLoads.TryGetValue(chunkIndex, out Exception? failure))
            {
                return new ChunkReadResult<T>(ChunkReadStatus.Failed, null, failure);
            }

            if (_chunkOffsets[chunkIndex] < 0)
            {
                return new ChunkReadResult<T>(ChunkReadStatus.Missing, null, null);
            }

            if (!_loadingChunks.Add(chunkIndex))
            {
                return new ChunkReadResult<T>(ChunkReadStatus.Loading, null, null);
            }
        }

        _operations.Run(
            $"load_world_chunk_{chunkIndex}",
            _ => LoadChunkAsync(chunkIndex));
        return new ChunkReadResult<T>(ChunkReadStatus.Loading, null, null);
    }

    public void Flush(bool flushToDisk = false)
    {
        List<(int Index, T[] Chunk)> snapshot = TakeDirtySnapshot();
        try
        {
            WriteSnapshot(snapshot, flushToDisk);
        }
        catch
        {
            RestoreDirty(snapshot);
            throw;
        }
    }

    // Снимок берётся на главном потоке, пока кэш не меняется параллельно.
    // Массивы не клонируются здесь: они отсоединяются от dirty-набора, а при
    // следующей записи в такой чанк PrepareForWrite делает ровно одну копию.
    // Поэтому запись видит стабильное состояние, а изменения после снимка
    // остаются отдельной dirty-версией.
    public List<(int Index, T[] Chunk)> TakeDirtySnapshot()
    {
        return _cache.DetachDirtySnapshot();
    }

    // Запись не удалась — отметки возвращаются, следующее сохранение повторит.
    // Вызывать на главном потоке, как и TakeDirtySnapshot.
    public void RestoreDirty(List<(int Index, T[] Chunk)> snapshot)
    {
        _cache.RestoreDirtySnapshot(EnumerateSnapshotIndices(snapshot));
    }

    // Можно из любого потока: снимок стабилен благодаря copy-on-write, файл —
    // под _ioLock. После успешной записи detached-состояние снимается.
    public void WriteSnapshot(List<(int Index, T[] Chunk)> snapshot, bool flushToDisk)
    {
        try
        {
            foreach ((int index, T[] chunk) in snapshot)
            {
                SaveChunkToDisk(index, chunk);
            }

            lock (_ioLock)
            {
                if (_fileStream == null)
                {
                    _cache.CompleteDirtySnapshot(EnumerateSnapshotIndices(snapshot));
                    return;
                }

                if (flushToDisk && _fileStream is FileStream file)
                {
                    file.Flush(true);
                }
                else
                {
                    _fileStream.Flush();
                }
            }
        }
        catch
        {
            RestoreDirty(snapshot);
            throw;
        }

        _cache.CompleteDirtySnapshot(EnumerateSnapshotIndices(snapshot));
    }

    private static IEnumerable<int> EnumerateSnapshotIndices(
        IEnumerable<(int Index, T[] Chunk)> snapshot)
    {
        foreach ((int index, _) in snapshot)
        {
            yield return index;
        }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool GetChunkIndexAndLocal(int x, int y, out int chunkIndex, out int localIndex)
    {
        if (x < 0 || y < 0 || x >= _widthChunks * _chunkSize || y >= _heightChunks * _chunkSize)
        {
            chunkIndex = -1;
            localIndex = -1;
            return false;
        }

        int cx = x / _chunkSize;
        int cy = y / _chunkSize;
        int lx = x % _chunkSize;
        int ly = y % _chunkSize;

        // Column-major indexing (Original project standard)
        chunkIndex = cy + (cx * _heightChunks);
        localIndex = ly + (lx * _chunkSize);
        return true;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "SonarAnalyzer.CSharp",
        "S3877",
        Justification = "Persistent map close failures must propagate instead of becoming silent data loss.")]
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Exception? disposeFailure = null;
        try
        {
            Flush(flushToDisk: true);
        }
        catch (Exception ex) when (
            ex is IOException ||
            ex is UnauthorizedAccessException ||
            ex is ObjectDisposedException)
        {
            disposeFailure = ex;
        }

        lock (_ioLock)
        {
            _disposed = true;
            try
            {
                _reader?.Dispose();
                _reader = null;
                _fileStream?.Dispose();
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException)
            {
                disposeFailure ??= ex;
            }
        }

        _cache.Clear();
        lock (_loadingLock)
        {
            _loadingChunks.Clear();
            _failedChunkLoads.Clear();
        }

        if (disposeFailure != null)
        {
            throw new IOException(
                $"[WorldLayer] Failed to persist or close map file '{_filePath}'.",
                disposeFailure);
        }
    }

    internal static Stream OpenMapFile(string path) =>
        new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 4096);

    private void InitializeFile()
    {
        _fileStream = _openFile(_filePath);

        bool valid = WorldLayerFileHeader.TryReadHeader(
            _fileStream,
            _widthChunks,
            _heightChunks,
            _chunkSize,
            _chunkOffsets);

        if (!valid)
        {
            if (_fileStream.Length > 0 &&
                WorldLayerFileHeader.TryReadFormatVersion(_fileStream) is int version &&
                version != WorldLayerFileHeader.CurrentFormatVersion)
            {
                // Легаси формат не поддерживается и не мигрируется: старый
                // файл удаляется, карта пересоздаётся и перекачивается с
                // сервера. Данные восстановимы, формат — нет.
                _fileStream.Dispose();
                _fileStream = null;
                File.Delete(_filePath);
                _fileStream = _openFile(_filePath);
            }
            else if (_fileStream.Length > 0)
            {
                // Fail-fast: a damaged map file must never be silently
                // recreated as an empty world. Surface the failure instead.
                _fileStream.Dispose();
                _fileStream = null;
                throw new IOException($"Map file '{_filePath}' is corrupt or its header does not match the expected world dimensions. Refusing to recreate it.");
            }

            WorldLayerFileHeader.WriteHeader(
                _fileStream,
                _widthChunks,
                _heightChunks,
                _chunkSize,
                _chunkOffsets);
        }
    }

    private async Cysharp.Threading.Tasks.UniTask LoadChunkAsync(int chunkIndex)
    {
        T[]? chunk = null;
        Exception? failure = null;
        try
        {
            chunk = await Cysharp.Threading.Tasks.UniTask.RunOnThreadPool(() => LoadChunkFromDisk(chunkIndex));
        }
        catch (IOException ioEx)
        {
            failure = ioEx;
        }
        catch (ObjectDisposedException disposedEx)
        {
            failure = disposedEx;
        }
        catch (UnauthorizedAccessException authEx)
        {
            failure = authEx;
        }
        catch (InvalidDataException invalidDataEx)
        {
            failure = invalidDataEx;
        }
        catch (OutOfMemoryException outOfMemoryEx)
        {
            failure = outOfMemoryEx;
        }

        await Cysharp.Threading.Tasks.UniTask.SwitchToMainThread();

        ClearLoadingChunk(chunkIndex);
        if (_disposed)
        {
            return;
        }

        if (failure != null)
        {
            lock (_loadingLock)
            {
                _failedChunkLoads[chunkIndex] = failure;
            }

            LogChunkDiskFailure(
                _loggedChunkLoadFailures,
                chunkIndex,
                $"[WorldLayer] Failed to load chunk {chunkIndex}: {failure.Message}");
            return;
        }

        // A synchronous request may have filled this slot while the disk
        // read was in flight. Do not overwrite it and, more importantly,
        // do not append a second LRU node for the same chunk.
        if (_cache.Contains(chunkIndex))
        {
            return;
        }

        // A sparse map is expected while the server is streaming regions.
        // Missing data is not an empty chunk: keep it unloaded so consumers
        // can render the explicit unloaded/black state and retry only after
        // an actual region is received.
        if (chunk == null)
        {
            return;
        }

        AddToCache(chunkIndex, chunk);
        int chunkX = chunkIndex / _heightChunks;
        int chunkY = chunkIndex % _heightChunks;
        ChunkLoaded?.Invoke(
            chunkX * _chunkSize,
            chunkY * _chunkSize,
            _chunkSize,
            _chunkSize);
    }

    private void ClearLoadingChunk(int chunkIndex)
    {
        lock (_loadingLock)
        {
            _loadingChunks.Remove(chunkIndex);
        }
    }

    private void AddToCache(int chunkIndex, T[] chunk)
    {
        if (_disposed)
        {
            return;
        }

        _cache.AddOrUpdate(chunkIndex, chunk);
        lock (_loadingLock)
        {
            _failedChunkLoads.Remove(chunkIndex);
        }
    }

    private void LogChunkDiskFailure(HashSet<int> reported, int chunkIndex, string message)
    {
        if (!reported.Add(chunkIndex))
        {
            return;
        }

        Debug.LogWarning(message);
        if (!_chunkDiskFailureCapLogged && reported.Count >= MAX_LOGGED_CHUNK_DISK_FAILURES)
        {
            _chunkDiskFailureCapLogged = true;
            Debug.LogWarning(
                "[WorldLayer] Further per-chunk disk failure warnings are suppressed for this session.");
        }
    }

    private void MarkDirty(int chunkIndex)
    {
        _cache.MarkDirty(chunkIndex);
        lock (_loadingLock)
        {
            _failedChunkLoads.Remove(chunkIndex);
        }
    }

    private T[]? LoadChunkFromDisk(int index)
    {
        if (index < 0 || index >= _chunkOffsets.Length)
        {
            return null;
        }

        lock (_ioLock)
        {
            if (_disposed)
            {
                return null;
            }

            long offset = _chunkOffsets[index];
            if (offset < 0 || _fileStream == null)
            {
                return null;
            }

            _fileStream.Seek(offset, SeekOrigin.Begin);
            _reader ??= new BinaryReader(_fileStream, System.Text.Encoding.UTF8, leaveOpen: true);
            return WorldChunkRleCodec.DecodeChunk<T>(_reader, _chunkArea);
        }
    }

    private void SaveChunkToDisk(int index, T[] chunk)
    {
        if (_fileStream == null)
        {
            throw new ObjectDisposedException(
                nameof(WorldLayer<T>),
                $"World layer '{_filePath}' has no open file stream.");
        }

        lock (_ioLock)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WorldLayer<T>));
            }

            _fileStream.Seek(0, SeekOrigin.End);
            long newOffset = _fileStream.Position;

            using var writer = new BinaryWriter(_fileStream, System.Text.Encoding.UTF8, true);
            WorldChunkRleCodec.EncodeChunk(writer, chunk, _chunkArea);

            _chunkOffsets[index] = newOffset;
            WorldLayerFileHeader.WriteChunkOffset(_fileStream, index, newOffset);
        }
    }

    private void SaveOffsetTable()
    {
        if (_fileStream == null)
        {
            return;
        }

        _fileStream.Seek(WorldLayerFileHeader.HeaderSize, SeekOrigin.Begin);
        var byteSpan = MemoryMarshal.AsBytes(_chunkOffsets.AsSpan());
        _fileStream.Write(byteSpan);
    }
}

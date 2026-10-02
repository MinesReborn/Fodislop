#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern;
using Kern.Core;

[assembly: InternalsVisibleTo("Kern.Tests.Editor")]
[assembly: InternalsVisibleTo("Kern.Tests.PlayMode")]
[assembly: InternalsVisibleTo("Kern.World")]

namespace Kern.Persistence;
public sealed class WorldLayer<T> : IWorldLayer<T>, IStoredChunkSource<T>
    where T : unmanaged
{
    private readonly int _chunkSize;
    private readonly int _widthChunks;
    private readonly int _heightChunks;
    private readonly int _maxChunksInMemory;
    private readonly string _filePath;

    private readonly WorldLayerLifetime _lifetime = new();
    private readonly ChunkLRUCache<T> _cache;
    private readonly WorldLayerFile<T> _file;
    private readonly WorldLayerChunkLoader<T> _loader;
    private readonly WorldLayerRegionWriter<T> _regionWriter;
    private readonly WorldLayerDirtyWriter<T> _dirtyWriter;
    private readonly HashSet<int> _reportedCorruptChunks = new();
    private readonly object _corruptChunkLogLock = new();

    /// <summary>
    /// Converts legacy layer files away from Unity's main thread before opening
    /// them in a scene transition or other latency-sensitive path.
    /// </summary>
    public static async UniTask MigrateLegacyFileAsync(
        string filePath,
        int widthChunks,
        int heightChunks,
        int chunkSize = ProjectRuntimeContracts.World.ChunkSize,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("World layer file path is required.", nameof(filePath));
        }

        if (widthChunks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(widthChunks));
        }

        if (heightChunks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(heightChunks));
        }

        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize));
        }

        cancellationToken.ThrowIfCancellationRequested();
        await UniTask.RunOnThreadPool(
            () => MigrateLegacyFileIfRequired(filePath, widthChunks, heightChunks, chunkSize));
        cancellationToken.ThrowIfCancellationRequested();
    }

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
        if (openFile == null)
        {
            throw new ArgumentNullException(nameof(openFile));
        }

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

        if (operations == null)
        {
            throw new ArgumentNullException(nameof(operations));
        }

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
        _maxChunksInMemory = maxRamChunks;

        int chunkArea = CHUNK_SIZE * CHUNK_SIZE;
        _cache = new ChunkLRUCache<T>(
            maxRamChunks,
            allowDirtyEviction: false);
        _file = new WorldLayerFile<T>(
            filePath, WIDTH_CHUNKS, HEIGHT_CHUNKS, CHUNK_SIZE, openFile, _lifetime);
        _loader = new WorldLayerChunkLoader<T>(
            _cache,
            _file,
            _lifetime,
            operations,
            (minX, minY, width, height) => ChunkLoaded?.Invoke(minX, minY, width, height),
            CHUNK_SIZE,
            chunkArea,
            WIDTH_CHUNKS,
            HEIGHT_CHUNKS);
        _regionWriter = new WorldLayerRegionWriter<T>(
            _cache, _loader, CHUNK_SIZE, chunkArea, HEIGHT_CHUNKS);
        _dirtyWriter = new WorldLayerDirtyWriter<T>(_cache, _file, chunkArea);

        MigrateLegacyFileIfRequired(_filePath, _widthChunks, _heightChunks, _chunkSize);

        _file.Initialize();
    }

    internal static void MigrateLegacyFileIfRequired(
        string filePath,
        int widthChunks,
        int heightChunks,
        int chunkSize)
    {
        WorldLayerFileHeader.MigrateLegacyFormatIfRequired(
            filePath,
            widthChunks,
            heightChunks,
            chunkSize);
        if (!File.Exists(filePath))
        {
            return;
        }

        int? fileVersion;
        using (var versionStream = File.OpenRead(filePath))
        {
            fileVersion = WorldLayerFileHeader.TryReadFormatVersion(versionStream);
        }

        if (fileVersion == WorldLayerFileHeader.LegacyRLEFormatVersion)
        {
            WorldChunkV2Codec.MigrateV1ToV2<T>(filePath, widthChunks, heightChunks, chunkSize);
        }
    }

    public int ChunkSize => _chunkSize;

    public int WidthChunks => _widthChunks;

    public int HeightChunks => _heightChunks;

    public int MaxChunksInMemory => _maxChunksInMemory;

    public event Action<int, int, int, int>? ChunkLoaded;

    public bool LastSetRegionOnlyMaterializedNewChunks { get; private set; }

    public void BeginChunkLoadBatch() => _loader.BeginBatch();

    public void EndChunkLoadBatch() => _loader.EndBatch();

    public void NotifyRegionLoaded(int startX, int startY, int width, int height) =>
        _loader.NotifyRegionLoaded(startX, startY, width, height);

    public T this[int x, int y]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => GetCell(x, y, touchLRU: true);
        set => SetCell(x, y, value);
    }

    // --- Debug Access ---
    [ExcludeFromCodeCoverage]
    internal long[] GetChunkOffsets() => _file.ChunkOffsets;

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
    public T GetCell(int x, int y, bool touchLRU = true)
    {
        if (!GetChunkIndexAndLocal(x, y, out int chunkIndex, out int localIndex))
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                $"Cell coordinate ({x}, {y}) is outside the world layer bounds.");
        }

        T[] chunk = _loader.GetOrCreateChunk(chunkIndex, touchLRU);

        return chunk[localIndex];
    }

    public T GetCellSync(int x, int y, bool touchLRU = true)
    {
        if (!GetChunkIndexAndLocal(x, y, out int chunkIndex, out int localIndex))
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                $"Cell coordinate ({x}, {y}) is outside the world layer bounds.");
        }

        T[] chunk = _loader.GetOrCreateChunk(chunkIndex, touchLRU);

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

        T[] chunk = _loader.GetOrCreateChunk(chunkIndex, touchLRU: true);

        if (!EqualityComparer<T>.Default.Equals(chunk[localIndex], value))
        {
            chunk = _cache.PrepareForWrite(chunkIndex, chunk);
            chunk[localIndex] = value;
            _loader.MarkDirty(chunkIndex);
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

        (int changedCount, bool onlyNewChunks) = _regionWriter.Write(
            startX,
            startY,
            width,
            height,
            cells,
            cellsOffset,
            worldWidth,
            worldHeight);
        LastSetRegionOnlyMaterializedNewChunks = onlyNewChunks;
        return changedCount;
    }

    public T[] GetOrCreateChunk(int chunkIndex, bool touchLRU = true) =>
        _loader.GetOrCreateChunk(chunkIndex, touchLRU);

    public ChunkReadResult<T> ReadChunk(int chunkIndex, bool touchLRU = true) =>
        _loader.ReadChunk(chunkIndex, touchLRU);

    public UniTask VisitStoredChunkRunsAsync(
        Action<int, T, int> runVisitor,
        Action<int, int>? progress,
        CancellationToken cancellationToken = default)
    {
        if (runVisitor == null)
        {
            throw new ArgumentNullException(nameof(runVisitor));
        }

        return UniTask.RunOnThreadPool(
            () =>
            {
                const int batchCapacity = 512;
                var chunkIndices = new int[batchCapacity];
                int nextIndex = 0;

                while (nextIndex < _file.ChunkCount)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int count = _file.CopyStoredChunkIndices(
                        nextIndex,
                        chunkIndices,
                        out int batchEndIndex);

                    for (int i = 0; i < count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int chunkIndex = chunkIndices[i];
                        bool found = _file.VisitChunkRuns(
                            chunkIndex,
                            _chunkSize * _chunkSize,
                            runVisitor,
                            out bool corrupted);
                        if (found && corrupted)
                        {
                            LogCorruptStoredChunk(chunkIndex);
                        }
                    }

                    nextIndex = batchEndIndex;
                    progress?.Invoke(nextIndex, _file.ChunkCount);
                }
            },
            cancellationToken: cancellationToken);
    }

    private void LogCorruptStoredChunk(int chunkIndex)
    {
        lock (_corruptChunkLogLock)
        {
            if (!_reportedCorruptChunks.Add(chunkIndex))
            {
                return;
            }
        }

        UnityEngine.Debug.LogWarning(
            $"[WorldLayer] Chunk {chunkIndex} is corrupt; scanning it as an all-zero chunk.");
    }

    public void Flush(bool flushToDisk = false) => _dirtyWriter.Flush(flushToDisk);

    public List<(int Index, T[] Chunk)> TakeDirtySnapshot() => _dirtyWriter.TakeSnapshot();

    public void RestoreDirty(List<(int Index, T[] Chunk)> snapshot) =>
        _dirtyWriter.RestoreDirty(snapshot);

    /// <summary>Записать снимок в файл. Можно из пула; учёт — <see cref="CompleteDirty"/>.</summary>
    public void WriteSnapshot(List<(int Index, T[] Chunk)> snapshot, bool flushToDisk) =>
        _dirtyWriter.WriteSnapshot(snapshot, flushToDisk);

    /// <summary>Главный поток после успешной записи снимка.</summary>
    public void CompleteDirty(List<(int Index, T[] Chunk)> snapshot) =>
        _dirtyWriter.CompleteSnapshot(snapshot);

    public void CreateDurableBackup(string backupPath) =>
        _file.CreateDurableBackup(backupPath);

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

    public void Dispose()
    {
        if (_lifetime.Disposed)
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

        disposeFailure ??= _file.DisposeStreams();

        _cache.Clear();
        _loader.ClearLoadingState();

        if (disposeFailure != null)
        {
            throw new IOException(
                $"[WorldLayer] Failed to persist or close map file '{_filePath}'.",
                disposeFailure);
        }
    }

    internal static Stream OpenMapFile(string path) =>
        new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 4096);
}

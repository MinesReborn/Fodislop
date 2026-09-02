#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fodinae;
using Fodinae.Core;
using UnityEngine;

namespace Fodinae.Persistence
{
    public sealed class WorldLayer<T> : IWorldLayer<T>
        where T : unmanaged
    {
        private const int HEADER_SIZE = 16; // 4 ints
        private const int FORMAT_VERSION_OFFSET = sizeof(int) * 3;
        private const int CURRENT_FORMAT_VERSION = 1;

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
        private readonly Dictionary<int, T[]> _loadedChunks;
        private readonly Dictionary<int, LinkedListNode<int>> _lruIndexMap;
        private readonly LinkedList<int> _lruList;
        private readonly HashSet<int> _dirtyChunks;
        private readonly HashSet<int> _loadingChunks;
        private readonly Dictionary<int, Exception> _failedChunkLoads = new();
        private readonly object _loadingLock = new object();
        private readonly IAsyncOperationSupervisor _operations;
        private bool _disposed;

        // A failing disk would otherwise warn once per chunk per streaming
        // pass and flood the console within seconds.
        private const int MAX_LOGGED_CHUNK_DISK_FAILURES = 8;
        private readonly HashSet<int> _loggedChunkLoadFailures = [];
        private bool _chunkDiskFailureCapLogged;

        private FileStream? _fileStream;

        public WorldLayer(
            string filePath,
            int WIDTH_CHUNKS,
            int HEIGHT_CHUNKS,
            IAsyncOperationSupervisor operations,
            int CHUNK_SIZE = ProjectRuntimeContracts.World.ChunkSize,
            int maxRamChunks = 1000)
        {
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

            _loadedChunks = new Dictionary<int, T[]>(maxRamChunks);
            _lruIndexMap = new Dictionary<int, LinkedListNode<int>>(maxRamChunks);
            _lruList = new LinkedList<int>();
            _dirtyChunks = new HashSet<int>();
            _loadingChunks = new HashSet<int>();

            MigrateLegacyFormatIfRequired();
            InitializeFile();
        }

        public int ChunkSize => _chunkSize;

        public int WidthChunks => _widthChunks;

        public int HeightChunks => _heightChunks;

        public int MaxChunksInMemory => _maxChunksInMemory;

        public event Action<int, int, int, int>? ChunkLoaded;

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
        public IEnumerable<int> GetLoadedChunkIndices()
        {
            return _loadedChunks.Keys;
        }

        public long[] GetChunkOffsets()
        {
            return _chunkOffsets;
        }

        public int GetLoadedCount()
        {
            return _loadedChunks.Count;
        }

        public int GetDirtyCount()
        {
            return _dirtyChunks.Count;
        }

        public bool HasDirtyChunks => _dirtyChunks.Count > 0;

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
                chunk[localIndex] = value;
                MarkDirty(chunkIndex);
            }
        }

        /// <summary>
        /// Applies a region payload in one pass, chunk by chunk, without touching
        /// the LRU once per cell. This is the hot path for server region streams
        /// (a 32x32 region previously issued ~2048 LRU/Dictionary operations per
        /// region through <see cref="GetCellSync"/> + <see cref="SetCell"/>), which
        /// made every region cost several milliseconds and stretched the initial
        /// world burst across dozens of frames under the packet-drain budget).
        /// </summary>
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
            for (int chunkX = firstChunkX; chunkX <= lastChunkX; chunkX++)
            {
                for (int chunkY = firstChunkY; chunkY <= lastChunkY; chunkY++)
                {
                    int chunkIndex = chunkY + (chunkX * _heightChunks);
                    T[] chunk = GetOrCreateChunk(chunkIndex, touchLru: true);

                    int regionX0 = Math.Max(startX, chunkX * _chunkSize);
                    int regionX1 = Math.Min(endX, (chunkX + 1) * _chunkSize);
                    int regionY0 = Math.Max(startY, chunkY * _chunkSize);
                    int regionY1 = Math.Min(endY, (chunkY + 1) * _chunkSize);

                    bool chunkChanged = false;
                    for (int x = regionX0; x < regionX1; x++)
                    {
                        int localX = x - (chunkX * _chunkSize);
                        for (int y = regionY0; y < regionY1; y++)
                        {
                            int payloadIndex = cellsOffset + ((y - startY) * width) + (x - startX);
                            T value = cells[payloadIndex];
                            int localIndex = (y - (chunkY * _chunkSize)) + (localX * _chunkSize);
                            if (!EqualityComparer<T>.Default.Equals(chunk[localIndex], value))
                            {
                                chunk[localIndex] = value;
                                chunkChanged = true;
                                changedCount++;
                            }
                        }
                    }

                    if (chunkChanged)
                    {
                        MarkDirty(chunkIndex);
                    }
                }
            }

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

            if (_loadedChunks.TryGetValue(chunkIndex, out T[]? chunk))
            {
                if (touchLru)
                {
                    TouchLru(chunkIndex);
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

            if (_loadedChunks.TryGetValue(chunkIndex, out T[]? chunk))
            {
                if (touchLru)
                {
                    TouchLru(chunkIndex);
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
            foreach (int index in _dirtyChunks)
            {
                if (_loadedChunks.TryGetValue(index, out T[]? chunk))
                {
                    SaveChunkToDisk(index, chunk);
                }
            }

            _dirtyChunks.Clear();
            lock (_ioLock)
            {
                if (_fileStream == null)
                {
                    return;
                }

                if (flushToDisk)
                {
                    _fileStream.Flush(true);
                }
                else
                {
                    _fileStream.Flush();
                }
            }
        }

        public void CompactFile()
        {
            string tempPath = _filePath + ".tmp";
            Flush();

            using (var newLayer = new WorldLayer<T>(
                tempPath,
                _widthChunks,
                _heightChunks,
                _operations,
                _chunkSize,
                _maxChunksInMemory))
            {
                for (int i = 0; i < _chunkOffsets.Length; i++)
                {
                    if (_chunkOffsets[i] != -1)
                    {
                        var chunk = LoadChunkFromDisk(i);
                        if (chunk != null && newLayer._fileStream != null)
                        {
                            newLayer._fileStream.Seek(0, SeekOrigin.End);
                            long newOffset = newLayer._fileStream.Position;
                            using var w = new BinaryWriter(newLayer._fileStream, System.Text.Encoding.UTF8, true);
                            newLayer.WriteChunkRLE(w, chunk);
                            newLayer._chunkOffsets[i] = newOffset;
                        }
                    }
                }

                newLayer.SaveOffsetTable();
            }

            _fileStream?.Close();
            File.Replace(tempPath, _filePath, null);
            InitializeFile(); // Re-open
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
                    _fileStream?.Dispose();
                }
                catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException)
                {
                    disposeFailure ??= ex;
                }
            }

            _loadedChunks.Clear();
            _lruIndexMap.Clear();
            _lruList.Clear();
            _dirtyChunks.Clear();
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

        private static void ReadExactly(Stream stream, Span<byte> buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int n = stream.Read(buffer.Slice(total));
                if (n <= 0)
                {
                    throw new EndOfStreamException();
                }

                total += n;
            }
        }

        private static void WriteT(BinaryWriter w, T value)
        {
            Span<T> span = stackalloc T[1];
            span[0] = value;
            w.Write(MemoryMarshal.AsBytes(span));
        }

        private static T ReadT(BinaryReader r)
        {
            int size = Unsafe.SizeOf<T>();
            ReadOnlySpan<byte> bytes = r.ReadBytes(size);
            if (bytes.Length != size)
            {
                throw new EndOfStreamException(
                    $"Expected {size} bytes for a world-layer value, received {bytes.Length}.");
            }

            return MemoryMarshal.Read<T>(bytes);
        }

        private void InitializeFile()
        {
            _fileStream = new FileStream(_filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 4096);

            bool valid = false;
            long offsetTableBytes = (long)_chunkOffsets.Length * sizeof(long);
            if (_fileStream.Length >= HEADER_SIZE)
            {
                try
                {
                    using var reader = new BinaryReader(_fileStream, System.Text.Encoding.UTF8, true);
                    _fileStream.Seek(0, SeekOrigin.Begin);
                    int w = reader.ReadInt32();
                    int h = reader.ReadInt32();
                    int s = reader.ReadInt32();
                    int formatVersion = reader.ReadInt32();

                    if (w == _widthChunks && h == _heightChunks && s == _chunkSize &&
                        formatVersion == CURRENT_FORMAT_VERSION &&
                        _fileStream.Length >= HEADER_SIZE + offsetTableBytes)
                    {
                        var byteSpan = MemoryMarshal.AsBytes(_chunkOffsets.AsSpan());
                        ReadExactly(_fileStream, byteSpan);
                        valid = true;
                    }
                }
                catch (EndOfStreamException)
                {
                    valid = false;
                }
                catch (IOException)
                {
                    valid = false;
                }
            }

            if (!valid)
            {
                if (_fileStream.Length > 0)
                {
                    // Fail-fast: a damaged map file must never be silently
                    // recreated as an empty world. Surface the failure instead.
                    _fileStream.Dispose();
                    _fileStream = null;
                    throw new IOException($"Map file '{_filePath}' is corrupt or its header does not match the expected world dimensions. Refusing to recreate it.");
                }

                Array.Fill(_chunkOffsets, -1);
                _fileStream.SetLength(0);
                _fileStream.Seek(0, SeekOrigin.Begin);
                using var writer = new BinaryWriter(_fileStream, System.Text.Encoding.UTF8, true);
                writer.Write(_widthChunks);
                writer.Write(_heightChunks);
                writer.Write(_chunkSize);
                writer.Write(CURRENT_FORMAT_VERSION);
                var byteSpan = MemoryMarshal.AsBytes(_chunkOffsets.AsSpan());
                _fileStream.Write(byteSpan);
                _fileStream.Flush();
            }
        }

        private void MigrateLegacyFormatIfRequired()
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            string tempPath = _filePath + ".migrate.tmp";
            string backupPath = _filePath + ".v0.backup";
            try
            {
                using (var source = new FileStream(
                    _filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.None))
                {
                    if (source.Length == 0)
                    {
                        return;
                    }

                    if (source.Length < HEADER_SIZE)
                    {
                        return;
                    }

                    using var reader = new BinaryReader(
                        source,
                        System.Text.Encoding.UTF8,
                        leaveOpen: true);
                    int width = reader.ReadInt32();
                    int height = reader.ReadInt32();
                    int chunkSize = reader.ReadInt32();
                    int formatVersion = reader.ReadInt32();
                    if (formatVersion == CURRENT_FORMAT_VERSION)
                    {
                        return;
                    }

                    if (formatVersion != 0)
                    {
                        throw new IOException(
                            $"Map file '{_filePath}' uses unsupported format version {formatVersion}; " +
                            $"this client supports version {CURRENT_FORMAT_VERSION}.");
                    }

                    if (width != _widthChunks || height != _heightChunks || chunkSize != _chunkSize)
                    {
                        return;
                    }

                    source.Seek(0, SeekOrigin.Begin);
                    using var destination = new FileStream(
                        tempPath,
                        FileMode.Create,
                        FileAccess.ReadWrite,
                        FileShare.None);
                    source.CopyTo(destination);
                    destination.Seek(FORMAT_VERSION_OFFSET, SeekOrigin.Begin);
                    using var writer = new BinaryWriter(
                        destination,
                        System.Text.Encoding.UTF8,
                        leaveOpen: true);
                    writer.Write(CURRENT_FORMAT_VERSION);
                    writer.Flush();
                    destination.Flush(true);
                }

                if (!File.Exists(backupPath))
                {
                    File.Copy(_filePath, backupPath);
                }

                File.Replace(tempPath, _filePath, destinationBackupFileName: null);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
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
            if (_loadedChunks.ContainsKey(chunkIndex))
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

            if (_lruIndexMap.TryGetValue(chunkIndex, out var existingNode))
            {
                _lruList.Remove(existingNode);
                _lruIndexMap.Remove(chunkIndex);
                _loadedChunks.Remove(chunkIndex);
            }

            if (_loadedChunks.Count >= _maxChunksInMemory)
            {
                EvictOldestChunk();
            }

            _loadedChunks[chunkIndex] = chunk;
            lock (_loadingLock)
            {
                _failedChunkLoads.Remove(chunkIndex);
            }

            var node = _lruList.AddFirst(chunkIndex);
            _lruIndexMap[chunkIndex] = node;
        }

        private void TouchLru(int chunkIndex)
        {
            lock (_loadingLock)
            {
                if (_lruIndexMap.TryGetValue(chunkIndex, out var node))
                {
                    _lruList.Remove(node);
                    _lruList.AddFirst(node);
                }
            }
        }

        private void EvictOldestChunk()
        {
            if (_lruList.Count == 0 || _lruList.Last == null)
            {
                return;
            }

            int oldestIndex = _lruList.Last.Value;
            if (_dirtyChunks.Contains(oldestIndex) &&
                _loadedChunks.TryGetValue(oldestIndex, out T[]? dirtyChunk))
            {
                // Eviction is a durability boundary: the only in-memory copy
                // must not disappear before its append and offset-table update
                // have completed. A fire-and-forget save allowed Dispose to
                // close the stream while this write was still queued and also
                // cleared the dirty bit before success was known.
                SaveChunkToDisk(oldestIndex, dirtyChunk);
                _dirtyChunks.Remove(oldestIndex);
            }

            _loadedChunks.Remove(oldestIndex);
            _lruIndexMap.Remove(oldestIndex);
            _lruList.RemoveLast();
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
            _dirtyChunks.Add(chunkIndex);
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
                using var reader = new BinaryReader(_fileStream, System.Text.Encoding.UTF8, true);
                return ReadChunkRLE(reader);
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
                WriteChunkRLE(writer, chunk);

                _chunkOffsets[index] = newOffset;

                long tablePos = HEADER_SIZE + (index * sizeof(long));
                _fileStream.Seek(tablePos, SeekOrigin.Begin);
                writer.Write(newOffset);
            }
        }

        private void WriteChunkRLE(BinaryWriter writer, T[] chunk)
        {
            // EqualityComparer<T>.Default, matching the two other equality
            // sites in this class, and not the plain .Equals this loop used to
            // call. T is only constrained to `unmanaged`, so `.Equals(current)`
            // binds to ValueType.Equals(object) and boxes `current` on every
            // single comparison - once per cell, 1024 per chunk, for every
            // dirty chunk of every flush and every LRU eviction. The comparer
            // resolves to a specialized non-boxing implementation instead.
            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            int ptr = 0;
            while (ptr < _chunkArea)
            {
                T current = chunk[ptr];
                ushort count = 1;
                ptr++;
                while (ptr < _chunkArea && count < ushort.MaxValue && comparer.Equals(chunk[ptr], current))
                {
                    count++;
                    ptr++;
                }

                writer.Write(count);
                WriteT(writer, current);
            }
        }

        private T[] ReadChunkRLE(BinaryReader reader)
        {
            T[] chunk = new T[_chunkArea];
            int ptr = 0;
            try
            {
                while (ptr < _chunkArea)
                {
                    ushort count = reader.ReadUInt16();
                    T value = ReadT(reader);
                    if (count == 0)
                    {
                        break;
                    }

                    int fill = Math.Min(count, _chunkArea - ptr);
                    chunk.AsSpan(ptr, fill).Fill(value);
                    ptr += fill;
                    if (fill < count)
                    {
                        break;
                    }
                }
            }
            catch (EndOfStreamException)
            {
                throw new InvalidDataException(
                    $"World layer chunk ended before {_chunkArea} cells were decoded.");
            }

            if (ptr != _chunkArea)
            {
                throw new InvalidDataException(
                    $"World layer chunk contains {ptr} cells; expected {_chunkArea}.");
            }

            return chunk;
        }

        private void SaveOffsetTable()
        {
            if (_fileStream == null)
            {
                return;
            }

            _fileStream.Seek(HEADER_SIZE, SeekOrigin.Begin);
            var byteSpan = MemoryMarshal.AsBytes(_chunkOffsets.AsSpan());
            _fileStream.Write(byteSpan);
        }
    }
}

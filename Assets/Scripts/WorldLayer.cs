using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Fodinae.Scripts
{
    public class WorldLayer<T> : IDisposable
        where T : unmanaged
    {
        private const int HeaderSize = 16; // 4 ints

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

        private FileStream _fileStream;

        public WorldLayer(string filePath, int widthChunks, int heightChunks, int chunkSize = 32, int maxRamChunks = 1000)
        {
            _filePath = filePath;
            _widthChunks = widthChunks;
            _heightChunks = heightChunks;
            _chunkSize = chunkSize;
            _chunkArea = chunkSize * chunkSize;
            _maxChunksInMemory = maxRamChunks;

            int totalChunks = widthChunks * heightChunks;
            _chunkOffsets = new long[totalChunks];
            Array.Fill(_chunkOffsets, -1);

            _loadedChunks = new Dictionary<int, T[]>(maxRamChunks);
            _lruIndexMap = new Dictionary<int, LinkedListNode<int>>(maxRamChunks);
            _lruList = new LinkedList<int>();
            _dirtyChunks = new HashSet<int>();
            _loadingChunks = new HashSet<int>();

            InitializeFile();
        }

        public int ChunkSize => _chunkSize;

        public int WidthChunks => _widthChunks;

        public int HeightChunks => _heightChunks;

        public int MaxChunksInMemory => _maxChunksInMemory;

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T GetCell(int x, int y, bool touchLru = true)
        {
            if (!GetChunkIndexAndLocal(x, y, out int chunkIndex, out int localIndex))
            {
                return default;
            }

            T[] chunk = GetChunk(chunkIndex, createIfMissing: false, touchLru: touchLru);
            return chunk == null ? default : chunk[localIndex];
        }

        public void SetCell(int x, int y, T value)
        {
            if (!GetChunkIndexAndLocal(x, y, out int chunkIndex, out int localIndex))
            {
                return;
            }

            T[] chunk = GetChunk(chunkIndex, createIfMissing: true, touchLru: true);

            if (!chunk[localIndex].Equals(value))
            {
                chunk[localIndex] = value;
                MarkDirty(chunkIndex);
            }
        }

        // --- Core Paging Logic ---
        public T[] GetChunk(int chunkIndex, bool createIfMissing = false, bool touchLru = true)
        {
            if (_loadedChunks.TryGetValue(chunkIndex, out T[] chunk))
            {
                if (touchLru)
                {
                    TouchLru(chunkIndex);
                }

                return chunk;
            }

            if (createIfMissing)
            {
                chunk = LoadChunkFromDisk(chunkIndex);
                if (chunk == null)
                {
                    chunk = new T[_chunkArea];
                }

                AddToCache(chunkIndex, chunk);
                return chunk;
            }
            else
            {
                if (!_loadingChunks.Contains(chunkIndex))
                {
                    _loadingChunks.Add(chunkIndex);
                    LoadChunkAsync(chunkIndex).Forget();
                }

                return null;
            }
        }

        public void Flush()
        {
            foreach (int index in _dirtyChunks)
            {
                SaveChunkToDisk(index, _loadedChunks[index]);
            }

            _dirtyChunks.Clear();
            lock (_ioLock)
            {
                _fileStream.Flush();
            }
        }

        public void CompactFile()
        {
            string tempPath = _filePath + ".tmp";
            Flush();

            using (var newLayer = new WorldLayer<T>(tempPath, _widthChunks, _heightChunks, _chunkSize, _maxChunksInMemory))
            {
                for (int i = 0; i < _chunkOffsets.Length; i++)
                {
                    if (_chunkOffsets[i] != -1)
                    {
                        var chunk = LoadChunkFromDisk(i);
                        if (chunk != null)
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

            _fileStream.Close();
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

        public void Dispose()
        {
            try
            {
                Flush();
            }
            catch (IOException ioEx)
            {
                Debug.LogWarning($"[WorldLayer] I/O error flushing during dispose: {ioEx.Message}");
            }
            catch (ObjectDisposedException)
            {
                // Already disposed, ignore
            }
            catch (UnauthorizedAccessException authEx)
            {
                Debug.LogWarning($"[WorldLayer] Unauthorized access during dispose: {authEx.Message}");
            }

            try
            {
                _fileStream?.Dispose();
            }
            catch (IOException ioEx)
            {
                Debug.LogWarning($"[WorldLayer] I/O error closing stream during dispose: {ioEx.Message}");
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
            return MemoryMarshal.Read<T>(bytes);
        }

        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Handle corrupted stream header errors by overwriting and recreating structure")]
        private void InitializeFile()
        {
            _fileStream = new FileStream(_filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 4096);

            bool valid = false;
            long offsetTableBytes = (long)_chunkOffsets.Length * sizeof(long);
            if (_fileStream.Length >= HeaderSize)
            {
                try
                {
                    using var reader = new BinaryReader(_fileStream, System.Text.Encoding.UTF8, true);
                    _fileStream.Seek(0, SeekOrigin.Begin);
                    int w = reader.ReadInt32();
                    int h = reader.ReadInt32();
                    int s = reader.ReadInt32();
                    reader.ReadInt32(); // Reserved

                    if (w == _widthChunks && h == _heightChunks && s == _chunkSize &&
                        _fileStream.Length >= HeaderSize + offsetTableBytes)
                    {
                        var byteSpan = MemoryMarshal.AsBytes(_chunkOffsets.AsSpan());
                        ReadExactly(_fileStream, byteSpan);
                        valid = true;
                    }
                }
                catch
                {
                    valid = false;
                }
            }

            if (!valid)
            {
                Array.Fill(_chunkOffsets, -1);
                _fileStream.SetLength(0);
                _fileStream.Seek(0, SeekOrigin.Begin);
                using var writer = new BinaryWriter(_fileStream, System.Text.Encoding.UTF8, true);
                writer.Write(_widthChunks);
                writer.Write(_heightChunks);
                writer.Write(_chunkSize);
                writer.Write(0);
                var byteSpan = MemoryMarshal.AsBytes(_chunkOffsets.AsSpan());
                _fileStream.Write(byteSpan);
                _fileStream.Flush();
            }
        }

        private async Cysharp.Threading.Tasks.UniTaskVoid LoadChunkAsync(int chunkIndex)
        {
            T[] chunk = null;
            try
            {
                chunk = await Cysharp.Threading.Tasks.UniTask.RunOnThreadPool(() => LoadChunkFromDisk(chunkIndex));
            }
            catch (IOException ioEx)
            {
                Debug.LogError($"[WorldLayer] Disk I/O error loading chunk {chunkIndex}: {ioEx.Message}");
            }
            catch (ObjectDisposedException disposedEx)
            {
                Debug.LogWarning($"[WorldLayer] Stream disposed while loading chunk {chunkIndex}: {disposedEx.Message}");
            }

            await Cysharp.Threading.Tasks.UniTask.SwitchToMainThread();

            if (chunk == null)
            {
                chunk = new T[_chunkArea];
            }

            AddToCache(chunkIndex, chunk);
            _loadingChunks.Remove(chunkIndex);
        }

        private void AddToCache(int chunkIndex, T[] chunk)
        {
            if (_loadedChunks.Count >= _maxChunksInMemory)
            {
                EvictOldestChunk();
            }

            _loadedChunks[chunkIndex] = chunk;
            var node = _lruList.AddFirst(chunkIndex);
            _lruIndexMap[chunkIndex] = node;
        }

        private void TouchLru(int chunkIndex)
        {
            if (_lruIndexMap.TryGetValue(chunkIndex, out var node))
            {
                _lruList.Remove(node);
                _lruList.AddFirst(node);
            }
        }

        private void EvictOldestChunk()
        {
            if (_lruList.Count == 0)
            {
                return;
            }

            int oldestIndex = _lruList.Last.Value;
            if (_dirtyChunks.Contains(oldestIndex))
            {
                SaveChunkToDisk(oldestIndex, _loadedChunks[oldestIndex]);
                _dirtyChunks.Remove(oldestIndex);
            }

            _loadedChunks.Remove(oldestIndex);
            _lruIndexMap.Remove(oldestIndex);
            _lruList.RemoveLast();
        }

        private void MarkDirty(int chunkIndex)
        {
            _dirtyChunks.Add(chunkIndex);
        }

        private T[] LoadChunkFromDisk(int index)
        {
            long offset = _chunkOffsets[index];
            if (offset < 0)
            {
                return null;
            }

            lock (_ioLock)
            {
                _fileStream.Seek(offset, SeekOrigin.Begin);
                using var reader = new BinaryReader(_fileStream, System.Text.Encoding.UTF8, true);
                return ReadChunkRLE(reader);
            }
        }

        private void SaveChunkToDisk(int index, T[] chunk)
        {
            lock (_ioLock)
            {
                _fileStream.Seek(0, SeekOrigin.End);
                long newOffset = _fileStream.Position;

                using var writer = new BinaryWriter(_fileStream, System.Text.Encoding.UTF8, true);
                WriteChunkRLE(writer, chunk);

                _chunkOffsets[index] = newOffset;

                long tablePos = HeaderSize + (index * sizeof(long));
                _fileStream.Seek(tablePos, SeekOrigin.Begin);
                writer.Write(newOffset);
            }
        }

        private void WriteChunkRLE(BinaryWriter writer, T[] chunk)
        {
            int ptr = 0;
            while (ptr < _chunkArea)
            {
                T current = chunk[ptr];
                ushort count = 1;
                ptr++;
                while (ptr < _chunkArea && count < ushort.MaxValue && chunk[ptr].Equals(current))
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
                // Ignore end of stream
            }

            return chunk;
        }

        private void SaveOffsetTable()
        {
            _fileStream.Seek(HeaderSize, SeekOrigin.Begin);
            var byteSpan = MemoryMarshal.AsBytes(_chunkOffsets.AsSpan());
            _fileStream.Write(byteSpan);
        }
    }
}

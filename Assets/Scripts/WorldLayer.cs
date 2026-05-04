using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public class WorldLayer<T> : IDisposable
    where T : unmanaged
{
    private static bool _initialized = false;

    // --- Config ---
    private const int HEADER_SIZE = 16; // 4 ints
    private readonly int _chunkSize;
    private readonly int _chunkArea;
    private readonly int _widthChunks;
    public readonly int _heightChunks;
    private readonly int _maxChunksInMemory;

    // Public properties for access
    public int ChunkSize => _chunkSize;
    public int WidthChunks => _widthChunks;
    public int HeightChunks => _heightChunks;
    public int MaxChunksInMemory => _maxChunksInMemory;

    // --- State ---
    private readonly string _filePath;
    private FileStream _fileStream;

    // The Look-Up Table (FAT). Stores file offset for each chunk. 
    // -1 = Chunk doesn't exist (empty/void).
    // Size: ~28MB for a 60k x 60k map (acceptable for mobile).
    private readonly long[] _chunkOffsets;

    // --- Memory Cache (LRU) ---
    // Maps ChunkIndex -> Actual Data
    private readonly Dictionary<int, T[]> _loadedChunks;
    // Maps ChunkIndex -> LinkedListNode (for O(1) LRU updates)
    private readonly Dictionary<int, LinkedListNode<int>> _lruIndexMap;
    private readonly LinkedList<int> _lruList;
    private readonly HashSet<int> _dirtyChunks;

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

        if (!_initialized)
        {
            InitializeFile();
            _initialized = true;
        }
    }

    private void InitializeFile()
    {
        bool exists = File.Exists(_filePath);
        _fileStream = new FileStream(_filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 4096);
        if (exists)
        {
            using var reader = new BinaryReader(_fileStream, System.Text.Encoding.UTF8, true);
            if (_fileStream.Length >= HEADER_SIZE)
            {
                _fileStream.Seek(0, SeekOrigin.Begin);
                int w = reader.ReadInt32();
                int h = reader.ReadInt32();
                int s = reader.ReadInt32();
                int r = reader.ReadInt32();
                var bytesToRead = _chunkOffsets.Length * sizeof(long);
                var byteSpan = MemoryMarshal.AsBytes(_chunkOffsets.AsSpan());
                _fileStream.Read(byteSpan);
            }
        }
        else
        {
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

    // --- Accessors ---

    public T this[int x, int y]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (!GetChunkIndexAndLocal(x, y, out int chunkIndex, out int localIndex))
                return default;

            T[] chunk = GetChunk(chunkIndex);
            return chunk == null ? default : chunk[localIndex];
        }
        set
        {
            if (!GetChunkIndexAndLocal(x, y, out int chunkIndex, out int localIndex))
                return;

            T[] chunk = GetChunk(chunkIndex, createIfMissing: true);

            if (!chunk[localIndex].Equals(value))
            {
                chunk[localIndex] = value;
                MarkDirty(chunkIndex);
            }
        }
    }

    // --- Core Paging Logic ---

    public T[] GetChunk(int chunkIndex, bool createIfMissing = false)
    {
        if (_loadedChunks.TryGetValue(chunkIndex, out T[] chunk))
        {
            TouchLru(chunkIndex);
            return chunk;
        }
        chunk = LoadChunkFromDisk(chunkIndex);
        if (chunk == null && createIfMissing)
        {
            chunk = new T[_chunkArea];
        }
        if (chunk != null)
        {
            AddToCache(chunkIndex, chunk);
        }
        return chunk;
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
        if (_lruList.Count == 0) return;
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

    // --- Disk I/O (RLE + Append Only) ---

    private T[] LoadChunkFromDisk(int index)
    {
        long offset = _chunkOffsets[index];
        if (offset < 0) return null;
        _fileStream.Seek(offset, SeekOrigin.Begin);
        using var reader = new BinaryReader(_fileStream, System.Text.Encoding.UTF8, true);

        return ReadChunkRLE(reader);
    }

    private void SaveChunkToDisk(int index, T[] chunk)
    {
        // APPEND strategy: Always write to the end of the file.
        // This prevents overwriting other chunks if RLE size increases.
        // It creates "dead space" in the file, but ensures safety.
        // We update the _chunkOffsets table to point to the new location.

        _fileStream.Seek(0, SeekOrigin.End);
        long newOffset = _fileStream.Position;

        using var writer = new BinaryWriter(_fileStream, System.Text.Encoding.UTF8, true);
        WriteChunkRLE(writer, chunk);

        // Update Offset Table in RAM
        _chunkOffsets[index] = newOffset;

        // Update Offset Table on Disk (So if we crash, we know where the new chunk is)
        long tablePos = HEADER_SIZE + (index * sizeof(long));
        _fileStream.Seek(tablePos, SeekOrigin.Begin);
        writer.Write(newOffset);
    }

    public void Flush()
    {
        // Force save all dirty chunks currently in RAM
        foreach (int index in _dirtyChunks)
        {
            SaveChunkToDisk(index, _loadedChunks[index]);
        }
        _dirtyChunks.Clear();
        _fileStream.Flush();
    }

    // --- Compression (Same RLE logic, optimized) ---

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
                chunk.AsSpan(ptr, count).Fill(value);
                ptr += count;
            }
        }
        catch (EndOfStreamException) { /* Handle corruption gracefully */ }
        return chunk;
    }

    private void WriteT(BinaryWriter w, T value)
    {
        Span<T> span = stackalloc T[1];
        span[0] = value;
        w.Write(MemoryMarshal.AsBytes(span));
    }

    private T ReadT(BinaryReader r)
    {
        int size = Unsafe.SizeOf<T>();
        ReadOnlySpan<byte> bytes = r.ReadBytes(size);
        return MemoryMarshal.Read<T>(bytes);
    }

    // --- Maintenance ---

    /// <summary>
    /// Because we use Append-Only writes, the file grows indefinitely with edits.
    /// Call this occasionally (e.g., loading screen) to rewrite the file cleanly.
    /// </summary>
    public void CompactFile()
    {
        string tempPath = _filePath + ".tmp";
        // Flush memory first
        Flush();

        // Create new clean file
        using (var newLayer = new WorldLayer<T>(tempPath, _widthChunks, _heightChunks, _chunkSize, _maxChunksInMemory))
        {
            // Copy every chunk from current file to new file
            for (int i = 0; i < _chunkOffsets.Length; i++)
            {
                if (_chunkOffsets[i] != -1)
                {
                    // This reads from 'this' file and writes to 'newLayer' file (which will append compactly)
                    var chunk = LoadChunkFromDisk(i);
                    if (chunk != null)
                    {
                        // Manually inject into new file logic
                        newLayer._fileStream.Seek(0, SeekOrigin.End);
                        long newOffset = newLayer._fileStream.Position;
                        using var w = new BinaryWriter(newLayer._fileStream, System.Text.Encoding.UTF8, true);
                        newLayer.WriteChunkRLE(w, chunk);

                        // Update RAM table of new file
                        newLayer._chunkOffsets[i] = newOffset;
                    }
                }
            }
            // Save the populated offset table in the new file
            newLayer.SaveOffsetTable();
        }

        // Swap files
        _fileStream.Close();
        File.Replace(tempPath, _filePath, null);
        InitializeFile(); // Re-open
    }

    private void SaveOffsetTable()
    {
        _fileStream.Seek(HEADER_SIZE, SeekOrigin.Begin);
        var byteSpan = MemoryMarshal.AsBytes(_chunkOffsets.AsSpan());
        _fileStream.Write(byteSpan);
    }

    // --- Helpers ---

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

        // Given Y increases downwards, cy=0 is the top chunk row.
        // ChunkIndex = cy (row) + cx (col) * _heightChunks (total chunks in a column).
        chunkIndex = cy + cx * _heightChunks;
        localIndex = ly + lx * _chunkSize;
        return true;
    }

    public void Dispose()
    {
        Flush();
        _fileStream?.Dispose();
    }
}
#nullable enable

namespace Kern.Persistence;

using System;
using System.Collections.Generic;

/// <typeparam name="T">Unmanaged cell value type.</typeparam>
public sealed class ChunkLruCache<T>
    where T : unmanaged
{
    private readonly int _maxCapacity;
    private readonly Action<int, T[]>? _onEvictDirty;
    private readonly bool _allowDirtyEviction;
    private readonly Dictionary<int, T[]> _loadedChunks;
    private readonly Dictionary<int, LinkedListNode<int>> _lruIndexMap;
    private readonly LinkedList<int> _lruList;
    private readonly HashSet<int> _dirtyChunks;
    private readonly HashSet<int> _detachedDirtyChunks;

    public ChunkLruCache(
        int maxCapacity,
        Action<int, T[]>? onEvictDirty = null,
        bool allowDirtyEviction = true)
    {
        if (maxCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxCapacity),
                maxCapacity,
                "Cache capacity must be positive.");
        }

        _maxCapacity = maxCapacity;
        _onEvictDirty = onEvictDirty;
        _allowDirtyEviction = allowDirtyEviction;
        _loadedChunks = new Dictionary<int, T[]>(maxCapacity);
        _lruIndexMap = new Dictionary<int, LinkedListNode<int>>(maxCapacity);
        _lruList = new LinkedList<int>();
        _dirtyChunks = new HashSet<int>();
        _detachedDirtyChunks = new HashSet<int>();
    }

    /// <summary>
    /// Target resident capacity. A cache configured to preserve dirty chunks
    /// may temporarily exceed it until the dirty set is flushed.
    /// </summary>
    public int Capacity => _maxCapacity;

    public int LoadedCount => _loadedChunks.Count;

    public int DirtyCount => _dirtyChunks.Count;

    public bool HasDirtyChunks => _dirtyChunks.Count > 0;

    public IEnumerable<int> LoadedIndices => _loadedChunks.Keys;

    public bool Contains(int chunkIndex) => _loadedChunks.ContainsKey(chunkIndex);

    public bool IsDirty(int chunkIndex) => _dirtyChunks.Contains(chunkIndex);

    public bool TryGet(int chunkIndex, out T[]? chunk)
    {
        return _loadedChunks.TryGetValue(chunkIndex, out chunk);
    }

    public void Touch(int chunkIndex)
    {
        if (_lruIndexMap.TryGetValue(chunkIndex, out var node))
        {
            _lruList.Remove(node);
            _lruList.AddFirst(node);
        }
    }

    public void AddOrUpdate(int chunkIndex, T[] chunk)
    {
        if (chunk == null)
        {
            throw new ArgumentNullException(nameof(chunk));
        }

        if (_lruIndexMap.TryGetValue(chunkIndex, out var existingNode))
        {
            _lruList.Remove(existingNode);
            _lruIndexMap.Remove(chunkIndex);
            _loadedChunks.Remove(chunkIndex);
        }

        // Пока чанки грязные, вытеснять нечего, и кэш растёт выше ёмкости.
        // Вытеснение одного чанка на вставку держало бы его на этом пике
        // навсегда, поэтому вытесняется всё, что уже можно. Здесь, а не при
        // завершении записи: та идёт в пуле потоков, а кэш меняется на главном.
        TrimTo(_maxCapacity - 1);

        _loadedChunks[chunkIndex] = chunk;
        var node = _lruList.AddFirst(chunkIndex);
        _lruIndexMap[chunkIndex] = node;
    }

    public void MarkDirty(int chunkIndex)
    {
        _dirtyChunks.Add(chunkIndex);
    }

    public void ClearDirty()
    {
        _dirtyChunks.Clear();
    }

    /// <summary>
    /// Detaches the current dirty arrays from the mutable dirty set. A later
    /// write to a detached chunk must go through <see cref="PrepareForWrite"/>
    /// so the writer keeps a stable array without cloning every chunk here.
    /// </summary>
    public List<(int Index, T[] Chunk)> DetachDirtySnapshot()
    {
        var snapshot = new List<(int Index, T[] Chunk)>(_dirtyChunks.Count);
        foreach (int index in _dirtyChunks)
        {
            if (_loadedChunks.TryGetValue(index, out T[]? chunk) && chunk != null)
            {
                snapshot.Add((index, chunk));
                _detachedDirtyChunks.Add(index);
            }
        }

        _dirtyChunks.Clear();
        return snapshot;
    }

    public T[] PrepareForWrite(int chunkIndex, T[] chunk)
    {
        if (!_detachedDirtyChunks.Remove(chunkIndex))
        {
            return chunk;
        }

        T[] writableChunk = (T[])chunk.Clone();
        _loadedChunks[chunkIndex] = writableChunk;
        return writableChunk;
    }

    public void CompleteDirtySnapshot(IEnumerable<int> indices)
    {
        foreach (int index in indices)
        {
            _detachedDirtyChunks.Remove(index);
        }
    }

    public void RestoreDirtySnapshot(IEnumerable<int> indices)
    {
        foreach (int index in indices)
        {
            _detachedDirtyChunks.Remove(index);
            _dirtyChunks.Add(index);
        }
    }

    public void Clear()
    {
        _loadedChunks.Clear();
        _lruIndexMap.Clear();
        _lruList.Clear();
        _dirtyChunks.Clear();
        _detachedDirtyChunks.Clear();
    }

    private void TrimTo(int count)
    {
        while (_loadedChunks.Count > count && EvictOldest())
        {
        }
    }

    private bool EvictOldest()
    {
        LinkedListNode<int>? evictionNode = FindEvictionNode();
        if (evictionNode == null)
        {
            return false;
        }

        int oldestIndex = evictionNode.Value;
        if (_dirtyChunks.Contains(oldestIndex) &&
            _loadedChunks.TryGetValue(oldestIndex, out T[]? dirtyChunk))
        {
            _onEvictDirty?.Invoke(oldestIndex, dirtyChunk);
            _dirtyChunks.Remove(oldestIndex);
        }

        _loadedChunks.Remove(oldestIndex);
        _lruIndexMap.Remove(oldestIndex);
        _lruList.Remove(evictionNode);
        return true;
    }

    private LinkedListNode<int>? FindEvictionNode()
    {
        LinkedListNode<int>? node = _lruList.Last;
        if (_allowDirtyEviction)
        {
            return node;
        }

        while (node != null &&
               (_dirtyChunks.Contains(node.Value) ||
                _detachedDirtyChunks.Contains(node.Value)))
        {
            node = node.Previous;
        }

        return node;
    }
}

#nullable enable

namespace Kern.Persistence;

using System;
using System.Collections.Generic;

/// <typeparam name="T">Unmanaged cell value type.</typeparam>
public sealed class ChunkLRUCache<T>
    where T : unmanaged
{
    private readonly int _maxCapacity;
    private readonly Action<int, T[]>? _onEvictDirty;
    private readonly bool _allowDirtyEviction;
    private readonly Dictionary<int, T[]> _loadedChunks;
    private readonly Dictionary<int, LinkedListNode<int>> _lruIndexMap;
    private readonly LinkedList<int> _lruList;
    private readonly HashSet<int> _dirtyChunks;

    // Отданные в запись чанки: индекс → тот самый массив, что ушёл в снимок.
    // Завершение записи снимает отметку, только если массив тот же: иначе
    // запись старого снимка сняла бы защиту copy-on-write с более нового,
    // который уже отдан в следующую запись.
    private readonly Dictionary<int, T[]> _detachedDirtyChunks;

    // Загруженные чанки, которые МОЖНО вытеснить: ни грязные, ни отданные в
    // запись. Держится отдельным множеством, а не выводится обходом, потому
    // что обход — ровно то, что здесь стоило кадра (см. FindEvictionNode).
    private readonly HashSet<int> _evictableChunks;

    public ChunkLRUCache(
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
        _detachedDirtyChunks = new Dictionary<int, T[]>();
        _evictableChunks = new HashSet<int>();
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
            _evictableChunks.Remove(chunkIndex);
        }

        // Пока чанки грязные, вытеснять нечего, и кэш растёт выше ёмкости.
        // Вытеснение одного чанка на вставку держало бы его на этом пике
        // навсегда, поэтому вытесняется всё, что уже можно. Здесь, а не при
        // завершении записи: та идёт в пуле потоков, а кэш меняется на главном.
        TrimTo(_maxCapacity - 1);

        _loadedChunks[chunkIndex] = chunk;
        var node = _lruList.AddFirst(chunkIndex);
        _lruIndexMap[chunkIndex] = node;
        RefreshEvictable(chunkIndex);
    }

    public void MarkDirty(int chunkIndex)
    {
        _dirtyChunks.Add(chunkIndex);
        _evictableChunks.Remove(chunkIndex);
    }

    public void ClearDirty()
    {
        foreach (int index in _dirtyChunks)
        {
            if (!_detachedDirtyChunks.ContainsKey(index) && _loadedChunks.ContainsKey(index))
            {
                _evictableChunks.Add(index);
            }
        }

        _dirtyChunks.Clear();
    }

    private static readonly List<(int Index, T[] Chunk)> s_emptySnapshot = new();

    /// <summary>
    /// Detaches the current dirty arrays from the mutable dirty set. A later
    /// write to a detached chunk must go through <see cref="PrepareForWrite"/>
    /// so the writer keeps a stable array without cloning every chunk here.
    /// </summary>
    public List<(int Index, T[] Chunk)> DetachDirtySnapshot()
    {
        if (_dirtyChunks.Count == 0)
        {
            return s_emptySnapshot;
        }

        var snapshot = new List<(int Index, T[] Chunk)>(_dirtyChunks.Count);
        foreach (int index in _dirtyChunks)
        {
            if (_loadedChunks.TryGetValue(index, out T[]? chunk) && chunk != null)
            {
                snapshot.Add((index, chunk));
                _detachedDirtyChunks[index] = chunk;
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
        RefreshEvictable(chunkIndex);
        return writableChunk;
    }

    /// <summary>
    /// Снимок записан. Главный поток: кэш не защищён от параллельного доступа,
    /// а запись идёт в пуле, поэтому учёт делает тот, кто её дождался.
    /// </summary>
    public void CompleteDirtySnapshot(List<(int Index, T[] Chunk)> snapshot)
    {
        foreach ((int index, T[] chunk) in snapshot)
        {
            ReleaseDetached(index, chunk);
            RefreshEvictable(index);
        }
    }

    /// <summary>
    /// Запись не удалась: отметки возвращаются, следующее сохранение повторит.
    /// Если чанк успели переписать, он и так грязный новой версией.
    /// </summary>
    public void RestoreDirtySnapshot(List<(int Index, T[] Chunk)> snapshot)
    {
        foreach ((int index, T[] chunk) in snapshot)
        {
            ReleaseDetached(index, chunk);
            _dirtyChunks.Add(index);
            _evictableChunks.Remove(index);
        }
    }

    public void Clear()
    {
        _loadedChunks.Clear();
        _lruIndexMap.Clear();
        _lruList.Clear();
        _dirtyChunks.Clear();
        _detachedDirtyChunks.Clear();
        _evictableChunks.Clear();
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
        _evictableChunks.Remove(oldestIndex);
        _lruList.Remove(evictionNode);
        return true;
    }

    // Кандидат на вытеснение, идя от хвоста списка.
    //
    // ПОЧЕМУ ЗДЕСЬ СТОИТ БЫСТРЫЙ ВЫХОД. Кэш мира настроен беречь грязные
    // чанки, а всё, что приехало с сервера, грязное до ближайшей записи на
    // диск. Пока стример льёт чанки, чистых в кэше нет вовсе — и этот обход
    // каждый раз проходил ВЕСЬ список, чтобы не найти ничего. Список при этом
    // растёт выше ёмкости (вытеснять-то нечего), так что цена обхода росла
    // вместе с ним: чем дольше идёшь по миру, тем дороже приход каждого чанка.
    // Счётчик чистых чанков отвечает на тот же вопрос за O(1).
    private LinkedListNode<int>? FindEvictionNode()
    {
        if (_allowDirtyEviction)
        {
            return _lruList.Last;
        }

        if (_evictableChunks.Count == 0)
        {
            return null;
        }

        LinkedListNode<int>? node = _lruList.Last;
        while (node != null && !_evictableChunks.Contains(node.Value))
        {
            node = node.Previous;
        }

        return node;
    }

    private void ReleaseDetached(int chunkIndex, T[] chunk)
    {
        if (_detachedDirtyChunks.TryGetValue(chunkIndex, out T[]? detached) &&
            ReferenceEquals(detached, chunk))
        {
            _detachedDirtyChunks.Remove(chunkIndex);
        }
    }

    private void RefreshEvictable(int chunkIndex)
    {
        if (_dirtyChunks.Contains(chunkIndex) || _detachedDirtyChunks.ContainsKey(chunkIndex))
        {
            _evictableChunks.Remove(chunkIndex);
            return;
        }

        _evictableChunks.Add(chunkIndex);
    }
}

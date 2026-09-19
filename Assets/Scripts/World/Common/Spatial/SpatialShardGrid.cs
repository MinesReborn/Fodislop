#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Kern.Core;
using UnityEngine;

namespace Kern.World;

/// <summary>
/// High-performance 2D spatial sharding grid with zero runtime GC allocations in steady state.
/// Partitions world entities into cell shards for fast O(K) viewport culling and spatial queries.
/// </summary>
/// <typeparam name="T">The indexed entity or handle type.</typeparam>
public sealed class SpatialShardGrid<T> : ISpatialIndex<T> where T : class
{
    private readonly int _shardSize;
    private readonly Dictionary<long, List<T>> _shards = new();
    private readonly Dictionary<T, ItemRecord> _items = new();
    private readonly Stack<List<T>> _listPool = new();

    private readonly struct ItemRecord(long shardKey, Vector2 position)
    {
        public readonly long ShardKey = shardKey;
        public readonly Vector2 Position = position;
    }

    public SpatialShardGrid(int shardSize = ProjectRuntimeContracts.World.ChunkSize)
    {
        if (shardSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(shardSize), shardSize, "ShardSize must be greater than zero.");
        }

        _shardSize = shardSize;
    }

    public int ShardSize => _shardSize;

    public int Count => _items.Count;

    public int ActiveShardCount => _shards.Count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ComputeShardKey(float x, float y, int shardSize)
    {
        int sx = Mathf.FloorToInt(x / shardSize);
        int sy = Mathf.FloorToInt(y / shardSize);
        return ((long)sx << 32) | (uint)sy;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long GetShardKey(Vector2 position)
    {
        return ComputeShardKey(position.x, position.y, _shardSize);
    }

    public void Insert(T item, Vector2 position)
    {
        if (item == null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        if (_items.TryGetValue(item, out ItemRecord existing))
        {
            Update(item, position);
            return;
        }

        long key = GetShardKey(position);
        List<T> list = GetOrCreateShardList(key);
        list.Add(item);
        _items[item] = new ItemRecord(key, position);
    }

    public void Update(T item, Vector2 newPosition)
    {
        if (item == null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        if (!_items.TryGetValue(item, out ItemRecord record))
        {
            Insert(item, newPosition);
            return;
        }

        long newKey = GetShardKey(newPosition);
        if (newKey == record.ShardKey)
        {
            _items[item] = new ItemRecord(newKey, newPosition);
            return;
        }

        // Entity crossed shard boundary: remove from old list and add to new list.
        if (_shards.TryGetValue(record.ShardKey, out List<T>? oldList))
        {
            oldList.Remove(item);
            if (oldList.Count == 0)
            {
                _shards.Remove(record.ShardKey);
                _listPool.Push(oldList);
            }
        }

        List<T> newList = GetOrCreateShardList(newKey);
        newList.Add(item);
        _items[item] = new ItemRecord(newKey, newPosition);
    }

    public bool Remove(T item)
    {
        if (item == null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        if (!_items.TryGetValue(item, out ItemRecord record))
        {
            return false;
        }

        _items.Remove(item);

        if (_shards.TryGetValue(record.ShardKey, out List<T>? list))
        {
            list.Remove(item);
            if (list.Count == 0)
            {
                _shards.Remove(record.ShardKey);
                _listPool.Push(list);
            }
        }

        return true;
    }

    public void Clear()
    {
        foreach (KeyValuePair<long, List<T>> pair in _shards)
        {
            pair.Value.Clear();
            _listPool.Push(pair.Value);
        }

        _shards.Clear();
        _items.Clear();
    }

    public bool TryGetPosition(T item, out Vector2 position)
    {
        if (item == null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        if (_items.TryGetValue(item, out ItemRecord record))
        {
            position = record.Position;
            return true;
        }

        position = default;
        return false;
    }

    public void QueryRect(in Rect bounds, List<T> results)
    {
        if (results == null)
        {
            throw new ArgumentNullException(nameof(results));
        }

        if (_items.Count == 0 || bounds.width <= 0f || bounds.height <= 0f)
        {
            return;
        }

        int minSx = Mathf.FloorToInt(bounds.xMin / _shardSize);
        int maxSx = Mathf.FloorToInt(bounds.xMax / _shardSize);
        int minSy = Mathf.FloorToInt(bounds.yMin / _shardSize);
        int maxSy = Mathf.FloorToInt(bounds.yMax / _shardSize);

        for (int sx = minSx; sx <= maxSx; sx++)
        {
            float shardMinX = sx * _shardSize;
            float shardMaxX = shardMinX + _shardSize;
            bool xFullyContained = shardMinX >= bounds.xMin && shardMaxX <= bounds.xMax;

            for (int sy = minSy; sy <= maxSy; sy++)
            {
                long key = ((long)sx << 32) | (uint)sy;
                if (!_shards.TryGetValue(key, out List<T>? list) || list.Count == 0)
                {
                    continue;
                }

                float shardMinY = sy * _shardSize;
                float shardMaxY = shardMinY + _shardSize;
                bool fullyContained = xFullyContained && shardMinY >= bounds.yMin && shardMaxY <= bounds.yMax;

                if (fullyContained)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        results.Add(list[i]);
                    }
                }
                else
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        T item = list[i];
                        if (_items.TryGetValue(item, out ItemRecord rec) && bounds.Contains(rec.Position))
                        {
                            results.Add(item);
                        }
                    }
                }
            }
        }
    }

    public void QueryRadius(Vector2 center, float radius, List<T> results)
    {
        if (results == null)
        {
            throw new ArgumentNullException(nameof(results));
        }

        if (_items.Count == 0 || radius <= 0f)
        {
            return;
        }

        float sqrRadius = radius * radius;
        Rect bounds = new(center.x - radius, center.y - radius, radius * 2f, radius * 2f);

        int minSx = Mathf.FloorToInt(bounds.xMin / _shardSize);
        int maxSx = Mathf.FloorToInt(bounds.xMax / _shardSize);
        int minSy = Mathf.FloorToInt(bounds.yMin / _shardSize);
        int maxSy = Mathf.FloorToInt(bounds.yMax / _shardSize);

        for (int sx = minSx; sx <= maxSx; sx++)
        {
            for (int sy = minSy; sy <= maxSy; sy++)
            {
                long key = ((long)sx << 32) | (uint)sy;
                if (!_shards.TryGetValue(key, out List<T>? list) || list.Count == 0)
                {
                    continue;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    T item = list[i];
                    if (_items.TryGetValue(item, out ItemRecord rec) &&
                        (rec.Position - center).sqrMagnitude <= sqrRadius)
                    {
                        results.Add(item);
                    }
                }
            }
        }
    }

    private List<T> GetOrCreateShardList(long key)
    {
        if (_shards.TryGetValue(key, out List<T>? list))
        {
            return list;
        }

        list = _listPool.Count > 0 ? _listPool.Pop() : new List<T>(16);
        list.Clear();
        _shards[key] = list;
        return list;
    }
}

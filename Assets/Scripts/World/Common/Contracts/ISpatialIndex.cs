#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace Kern.World;

/// <summary>
/// Unified spatial partitioning index contract for 2D world entities and objects.
/// Shards the 2D world into integer-coordinate cells/chunks to allow fast O(K) viewport
/// queries, proximity lookups, and culling without scanning all O(N) registered entities.
/// </summary>
/// <typeparam name="T">The indexed entity or handle type.</typeparam>
public interface ISpatialIndex<T>
{
    /// <summary>
    /// Gets the width and height of each spatial shard cell in world units.
    /// </summary>
    int ShardSize { get; }

    /// <summary>
    /// Gets the total number of registered items across all shards.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Inserts an item at the specified 2D world position.
    /// </summary>
    void Insert(T item, Vector2 position);

    /// <summary>
    /// Updates the spatial shard of an item when its world position changes.
    /// If the item remains in the same shard, this is an O(1) position update with zero list reallocations.
    /// </summary>
    void Update(T item, Vector2 newPosition);

    /// <summary>
    /// Removes an item from the spatial index.
    /// </summary>
    bool Remove(T item);

    /// <summary>
    /// Clears all items and shards from the spatial index.
    /// </summary>
    void Clear();

    /// <summary>
    /// Populates the results list with all items located within the specified world bounding rectangle.
    /// </summary>
    void QueryRect(in Rect bounds, List<T> results);

    /// <summary>
    /// Populates the results list with all items located within the specified world radius.
    /// </summary>
    void QueryRadius(Vector2 center, float radius, List<T> results);

    /// <summary>
    /// Attempts to retrieve the last recorded position of an indexed item.
    /// </summary>
    bool TryGetPosition(T item, out Vector2 position);
}

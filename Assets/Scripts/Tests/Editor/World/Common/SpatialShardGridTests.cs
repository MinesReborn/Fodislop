#nullable enable

using System.Collections.Generic;
using Kern.World;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World;

[TestFixture]
public class SpatialShardGridTests
{
    private sealed class MockEntity(string name)
    {
        public string Name { get; } = name;
    }

    [Test]
    public void Insert_IncreasesCount_AndStoresPosition()
    {
        var grid = new SpatialShardGrid<MockEntity>(shardSize: 32);
        var e1 = new MockEntity("Robot1");

        grid.Insert(e1, new Vector2(10f, 15f));

        Assert.AreEqual(1, grid.Count);
        Assert.AreEqual(1, grid.ActiveShardCount);
        Assert.IsTrue(grid.TryGetPosition(e1, out Vector2 pos));
        Assert.AreEqual(new Vector2(10f, 15f), pos);
    }

    [Test]
    public void Update_WithinSameShard_UpdatesPositionWithoutChangingActiveShardCount()
    {
        var grid = new SpatialShardGrid<MockEntity>(shardSize: 32);
        var e1 = new MockEntity("Robot1");

        grid.Insert(e1, new Vector2(5f, 5f));
        Assert.AreEqual(1, grid.ActiveShardCount);

        grid.Update(e1, new Vector2(12f, 14f));

        Assert.AreEqual(1, grid.Count);
        Assert.AreEqual(1, grid.ActiveShardCount);
        Assert.IsTrue(grid.TryGetPosition(e1, out Vector2 pos));
        Assert.AreEqual(new Vector2(12f, 14f), pos);
    }

    [Test]
    public void Update_CrossingShardBoundary_MovesItemAndRecyclesEmptyShard()
    {
        var grid = new SpatialShardGrid<MockEntity>(shardSize: 32);
        var e1 = new MockEntity("Robot1");

        grid.Insert(e1, new Vector2(5f, 5f)); // Shard (0, 0)
        Assert.AreEqual(1, grid.ActiveShardCount);

        grid.Update(e1, new Vector2(65f, 70f)); // Shard (2, 2)

        Assert.AreEqual(1, grid.Count);
        Assert.AreEqual(1, grid.ActiveShardCount); // Old shard (0, 0) emptied and pruned
        Assert.IsTrue(grid.TryGetPosition(e1, out Vector2 pos));
        Assert.AreEqual(new Vector2(65f, 70f), pos);
    }

    [Test]
    public void Remove_RemovesItem_AndPrunesEmptyShard()
    {
        var grid = new SpatialShardGrid<MockEntity>(shardSize: 32);
        var e1 = new MockEntity("Robot1");
        var e2 = new MockEntity("Robot2");

        grid.Insert(e1, new Vector2(5f, 5f));
        grid.Insert(e2, new Vector2(10f, 10f));
        Assert.AreEqual(2, grid.Count);
        Assert.AreEqual(1, grid.ActiveShardCount);

        bool removed1 = grid.Remove(e1);
        Assert.IsTrue(removed1);
        Assert.AreEqual(1, grid.Count);
        Assert.AreEqual(1, grid.ActiveShardCount);

        bool removed2 = grid.Remove(e2);
        Assert.IsTrue(removed2);
        Assert.AreEqual(0, grid.Count);
        Assert.AreEqual(0, grid.ActiveShardCount);
    }

    [Test]
    public void QueryRect_FindsOnlyEntitiesWithinBounds()
    {
        var grid = new SpatialShardGrid<MockEntity>(shardSize: 32);
        var inside = new MockEntity("Inside");
        var outside = new MockEntity("Outside");
        var otherShard = new MockEntity("OtherShard");

        grid.Insert(inside, new Vector2(10f, 10f));
        grid.Insert(outside, new Vector2(25f, 25f));
        grid.Insert(otherShard, new Vector2(100f, 100f));

        var results = new List<MockEntity>();
        grid.QueryRect(new Rect(0f, 0f, 15f, 15f), results);

        Assert.AreEqual(1, results.Count);
        Assert.AreSame(inside, results[0]);
    }

    [Test]
    public void QueryRect_NegativeCoordinates_HandledCorrectly()
    {
        var grid = new SpatialShardGrid<MockEntity>(shardSize: 32);
        var eNeg = new MockEntity("Negative");
        var ePos = new MockEntity("Positive");

        grid.Insert(eNeg, new Vector2(-10f, -10f));
        grid.Insert(ePos, new Vector2(10f, 10f));

        var results = new List<MockEntity>();
        grid.QueryRect(new Rect(-20f, -20f, 15f, 15f), results);

        Assert.AreEqual(1, results.Count);
        Assert.AreSame(eNeg, results[0]);
    }

    [Test]
    public void QueryRadius_FiltersByExactDistance()
    {
        var grid = new SpatialShardGrid<MockEntity>(shardSize: 32);
        var near = new MockEntity("Near");
        var far = new MockEntity("Far");

        grid.Insert(near, new Vector2(0f, 3f)); // dist 3
        grid.Insert(far, new Vector2(0f, 10f)); // dist 10

        var results = new List<MockEntity>();
        grid.QueryRadius(Vector2.zero, 5f, results);

        Assert.AreEqual(1, results.Count);
        Assert.AreSame(near, results[0]);
    }

    [Test]
    public void Clear_EmptiesAllItemsAndShards()
    {
        var grid = new SpatialShardGrid<MockEntity>(shardSize: 32);
        grid.Insert(new MockEntity("1"), new Vector2(1f, 1f));
        grid.Insert(new MockEntity("2"), new Vector2(50f, 50f));

        grid.Clear();

        Assert.AreEqual(0, grid.Count);
        Assert.AreEqual(0, grid.ActiveShardCount);
    }
}

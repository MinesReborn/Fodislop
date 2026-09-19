#nullable enable

using System;
using System.Collections.Generic;
using Kern.Persistence;
using NUnit.Framework;

namespace Kern.Tests.World;

[TestFixture]
public class ChunkLruCacheBoundaryTests
{
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(64)]
    [TestCase(1024)]
    public void LoadedCount_NeverExceedsCapacity(int capacity)
    {
        var cache = new ChunkLruCache<int>(capacity);
        for (int i = 0; i < capacity * 4; i++)
        {
            cache.AddOrUpdate(i, new int[1]);
            Assert.That(cache.LoadedCount, Is.LessThanOrEqualTo(capacity), $"i={i}");
        }
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(64)]
    public void DirtyEviction_FiresCallbackOnceAndClearsDirty(int capacity)
    {
        int evictCalls = 0;
        int? evictedIndex = null;
        var cache = new ChunkLruCache<int>(capacity, (idx, _) =>
        {
            evictCalls++;
            evictedIndex = idx;
        });

        for (int i = 0; i < capacity; i++)
        {
            cache.AddOrUpdate(i, new int[1]);
            cache.MarkDirty(i);
        }

        cache.AddOrUpdate(capacity, new int[1]);
        Assert.That(evictCalls, Is.EqualTo(1), "eviction fires exactly once");
        Assert.That(evictedIndex, Is.Not.Null);
        Assert.That(cache.Contains(evictedIndex!.Value), Is.False);
        Assert.That(cache.IsDirty(evictedIndex.Value), Is.False);
    }

    [Test]
    public void PreserveDirtyChunks_SkipsDirtyEvictionAndKeepsDataResident()
    {
        int evictCalls = 0;
        var cache = new ChunkLruCache<int>(
            maxCapacity: 1,
            onEvictDirty: (_, _) => evictCalls++,
            allowDirtyEviction: false);

        int[] first = [10];
        int[] second = [20];
        cache.AddOrUpdate(1, first);
        cache.MarkDirty(1);
        cache.AddOrUpdate(2, second);

        Assert.That(cache.LoadedCount, Is.EqualTo(2));
        Assert.That(cache.TryGet(1, out int[]? resident), Is.True);
        Assert.That(resident, Is.SameAs(first));
        Assert.That(evictCalls, Is.EqualTo(0));
    }

    [Test]
    public void DetachedDirtySnapshot_UsesCopyOnWriteForConcurrentMutation()
    {
        var cache = new ChunkLruCache<int>(4);
        int[] original = [10, 20];
        cache.AddOrUpdate(7, original);
        cache.MarkDirty(7);

        List<(int Index, int[] Chunk)> snapshot = cache.DetachDirtySnapshot();
        int[] writable = cache.PrepareForWrite(7, original);
        writable[0] = 99;
        cache.MarkDirty(7);

        Assert.That(snapshot, Has.Count.EqualTo(1));
        Assert.That(snapshot[0].Chunk, Is.SameAs(original));
        Assert.That(snapshot[0].Chunk[0], Is.EqualTo(10));
        Assert.That(cache.TryGet(7, out int[]? resident), Is.True);
        Assert.That(resident, Is.SameAs(writable));
        Assert.That(resident![0], Is.EqualTo(99));
        Assert.That(cache.IsDirty(7), Is.True);
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(64)]
    public void AddOrUpdate_SameIndex_DoesNotGrowCount(int capacity)
    {
        var cache = new ChunkLruCache<int>(capacity);
        cache.AddOrUpdate(0, new int[1]);
        int before = cache.LoadedCount;
        for (int i = 0; i < 5; i++) cache.AddOrUpdate(0, new int[1]);
        Assert.That(cache.LoadedCount, Is.EqualTo(before));
    }

    [Test]
    public void Touch_PromotesToMostRecentlyUsed()
    {
        var cache = new ChunkLruCache<int>(2);
        cache.AddOrUpdate(0, new int[1]);
        cache.AddOrUpdate(1, new int[1]);
        cache.Touch(0);
        cache.AddOrUpdate(2, new int[1]);
        Assert.That(cache.Contains(0), Is.True, "touched index should survive");
        Assert.That(cache.Contains(1), Is.False, "untouched index should evict");
    }

    [Test]
    public void MarkDirty_NonExistent_AddsToDirtyWithoutLoaded()
    {
        var cache = new ChunkLruCache<int>(4);
        cache.MarkDirty(99);
        Assert.That(cache.HasDirtyChunks, Is.True);
        Assert.That(cache.DirtyCount, Is.EqualTo(1));
    }

    [Test]
    public void ClearDirty_EmptiesDirtySet()
    {
        var cache = new ChunkLruCache<int>(4);
        cache.AddOrUpdate(0, new int[1]);
        cache.AddOrUpdate(1, new int[1]);
        cache.MarkDirty(0);
        cache.MarkDirty(1);
        cache.ClearDirty();
        Assert.That(cache.DirtyCount, Is.EqualTo(0));
        Assert.That(cache.HasDirtyChunks, Is.False);
    }

    [Test]
    public void Clear_DropsAllLoaded()
    {
        var cache = new ChunkLruCache<int>(4);
        cache.AddOrUpdate(0, new int[1]);
        cache.AddOrUpdate(1, new int[1]);
        cache.Clear();
        Assert.That(cache.LoadedCount, Is.EqualTo(0));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(int.MinValue)]
    public void NonPositiveCapacity_Throws(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkLruCache<int>(capacity));
    }

    [Test]
    public void AddOrUpdate_NullChunk_Throws()
    {
        var cache = new ChunkLruCache<int>(4);
        Assert.Throws<ArgumentNullException>(() => cache.AddOrUpdate(0, null!));
    }
}

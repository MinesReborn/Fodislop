#nullable enable

namespace Kern.Tests.World;

using System;
using System.Collections.Generic;
using Kern.Persistence;
using NUnit.Framework;

[TestFixture]
public class ChunkLRUCacheTests
{
    [Test]
    public void AddAndGet_SingleChunk_StoresAndRetrieves()
    {
        var cache = new ChunkLRUCache<int>(maxCapacity: 10);
        int[] chunk = [1, 2, 3];

        cache.AddOrUpdate(5, chunk);

        Assert.IsTrue(cache.Contains(5));
        Assert.AreEqual(1, cache.LoadedCount);
        Assert.IsTrue(cache.TryGet(5, out int[]? retrieved));
        Assert.AreSame(chunk, retrieved);
    }

    [Test]
    public void Add_ExceedingCapacity_EvictsOldestChunk()
    {
        var cache = new ChunkLRUCache<int>(maxCapacity: 2);
        int[] c1 = [1];
        int[] c2 = [2];
        int[] c3 = [3];

        cache.AddOrUpdate(1, c1);
        cache.AddOrUpdate(2, c2);
        cache.AddOrUpdate(3, c3); // should evict 1

        Assert.AreEqual(2, cache.LoadedCount);
        Assert.IsFalse(cache.Contains(1));
        Assert.IsTrue(cache.Contains(2));
        Assert.IsTrue(cache.Contains(3));
    }

    [Test]
    public void Touch_RefreshesLRUOrder_EvictsOldestInstead()
    {
        var cache = new ChunkLRUCache<int>(maxCapacity: 2);
        int[] c1 = [1];
        int[] c2 = [2];
        int[] c3 = [3];

        cache.AddOrUpdate(1, c1);
        cache.AddOrUpdate(2, c2);

        // Touch 1 so it becomes the most recently used
        cache.Touch(1);

        // Now adding 3 should evict 2 (which is now oldest) instead of 1
        cache.AddOrUpdate(3, c3);

        Assert.AreEqual(2, cache.LoadedCount);
        Assert.IsTrue(cache.Contains(1));
        Assert.IsFalse(cache.Contains(2));
        Assert.IsTrue(cache.Contains(3));
    }

    [Test]
    public void EvictDirty_InvokesCallbackAndClearsDirty()
    {
        var evicted = new List<(int Index, int[] Chunk)>();
        var cache = new ChunkLRUCache<int>(
            maxCapacity: 1,
            onEvictDirty: (index, data) => evicted.Add((index, data)));

        int[] c1 = [10];
        cache.AddOrUpdate(1, c1);
        cache.MarkDirty(1);

        Assert.IsTrue(cache.IsDirty(1));
        Assert.IsTrue(cache.HasDirtyChunks);

        int[] c2 = [20];
        cache.AddOrUpdate(2, c2); // triggers eviction of 1

        Assert.AreEqual(1, evicted.Count);
        Assert.AreEqual(1, evicted[0].Index);
        Assert.AreSame(c1, evicted[0].Chunk);
        Assert.IsFalse(cache.IsDirty(1));
    }

    [Test]
    public void Clear_EmptiesAllLoadedAndDirtyState()
    {
        var cache = new ChunkLRUCache<int>(maxCapacity: 5);
        cache.AddOrUpdate(1, [1]);
        cache.AddOrUpdate(2, [2]);
        cache.MarkDirty(1);

        cache.Clear();

        Assert.AreEqual(0, cache.LoadedCount);
        Assert.AreEqual(0, cache.DirtyCount);
        Assert.IsFalse(cache.HasDirtyChunks);
        Assert.IsFalse(cache.Contains(1));
    }

    [Test]
    public void Constructor_NonPositiveCapacity_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = new ChunkLRUCache<int>(0);
        });

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = new ChunkLRUCache<int>(-5);
        });
    }

    [Test]
    public void AddOrUpdate_NullChunk_ThrowsArgumentNullException()
    {
        var cache = new ChunkLRUCache<int>(5);
        Assert.Throws<ArgumentNullException>(() =>
        {
            cache.AddOrUpdate(1, null!);
        });
    }

    [Test]
    public void DirtyOverflow_ShrinksBackToCapacityOnceWritten()
    {
        var cache = new ChunkLRUCache<int>(maxCapacity: 2, allowDirtyEviction: false);
        for (int index = 0; index < 5; index++)
        {
            cache.AddOrUpdate(index, [index]);
            cache.MarkDirty(index);
        }

        Assert.That(cache.LoadedCount, Is.EqualTo(5), "Dirty chunks must not be evicted before they are written.");

        var snapshot = cache.DetachDirtySnapshot();
        cache.CompleteDirtySnapshot(snapshot);
        cache.AddOrUpdate(5, [5]);

        Assert.That(cache.LoadedCount, Is.EqualTo(2), "The cache stayed at its dirty high-water mark after the write.");
        Assert.That(cache.Contains(5), Is.True);
        Assert.That(cache.Contains(4), Is.True, "Trimming evicted the most recently used chunk.");
    }

    // Кэш мира бережёт грязные чанки, а всё, что приехало с сервера, грязное
    // до записи на диск. Пока стример льёт чанки, вытеснять нечего — и это
    // обязано стоить O(1), а не обхода всего списка на каждую вставку.
    // Проверяется наблюдаемое поведение: ничего не вытеснено, кэш вырос выше
    // ёмкости, а после сброса грязноты вытеснение возобновилось.
    [Test]
    public void DirtyChunksAreNeverEvictedAndTheCacheGrowsPastCapacity()
    {
        var cache = new ChunkLRUCache<int>(maxCapacity: 4, allowDirtyEviction: false);

        for (int index = 0; index < 32; index++)
        {
            cache.AddOrUpdate(index, [index]);
            cache.MarkDirty(index);
        }

        Assert.AreEqual(32, cache.LoadedCount);
        for (int index = 0; index < 32; index++)
        {
            Assert.IsTrue(cache.Contains(index), $"чанк {index} вытеснен, хотя грязный");
        }
    }

    [Test]
    public void EvictionResumesOnceTheDirtySetIsCleared()
    {
        var cache = new ChunkLRUCache<int>(maxCapacity: 4, allowDirtyEviction: false);
        for (int index = 0; index < 8; index++)
        {
            cache.AddOrUpdate(index, [index]);
            cache.MarkDirty(index);
        }

        cache.ClearDirty();
        cache.AddOrUpdate(100, [100]);

        Assert.AreEqual(4, cache.LoadedCount);
        Assert.IsTrue(cache.Contains(100));
    }

    [Test]
    public void ADetachedChunkIsNotEvictedUntilItsWriteCompletes()
    {
        var cache = new ChunkLRUCache<int>(maxCapacity: 2, allowDirtyEviction: false);
        cache.AddOrUpdate(1, [1]);
        cache.MarkDirty(1);
        List<(int Index, int[] Chunk)> snapshot = cache.DetachDirtySnapshot();

        cache.AddOrUpdate(2, [2]);
        cache.AddOrUpdate(3, [3]);
        Assert.IsTrue(cache.Contains(1), "чанк вытеснен, пока его писали на диск");

        cache.CompleteDirtySnapshot(snapshot);
        cache.AddOrUpdate(4, [4]);

        Assert.IsFalse(cache.Contains(1));
    }

    // Запись старого снимка не имеет права снять защиту с нового: чанк,
    // переписанный после первого снимка и отданный во второй, обязан снова
    // копироваться при записи, пока второй снимок не записан.
    [Test]
    public void CompletingAnOlderSnapshotKeepsTheNewerSnapshotDetached()
    {
        var cache = new ChunkLRUCache<int>(maxCapacity: 4, allowDirtyEviction: false);
        cache.AddOrUpdate(1, [1]);
        cache.MarkDirty(1);
        List<(int Index, int[] Chunk)> first = cache.DetachDirtySnapshot();

        Assert.IsTrue(cache.TryGet(1, out int[]? original));
        int[] rewritten = cache.PrepareForWrite(1, original!);
        Assert.AreNotSame(original, rewritten, "запись в отданный чанк обязана копировать");
        rewritten[0] = 2;
        cache.MarkDirty(1);
        List<(int Index, int[] Chunk)> second = cache.DetachDirtySnapshot();

        cache.CompleteDirtySnapshot(first);

        int[] again = cache.PrepareForWrite(1, rewritten);
        Assert.AreNotSame(rewritten, again, "второй снимок остался без защиты copy-on-write");
        Assert.AreEqual(2, second[0].Chunk[0]);
    }

    [Test]
    public void RestoringAFailedSnapshotMarksTheChunkDirtyAgain()
    {
        var cache = new ChunkLRUCache<int>(maxCapacity: 4, allowDirtyEviction: false);
        cache.AddOrUpdate(1, [1]);
        cache.MarkDirty(1);
        List<(int Index, int[] Chunk)> snapshot = cache.DetachDirtySnapshot();
        Assert.IsFalse(cache.IsDirty(1));

        cache.RestoreDirtySnapshot(snapshot);

        Assert.IsTrue(cache.IsDirty(1));
    }
}

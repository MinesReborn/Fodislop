#nullable enable

using System;
using System.Collections.Generic;
using Kern.World.Terrain;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World;

[TestFixture]
public class DirtyRectSetFuzzTests
{
    private const int MeshWidth = 128;
    private const int MeshHeight = 96;

    private static RectInt Bounds => new(1000, 2000, MeshWidth, MeshHeight);

    private static readonly int[] s_seeds = [1, 7, 42, 1337, 90210, 2147483, 8675309];

    private static IEnumerable<int> HostileCoordinates()
    {
        yield return int.MinValue;
        yield return int.MinValue + 1;
        yield return -1;
        yield return 0;
        yield return 1;
        yield return 999;
        yield return 1000;
        yield return 1000 + MeshWidth - 1;
        yield return 1000 + MeshWidth;
        yield return 2000;
        yield return 2000 + MeshHeight;
        yield return int.MaxValue - 1;
        yield return int.MaxValue;
    }

    [Test]
    public void EveryAcceptedRectStaysInsideTheCachedRegion([ValueSource(nameof(s_seeds))] int seed)
    {
        var random = new System.Random(seed);
        var set = new DirtyRectSet();

        for (int iteration = 0; iteration < 2000; iteration++)
        {
            set.Add(RandomRect(random), Bounds);

            for (int i = 0; i < set.Count; i++)
            {
                RectInt rect = set[i];
                Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(Bounds.xMin), Describe(set, i, seed));
                Assert.That(rect.yMin, Is.GreaterThanOrEqualTo(Bounds.yMin), Describe(set, i, seed));
                Assert.That(rect.xMax, Is.LessThanOrEqualTo(Bounds.xMax), Describe(set, i, seed));
                Assert.That(rect.yMax, Is.LessThanOrEqualTo(Bounds.yMax), Describe(set, i, seed));
                Assert.That(rect.width, Is.GreaterThan(0), Describe(set, i, seed));
                Assert.That(rect.height, Is.GreaterThan(0), Describe(set, i, seed));
            }
        }
    }

    [Test]
    public void ScatteredRegionsAreNotCollapsedByAnArtificialCapacity()
    {
        var set = new DirtyRectSet();

        for (int index = 0; index < 16; index++)
        {
            set.Add(new RectInt(1000 + (index * 2), 2000, 1, 1), Bounds);
        }

        Assert.That(set.Count, Is.EqualTo(16));
    }

    [Test]
    public void TotalAreaNeverExceedsTheCachedRegion([ValueSource(nameof(s_seeds))] int seed)
    {
        var random = new System.Random(seed);
        var set = new DirtyRectSet();

        for (int iteration = 0; iteration < 2000; iteration++)
        {
            set.Add(RandomRect(random), Bounds);

            // Rectangles are merged when their union does not add wasted
            // area, so disjoint accepted rectangles remain a bounded subset
            // of the cached region.
            Assert.That(
                set.TotalArea,
                Is.LessThanOrEqualTo((long)MeshWidth * MeshHeight),
                $"seed {seed}, iteration {iteration}");
            Assert.That(set.TotalArea, Is.GreaterThanOrEqualTo(0));
        }
    }

    [Test]
    public void NoChangedCellIsEverDropped([ValueSource(nameof(s_seeds))] int seed)
    {
        var random = new System.Random(seed);

        // Small bounds so the reference set of covered cells stays cheap to
        // compare exhaustively.
        var bounds = new RectInt(10, 20, 24, 18);

        for (int round = 0; round < 400; round++)
        {
            var set = new DirtyRectSet();
            var expected = new HashSet<(int x, int y)>();

            int batch = random.Next(1, 20);
            for (int i = 0; i < batch; i++)
            {
                RectInt candidate = RandomRect(random);
                set.Add(candidate, bounds);
                RecordClippedCells(candidate, bounds, expected);
            }

            var covered = new HashSet<(int x, int y)>();
            for (int i = 0; i < set.Count; i++)
            {
                RectInt rect = set[i];
                for (int x = rect.xMin; x < rect.xMax; x++)
                {
                    for (int y = rect.yMin; y < rect.yMax; y++)
                    {
                        covered.Add((x, y));
                    }
                }
            }

            // Superset, not equality: merging and overflow absorption are
            // allowed to repaint extra cells, never to skip one. A skipped
            // cell is a stale tile the player can see.
            Assert.That(
                covered.IsSupersetOf(expected),
                Is.True,
                $"seed {seed}, round {round}: {expected.Count} cells changed, " +
                $"{covered.Count} covered, missing " +
                $"{CountMissing(expected, covered)}.");
        }
    }

    [Test]
    public void RectanglesOutsideTheCachedRegionAreRejected()
    {
        var set = new DirtyRectSet();

        Assert.That(set.Add(new RectInt(0, 0, 10, 10), Bounds), Is.False);
        Assert.That(set.Add(new RectInt(5000, 5000, 10, 10), Bounds), Is.False);
        Assert.That(set.Add(new RectInt(1000, 2000, 0, 10), Bounds), Is.False);
        Assert.That(set.Add(new RectInt(1000, 2000, 10, -5), Bounds), Is.False);
        Assert.That(set.IsEmpty, Is.True);
    }

    [Test]
    public void AnOverflowingRectangleIsRejectedRatherThanWrapped()
    {
        var set = new DirtyRectSet();

        // x + width overflows int. Computed in int, xMax comes out negative
        // and the rectangle reads as one that starts far to the left of the
        // cached region and ends inside it - so a clip written in int
        // arithmetic accepts it and hands the renderer nonsense offsets.
        Assert.That(
            set.Add(new RectInt(int.MaxValue - 4, 2000, int.MaxValue, 10), Bounds),
            Is.False,
            "A rectangle whose extent overflows int must be rejected.");
        Assert.That(set.IsEmpty, Is.True);

        // The same trick on the negative side.
        Assert.That(
            set.Add(new RectInt(int.MinValue, 2000, int.MinValue, 10), Bounds),
            Is.False);
        Assert.That(set.IsEmpty, Is.True);
    }

    [Test]
    public void ScatteredSmallChunksDoNotUnionIntoTheWholeViewport()
    {
        // The regression this type exists for: chunks arriving at opposite
        // corners used to merge into one screen-sized rectangle, whose area
        // tripped the renderer's size check and forced a full rebuild.
        var set = new DirtyRectSet();
        set.Add(new RectInt(1000, 2000, 32, 32), Bounds);
        set.Add(new RectInt(1000 + MeshWidth - 32, 2000 + MeshHeight - 32, 32, 32), Bounds);

        Assert.That(set.Count, Is.EqualTo(2), "Distant chunks must stay separate.");
        Assert.That(
            set.TotalArea,
            Is.EqualTo(2 * 32 * 32),
            "Total area must describe the cells patched, not their bounding box.");
        Assert.That(
            set.TotalArea * 2,
            Is.LessThan((long)MeshWidth * MeshHeight),
            "Two small chunks must not trip the full-rebuild threshold.");
    }

    // Касание углом — не повод сливать: объединение вчетверо больше суммы, а
    // цепочка таких слияний вдоль диагонали снова собирала прямоугольник во
    // весь экран. Полоса вплотную при этом обязана продолжать сливаться.
    [Test]
    public void DiagonallyTouchingChunksDoNotMerge()
    {
        var set = new DirtyRectSet();
        set.Add(new RectInt(1000, 2000, 32, 32), Bounds);
        set.Add(new RectInt(1032, 2032, 32, 32), Bounds);

        Assert.That(set.Count, Is.EqualTo(2), "Углом касаются — площади не делят.");
        Assert.That(set.TotalArea, Is.EqualTo(2 * 32 * 32));
    }

    // Цепочка по диагонали: каждый следующий чанк касается предыдущего углом.
    // Раньше это схлопывалось в один прямоугольник, растущий по всему окну.
    [Test]
    public void ADiagonalChainDoesNotGrowIntoTheWholeWindow()
    {
        var set = new DirtyRectSet();
        const int Steps = 3;
        for (int i = 0; i < Steps; i++)
        {
            set.Add(new RectInt(1000 + (i * 32), 2000 + (i * 32), 32, 32), Bounds);
        }

        Assert.That(set.TotalArea, Is.EqualTo(Steps * 32 * 32));
    }

    // Перекрывающиеся прямоугольники обязаны сливаться: иначе клетка попадает
    // в заплатку дважды и дважды же считается в оценке стоимости.
    [Test]
    public void OverlappingChunksMerge()
    {
        var set = new DirtyRectSet();
        set.Add(new RectInt(1000, 2000, 32, 32), Bounds);
        set.Add(new RectInt(1016, 2016, 32, 32), Bounds);

        Assert.That(set.Count, Is.EqualTo(1), "Перекрытие обязано слиться.");
    }

    [Test]
    public void AdjacentChunksMergeInsteadOfConsumingSlots()
    {
        var set = new DirtyRectSet();
        for (int i = 0; i < 4; i++)
        {
            set.Add(new RectInt(1000 + (i * 32), 2000, 32, 32), Bounds);
        }

        Assert.That(set.Count, Is.EqualTo(1), "A contiguous strip should collapse to one rect.");
        Assert.That(set.TotalArea, Is.EqualTo(128 * 32));
    }

    private static RectInt RandomRect(System.Random random)
    {
        // A quarter of the draws come from the hostile pool, so overflow and
        // boundary cases are hit constantly rather than by luck.
        if (random.Next(4) == 0)
        {
            var pool = new List<int>(HostileCoordinates());
            return new RectInt(
                pool[random.Next(pool.Count)],
                pool[random.Next(pool.Count)],
                pool[random.Next(pool.Count)],
                pool[random.Next(pool.Count)]);
        }

        return new RectInt(
            random.Next(900, 1000 + MeshWidth + 100),
            random.Next(1900, 2000 + MeshHeight + 100),
            random.Next(-8, 80),
            random.Next(-8, 80));
    }

    private static void RecordClippedCells(
        RectInt candidate,
        RectInt bounds,
        HashSet<(int x, int y)> into)
    {
        long rawMaxX = (long)candidate.x + candidate.width;
        long rawMaxY = (long)candidate.y + candidate.height;
        if (rawMaxX > int.MaxValue || rawMaxX < int.MinValue ||
            rawMaxY > int.MaxValue || rawMaxY < int.MinValue)
        {
            return;
        }

        long minX = Math.Max((long)candidate.xMin, bounds.xMin);
        long minY = Math.Max((long)candidate.yMin, bounds.yMin);
        long maxX = Math.Min((long)candidate.xMin + candidate.width, bounds.xMax);
        long maxY = Math.Min((long)candidate.yMin + candidate.height, bounds.yMax);

        for (long x = minX; x < maxX; x++)
        {
            for (long y = minY; y < maxY; y++)
            {
                into.Add(((int)x, (int)y));
            }
        }
    }

    private static int CountMissing(
        HashSet<(int x, int y)> expected,
        HashSet<(int x, int y)> covered)
    {
        int missing = 0;
        foreach ((int x, int y) cell in expected)
        {
            if (!covered.Contains(cell))
            {
                missing++;
            }
        }

        return missing;
    }

    private static string Describe(DirtyRectSet set, int index, int seed)
    {
        return $"seed {seed}: rect {index} of {set.Count} is {set[index]}, " +
            $"bounds {Bounds}.";
    }
}

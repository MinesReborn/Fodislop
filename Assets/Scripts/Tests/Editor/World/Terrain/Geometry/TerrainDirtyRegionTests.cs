#nullable enable

using System;
using System.Collections.Generic;
using Kern.World.Terrain;
using NUnit.Framework;
using UnityEngine;
using Random = System.Random;

namespace Kern.Tests.World;

[TestFixture]
public class TerrainDirtyRegionTests
{
    private const int Width = 192;
    private const int Height = 128;

    private static TerrainDirtyRegion Fresh()
    {
        var region = new TerrainDirtyRegion();
        region.Reset(Width, Height);
        region.Clear();
        return region;
    }

    private static bool Covers(TerrainDirtyRegion region, int ringX, int ringY)
    {
        for (int layer = 0; layer < TerrainCellDataPacker.LayersPerCell; layer++)
        {
            int row = (ringY * TerrainCellDataPacker.LayersPerCell) + layer;
            bool covered = false;
            for (int i = 0; i < region.Count; i++)
            {
                RectInt rect = region[i];
                covered |= ringX >= rect.xMin && ringX < rect.xMax && row >= rect.yMin && row < rect.yMax;
            }

            if (!covered)
            {
                return false;
            }
        }

        return true;
    }

    // Регрессия, найденная бенчмарком: полосы x и y диагонального шага
    // касались в углу, сливались в прямоугольник на всю текстуру, и каждый
    // такой шаг выгружал её целиком.
    [Test]
    public void DiagonalStep_DoesNotMergeBandsIntoWholeTexture()
    {
        TerrainDirtyRegion region = Fresh();
        region.MarkCells(Width - 2, 0, 2, Height);
        region.MarkCells(0, Height - 2, Width, 2);

        Assert.That(region.IsAll, Is.False);
        Assert.That(region.Count, Is.EqualTo(2));
        Assert.That(region.Area, Is.LessThan((long)Width * Height * 2 / 10));
    }

    [Test]
    public void BandOnRingSeam_IsSplitAndCoversExactlyTheMarkedCells()
    {
        TerrainDirtyRegion region = Fresh();
        region.MarkCells(Width - 1, Height - 1, 3, 2);

        Assert.That(region.Area, Is.EqualTo(3L * 2 * TerrainCellDataPacker.LayersPerCell));
        foreach ((int x, int y) in new[] { (Width - 1, Height - 1), (0, Height - 1), (1, 0), (Width - 1, 0) })
        {
            Assert.That(Covers(region, x, y), Is.True, $"cell {x},{y}");
        }
    }

    [Test]
    public void ScatteredPatches_DoNotInflateAreaAndCoverEveryMarkedCell([Values(1, 7, 42, 1337)] int seed)
    {
        TerrainDirtyRegion region = Fresh();
        var random = new Random(seed);
        var marked = new List<(int X, int Y)>();
        for (int i = 0; i < 60; i++)
        {
            int x = random.Next(Width);
            int y = random.Next(Height);
            marked.Add((x, y));
            region.MarkCells(x, y, 1, 1);
        }

        Assert.That(region.Area, Is.LessThanOrEqualTo(60L * TerrainCellDataPacker.LayersPerCell));

        foreach ((int x, int y) in marked)
        {
            Assert.That(Covers(region, x, y), Is.True, $"cell {x},{y}");
        }
    }

    [Test]
    public void LocalDigging_StaysSmall()
    {
        TerrainDirtyRegion region = Fresh();
        var random = new Random(3);
        for (int i = 0; i < 20; i++)
        {
            region.MarkCells(90 + random.Next(8), 60 + random.Next(8), 1, 1);
        }

        Assert.That(region.Area, Is.LessThanOrEqualTo(8L * 8 * TerrainCellDataPacker.LayersPerCell));
    }

    [Test]
    public void MarkAll_IgnoresFurtherRectsUntilCleared()
    {
        TerrainDirtyRegion region = Fresh();
        region.MarkAll();
        region.MarkCells(3, 3, 1, 1);

        Assert.That(region.IsAll, Is.True);
        Assert.That(region.Count, Is.Zero);

        region.Clear();
        Assert.That(region.IsEmpty, Is.True);
    }
}

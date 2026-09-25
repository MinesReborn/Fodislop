#nullable enable

using System.Collections.Generic;
using Kern.World.Terrain;
using Kern.World.Terrain.Background;
using MinesServer.Data;
using NUnit.Framework;

namespace Kern.Tests.World;

// Полная сборка клеток идёт через Parallel.For. Значит, её результат обязан
// не зависеть ни от числа потоков, ни от того, какой поток успел первым.
//
// До прогрева метаданных это было не так: FillCell по дороге разрешал тип
// клетки, то есть писал общую структуру и дозаказывал текстуру прямо из
// рабочего потока. Эти тесты — граница, за которую такое не должно вернуться.
[TestFixture]
public sealed class TerrainCellBuilderDeterminismTests
{
    private const int Width = 48;
    private const int Height = 32;
    private const int OriginX = 96;
    private const int OriginY = 64;

    [Test]
    public void DoorAtlasChangeInvalidatesOverlayWithoutChangingVertices()
    {
        var index = new TerrainDoorOverlayIndex();
        index.EnsureSize(1, 1);
        var vertices = new TerrainVertex[4];
        index.BeginFullBuild();
        index.RecordCell(0, 0, 0, true, vertices);
        index.CompleteFullBuild();

        Assert.That(index.RecordCell(0, 0, 1, true, vertices), Is.True);
        Assert.That(index.RecordCell(0, 0, 1, true, vertices), Is.False);
    }

    [Test]
    public void RoadTextureArrivalUpdatesBackgroundUnderEveryDoor()
    {
        var world = new TerrainTestWorld { RoadTextureReady = false };
        var cache = new TerrainCellCache();
        TerrainCellSources sources = world.BuildSources(
            cache, new TerrainPrecalculator(), new BackgroundFloodFill(),
            OriginX, OriginY, Width, Height);
        using var builder = new TerrainCellBuilder();
        builder.EnsureCapacity(Width, Height, 1f);
        builder.BuildFull(sources, OriginX, OriginY);
        Assert.That(builder.HasDoors, Is.True);

        world.RoadTextureReady = true;
        HashSet<CellType> changedTypes = [CellType.Road];
        cache.RefreshTextureMetadata(changedTypes, world.MapData, world.Textures, world.Atlases);
        builder.BuildTextureCells(changedTypes, sources, OriginX, OriginY);

        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                if (cache.GetCellData(x + 1, y + 1).Type != CellType.BuildingDoor)
                {
                    continue;
                }

                TerrainCellTexels background = builder.Textures.GetCell(
                    TerrainCellDataTextures.Ring(OriginX + x, Width),
                    TerrainCellDataTextures.Ring(OriginY + y, Height),
                    TerrainCellDataPacker.BackgroundLayer);
                Assert.That(background.AtlasRect.B, Is.Not.Zero, $"Road under door ({x},{y})");
            }
        }
    }

    [Test]
    public void BuildTextureCells_LoadedDoorTexture_InvalidatesOverlayOnce()
    {
        var world = new TerrainTestWorld { DoorTextureReady = false };
        var cache = new TerrainCellCache();
        TerrainCellSources sources = world.BuildSources(
            cache, new TerrainPrecalculator(), new BackgroundFloodFill(),
            OriginX, OriginY, Width, Height);
        using var builder = new TerrainCellBuilder();
        builder.EnsureCapacity(Width, Height, 1f);
        builder.BuildFull(sources, OriginX, OriginY);
        Assert.That(builder.HasDoors, Is.True, "Fixture must include doors.");

        var vertices = new List<TerrainVertex>();
        List<int>[] indices = [new List<int>()];
        builder.BuildDoorOverlay(sources, OriginX, OriginY, vertices, indices);
        Assert.That(vertices, Is.Not.Empty);
        Assert.That(vertices.TrueForAll(vertex => vertex.UV1z == 0), Is.True);

        world.DoorTextureReady = true;
        HashSet<CellType> changedTypes = [CellType.BuildingDoor];
        cache.RefreshTextureMetadata(changedTypes, world.MapData, world.Textures, world.Atlases);
        builder.BuildTextureCells(changedTypes, sources, OriginX, OriginY);

        Assert.That(builder.DoorsTouched, Is.True,
            "The driver must rebuild the overlay after its missing texture arrives.");
        builder.BuildDoorOverlay(sources, OriginX, OriginY, vertices, indices);
        Assert.That(vertices.TrueForAll(vertex => vertex.UV1z != 0), Is.True);

        builder.BuildTextureCells(changedTypes, sources, OriginX, OriginY);
        Assert.That(builder.DoorsTouched, Is.False,
            "Unchanged door data must not rebuild the overlay repeatedly.");
    }

    [Test]
    public void BuildFull_RepeatedOnSameWindow_ProducesIdenticalTexels()
    {
        List<TerrainCellTexels> first = BuildAndSnapshot(builder =>
            builder.BuildFull(CreateSources(), OriginX, OriginY));
        List<TerrainCellTexels> second = BuildAndSnapshot(builder =>
            builder.BuildFull(CreateSources(), OriginX, OriginY));

        AssertTexelsEqual(first, second, "повторная полная сборка");
    }

    // Последовательная сборка того же окна — независимый оракул: она не
    // касается планировщика вовсе. Расхождение с ней означает гонку.
    [Test]
    public void BuildFull_MatchesSequentialRegionBuild()
    {
        List<TerrainCellTexels> parallel = BuildAndSnapshot(builder =>
            builder.BuildFull(CreateSources(), OriginX, OriginY));
        List<TerrainCellTexels> sequential = BuildAndSnapshot(builder =>
        {
            TerrainCellSources sources = CreateSources();
            // A one-column range has only one Parallel.For iteration, so
            // independent columns cannot race in this reference build.
            for (int x = 0; x < Width; x++)
            {
                builder.BuildRegion(sources, OriginX, OriginY, x, 0, 1, Height);
            }
        });

        AssertTexelsEqual(parallel, sequential, "полная против последовательной");
    }

    [Test]
    public void BuildRegion_ParallelThresholdMatchesSerialColumns()
    {
        const int width = 96;
        const int height = 64;
        const int originX = 96;
        const int originY = 64;

        List<TerrainCellTexels> parallel = BuildAndSnapshot(
            width,
            height,
            originX,
            originY,
            builder => builder.BuildRegion(
                CreateSources(width, height, originX, originY),
                originX,
                originY,
                0,
                0,
                width,
                height));
        List<TerrainCellTexels> serial = BuildAndSnapshot(
            width,
            height,
            originX,
            originY,
            builder =>
            {
                TerrainCellSources sources = CreateSources(width, height, originX, originY);
                for (int x = 0; x < width; x++)
                {
                    builder.BuildRegion(sources, originX, originY, x, 0, 1, height);
                }
            });

        AssertTexelsEqual(parallel, serial, "parallel BuildRegion против последовательных колонок");
    }

    private static TerrainCellSources CreateSources()
    {
        return CreateSources(Width, Height, OriginX, OriginY);
    }

    private static TerrainCellSources CreateSources(int width, int height, int originX, int originY)
    {
        var world = new TerrainTestWorld();
        return world.BuildSources(
            new TerrainCellCache(),
            new TerrainPrecalculator(),
            new BackgroundFloodFill(),
            originX,
            originY,
            width,
            height);
    }

    private static List<TerrainCellTexels> BuildAndSnapshot(
        System.Action<TerrainCellBuilder> build)
        => BuildAndSnapshot(build, Width, Height, OriginX, OriginY);

    private static List<TerrainCellTexels> BuildAndSnapshot(
        int width,
        int height,
        int originX,
        int originY,
        System.Action<TerrainCellBuilder> build) =>
        BuildAndSnapshot(build, width, height, originX, originY);

    private static List<TerrainCellTexels> BuildAndSnapshot(
        System.Action<TerrainCellBuilder> build,
        int width,
        int height,
        int originX,
        int originY)
    {
        using var builder = new TerrainCellBuilder();
        builder.EnsureCapacity(width, height, 1f);
        build(builder);

        var snapshot = new List<TerrainCellTexels>(
            width * height * TerrainCellDataPacker.LayersPerCell);
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int ringX = TerrainCellDataTextures.Ring(originX + x, width);
                int ringY = TerrainCellDataTextures.Ring(originY + y, height);
                snapshot.Add(builder.Textures.GetCell(
                    ringX, ringY, TerrainCellDataPacker.BackgroundLayer));
                snapshot.Add(builder.Textures.GetCell(
                    ringX, ringY, TerrainCellDataPacker.ForegroundLayer));
            }
        }

        return snapshot;
    }

    private static void AssertTexelsEqual(
        List<TerrainCellTexels> expected,
        List<TerrainCellTexels> actual,
        string what)
    {
        Assert.That(actual.Count, Is.EqualTo(expected.Count), what);
        for (int index = 0; index < expected.Count; index++)
        {
            Assert.That(
                actual[index],
                Is.EqualTo(expected[index]),
                $"{what}: тексель {index} (клетка {index / 2}, слой {index % 2})");
        }
    }
}

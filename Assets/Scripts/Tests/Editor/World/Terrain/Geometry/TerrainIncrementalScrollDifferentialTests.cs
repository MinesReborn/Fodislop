#nullable enable

using Kern.World.Terrain;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World;

// Сдвиг окна переносит перекрытие даром и пересчитывает только вошедшие
// полосы. Это верно ровно до тех пор, пока полосы посчитаны правильно, а
// проверить это было нечем: инкрементальный путь ни разу не сверялся с полным.
//
// Здесь окно едет последовательностью шагов, а потом то же самое окно
// собирается с нуля. Кэш клеток, маски соседства и узлы искажения — чистые
// функции содержимого окна, поэтому обязаны совпасть до значения.
//
// Заливка фона сюда НЕ входит и совпадать не обязана: её инкрементальный шаг
// намеренно сохраняет уже разрешённую внутренность вместо того, чтобы
// перезаливать связную породу заново (см. BackgroundFloodFillScrollTests).
// Это выбор, а не расхождение, и оракулом полной заливки он не проверяется.
[TestFixture]
public sealed class TerrainIncrementalScrollDifferentialTests
{
    private const int Width = 40;
    private const int Height = 28;
    private const int StartX = 96;
    private const int StartY = 80;

    // Vector2Int, а не собственный record struct: сборке тестов недоступен
    // System.Runtime.CompilerServices.IsExternalInit, без которого позиционный
    // record не компилируется.
    private static readonly Vector2Int[] _Walk =
    [
        new(1, 0), new(0, 1), new(1, 1), new(-1, 0), new(0, -1),
        new(3, 2), new(-2, -3), new(5, 0), new(0, 7), new(-4, 6),
    ];

    [Test]
    public void IncrementalScroll_MatchesFullRebuild()
    {
        var world = new TerrainTestWorld();

        var incrementalCache = new TerrainCellCache();
        var incrementalPrecalc = new TerrainPrecalculator();
        incrementalCache.EnsureCapacity(Width, Height);
        incrementalPrecalc.EnsureCapacity(Width, Height);
        incrementalCache.PopulateFull(
            StartX, StartY, world.Storage, world.MapData, world.Textures, world.Atlases);
        var incrementalInput = new TerrainPrecalculationInput(
            incrementalCache,
            new Vector2Int(Width, Height),
            new Vector2Int(TerrainTestWorld.WorldWidth, TerrainTestWorld.WorldHeight));
        incrementalPrecalc.PrecalculateFull(incrementalInput);

        int originX = StartX;
        int originY = StartY;
        foreach (Vector2Int step in _Walk)
        {
            originX += step.x;
            originY += step.y;
            incrementalCache.ScrollAndFill(
                step.x, step.y, world.Storage, world.MapData, world.Textures, world.Atlases);
            incrementalPrecalc.PrecalculateIncremental(incrementalInput, step);
        }

        var fullCache = new TerrainCellCache();
        var fullPrecalc = new TerrainPrecalculator();
        fullCache.EnsureCapacity(Width, Height);
        fullPrecalc.EnsureCapacity(Width, Height);
        fullCache.PopulateFull(
            originX, originY, world.Storage, world.MapData, world.Textures, world.Atlases);
        fullPrecalc.PrecalculateFull(new TerrainPrecalculationInput(
            fullCache,
            new Vector2Int(Width, Height),
            new Vector2Int(TerrainTestWorld.WorldWidth, TerrainTestWorld.WorldHeight)));

        Assert.That(incrementalCache.CacheMinX, Is.EqualTo(fullCache.CacheMinX));
        Assert.That(incrementalCache.CacheMinY, Is.EqualTo(fullCache.CacheMinY));

        // Кэш держит кайму в одну клетку вокруг окна: она тоже обязана совпасть,
        // иначе маски на краю посчитаны по чужим соседям.
        for (int x = 0; x < Width + 2; x++)
        {
            for (int y = 0; y < Height + 2; y++)
            {
                Assert.That(
                    incrementalCache.GetCellData(x, y).Type,
                    Is.EqualTo(fullCache.GetCellData(x, y).Type),
                    $"тип клетки кэша {x},{y}");
            }
        }

        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                Assert.That(
                    incrementalPrecalc.CellTilingDescriptors[x, y],
                    Is.EqualTo(fullPrecalc.CellTilingDescriptors[x, y]),
                    $"тайлинг {x},{y}");
                Assert.That(
                    incrementalPrecalc.CellCornerVariants[x, y],
                    Is.EqualTo(fullPrecalc.CellCornerVariants[x, y]),
                    $"вариант угла {x},{y}");
                Assert.That(
                    incrementalPrecalc.CellReliefMasks[x, y],
                    Is.EqualTo(fullPrecalc.CellReliefMasks[x, y]),
                    $"рельефная маска {x},{y}");
                Assert.That(
                    incrementalPrecalc.CellSolidBoundaryMasks[x, y],
                    Is.EqualTo(fullPrecalc.CellSolidBoundaryMasks[x, y]),
                    $"маска твёрдости {x},{y}");
            }
        }

        for (int x = 0; x < Width + 1; x++)
        {
            for (int y = 0; y < Height + 1; y++)
            {
                Assert.That(
                    incrementalPrecalc.GridVertexOffsets[x, y],
                    Is.EqualTo(fullPrecalc.GridVertexOffsets[x, y]),
                    $"смещение узла {x},{y}");
            }
        }
    }
}

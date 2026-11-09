#nullable enable

using Kern.World.Terrain;
using NUnit.Framework;
using UnityEngine;

using Kern.Networking.Dummy;

namespace Kern.Tests.World;

// Сдвиг окна переносит перекрытие даром и пересчитывает только вошедшие
// полосы. Это верно ровно до тех пор, пока полосы посчитаны правильно, а
// проверить это было нечем: инкрементальный путь ни разу не сверялся с полным.
//
// Здесь окно едет последовательностью шагов, а потом то же самое окно
// собирается с нуля. Кэш клеток — чистая функция содержимого окна, поэтому
// обязан совпасть до значения.
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
    private static readonly Vector2Int[] s_walk = new Vector2Int[]
    {

        new(1, 0), new(0, 1), new(1, 1), new(-1, 0), new(0, -1),
        new(3, 2), new(-2, -3), new(5, 0), new(0, 7), new(-4, 6),

    };

    [Test]
    public void IncrementalScroll_MatchesFullRebuild()
    {
        var world = new TerrainTestWorld();

        var incrementalCache = new TerrainCellCache();
        incrementalCache.EnsureCapacity(Width, Height);
        incrementalCache.PopulateFull(
            StartX, StartY, world.Storage, world.MapData, world.Textures, world.Atlases);

        int originX = StartX;
        int originY = StartY;
        foreach (Vector2Int step in s_walk)
        {
            originX += step.x;
            originY += step.y;
            incrementalCache.ScrollAndFill(
                step.x, step.y, world.Storage, world.MapData, world.Textures, world.Atlases);
        }

        var fullCache = new TerrainCellCache();
        fullCache.EnsureCapacity(Width, Height);
        fullCache.PopulateFull(
            originX, originY, world.Storage, world.MapData, world.Textures, world.Atlases);

        Assert.That(incrementalCache.CacheMinX, Is.EqualTo(fullCache.CacheMinX));
        Assert.That(incrementalCache.CacheMinY, Is.EqualTo(fullCache.CacheMinY));

        // Кэш держит кайму в одну клетку вокруг окна: она тоже обязана совпасть,
        // иначе кайма буфера клеток на краю собрана по чужим соседям.
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
    }
}

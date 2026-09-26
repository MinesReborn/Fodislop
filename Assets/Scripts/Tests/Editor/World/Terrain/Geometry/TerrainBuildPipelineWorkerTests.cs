#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kern.Core;
using Kern.World.Terrain;
using MinesServer.Data;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World;

// Фоновая сборка террейна делится по потокам: кэш клеток заполняется на
// главном потоке (Prepare), всё остальное — на рабочем (Execute). Здесь
// production-конвейер проходит ту же последовательность шагов, что в игре,
// причём Execute идёт на настоящем рабочем потоке, а итог сверяется с
// независимой полной сборкой того же конечного окна.
//
// Сверяются кэш с каймой, маски соседства и узлы искажения — чистые функции
// содержимого окна. Заливка фона и зависящий от неё фоновый слой текселей
// намеренно не сверяются: инкрементальная заливка сохраняет уже разрешённую
// внутренность (см. TerrainIncrementalScrollDifferentialTests).
[TestFixture]
public sealed class TerrainBuildPipelineWorkerTests
{
    private const int Width = 40;
    private const int Height = 28;
    private const int StartX = 96;
    private const int StartY = 80;

    private static readonly Vector2Int[] _Walk =
    [
        new(1, 0), new(0, 1), new(1, 1), new(-1, 0), new(0, -1),
        new(3, 2), new(-2, -3), new(5, 0), new(0, 7), new(-4, 6),
    ];

    [Test]
    public void WorkerSteps_WithScrollPatchAndTextureRefresh_MatchFullBuild()
    {
        var world = new TerrainTestWorld { DoorTextureReady = false };
        using var telemetry = new FrameTelemetry();
        using var incremental = new TerrainBuildPipeline();
        incremental.EnsureCapacity(Width, Height, 1f);
        var noTextures = new HashSet<CellType>();
        var dirty = new DirtyRectSet();

        var origin = new Vector2Int(StartX, StartY);
        RunOnWorker(incremental, incremental.Prepare(
            Context(world, telemetry), world.Atlases, origin,
            forceFull: true, rebuildAllCells: false,
            dirty, noTextures, contentRevision: 1, worldGeneration: 0));

        ulong revision = 1;
        for (int index = 0; index < _Walk.Length; index++)
        {
            origin += _Walk[index];
            dirty.Clear();

            // Каждый третий шаг несёт заплатку: мир детерминирован, поэтому
            // перечитанные клетки те же, но путь заплатки после сдвига
            // проходится целиком на рабочем потоке.
            if (index % 3 == 0)
            {
                dirty.Add(
                    new RectInt(origin.x + 5, origin.y + 4, 3, 2),
                    new RectInt(origin.x, origin.y, Width, Height));
            }

            // Посреди прогулки приезжает текстура двери: метаданные типа
            // обязаны быть разрешены до старта рабочего потока, иначе прогрев
            // в режиме проверки роняет шаг.
            HashSet<CellType> textures = noTextures;
            if (index == 4)
            {
                world.DoorTextureReady = true;
                textures = new HashSet<CellType> { CellType.BuildingDoor };
            }

            TerrainCpuBuildRequest request = incremental.Prepare(
                Context(world, telemetry), world.Atlases, origin,
                forceFull: false, rebuildAllCells: false,
                dirty, textures, ++revision, worldGeneration: 0);
            Assert.That(request.BuildFull, Is.False, $"шаг {index} обязан идти приращением");
            RunOnWorker(incremental, request);
        }

        using var full = new TerrainBuildPipeline();
        full.EnsureCapacity(Width, Height, 1f);
        RunOnWorker(full, full.Prepare(
            Context(world, telemetry), world.Atlases, origin,
            forceFull: true, rebuildAllCells: false,
            new DirtyRectSet(), noTextures, contentRevision: 1, worldGeneration: 0));

        AssertCachesEqual(incremental.CellCache, full.CellCache);
        AssertPrecalculationEqual(incremental.Precalculator, full.Precalculator);
        Assert.That(
            incremental.CellCache.TryGet(CellType.BuildingDoor, out CellMetadata door) &&
            door.IsTextureReady,
            Is.True,
            "приехавшая текстура двери обязана попасть в метаданные");
    }

    [Test]
    public void WorkerStep_ThrowsWhenBackgroundTypeWasNotResolvedOnMainThread()
    {
        var world = new TerrainTestWorld();
        using var telemetry = new FrameTelemetry();
        using var pipeline = new TerrainBuildPipeline();
        pipeline.EnsureCapacity(Width, Height, 1f);
        TerrainCpuBuildRequest request = pipeline.Prepare(
            Context(world, telemetry), world.Atlases, new Vector2Int(StartX, StartY), forceFull: true,
            rebuildAllCells: false, new DirtyRectSet(), new HashSet<CellType>(),
            contentRevision: 1, worldGeneration: 0);

        // Сброс метаданных после подготовки — ровно то, чего главный поток
        // делать не имеет права, пока шаг идёт. Рабочий поток обязан назвать
        // дефект, а не нарисовать подделку.
        pipeline.CellCache.ClearCaches();
        Assert.Throws<InvalidOperationException>(
            () => pipeline.Execute(request, CancellationToken.None));
    }

    private static TerrainBuildContext Context(TerrainTestWorld world, IFrameTelemetry telemetry) =>
        new(world.Storage, world.MapData, world.Textures, telemetry, Width, Height);

    private static void RunOnWorker(TerrainBuildPipeline pipeline, TerrainCpuBuildRequest request)
    {
        int mainThread = Environment.CurrentManagedThreadId;
        int workerThread = mainThread;
        Task<TerrainCpuBuildResult> task = Task.Run(() =>
        {
            workerThread = Environment.CurrentManagedThreadId;
            return pipeline.Execute(request, CancellationToken.None);
        });
        Assert.That(task.Wait(TimeSpan.FromSeconds(10)), Is.True, "шаг не завершился");
        Assert.That(workerThread, Is.Not.EqualTo(mainThread));
        pipeline.RecordPublished(request, task.Result, latencyMs: 0f);
    }

    private static void AssertPrecalculationEqual(
        TerrainPrecalculator actual,
        TerrainPrecalculator expected)
    {
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                Assert.That(
                    actual.CellTilingDescriptors[x, y],
                    Is.EqualTo(expected.CellTilingDescriptors[x, y]),
                    $"тайлинг {x},{y}");
                Assert.That(
                    actual.CellCornerVariants[x, y],
                    Is.EqualTo(expected.CellCornerVariants[x, y]),
                    $"вариант угла {x},{y}");
                Assert.That(
                    actual.CellReliefMasks[x, y],
                    Is.EqualTo(expected.CellReliefMasks[x, y]),
                    $"рельефная маска {x},{y}");
                Assert.That(
                    actual.CellSolidBoundaryMasks[x, y],
                    Is.EqualTo(expected.CellSolidBoundaryMasks[x, y]),
                    $"маска твёрдости {x},{y}");
            }
        }

        for (int x = 0; x < Width + 1; x++)
        {
            for (int y = 0; y < Height + 1; y++)
            {
                Assert.That(
                    actual.GridVertexOffsets[x, y],
                    Is.EqualTo(expected.GridVertexOffsets[x, y]),
                    $"смещение узла {x},{y}");
            }
        }
    }

    private static void AssertCachesEqual(TerrainCellCache actual, TerrainCellCache expected)
    {
        Assert.That(actual.CacheMinX, Is.EqualTo(expected.CacheMinX));
        Assert.That(actual.CacheMinY, Is.EqualTo(expected.CacheMinY));
        for (int x = 0; x < Width + 2; x++)
        {
            for (int y = 0; y < Height + 2; y++)
            {
                CachedCellData actualCell = actual.GetCellData(x, y);
                CachedCellData expectedCell = expected.GetCellData(x, y);
                Assert.That(actualCell.Type, Is.EqualTo(expectedCell.Type), $"тип клетки кэша {x},{y}");
                Assert.That(
                    actualCell.IsTextureReady,
                    Is.EqualTo(expectedCell.IsTextureReady),
                    $"готовность текстуры {x},{y}");
                Assert.That(actualCell.AtlasRect, Is.EqualTo(expectedCell.AtlasRect), $"rect {x},{y}");
            }
        }
    }
}

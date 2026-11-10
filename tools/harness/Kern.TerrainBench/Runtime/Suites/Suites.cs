#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.World;
using Kern.World.Terrain;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Kern.TerrainBench;

// Наборы замеров. Везде, где можно, вызывается настоящий код игры; старый
// путь буфера вершин оставлен только там, где он удалён из игры, — для
// сравнения порядков.
public static class Suites
{
    private const int Seed = 1337;

    private sealed class ResidencyLayer(int chunkSize, int heightChunks) : IWorldLayer<CellType>
    {
        private readonly HashSet<int> _resident = [];
        private readonly Dictionary<int, LinkedListNode<int>> _nodes = [];
        private readonly LinkedList<int> _lru = [];

        public int ChunkSize { get; } = chunkSize;

        public int HeightChunks { get; } = heightChunks;

        public int ReadCount { get; private set; }

        public int TouchCount { get; private set; }

        public void AddResident(int chunkIndex)
        {
            _resident.Add(chunkIndex);
            LinkedListNode<int> node = _lru.AddFirst(chunkIndex);
            _nodes.Add(chunkIndex, node);
        }

        public void ResetCounters()
        {
            ReadCount = 0;
            TouchCount = 0;
        }

        public ChunkReadResult<CellType> ReadChunk(int chunkIndex, bool touchLRU = true)
        {
            ReadCount++;
            if (!_resident.Contains(chunkIndex))
            {
                return new ChunkReadResult<CellType>(ChunkReadStatus.Missing, null, null);
            }

            if (touchLRU)
            {
                LinkedListNode<int> node = _nodes[chunkIndex];
                _lru.Remove(node);
                _lru.AddFirst(node);
                TouchCount++;
            }

            return new ChunkReadResult<CellType>(ChunkReadStatus.Available, null, null);
        }
    }

    private sealed class ResidencyStorage(ResidencyLayer layer) : IWorldDataStorage
    {
        public IWorldLayer<CellType>? CellLayer { get; } = layer;

        public string GetWorldCodeName() => "terrain-bench";
    }

    private sealed class ResidencyMap(ushort width, ushort height) : IMapDataProvider
    {
        public ushort WorldWidth { get; } = width;

        public ushort WorldHeight { get; } = height;
    }

    private sealed class BenchAtlasDescriptor(int size) : IAtlasDescriptor
    {
        public int Size { get; } = size;

        public bool IsFullyOpaque(CellType cellType) => true;
    }

    public static void All(BenchRunner runner, int width, int height)
    {
        Scroll(runner, width, height);
        Pack(runner, width, height);
        Upload(runner, width, height);
        DirtyRegion(runner, width, height);
        DirtyRects(runner, width, height);
        CacheScroll(runner, width, height);
        ResidencyProbe(runner, width, height);
        QuadCatalogs(runner, width, height);
        Spatial(runner, width, height);
        SessionPipeline(runner, width, height);
        GlobalFrame(runner, width, height);
        TimingDiagnostics(runner);
    }

    // ── Глобальный кадр production terrain pipeline ──────────────────────
    //
    // Один sample — это не отдельная функция, а последовательность кадра:
    // residency probe → сдвиг TerrainCellCache → маски → distortion →
    // flood-fill → упаковка полос → копирование dirty-областей. Каждый десятый
    // кадр дополнительно содержит patch после копания. Это CPU-часть полного
    // terrain transport path; Unity GPU dispatch/upload здесь недоступны.
    private static void GlobalFrame(BenchRunner runner, int width, int height)
    {
        runner.Suite = "global";
        if (!runner.Wants("глобальный кадр"))
        {
            return;
        }

        const int Steps = 600;
        const int MinX = 500;
        const int MinY = 700;
        const int WorldSize = 4096;
        const int ChunkSize = 16;
        const int HeightChunks = WorldSize / ChunkSize;

        var layer = new ResidencyLayer(ChunkSize, HeightChunks);
        for (int chunkX = 0; chunkX < WorldSize / ChunkSize; chunkX++)
        {
            for (int chunkY = 0; chunkY < HeightChunks; chunkY++)
            {
                layer.AddResident(chunkY + (chunkX * HeightChunks));
            }
        }

        var storage = new ResidencyStorage(layer);
        var map = new ResidencyMap(WorldSize, WorldSize);
        var residencyCache = new TerrainResidencyProbe.FrameCache();
        var cache = new TerrainCellCache();
        cache.FillCaves(width, height, MinX, MinY, Seed);
        TerrainVertex[] vertices = SyntheticVertices(width, height);
        var texels = new TexelArrays(width, height);
        var dirty = new TerrainDirtyRegion();
        dirty.Reset(width, height);
        dirty.Clear();
        var random = new Random(Seed);
        Vector2Int position = new(MinX, MinY);
        var samples = new List<double>(Steps);
        var cpuSamples = new List<double>(Steps);
        double[] stageTotals = new double[4];
        long uploadedTexels = 0;
        int fullRebuilds = 0;
        int patches = 0;
        int residencyReads = 0;
        int residencyTouches = 0;
        using Process currentProcess = Process.GetCurrentProcess();

        for (int step = 0; step < Steps; step++)
        {
            int dx;
            int dy;
            do
            {
                dx = random.Next(-1, 2);
                dy = random.Next(-1, 2);
            }
            while (dx == 0 && dy == 0);

            position += new Vector2Int(dx, dy);
            residencyCache.BeginFrame(storage, map, width, height);
            long start = Stopwatch.GetTimestamp();
            long cpuStart = currentProcess.TotalProcessorTime.Ticks;

            bool resident = TerrainResidencyProbe.IsWindowResident(
                storage,
                map,
                connectionService: null,
                position,
                width,
                height,
                residencyCache);
            if (!resident)
            {
                throw new InvalidOperationException("Global benchmark fixture lost terrain residency.");
            }

            long stage = Lap(stageTotals, 0, start);
            cache.ScrollTo(position.x, position.y, Seed);
            stage = Lap(stageTotals, 1, stage);

            TerrainScrollBands bands = TerrainScrollBands.Resolve(width, height, dx, dy, neighbourMargin: 1);
            PackRect(texels, vertices, dirty, bands.ColumnBand.xMin, bands.ColumnBand.xMax,
                bands.ColumnBand.yMin, bands.ColumnBand.yMax, position.x, position.y, width, height);
            PackRect(texels, vertices, dirty, bands.RowBand.xMin, bands.RowBand.xMax,
                bands.RowBand.yMin, bands.RowBand.yMax, position.x, position.y, width, height);
            stage = Lap(stageTotals, 2, stage);
            uploadedTexels += CopyDirty(texels, dirty, width, height);
            stage = Lap(stageTotals, 3, stage);

            if (step % 10 == 9)
            {
                int cx = 10 + random.Next(width - 20);
                int cy = 10 + random.Next(height - 20);
                cache.Dig(cx, cy, 3, 3);
                PackRect(texels, vertices, dirty, cx - 1, cx + 4, cy - 1, cy + 4,
                    position.x, position.y, width, height);
                uploadedTexels += CopyDirty(texels, dirty, width, height);
                patches++;
            }

            bool fullRebuild = false;
            if (step == Steps / 2)
            {
                // В середине сессии измеряем редкий cold path в том же общем
                // сценарии, чтобы p95/max не скрывали цену полного rebuild.
                cache.FillCaves(width, height, position.x, position.y, Seed);
                PackRect(texels, vertices, dirty, 0, width, 0, height,
                    position.x, position.y, width, height);
                uploadedTexels += CopyDirty(texels, dirty, width, height);
                fullRebuilds++;
                fullRebuild = true;
            }

            long now = Stopwatch.GetTimestamp();
            samples.Add(Stopwatch.GetElapsedTime(start, now).TotalMilliseconds);
            cpuSamples.Add((currentProcess.TotalProcessorTime.Ticks - cpuStart) /
                (double)TimeSpan.TicksPerMillisecond);
            GC.KeepAlive(fullRebuild);
            residencyReads += residencyCache.ChunkReads;
            residencyTouches += residencyCache.LRUTouches;
        }

        long allocated = 0;
        runner.Record("глобальный кадр: terrain transport", samples, allocated, 0, cpuSamples);
        runner.Metric("глобальный кадр: средняя стадия residency probe",
            stageTotals[0] / Steps, "мс");
        runner.Metric("глобальный кадр: средняя стадия cache",
            stageTotals[1] / Steps, "мс");
        runner.Metric("глобальный кадр: средняя стадия pack/upload",
            (stageTotals[2] + stageTotals[3]) / Steps, "мс");
        runner.Metric("глобальный кадр: полных rebuild",
            fullRebuilds, "кадров");
        runner.Metric("глобальный кадр: patch после копания",
            patches, "кадров");
        runner.Metric("глобальный кадр: ReadChunk",
            (double)residencyReads / Steps, "вызовов/кадр");
        runner.Metric("глобальный кадр: LRU touch",
            (double)residencyTouches / Steps, "вызовов/кадр");
        runner.Metric("глобальный кадр: выгружено текселей",
            (double)uploadedTexels / Steps, "текселей/кадр");
    }

    private static void ResidencyProbe(BenchRunner runner, int windowWidth, int windowHeight)
    {
        runner.Suite = "residency";
        if (!runner.Wants("полные пробы каждого кандидата"))
        {
            return;
        }

        StableResidencyProbe(runner, windowWidth, windowHeight);

        const int WorldSize = 4096;
        const int ChunkSize = 16;
        const int HeightChunks = WorldSize / ChunkSize;
        var layer = new ResidencyLayer(ChunkSize, HeightChunks);
        int residentChunkCount = ((500 + windowWidth) / ChunkSize) + 1;
        for (int chunkX = 0; chunkX < residentChunkCount; chunkX++)
        {
            for (int chunkY = 0; chunkY < HeightChunks; chunkY++)
            {
                layer.AddResident(chunkY + (chunkX * HeightChunks));
            }
        }

        var storage = new ResidencyStorage(layer);
        var map = new ResidencyMap(WorldSize, WorldSize);
        var cache = new TerrainResidencyProbe.FrameCache();
        Vector2Int committed = new(500, 500);
        Vector2Int requested = new(520, 500);
        int uncachedReads = 0;
        int cachedReads = 0;
        int cacheHits = 0;
        int cachedUniqueReads = 0;
        int cachedTouches = 0;
        Vector2Int uncachedTarget = default;
        Vector2Int cachedTarget = default;

        void RunUncached()
        {
            layer.ResetCounters();
            bool resident = TerrainResidencyProbe.IsWindowResident(
                storage, map, null, requested, windowWidth, windowHeight);
            uncachedTarget = resident
                ? requested
                : TerrainWindowAdvance.Resolve(
                    committed,
                    requested,
                    windowWidth,
                    windowHeight,
                    position => TerrainResidencyProbe.IsWindowResident(
                        storage, map, position, windowWidth, windowHeight));
            uncachedReads = layer.ReadCount;
        }

        void RunCached()
        {
            layer.ResetCounters();
            bool resident = TerrainResidencyProbe.IsWindowResident(
                storage, map, null, requested, windowWidth, windowHeight);
            cachedTarget = resident
                ? requested
                : ResolveCached();

            if (cachedTarget != committed)
            {
                TerrainResidencyProbe.TouchWindow(
                    storage,
                    map,
                    cachedTarget,
                    windowWidth,
                    windowHeight,
                    cache);
            }

            cachedReads = layer.ReadCount;
            cacheHits = cache.CacheHits;
            cachedUniqueReads = cache.ChunkReads;
            cachedTouches = layer.TouchCount;

            Vector2Int ResolveCached()
            {
                cache.BeginFrame(storage, map, windowWidth, windowHeight);
                return TerrainWindowAdvance.Resolve(
                    committed,
                    requested,
                    windowWidth,
                    windowHeight,
                    cache.ResidencyCallback);
            }
        }

        RunUncached();
        RunCached();
        if (uncachedTarget != cachedTarget)
        {
            throw new InvalidOperationException("Cached terrain residency changed the selected window origin.");
        }

        runner.Metric("расхождение выбранного окна для cached probe", 0, "клеток");
        runner.RunAlternating(
            "полные пробы каждого кандидата",
            RunUncached,
            "чтение статуса чанка один раз за план",
            RunCached);
        runner.Metric("ReadChunk без кэша за выбор окна", uncachedReads, "вызовов");
        runner.Metric("ReadChunk с кэшем за выбор окна", cachedReads, "вызовов");
        runner.Metric("уникальные статусы с кэшем", cachedUniqueReads, "чанков");
        runner.Metric("LRU touch для выбранного окна", cachedTouches, "вызовов");
        runner.Metric("повторные чтения чанков устранены", cacheHits, "вызовов");
    }

    private static void StableResidencyProbe(BenchRunner runner, int windowWidth, int windowHeight)
    {
        const int WorldSize = 4096;
        const int ChunkSize = WorldGridDefaults.ChunkSize;
        const int HeightChunks = WorldSize / ChunkSize;
        var layer = new ResidencyLayer(ChunkSize, HeightChunks);
        int residentChunkCount = ((500 + windowWidth) / ChunkSize) + 2;
        for (int chunkX = 0; chunkX < residentChunkCount; chunkX++)
        {
            for (int chunkY = 0; chunkY < HeightChunks; chunkY++)
            {
                layer.AddResident(chunkY + (chunkX * HeightChunks));
            }
        }

        var storage = new ResidencyStorage(layer);
        var map = new ResidencyMap(WorldSize, WorldSize);
        int readsPerProbe = 0;
        int touchesPerProbe = 0;
        void RunStableProbe()
        {
            layer.ResetCounters();
            bool resident = TerrainResidencyProbe.IsWindowResident(
                storage,
                map,
                new Vector2Int(500, 500),
                windowWidth,
                windowHeight);
            if (!resident)
            {
                throw new InvalidOperationException("Stable residency fixture unexpectedly missed a chunk.");
            }

            readsPerProbe = layer.ReadCount;
            touchesPerProbe = layer.TouchCount;
        }

        runner.Run("одна проверка окна на стабильном кадре", RunStableProbe);
        runner.Metric("ReadChunk на стабильный план", readsPerProbe, "вызовов");
        runner.Metric("LRU touch на стабильный план", touchesPerProbe, "вызовов");
    }

    private static void TimingDiagnostics(BenchRunner runner)
    {
        runner.Suite = "timing-diagnostic";
        if (!runner.Wants("sleep") && !runner.Wants("spin"))
        {
            return;
        }

        runner.Run("sleep 2 ms", () => System.Threading.Thread.Sleep(2));
        runner.Run("CPU spin", () => System.Threading.Thread.SpinWait(500_000));
    }

    // ── Сквозной сценарий: 600 шагов ходьбы с копанием ───────────────────
    //
    // Каждый шаг проходит весь процессорный конвейер террейна в том порядке,
    // что и TerrainRenderer: кэш со сдвигом и дозаполнением каймы, маски и
    // искажение со сдвигом, заливка фона со сдвигом, упаковка вошедшей полосы
    // в тексели, учёт грязной области и копирование под выгрузку. Каждый
    // десятый шаг — копание 3×3: заплатка кэша и клеток.
    private static void SessionPipeline(BenchRunner runner, int width, int height)
    {
        runner.Suite = "session";
        const int Steps = 600;
        if (!runner.Wants("шаг конвейера террейна"))
        {
            return;
        }

        int minX = 5000;
        int minY = 7000;
        var cache = new TerrainCellCache();
        cache.FillCaves(width, height, minX, minY, Seed);
        TerrainVertex[] vertices = SyntheticVertices(width, height);
        var texels = new TexelArrays(width, height);
        var region = new TerrainDirtyRegion();
        region.Reset(width, height);
        region.Clear();

        var random = new Random(Seed);
        string[] stageNames = ["кэш: сдвиг и кайма", "упаковка полос", "копирование под выгрузку"];
        var stageTotals = new double[stageNames.Length];
        var samples = new List<double>(Steps);
        var digSamples = new List<double>(Steps / 10);
        long uploadedTexels = 0;
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        int gen0Before = GC.CollectionCount(0);
        for (int step = 0; step < Steps; step++)
        {
            int dx;
            int dy;
            do
            {
                dx = random.Next(-1, 2);
                dy = random.Next(-1, 2);
            }
            while (dx == 0 && dy == 0);

            long start = Stopwatch.GetTimestamp();
            minX += dx;
            minY += dy;
            long stage = Stopwatch.GetTimestamp();
            cache.ScrollTo(minX, minY, Seed);
            stage = Lap(stageTotals, 0, stage);

            TerrainScrollBands bands = TerrainScrollBands.Resolve(width, height, dx, dy, neighbourMargin: 1);
            PackRect(texels, vertices, region, bands.ColumnBand.xMin, bands.ColumnBand.xMax, bands.ColumnBand.yMin, bands.ColumnBand.yMax, minX, minY, width, height);
            PackRect(texels, vertices, region, bands.RowBand.xMin, bands.RowBand.xMax, bands.RowBand.yMin, bands.RowBand.yMax, minX, minY, width, height);
            stage = Lap(stageTotals, 1, stage);
            uploadedTexels += CopyDirty(texels, region, width, height);
            Lap(stageTotals, 2, stage);
            samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);

            if (step % 10 == 9)
            {
                long digStart = Stopwatch.GetTimestamp();
                int cx = 10 + random.Next(width - 20);
                int cy = 10 + random.Next(height - 20);
                cache.Dig(cx, cy, 3, 3);
                PackRect(texels, vertices, region, cx - 1, cx + 4, cy - 1, cy + 4, minX, minY, width, height);
                uploadedTexels += CopyDirty(texels, region, width, height);
                digSamples.Add(Stopwatch.GetElapsedTime(digStart).TotalMilliseconds);
            }
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        int gen0 = GC.CollectionCount(0) - gen0Before;
        runner.Record("шаг конвейера террейна", samples, allocated, gen0);
        runner.Record("копание 3×3 (заплатка конвейера)", digSamples, 0, 0);
        for (int i = 0; i < stageNames.Length; i++)
        {
            runner.Metric($"стадия шага: {stageNames[i]}", stageTotals[i] / Steps, "мс");
        }

        runner.Metric("выгружено текселей за шаг, в среднем", (double)uploadedTexels / Steps, "текс");
        runner.Metric("доля текстуры за шаг, в среднем", uploadedTexels * 100.0 / Steps / (width * height * 2), "%");
    }

    private static long Lap(double[] totals, int index, long since)
    {
        long now = Stopwatch.GetTimestamp();
        totals[index] += Stopwatch.GetElapsedTime(since, now).TotalMilliseconds;
        return now;
    }

    private static void PackRect(
        TexelArrays texels, TerrainVertex[] vertices, TerrainDirtyRegion region,
        int startX, int endX, int startY, int endY, int minX, int minY, int width, int height)
    {
        if (endX <= startX || endY <= startY)
        {
            return;
        }

        for (int x = startX; x < endX; x++)
        {
            int ringX = Ring(minX + x, width);
            for (int y = startY; y < endY; y++)
            {
                texels.PackCellAt(vertices, x, y, ringX, Ring(minY + y, height), width, height);
            }
        }

        region.MarkCells(Ring(minX + startX, width), Ring(minY + startY, height), endX - startX, endY - startY);
    }

    // Как TerrainCellBuffers.Apply: строки прямоугольников, либо всё сразу,
    // если изменённого больше половины буфера.
    private static long CopyDirty(TexelArrays texels, TerrainDirtyRegion region, int width, int height)
    {
        long texelCount = (long)width * height;
        long uploaded;
        if (region.IsAll || region.Area * 2 >= texelCount)
        {
            texels.CopyAllTo(texels.Staging);
            uploaded = texelCount;
        }
        else
        {
            uploaded = 0;
            for (int i = 0; i < region.Count; i++)
            {
                RectInt rect = region[i];
                for (int row = rect.y; row < rect.yMax; row++)
                {
                    texels.CopyRowSpan(row, rect.x, rect.width, width);
                }

                uploaded += (long)rect.width * rect.height;
            }
        }

        region.Clear();
        return uploaded;
    }

    // ── Сдвиг окна камеры ────────────────────────────────────────────────
    // Плоский сдвиг буфера, каким террейн двигал вершины до кольцевой сетки.
    // Жил в TerrainMeshScroller, из игры удалён: кольцевая сетка двигает окно
    // за O(1) сменой двух индексов, копировать нечего. Оставлен здесь, чтобы
    // строка «старый» в сравнении продолжала мерить то же, что и раньше.
    private static void LegacyBufferScroll<T>(
        T[] buffer, int width, int height, int elementsPerCell, int dx, int dy)
    {
        if (dx == 0 && dy == 0)
        {
            return;
        }

        int keptWidth = width - Math.Abs(dx);
        int keptHeight = height - Math.Abs(dy);
        if (keptWidth <= 0 || keptHeight <= 0)
        {
            return;
        }

        int sourceY = dy > 0 ? dy : 0;
        int targetY = dy > 0 ? 0 : -dy;
        int runLength = keptHeight * elementsPerCell;
        if (dx >= 0)
        {
            for (int x = 0; x < keptWidth; x++)
            {
                CopyColumn(buffer, x, x + dx, height, elementsPerCell, sourceY, targetY, runLength);
            }
        }
        else
        {
            int firstTargetX = -dx;
            for (int x = firstTargetX + keptWidth - 1; x >= firstTargetX; x--)
            {
                CopyColumn(buffer, x, x + dx, height, elementsPerCell, sourceY, targetY, runLength);
            }
        }
    }

    private static void CopyColumn<T>(
        T[] buffer, int targetX, int sourceX, int height, int elementsPerCell,
        int sourceY, int targetY, int runLength)
    {
        int sourceOffset = ((sourceX * height) + sourceY) * elementsPerCell;
        int targetOffset = ((targetX * height) + targetY) * elementsPerCell;
        Array.Copy(buffer, sourceOffset, buffer, targetOffset, runLength);
    }

    private static void Scroll(BenchRunner runner, int width, int height)
    {
        runner.Suite = "scroll";
        TerrainVertex[] vertices = SyntheticVertices(width, height);
        var atlasGrid = new TerrainRingGrid<int>();
        var doorGrid = new TerrainRingGrid<bool>();
        atlasGrid.EnsureSize(width, height);
        doorGrid.EnsureSize(width, height);

        foreach ((int dx, int dy, string label) in new[] { (1, 0, "x"), (0, 1, "y"), (1, 1, "диагональ"), (-3, 2, "рывок -3,+2") })
        {
            runner.Run($"старый: буфер вершин + позиции, {label}", () =>
            {
                LegacyBufferScroll(vertices, width, height, 8, dx, dy);
                ShiftPositionsOld(vertices, width, height, 8, 1f, dx, dy);
            });
            runner.Run($"новый: атласы и двери, {label}", () =>
            {
                atlasGrid.Scroll(dx, dy);
                doorGrid.Scroll(dx, dy);
            });
        }

        RectInt packBand = TerrainScrollBands.Resolve(width, height, 1, 0, neighbourMargin: 1).ColumnBand;
        int bandStart = packBand.xMin;
        int bandLength = packBand.width;
        var texels = new TexelArrays(width, height);
        runner.Run($"новый: упаковка полосы {bandLength}×{height}", () =>
        {
            for (int x = bandStart; x < bandStart + bandLength; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    texels.PackCell(vertices, x, y, width, height);
                }
            }
        });
    }

    // ── Упаковка квадов в тексели ────────────────────────────────────────
    private static void Pack(BenchRunner runner, int width, int height)
    {
        runner.Suite = "pack";
        TerrainVertex[] vertices = SyntheticVertices(width, height);
        var texels = new TexelArrays(width, height);

        runner.Run("один PackCell", () =>
        {
            texels.PackCell(vertices, 0, 0, width, height);
        });
        runner.Run("вся сетка последовательно", () =>
        {
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    texels.PackCell(vertices, x, y, width, height);
                }
            }
        });
        runner.Run("вся сетка Parallel.For по столбцам", () =>
        {
            Parallel.For(0, width, x =>
            {
                for (int y = 0; y < height; y++)
                {
                    texels.PackCell(vertices, x, y, width, height);
                }
            });
        });
        runner.Run("вся сетка Parallel.For по строкам", () =>
        {
            Parallel.For(0, height, y =>
            {
                for (int x = 0; x < width; x++)
                {
                    texels.PackCell(vertices, x, y, width, height);
                }
            });
        });
        runner.Run("вся сетка кольцевой адрес (сдвиг окна 5000,7000)", () =>
        {
            for (int x = 0; x < width; x++)
            {
                int ringX = Ring(5000 + x, width);
                for (int y = 0; y < height; y++)
                {
                    texels.PackCellAt(vertices, x, y, ringX, Ring(7000 + y, height), width, height);
                }
            }
        });
    }

    // ── Подготовка к выгрузке на GPU ─────────────────────────────────────
    private static void Upload(BenchRunner runner, int width, int height)
    {
        runner.Suite = "upload";
        TerrainVertex[] vertices = SyntheticVertices(width, height);
        byte[] vertexStaging = new byte[vertices.Length * Marshal.SizeOf<TerrainVertex>()];
        var texels = new TexelArrays(width, height);
        byte[] texelStaging = new byte[texels.TotalBytes];

        runner.Run($"старый: весь буфер вершин {vertexStaging.Length / 1048576.0:F2} МБ", () =>
            MemoryMarshal.AsBytes(vertices.AsSpan()).CopyTo(vertexStaging));
        runner.Run($"новый: весь буфер клеток {texelStaging.Length / 1048576.0:F2} МБ", () => texels.CopyAllTo(texelStaging));

        int rows = height;
        runner.Run("новый: полоса 2×H буфера клеток", () =>
        {
            for (int row = 0; row < rows; row++)
            {
                texels.CopyRowSpan(row, width - 2, 2, width);
            }
        });
        runner.Run("новый: заплатка 3×3 клетки буфера клеток", () =>
        {
            for (int row = 40; row < 43; row++)
            {
                texels.CopyRowSpan(row, 60, 3, width);
            }
        });
    }

    // ── Учёт грязных областей (настоящий TerrainDirtyRegion) ──────────────
    private static void DirtyRegion(BenchRunner runner, int width, int height)
    {
        runner.Suite = "dirty-region";
        var region = new TerrainDirtyRegion();
        var random = new Random(Seed);

        runner.Run("полоса x + полоса y (диагональный шаг)", () =>
        {
            region.MarkCells(width - 2, 0, 2, height);
            region.MarkCells(0, height - 2, width, 2);
        }, () => { region.Reset(width, height); region.Clear(); });
        runner.Run("полоса на шве кольца", () =>
        {
            region.MarkCells(width - 1, height - 1, 3, height);
        }, () => { region.Reset(width, height); region.Clear(); });
        runner.Run("200 рассыпанных заплаток 1×1", () =>
        {
            for (int i = 0; i < 200; i++)
            {
                region.MarkCells(random.Next(width), random.Next(height), 1, 1);
            }
        }, () => { region.Reset(width, height); region.Clear(); });

        // Качество, а не время: какую долю текстуры в итоге пришлось бы выгрузить.
        region.Reset(width, height);
        region.Clear();
        region.MarkCells(width - 2, 0, 2, height);
        region.MarkCells(0, height - 2, width, 2);
        runner.Metric("диагональный шаг: доля выгрузки", region.Area * 100.0 / (width * height * 2), "%");
        runner.Metric("диагональный шаг: прямоугольников", region.Count, "шт");
        region.Clear();
        for (int i = 0; i < 20; i++)
        {
            region.MarkCells((width / 2) + random.Next(8), (height / 2) + random.Next(8), 1, 1);
        }

        runner.Metric("20 заплаток в пятне 8×8: доля выгрузки", region.Area * 100.0 / (width * height * 2), "%");
        region.Clear();
        for (int i = 0; i < 200; i++)
        {
            region.MarkCells(random.Next(width), random.Next(height), 1, 1);
        }

        runner.Metric("200 заплаток по всей сетке: доля выгрузки", region.Area * 100.0 / (width * height * 2), "%");
    }

    // ── DirtyRectSet (настоящий) ─────────────────────────────────────────
    private static void DirtyRects(BenchRunner runner, int width, int height)
    {
        runner.Suite = "dirty-rect-set";
        var set = new DirtyRectSet();
        var bounds = new RectInt(1000, 2000, width, height);
        var random = new Random(Seed);
        runner.Run("64 случайных прямоугольника копания", () =>
        {
            for (int i = 0; i < 64; i++)
            {
                set.Add(new RectInt(1000 + random.Next(width), 2000 + random.Next(height), 1 + random.Next(3), 1 + random.Next(3)), bounds);
            }
        }, set.Clear);
        runner.Run("TotalArea после 8 прямоугольников", () => GC.KeepAlive(set.TotalArea), () =>
        {
            set.Clear();
            for (int i = 0; i < 8; i++)
            {
                set.Add(new RectInt(1000 + (i * 20), 2000 + (i * 9), 4, 4), bounds);
            }
        });
    }

    // ── Кольцевой сдвиг кэша ───────────────────────────────────────────────
    private static void CacheScroll(BenchRunner runner, int width, int height)
    {
        runner.Suite = "cache-scroll";
        var ring = new TerrainRingGrid<CachedCellData>();
        ring.EnsureSize(width + 2, height + 2);
        runner.Run("TerrainRingGrid сдвиг x", () => ring.Scroll(1, 0));

        runner.Metric("размер CachedCellData", Marshal.SizeOf<CachedCellData>(), "Б");
        runner.Metric("кэш клеток целиком", Marshal.SizeOf<CachedCellData>() * (width + 2) * (height + 2) / 1048576.0, "МБ");
    }

    // ── Каталоги типов, которые FillQuad опрашивает на каждый квад ───────
    //
    // Все эти ответы зависят ТОЛЬКО от типа клетки, но спрашиваются на каждой
    // клетке и на каждом из двух слоёв. Замер показывает цену одного прохода
    // по окну и цену того же прохода через таблицу, разрешённую по типу.
    private static void QuadCatalogs(BenchRunner runner, int width, int height)
    {
        runner.Suite = "quad-catalogs";
        var random = new Random(Seed);
        var types = new CellType[width * height];
        for (int i = 0; i < types.Length; i++)
        {
            types[i] = (CellType)(1 + random.Next(60));
        }

        runner.Run("каталоги на клетку, оба слоя", () =>
        {
            int sink = 0;
            for (int i = 0; i < types.Length; i++)
            {
                CellType type = types[i];
                for (int layer = 0; layer < 2; layer++)
                {
                    BlockDefinition block = BlockRegistry.Get(type);
                    sink += block.Outline == CellOutline.Round ? 1 : 0;
                    sink += block.DecalAtlas == CellDecalAtlas.Ground ? 1 : 0;
                    sink += (int)block.SurfaceEffect;
                }
            }

            GC.KeepAlive(sink);
        });

        var roundable = new bool[65536];
        var ground = new bool[65536];
        var profile = new int[65536];
        for (int value = 0; value < 65536; value++)
        {
            var type = (CellType)value;
            BlockDefinition block = BlockRegistry.Get(type);
            roundable[value] = block.Outline == CellOutline.Round;
            ground[value] = block.DecalAtlas == CellDecalAtlas.Ground;
            profile[value] = (int)block.SurfaceEffect;
        }

        runner.Run("та же выборка из таблицы по типу", () =>
        {
            int sink = 0;
            for (int i = 0; i < types.Length; i++)
            {
                int type = (int)types[i];
                for (int layer = 0; layer < 2; layer++)
                {
                    sink += roundable[type] ? 1 : 0;
                    sink += ground[type] ? 1 : 0;
                    sink += profile[type];
                }
            }

            GC.KeepAlive(sink);
        });
    }

    // ── Пространственный индекс сущностей (настоящий SpatialShardGrid) ────
    private static void Spatial(BenchRunner runner, int width, int height)
    {
        runner.Suite = "spatial";
        const int Entities = 5000;
        var random = new Random(Seed);
        var items = new object[Entities];
        var positions = new Vector2[Entities];
        for (int i = 0; i < Entities; i++)
        {
            items[i] = new object();
            positions[i] = new Vector2(random.Next(10016), random.Next(40000));
        }

        var grid = new Kern.World.SpatialShardGrid<object>();
        var results = new List<object>(256);
        runner.Run($"Insert {Entities}", () =>
        {
            for (int i = 0; i < Entities; i++)
            {
                grid.Insert(items[i], positions[i]);
            }
        }, grid.Clear);
        runner.Run($"Update {Entities} на соседнюю клетку", () =>
        {
            for (int i = 0; i < Entities; i++)
            {
                positions[i] = new Vector2(positions[i].x + 1, positions[i].y);
                grid.Update(items[i], positions[i]);
            }
        });
        var view = new Rect(5000, 20000, width, height);
        runner.Run($"QueryRect окно {width}×{height}", () =>
        {
            results.Clear();
            grid.QueryRect(view, results);
        });
        runner.Run("QueryRadius 64", () =>
        {
            results.Clear();
            grid.QueryRadius(new Vector2(5000, 20000), 64, results);
        });
    }

    // ── Общее ────────────────────────────────────────────────────────────
    private static int Ring(int value, int size)
    {
        int remainder = value % size;
        return remainder < 0 ? remainder + size : remainder;
    }

    private static TerrainVertex[] SyntheticVertices(int width, int height)
    {
        var random = new Random(Seed);
        var vertices = new TerrainVertex[width * height * 8];
        Vector2[] corners = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
        for (int quad = 0; quad < vertices.Length / 4; quad++)
        {
            var atlasRect = new Vector4(random.NextSingle(), random.NextSingle(), 0.0625f, 0.0625f);
            for (int i = 0; i < 4; i++)
            {
                ref TerrainVertex vertex = ref vertices[(quad * 4) + i];
                vertex.Position = new Vector3(i, i, 0);
                vertex.UV0 = corners[i];
                vertex.UV1 = atlasRect;
                vertex.UV2 = new Vector4(0.015625f, 0.015625f, 1, 1);
                vertex.UV3 = new Vector4(random.Next(10000), random.Next(40000), 0, 0);
                vertex.UV4 = new Vector4(0, 0, 0, 0);
                vertex.UV5 = new Vector4(0, i, i, 0);
                vertex.UV6 = new Vector4(random.Next(16777215), 64, 0, 0);
            }
        }

        return vertices;
    }

    // Удалённый из игры TerrainMeshScroller.ShiftPositions — старый путь.
    private static void ShiftPositionsOld(TerrainVertex[] buffer, int meshWidth, int meshHeight, int verticesPerCell, float cellSize, int dx, int dy)
    {
        int keptWidth = meshWidth - Math.Abs(dx);
        int keptHeight = meshHeight - Math.Abs(dy);
        if (keptWidth <= 0 || keptHeight <= 0)
        {
            return;
        }

        int firstX = dx > 0 ? 0 : -dx;
        int firstY = dy > 0 ? 0 : -dy;
        float shiftX = dx * cellSize;
        float shiftY = dy * cellSize;
        Parallel.For(firstX, firstX + keptWidth, x =>
        {
            int start = ((x * meshHeight) + firstY) * verticesPerCell;
            int end = start + (keptHeight * verticesPerCell);
            for (int i = start; i < end; i++)
            {
                ref TerrainVertex vertex = ref buffer[i];
                vertex.Position.x -= shiftX;
                vertex.Position.y -= shiftY;
            }
        });
    }

    // Клетки в раскладке TerrainCellBuffers: один uint на клетку, оба слоя.
    private sealed class TexelArrays
    {
        public readonly TerrainCell[] Cells;
        private readonly TerrainCell[] _patch = new TerrainCell[1024];

        public byte[] Staging => _staging ??= new byte[TotalBytes];

        private byte[]? _staging;

        public TexelArrays(int width, int height)
        {
            Cells = new TerrainCell[width * height];
        }

        public int TotalBytes => Cells.Length * Marshal.SizeOf<TerrainCell>();

        public void PackCell(TerrainVertex[] vertices, int x, int y, int width, int height) =>
            PackCellAt(vertices, x, y, x, y, width, height);

        // Упаковка клетки: её тип — всё, что клетка хранит.
        public void PackCellAt(TerrainVertex[] vertices, int x, int y, int ringX, int ringY, int width, int height)
        {
            int first = ((x * height) + y) * 8;
            Cells[(ringY * width) + ringX] = TerrainCellData.PackCell(
                vertices[first].UV3.w != 0f ? CellType.Rock : CellType.Road);
        }

        public void CopyAllTo(byte[] target) =>
            MemoryMarshal.AsBytes(Cells.AsSpan()).CopyTo(target);

        public void CopyRowSpan(int row, int x, int count, int width) =>
            Array.Copy(Cells, (row * width) + x, _patch, 0, count);
    }
}

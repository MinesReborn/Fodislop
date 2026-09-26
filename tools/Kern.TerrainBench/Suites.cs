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
using Kern.World.Terrain.Background;
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
    private static int _quadFillSink;
    private static int _maskCompositionSink;

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

        public ChunkReadResult<CellType> ReadChunk(int chunkIndex, bool touchLru = true)
        {
            ReadCount++;
            if (!_resident.Contains(chunkIndex))
            {
                return new ChunkReadResult<CellType>(ChunkReadStatus.Missing, null, null);
            }

            if (touchLru)
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
    }

    public static void All(BenchRunner runner, int width, int height)
    {
        Scroll(runner, width, height);
        Pack(runner, width, height);
        Upload(runner, width, height);
        DirtyRegion(runner, width, height);
        DirtyRects(runner, width, height);
        CacheScroll(runner, width, height);
        FloodFill(runner, width, height);
        Masks(runner, width, height);
        MaskComposition(runner, width, height);
        Distortion(runner, width, height);
        QuadFill(runner, width, height);
        FloodFillEquivalence(runner, width, height);
        ResidencyProbe(runner, width, height);
        CellTypeIndex(runner, width, height);
        QuadCatalogs(runner, width, height);
        Spatial(runner, width, height);
        SessionPipeline(runner, width, height);
        TimingDiagnostics(runner);
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
        const int ChunkSize = ProjectRuntimeContracts.World.ChunkSize;
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
    // десятый шаг — копание 3×3: заплатка кэша, масок, заливки и текселей.
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
        var masks = new TerrainCellMaskCalculator();
        masks.EnsureCapacity(width, height);
        masks.PrecalculateFull(cache, width, height);
        var distortion = new TerrainVertexDistortionCalculator();
        distortion.EnsureCapacity(width, height);
        distortion.PrecalculateFull(cache, width, height, 10016, 40000);
        var provider = new CaveProvider(width, height, Seed) { OriginX = minX, OriginY = minY };
        var fill = new BackgroundFloodFill();
        fill.Allocate(width, height);
        fill.ComputeFull(provider);
        TerrainVertex[] vertices = SyntheticVertices(width, height);
        var texels = new TexelArrays(width, height);
        var region = new TerrainDirtyRegion();
        region.Reset(width, height);
        region.Clear();

        var random = new Random(Seed);
        string[] stageNames = ["кэш: сдвиг и кайма", "маски", "искажение", "заливка фона", "упаковка полос", "копирование под выгрузку"];
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
            provider.OriginX = minX;
            provider.OriginY = minY;
            long stage = Stopwatch.GetTimestamp();
            cache.ScrollTo(minX, minY, Seed);
            stage = Lap(stageTotals, 0, stage);
            masks.PrecalculateIncremental(cache, width, height, dx, dy);
            stage = Lap(stageTotals, 1, stage);
            distortion.PrecalculateIncremental(cache, width, height, dx, dy, 10016, 40000);
            stage = Lap(stageTotals, 2, stage);
            fill.ComputeScrolled(dx, dy, provider);
            stage = Lap(stageTotals, 3, stage);

            TerrainScrollBands bands = TerrainScrollBands.Resolve(width, height, dx, dy, neighbourMargin: 1);
            PackRect(texels, vertices, region, bands.ColumnBand.xMin, bands.ColumnBand.xMax, bands.ColumnBand.yMin, bands.ColumnBand.yMax, minX, minY, width, height);
            PackRect(texels, vertices, region, bands.RowBand.xMin, bands.RowBand.xMax, bands.RowBand.yMin, bands.RowBand.yMax, minX, minY, width, height);
            stage = Lap(stageTotals, 4, stage);
            uploadedTexels += CopyDirty(texels, region, width, height);
            Lap(stageTotals, 5, stage);
            samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);

            if (step % 10 == 9)
            {
                long digStart = Stopwatch.GetTimestamp();
                int cx = 10 + random.Next(width - 20);
                int cy = 10 + random.Next(height - 20);
                cache.Dig(cx, cy, 3, 3);
                masks.PrecalculateRegion(cache, width, height, cx - 1, cy - 1, 5, 5);
                distortion.PrecalculateRegion(cache, width, height, cx - 1, cy - 1, 5, 5, 10016, 40000);
                fill.UpdateLocalRegion(cx - 1, cy - 1, 5, 5, provider);
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

    // Как TerrainCellDataTextures.Apply: прямоугольники, либо всё сразу,
    // если изменённое больше половины текстуры.
    private static long CopyDirty(TexelArrays texels, TerrainDirtyRegion region, int width, int height)
    {
        long texelCount = (long)width * height * 2;
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

    // ── Эквивалентность заливки: сдвиг против полного пересчёта ──────────
    private static void FloodFillEquivalence(BenchRunner runner, int width, int height)
    {
        runner.Suite = "flood-fill";
        if (!runner.Wants("расхождение сдвига с полным пересчётом"))
        {
            return;
        }

        foreach ((int dx, int dy) in new[] { (1, 0), (0, 1), (1, 1), (-1, -1) })
        {
            var provider = new CaveProvider(width, height, Seed) { OriginX = 5000, OriginY = 7000 };
            var scrolled = new BackgroundFloodFill();
            scrolled.Allocate(width, height);
            scrolled.ComputeFull(provider);
            provider.OriginX += dx;
            provider.OriginY += dy;
            scrolled.ComputeScrolled(dx, dy, provider);

            var full = new BackgroundFloodFill();
            full.Allocate(width, height);
            full.ComputeFull(provider);

            int mismatches = 0;
            int floorMismatches = 0;
            int borderMismatches = 0;
            int interiorMismatches = 0;
            int interiorCells = 0;
            var mismatchSamples = new List<string>(8);
            const int BorderCells = 4;
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    bool interior = x >= BorderCells && y >= BorderCells && x < width - BorderCells && y < height - BorderCells;
                    interiorCells += interior ? 1 : 0;
                    if (scrolled.Buffer[x, y] == full.Buffer[x, y])
                    {
                        continue;
                    }

                    mismatches++;
                    if (mismatchSamples.Count < mismatchSamples.Capacity)
                    {
                        mismatchSamples.Add($"({x},{y}) {scrolled.Buffer[x, y]}→{full.Buffer[x, y]}");
                    }

                    CachedCellInfo cell = provider.GetCell(x + 1, y + 1);
                    if ((cell.Properties & CellConfigProperties.Passable) != 0)
                    {
                        floorMismatches++;
                    }

                    if (interior)
                    {
                        interiorMismatches++;
                    }
                    else
                    {
                        borderMismatches++;
                    }
                }
            }

            runner.Metric($"расхождение сдвига с полным пересчётом ({dx},{dy})", mismatches * 100.0 / (width * height), "%");
            runner.Metric($"  из них на проходимых клетках ({dx},{dy})", floorMismatches, "клеток");
            runner.Metric($"  из них у каймы ≤{BorderCells} ({dx},{dy})", borderMismatches, "клеток");
            runner.Metric($"  из них в глубине окна ({dx},{dy})", interiorMismatches * 100.0 / interiorCells, "% глубины");
            if (mismatchSamples.Count > 0)
            {
                Console.WriteLine($"  примеры mismatch ({dx},{dy}): {string.Join(", ", mismatchSamples)}");
            }
        }
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

        runner.Run("один PackQuad", () =>
        {
            GC.KeepAlive(TerrainCellDataPacker.PackQuad(vertices.AsSpan(0, 4), 1));
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
        runner.Run($"новый: все 7 текстур {texelStaging.Length / 1048576.0:F2} МБ", () => texels.CopyAllTo(texelStaging));

        int rows = height * 2;
        var patch = new Vector4[16 * rows];
        runner.Run("новый: полоса 2×2H одной float-текстуры", () =>
        {
            for (int row = 0; row < rows; row++)
            {
                Array.Copy(texels.World, (row * width) + width - 2, patch, row * 16, 2);
            }
        });
        runner.Run("новый: полоса 2×2H всех 7 текстур", () =>
        {
            for (int row = 0; row < rows; row++)
            {
                texels.CopyRowSpan(row, width - 2, 2, width);
            }
        });
        runner.Run("новый: заплатка 3×3 клетки всех 7 текстур", () =>
        {
            for (int row = 40; row < 46; row++)
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

        // FillQuad адресует кольцевые сетки девять раз на квад: четыре узла
        // искажения, три маски, атласы и флаг двери. Каждый индекс — два
        // целочисленных деления по размеру окна.
        var nodes = new TerrainRingGrid<int>();
        nodes.EnsureSize(width + 1, height + 1);
        runner.Run("девять чтений по кольцевому адресу на клетку", () =>
        {
            int sink = 0;
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    sink += nodes[x, y] + nodes[x + 1, y] + nodes[x, y + 1] + nodes[x + 1, y + 1];
                    sink += nodes[x, y] + nodes[x, y] + nodes[x, y] + nodes[x, y] + nodes[x, y];
                }
            }

            System.Threading.Volatile.Write(ref _quadFillSink, sink);
        });
        runner.Metric("размер CachedCellData", Marshal.SizeOf<CachedCellData>(), "Б");
        runner.Metric("кэш клеток целиком", Marshal.SizeOf<CachedCellData>() * (width + 2) * (height + 2) / 1048576.0, "МБ");
    }

    // ── Заливка фона (настоящий BackgroundFloodFill) ─────────────────────
    private static void FloodFill(BenchRunner runner, int width, int height)
    {
        runner.Suite = "flood-fill";
        if (!runner.Wants("ComputeFull") &&
            !runner.Wants("ComputeScrolled") &&
            !runner.Wants("UpdateLocalRegion") &&
            !runner.Wants("fixture"))
        {
            return;
        }

        var provider = new PrecomputedCaveProvider(width, height, Seed);
        var generatedProvider = new CaveProvider(width, height, Seed);
        var fill = new BackgroundFloodFill();
        int fullProcessorCount = Environment.ProcessorCount;
        var fullDegreeFill = new BackgroundFloodFill(fullProcessorCount);
        fill.Allocate(width, height);
        fullDegreeFill.Allocate(width, height);
        int providerMismatches = CountProviderMismatches(provider, generatedProvider, width, height);
        runner.Metric("расхождения предвычисленного и hash provider", providerMismatches, "клеток");
        if (providerMismatches != 0)
        {
            throw new InvalidOperationException($"Precomputed cave source differs in {providerMismatches} cells.");
        }

        runner.Run("fixture: cached source reads", () => ReadProvider(provider, width, height));
        runner.Run("fixture: cave hash generation", () => ReadProvider(generatedProvider, width, height));
        fill.ComputeFull(provider);
        fullDegreeFill.ComputeFull(provider);
        int fillMismatches = CountFillMismatches(fill, fullDegreeFill, width, height);
        runner.Metric("расхождения ComputeFull DOP4 и DOP max", fillMismatches, "клеток");
        if (fillMismatches != 0)
        {
            throw new InvalidOperationException($"Capped flood fill differs in {fillMismatches} cells.");
        }

        if (Math.Min(4, fullProcessorCount) != fullProcessorCount)
        {
            runner.RunAlternating(
                $"ComputeFull DOP {Math.Min(4, fullProcessorCount)}",
                () => fill.ComputeFull(provider),
                $"ComputeFull DOP {fullProcessorCount}",
                () => fullDegreeFill.ComputeFull(provider));
        }
        else
        {
            runner.Run("ComputeFull", () => fill.ComputeFull(provider));
        }

        runner.Run("ComputeScrolled +1,0", () => fill.ComputeScrolled(1, 0, provider));
        runner.Run("ComputeScrolled +1,+1", () => fill.ComputeScrolled(1, 1, provider));
        runner.Run("ComputeScrolled +13,0", () => fill.ComputeScrolled(13, 0, provider));
        runner.Run("ComputeScrolled +29,+29", () => fill.ComputeScrolled(29, 29, provider));
        runner.Run("UpdateLocalRegion 3×3", () => fill.UpdateLocalRegion(width / 2, height / 2, 3, 3, provider));
        runner.Run("UpdateLocalRegion 16×16", () => fill.UpdateLocalRegion(width / 3, height / 3, 16, 16, provider));
        // Приход чанков сливается в один прямоугольник во всё окно: заплатка
        // тогда идёт последовательно там, где полный путь идёт параллельно.
        runner.Run("UpdateLocalRegion во всё окно", () => fill.UpdateLocalRegion(0, 0, width, height, provider));
    }

    private static int CountProviderMismatches(
        ICachedCellDataProvider first,
        ICachedCellDataProvider second,
        int width,
        int height)
    {
        int mismatches = 0;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                CachedCellInfo a = first.GetCell(x + 1, y + 1);
                CachedCellInfo b = second.GetCell(x + 1, y + 1);
                mismatches += a.Type != b.Type || a.Properties != b.Properties ? 1 : 0;
            }
        }

        return mismatches;
    }

    private static int CountFillMismatches(
        BackgroundFloodFill first,
        BackgroundFloodFill second,
        int width,
        int height)
    {
        int mismatches = 0;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                mismatches += first.Buffer[x, y] != second.Buffer[x, y] ? 1 : 0;
            }
        }

        return mismatches;
    }

    private static void ReadProvider(ICachedCellDataProvider provider, int width, int height)
    {
        int sink = 0;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                sink += (byte)provider.GetCell(x + 1, y + 1).Type;
            }
        }

        System.Threading.Volatile.Write(ref _maskCompositionSink, sink);
    }

    // ── Маски клеток (настоящий TerrainCellMaskCalculator) ───────────────
    private static void Masks(BenchRunner runner, int width, int height)
    {
        runner.Suite = "masks";
        var cache = new TerrainCellCache();
        cache.FillCaves(width, height, 5000, 7000, Seed);
        var masks = new TerrainCellMaskCalculator();
        masks.EnsureCapacity(width, height);
        int neighborhoodMismatches = CountNeighborhoodMismatches(cache, width, height, masks);
        runner.Metric("расхождения скользящего окна с расчётом по клеткам", neighborhoodMismatches, "масок");
        if (neighborhoodMismatches != 0)
        {
            throw new InvalidOperationException(
                $"Sliding-neighbourhood mask calculation differs in {neighborhoodMismatches} outputs.");
        }

        int fullPassDegree = Math.Min(4, Environment.ProcessorCount);
        if (fullPassDegree < Environment.ProcessorCount)
        {
            var fullDegreeOptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
            runner.RunAlternating(
                $"PrecalculateFull DOP {fullPassDegree}",
                () => masks.PrecalculateFull(cache, width, height),
                $"PrecalculateFull DOP {Environment.ProcessorCount}",
                () => Parallel.For(0, width, fullDegreeOptions, x => masks.CalculateColumn(cache, x, 0, height)));
        }
        else
        {
            runner.Run("PrecalculateFull", () => masks.PrecalculateFull(cache, width, height));
        }

        var cellByCell = new TerrainCellMaskCalculator();
        cellByCell.EnsureCapacity(width, height);
        runner.Run("полный проход: по одной клетке", () =>
        {
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    cellByCell.CalculateCellNode(cache, x, y);
                }
            }
        });
        runner.Run("PrecalculateIncremental +1,0", () => masks.PrecalculateIncremental(cache, width, height, 1, 0));
        runner.Run("PrecalculateRegion 5×5", () => masks.PrecalculateRegion(cache, width, height, width / 2, height / 2, 5, 5));
        runner.Run("PrecalculateRegion во всё окно", () => masks.PrecalculateRegion(cache, width, height, 0, 0, width, height));
    }

    private static int CountNeighborhoodMismatches(
        TerrainCellCache cache,
        int width,
        int height,
        TerrainCellMaskCalculator sliding)
    {
        sliding.PrecalculateFull(cache, width, height);
        var reference = new TerrainCellMaskCalculator();
        reference.EnsureCapacity(width, height);
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                reference.CalculateCellNode(cache, x, y);
            }
        }

        int mismatches = 0;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                mismatches += sliding.CellTilingDescriptors[x, y] != reference.CellTilingDescriptors[x, y] ? 1 : 0;
                mismatches += sliding.CellCornerVariants[x, y] != reference.CellCornerVariants[x, y] ? 1 : 0;
                mismatches += sliding.CellReliefMasks[x, y] != reference.CellReliefMasks[x, y] ? 1 : 0;
                mismatches += sliding.CellReliefCornerMasks[x, y] != reference.CellReliefCornerMasks[x, y] ? 1 : 0;
                mismatches += sliding.CellSolidBoundaryMasks[x, y] != reference.CellSolidBoundaryMasks[x, y] ? 1 : 0;
            }
        }

        return mismatches;
    }

    // A/B the former pair of mask calculations against the fused production
    // calculation. Both read the same deterministic neighbourhoods; the
    // equivalence scan runs before timing so a faster wrong result is rejected.
    private static void MaskComposition(BenchRunner runner, int width, int height)
    {
        runner.Suite = "mask-composition";
        if (!runner.Wants("ReliefMask"))
        {
            return;
        }

        var cache = new TerrainCellCache();
        cache.FillCaves(width, height, 5000, 7000, Seed);
        int mismatches = CountReliefMaskMismatches(cache, width, height);
        runner.Metric("различия fused и исходной пары масок", mismatches, "клеток");
        if (mismatches != 0)
        {
            throw new InvalidOperationException($"Relief mask fusion differs on {mismatches} cells.");
        }

        runner.Run("исходная пара масок", () => RunReliefMaskComposition(cache, width, height, fused: false));
        runner.Run("объединённые маски", () => RunReliefMaskComposition(cache, width, height, fused: true));
    }

    private static int CountReliefMaskMismatches(TerrainCellCache cache, int width, int height)
    {
        int mismatches = 0;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                ReadReliefNeighborhood(cache, x, y, out CachedCellData data, out CachedCellData top,
                    out CachedCellData left, out CachedCellData bottom, out CachedCellData right,
                    out CachedCellData topLeft, out CachedCellData topRight,
                    out CachedCellData bottomLeft, out CachedCellData bottomRight);
                byte expectedRelief = TerrainCellMaskCalculator.CalculateReliefMask(data, top, left, bottom, right);
                byte expectedCorners = TerrainCellMaskCalculator.CalculateReliefCornerMask(
                    data, top, left, bottom, right, topLeft, topRight, bottomLeft, bottomRight);
                TerrainCellMaskCalculator.CalculateReliefMasks(
                    data, top, left, bottom, right, topLeft, topRight, bottomLeft, bottomRight,
                    out byte actualRelief, out byte actualCorners);
                if (expectedRelief != actualRelief || expectedCorners != actualCorners)
                {
                    mismatches++;
                }
            }
        }

        return mismatches;
    }

    private static void RunReliefMaskComposition(TerrainCellCache cache, int width, int height, bool fused)
    {
        int sink = 0;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                ReadReliefNeighborhood(cache, x, y, out CachedCellData data, out CachedCellData top,
                    out CachedCellData left, out CachedCellData bottom, out CachedCellData right,
                    out CachedCellData topLeft, out CachedCellData topRight,
                    out CachedCellData bottomLeft, out CachedCellData bottomRight);
                if (fused)
                {
                    TerrainCellMaskCalculator.CalculateReliefMasks(
                        data, top, left, bottom, right, topLeft, topRight, bottomLeft, bottomRight,
                        out byte relief, out byte corners);
                    sink += relief + corners;
                }
                else
                {
                    sink += TerrainCellMaskCalculator.CalculateReliefMask(data, top, left, bottom, right);
                    sink += TerrainCellMaskCalculator.CalculateReliefCornerMask(
                        data, top, left, bottom, right, topLeft, topRight, bottomLeft, bottomRight);
                }
            }
        }

        System.Threading.Volatile.Write(ref _maskCompositionSink, sink);
    }

    private static void ReadReliefNeighborhood(
        TerrainCellCache cache,
        int x,
        int y,
        out CachedCellData data,
        out CachedCellData top,
        out CachedCellData left,
        out CachedCellData bottom,
        out CachedCellData right,
        out CachedCellData topLeft,
        out CachedCellData topRight,
        out CachedCellData bottomLeft,
        out CachedCellData bottomRight)
    {
        int cx = x + 1;
        int cy = y + 1;
        data = cache.GetCellData(cx, cy);
        top = cache.GetCellData(cx, cy + 1);
        left = cache.GetCellData(cx - 1, cy);
        bottom = cache.GetCellData(cx, cy - 1);
        right = cache.GetCellData(cx + 1, cy);
        topLeft = cache.GetCellData(cx - 1, cy + 1);
        topRight = cache.GetCellData(cx + 1, cy + 1);
        bottomLeft = cache.GetCellData(cx - 1, cy - 1);
        bottomRight = cache.GetCellData(cx + 1, cy - 1);
    }

    // ── Искажение сетки (настоящий TerrainVertexDistortionCalculator) ─────
    private static void Distortion(BenchRunner runner, int width, int height)
    {
        runner.Suite = "distortion";
        var cache = new TerrainCellCache();
        cache.FillCaves(width, height, 5000, 7000, Seed);
        var distortion = new TerrainVertexDistortionCalculator();
        distortion.EnsureCapacity(width, height);
        runner.Run("PrecalculateFull", () => distortion.PrecalculateFull(cache, width, height, 10016, 40000));
        runner.Run("PrecalculateIncremental +1,0", () => distortion.PrecalculateIncremental(cache, width, height, 1, 0, 10016, 40000));
        runner.Run("PrecalculateRegion 5×5", () => distortion.PrecalculateRegion(cache, width, height, width / 2, height / 2, 5, 5, 10016, 40000));
        runner.Run("PrecalculateRegion во всё окно", () => distortion.PrecalculateRegion(cache, width, height, 0, 0, width, height, 10016, 40000));
    }

    // Production FillQuad: tile-neighbour math, organic edge checks, geometry
    // assembly, UV transform, lighting/decal packing, and all corner writes.
    // Atlas/metadata are inert benchmark adapters; the implementation under
    // measurement is the game's TerrainQuadBuilder itself.
    private static void QuadFill(BenchRunner runner, int width, int height)
    {
        runner.Suite = "quad-fill";
        if (!runner.Wants("FillQuad"))
        {
            return;
        }

        const int MinX = 5000;
        const int MinY = 20000;
        const int WorldWidth = 10016;
        const int WorldHeight = 40000;
        var cache = new TerrainCellCache();
        cache.FillCaves(width, height, MinX, MinY, Seed);
        cache.PrepareRenderData();

        var precalculator = new TerrainPrecalculator();
        precalculator.EnsureCapacity(width, height);
        var input = new TerrainPrecalculationInput(
            cache,
            new Vector2Int(width, height),
            new Vector2Int(WorldWidth, WorldHeight));
        precalculator.PrecalculateFull(input);

        var provider = new CaveProvider(width, height, Seed) { OriginX = MinX, OriginY = MinY };
        var floodFill = new BackgroundFloodFill();
        floodFill.Allocate(width, height);
        floodFill.ComputeFull(provider);

        IAtlasDescriptor[] atlases = [new BenchAtlasDescriptor(1024)];
        var sources = new TerrainCellSources(
            cache,
            precalculator,
            floodFill,
            WorldWidth,
            WorldHeight,
            atlases,
            new BenchTerrainMetadataLookup());
        var quad = new TerrainVertex[4];

        precalculator.DistortionStyle = TerrainDistortionStyle.Organic;
        precalculator.PrecalculateFull(input);
        MeasureQuadLayer(runner, sources, quad, width, height, MinX, MinY, TerrainQuadLayer.Foreground, "органический передний план");
        MeasureQuadLayer(runner, sources, quad, width, height, MinX, MinY, TerrainQuadLayer.Background, "органический фон");

        precalculator.DistortionStyle = TerrainDistortionStyle.Classic;
        precalculator.PrecalculateFull(input);
        MeasureQuadLayer(runner, sources, quad, width, height, MinX, MinY, TerrainQuadLayer.Foreground, "классический передний план");
    }

    private static void MeasureQuadLayer(
        BenchRunner runner,
        TerrainCellSources sources,
        TerrainVertex[] quad,
        int width,
        int height,
        int minX,
        int minY,
        TerrainQuadLayer layer,
        string name)
    {
        int previousResultCount = runner.Results.Count;
        runner.Run($"FillQuad · {name}", () =>
        {
            int sink = 0;
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    var site = new TerrainQuadSite(x, y, minX + x, minY + y, 1f);
                    TerrainQuadResult result = TerrainQuadBuilder.FillQuad(
                        sources,
                        site,
                        layer,
                        quad);
                    sink += result.AtlasIndex + (result.IsDoor ? 1 : 0);
                }
            }

            System.Threading.Volatile.Write(ref _quadFillSink, sink);
        });

        if (runner.Results.Count > previousResultCount)
        {
            BenchResult result = runner.Results[^1];
            runner.Metric(
                $"FillQuad · {name}, наносекунд на клетку",
                result.P50Ms * 1_000_000.0 / (width * (double)height),
                "нс/клетку");
        }
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
                    sink += MapCellConfigCatalog.IsRoundableLoose(type) ? 1 : 0;
                    sink += MapCellConfigCatalog.IsRoad(type) ? 1 : 0;
                    sink += TerrainSheetCatalog.IsContinuousSheet(type) ? 1 : 0;
                    sink += TerrainReliefRimCatalog.GetFamily(type) != TerrainRimFamily.None ? 1 : 0;
                    sink += TerrainDecalCatalog.IsGroundSurface(type) ? 1 : 0;
                    sink += (int)TerrainAnimationProfileCatalog.Get(type, 1f).Profile;
                }
            }

            GC.KeepAlive(sink);
        });

        var roundable = new bool[65536];
        var road = new bool[65536];
        var sheet = new bool[65536];
        var rim = new bool[65536];
        var ground = new bool[65536];
        var profile = new int[65536];
        for (int value = 0; value < 65536; value++)
        {
            var type = (CellType)value;
            roundable[value] = MapCellConfigCatalog.IsRoundableLoose(type);
            road[value] = MapCellConfigCatalog.IsRoad(type);
            sheet[value] = TerrainSheetCatalog.IsContinuousSheet(type);
            rim[value] = TerrainReliefRimCatalog.GetFamily(type) != TerrainRimFamily.None;
            ground[value] = TerrainDecalCatalog.IsGroundSurface(type);
            profile[value] = (int)TerrainAnimationProfileCatalog.Get(type, 1f).Profile;
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
                    sink += road[type] ? 1 : 0;
                    sink += sheet[type] ? 1 : 0;
                    sink += rim[type] ? 1 : 0;
                    sink += ground[type] ? 1 : 0;
                    sink += profile[type];
                }
            }

            GC.KeepAlive(sink);
        });
    }

    // ── Индекс типов клеток (настоящий CellTypeSpatialIndex) ─────────────
    //
    // Заплатка зовёт Set на ЗАПОЛНЕННОМ индексе, где тип почти всегда тот же;
    // полная сборка — после Clear, на пустом. Контрольный замер рядом показывает
    // цену того же прохода через словарь: ради неё обратная сторона индекса и
    // лежит плотным массивом по кольцевому адресу.
    private static void CellTypeIndex(BenchRunner runner, int width, int height)
    {
        runner.Suite = "cell-type-index";
        var random = new Random(Seed);
        var types = new CellType[width * height];
        for (int i = 0; i < types.Length; i++)
        {
            types[i] = (CellType)(1 + random.Next(24));
        }

        var index = new CellTypeSpatialIndex();
        index.EnsureWindow(width, height);
        void Fill()
        {
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    index.Set(
                        TerrainCoordinateKey.Pack(5000 + x, 7000 + y),
                        types[(x * height) + y]);
                }
            }
        }

        runner.Run("Set во всё окно после Clear (полная сборка)", Fill, index.Clear);
        Fill();
        runner.Run("Set во всё окно поверх заполненного (заплатка)", Fill);
        // Обратная сторона сделки: запись стала двумя записями в массив, а
        // вопрос «какие клетки этих типов» — проходом по окну. Проход платится
        // по приходу текстуры, запись — тысячами в каждом кадре.
        var wanted = new HashSet<CellType> { (CellType)3, (CellType)17, (CellType)42 };
        var collected = new List<(long Key, CellType Type)>(4096);
        runner.Run("проход по окну за клетками трёх типов", () =>
        {
            collected.Clear();
            index.CollectEntries(wanted, collected);
        });

        var alternate = new CellType[types.Length];
        for (int i = 0; i < types.Length; i++)
        {
            alternate[i] = (CellType)(1 + ((int)types[i] % 24));
        }

        bool flip = false;
        runner.Run("Set во всё окно со сменой типа каждой клетки", () =>
        {
            CellType[] source = flip ? types : alternate;
            flip = !flip;
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    index.Set(TerrainCoordinateKey.Pack(5000 + x, 7000 + y), source[(x * height) + y]);
                }
            }
        });

        Fill();
        var probe = new Dictionary<long, CellType>(width * height);
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                probe[TerrainCoordinateKey.Pack(5000 + x, 7000 + y)] = types[(x * height) + y];
            }
        }

        runner.Run("контроль: один поиск по обратному словарю на клетку", () =>
        {
            int hits = 0;
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (probe.TryGetValue(TerrainCoordinateKey.Pack(5000 + x, 7000 + y), out CellType t) &&
                        t == types[(x * height) + y])
                    {
                        hits++;
                    }
                }
            }

            GC.KeepAlive(hits);
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
            var color = new Color32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255);
            var atlasRect = new Vector4(random.NextSingle(), random.NextSingle(), 0.0625f, 0.0625f);
            for (int i = 0; i < 4; i++)
            {
                ref TerrainVertex vertex = ref vertices[(quad * 4) + i];
                vertex.Position = new Vector3(i, i, 0);
                vertex.Color = color;
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

    // Клетки окна для заливки фона: локальные (x, y) со смещением каймы кэша.
    private sealed class CaveProvider(int width, int height, int seed) : ICachedCellDataProvider
    {
        public int OriginX { get; set; } = 5000;

        public int OriginY { get; set; } = 7000;

        public int Width => width;

        public int Height => height;

        public CachedCellInfo GetCell(int x, int y)
        {
            CachedCellData cell = TerrainCellCache.CellAt(OriginX + x - 1, OriginY + y - 1, seed);
            return new CachedCellInfo { Type = cell.Type, Properties = cell.Properties };
        }
    }

    private sealed class PrecomputedCaveProvider : ICachedCellDataProvider
    {
        private readonly CachedCellInfo[] _cells;
        private readonly int _width;
        private readonly int _height;

        public PrecomputedCaveProvider(int width, int height, int seed)
        {
            _width = width;
            _height = height;
            _cells = new CachedCellInfo[width * height];
            const int OriginX = 5000;
            const int OriginY = 7000;
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    CachedCellData cell = TerrainCellCache.CellAt(OriginX + x, OriginY + y, seed);
                    _cells[(x * height) + y] = new CachedCellInfo
                    {
                        Type = cell.Type,
                        Properties = cell.Properties,
                    };
                }
            }
        }

        public CachedCellInfo GetCell(int x, int y)
        {
            int localX = x - 1;
            int localY = y - 1;
            if ((uint)localX >= (uint)_width || (uint)localY >= (uint)_height)
            {
                return new CachedCellInfo { Type = CellType.Unloaded };
            }

            return _cells[(localX * _height) + localY];
        }
    }

    // Семь массивов текселей в раскладке TerrainCellDataTextures.
    private sealed class TexelArrays
    {
        public readonly Color32[] Color;
        public readonly Color32[] Meta;
        public readonly TerrainHalfTexel[] AtlasRect;
        public readonly TerrainHalfTexel[] TileSize;
        public readonly TerrainHalfTexel[] Animation;
        public readonly Vector4[] World;
        public readonly Vector4[] Glow;
        private readonly Color32[] _patchColor = new Color32[1024];
        private readonly Vector4[] _patchWorld = new Vector4[1024];
        private readonly TerrainHalfTexel[] _patchHalf = new TerrainHalfTexel[1024];

        public byte[] Staging => _staging ??= new byte[TotalBytes];

        private byte[]? _staging;

        public TexelArrays(int width, int height)
        {
            int count = width * height * TerrainCellDataPacker.LayersPerCell;
            Color = new Color32[count];
            Meta = new Color32[count];
            AtlasRect = new TerrainHalfTexel[count];
            TileSize = new TerrainHalfTexel[count];
            Animation = new TerrainHalfTexel[count];
            World = new Vector4[count];
            Glow = new Vector4[count];
        }

        public int TotalBytes => (Color.Length * 4 * 2) + (AtlasRect.Length * 8 * 3) + (World.Length * 16 * 2);

        public void PackCell(TerrainVertex[] vertices, int x, int y, int width, int height) =>
            PackCellAt(vertices, x, y, x, y, width, height);

        public void PackCellAt(TerrainVertex[] vertices, int x, int y, int ringX, int ringY, int width, int height)
        {
            int quad = (x * height) + y;
            for (int layer = 0; layer < TerrainCellDataPacker.LayersPerCell; layer++)
            {
                TerrainCellTexels texels = TerrainCellDataPacker.PackQuad(vertices.AsSpan((quad * 8) + (layer * 4), 4), 0);
                int index = TerrainCellDataPacker.TexelIndex(ringX, ringY, layer, width);
                Color[index] = texels.Color;
                Meta[index] = texels.Meta;
                AtlasRect[index] = texels.AtlasRect;
                TileSize[index] = texels.TileSize;
                Animation[index] = texels.Animation;
                World[index] = texels.World;
                Glow[index] = texels.Glow;
            }
        }

        public void CopyAllTo(byte[] target)
        {
            int offset = 0;
            offset += CopyBytes(Color, target, offset);
            offset += CopyBytes(Meta, target, offset);
            offset += CopyBytes(AtlasRect, target, offset);
            offset += CopyBytes(TileSize, target, offset);
            offset += CopyBytes(Animation, target, offset);
            offset += CopyBytes(World, target, offset);
            CopyBytes(Glow, target, offset);
        }

        public void CopyRowSpan(int row, int x, int count, int width)
        {
            int start = (row * width) + x;
            Array.Copy(Color, start, _patchColor, 0, count);
            Array.Copy(Meta, start, _patchColor, 0, count);
            Array.Copy(AtlasRect, start, _patchHalf, 0, count);
            Array.Copy(TileSize, start, _patchHalf, 0, count);
            Array.Copy(Animation, start, _patchHalf, 0, count);
            Array.Copy(World, start, _patchWorld, 0, count);
            Array.Copy(Glow, start, _patchWorld, 0, count);
        }

        private static int CopyBytes<T>(T[] source, byte[] target, int offset)
            where T : struct
        {
            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(source.AsSpan());
            bytes.CopyTo(target.AsSpan(offset));
            return bytes.Length;
        }
    }
}

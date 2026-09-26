#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Interfaces.Diagnostics;
using Kern.World.Terrain.Background;
using MinesServer.Data;
using Unity.Profiling;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>
/// Путь клетки от данных мира до текселя: кэш → предрасчёт → заливка фона →
/// тексели.
/// </summary>
///
/// Четыре стадии всегда идут вместе и в этом порядке: маски соседства читают
/// кэш, заливка читает кэш, тексель читает и маски, и заливку. Поэтому они
/// живут одним типом, а не четырьмя полями рендерера, и каждая стадия имеет
/// ровно три входа — полный проход, сдвиг окна и заплатка по прямоугольнику.
///
/// Работа разделена по потокам ровно по границе живого мира. Кэш клеток
/// читает хранилище и разрешает типы — это главный поток, <see cref="Prepare"/>.
/// Всё остальное считает только из кэша и снимков атласов — это рабочий
/// поток, <see cref="Execute"/>. Между ними владение передаётся целиком:
/// пока шаг идёт, главный поток не трогает ни одну из четырёх стадий.
public sealed class TerrainBuildPipeline : IDisposable
{
    private static readonly ProfilerMarker _CacheMarker = new("Kern.Terrain.Cache");

    private readonly TerrainCellCache _cellCache = new();
    private readonly TerrainPrecalculator _precalc = new();
    private readonly BackgroundFloodFill _floodFill = new();
    private readonly TerrainCellBuilder _cellBuilder = new();
    private readonly List<IAtlasDescriptor> _snapshotSources = [];
    private IAtlasDescriptor[] _atlasSnapshots = [];
    private ulong _atlasRevision;

    public TerrainCellCache CellCache => _cellCache;

    public TerrainCellBuilder CellBuilder => _cellBuilder;

    internal TerrainPrecalculator Precalculator => _precalc;

    public bool EnableDistortion
    {
        get => _precalc.EnableDistortion;
        set => _precalc.EnableDistortion = value;
    }

    public TerrainDistortionStyle DistortionStyle
    {
        get => _precalc.DistortionStyle;
        set => _precalc.DistortionStyle = value;
    }

    /// <summary>Последний опубликованный шаг перенёс перекрытие вместо полной сборки.</summary>
    public bool LastBuildScrolled { get; private set; }

    /// <summary>
    /// На сколько клеток переехало окно в последнем шаге. Накладка дверей
    /// компенсирует этим сдвиг своего родителя, когда состав дверей не менялся.
    /// </summary>
    public Vector2Int LastScrollDelta { get; private set; }

    /// <summary>Сколько стоил рабочему потоку последний опубликованный шаг.</summary>
    public TerrainWorkerCost LastWorkerCost { get; private set; }

    public ulong AtlasRevision => _atlasRevision;

    public void EnsureCapacity(int meshWidth, int meshHeight, float cellSize)
    {
        _cellCache.EnsureCapacity(meshWidth, meshHeight);
        _precalc.EnsureCapacity(meshWidth, meshHeight);
        _cellBuilder.EnsureCapacity(meshWidth, meshHeight, cellSize);
        _floodFill.Allocate(meshWidth, meshHeight);
    }

    /// <summary>Источники для главного потока: накладка дверей после публикации.</summary>
    internal TerrainCellSources CreateSources(TerrainCpuBuildRequest request) =>
        new(
            _cellCache,
            _precalc,
            _floodFill,
            request.WorldWidth,
            request.WorldHeight,
            request.Atlases);

    /// <summary>
    /// Главный поток: довести кэш клеток до шага и составить задание рабочему.
    /// </summary>
    ///
    /// Порядок внутри кэша: сначала метаданные приехавших текстур, потом сдвиг
    /// или полное заполнение, потом изменённые клетки. Изменения берутся в
    /// мировых координатах и после сдвига ложатся в новые адреса; рабочий
    /// поток пересчитывает их тоже после сдвига, поэтому порядок совпадает.
    ///
    /// <param name="forceFull">Перекрытие переносить нельзя: содержимое окна изменилось целиком.</param>
    /// <param name="rebuildAllCells">
    /// Тексели собираются целиком, хотя кэш переносится: сменился набор
    /// атласов (индексы в текселях посчитаны по старому) или изменений так
    /// много, что заплатки дороже полной сборки. Изменённые клетки всё равно
    /// перечитываются в кэш только по своим прямоугольникам.
    /// </param>
    internal TerrainCpuBuildRequest Prepare(
        in TerrainBuildContext context,
        IReadOnlyList<IAtlasDescriptor> atlases,
        Vector2Int origin,
        bool forceFull,
        bool rebuildAllCells,
        DirtyRectSet dirtyRects,
        HashSet<CellType> textureTypes,
        ulong contentRevision,
        long worldGeneration)
    {
        IFrameTelemetry telemetry = context.Telemetry;
        int minX = origin.x;
        int minY = origin.y;
        int cacheDeltaX = (minX - 1) - _cellCache.CacheMinX;
        int cacheDeltaY = (minY - 1) - _cellCache.CacheMinY;
        bool canScrollCache =
            !forceFull &&
            _cellCache.CacheMinX != int.MinValue &&
            Math.Abs(cacheDeltaX) < _cellCache.CacheWidth &&
            Math.Abs(cacheDeltaY) < _cellCache.CacheHeight;
        Vector2Int scrollDelta = canScrollCache
            ? new Vector2Int(cacheDeltaX, cacheDeltaY)
            : Vector2Int.zero;
        telemetry.TerrainRebuildCount++;

        // Главный поток только снимает: байты типов из хранилища и
        // метаданные встретившихся типов. В кольцо кэша снятое раскладывает
        // рабочий поток в начале шага (ApplyPendingCapture).
        long cacheStart = Stopwatch.GetTimestamp();
        using (_CacheMarker.Auto())
        {
            if (textureTypes.Count > 0 && _cellCache.CacheMinX != int.MinValue)
            {
                _cellCache.CaptureTextureRefresh(
                    textureTypes, context.MapData, context.TextureService, atlases);
            }

            if (!canScrollCache)
            {
                telemetry.TerrainFullPopulateCount++;
                _cellCache.CaptureFull(
                    minX, minY, context.Storage, context.MapData,
                    context.TextureService, atlases);
            }
            else if (scrollDelta != Vector2Int.zero)
            {
                _cellCache.CaptureScroll(
                    cacheDeltaX, cacheDeltaY, context.Storage, context.MapData,
                    context.TextureService, atlases);
            }

            if (canScrollCache)
            {
                for (int index = 0; index < dirtyRects.Count; index++)
                {
                    RectInt rect = dirtyRects[index];
                    _cellCache.CaptureRegion(
                        rect.xMin - 1,
                        rect.yMin - 1,
                        rect.width + 2,
                        rect.height + 2,
                        context.Storage,
                        context.MapData,
                        context.TextureService,
                        atlases);
                }
            }

            _cellCache.ResolveCapturedTypes(
                context.MapData, context.TextureService, atlases);
        }

        telemetry.TerrainCacheTimeMs += ElapsedMs(cacheStart);

        IReadOnlyList<IAtlasDescriptor> atlasSnapshots = CaptureAtlases(
            atlases,
            textureTypes.Count > 0);

        bool buildFull = !canScrollCache || rebuildAllCells;
        TerrainWorldCellRegion[] regions = buildFull
            ? []
            : new TerrainWorldCellRegion[dirtyRects.Count];
        for (int index = 0; index < regions.Length; index++)
        {
            regions[index] = new TerrainWorldCellRegion(dirtyRects[index]);
        }

        if (regions.Length > 0)
        {
            telemetry.TerrainDirtyPatchCount++;
        }

        return new TerrainCpuBuildRequest(
            origin: origin,
            size: new Vector2Int(context.MeshWidth, context.MeshHeight),
            cacheScrolled: canScrollCache,
            scrollDelta: scrollDelta,
            buildFull: buildFull,
            atlases: atlasSnapshots,
            worldWidth: context.MapData.WorldWidth,
            worldHeight: context.MapData.WorldHeight,
            dirtyRegions: regions,
            textureTypes: buildFull || textureTypes.Count == 0
                ? default
                : TerrainCellTypeSet.Capture(textureTypes),
            contentRevision: contentRevision,
            worldGeneration: worldGeneration,
            atlasRevision: _atlasRevision);
    }

    /// <summary>
    /// Рабочий поток: предрасчёт, заливка и тексели по уже заполненному кэшу.
    /// Ни хранилища, ни сервисов, ни Unity-объектов здесь нет.
    /// </summary>
    ///
    /// Отмена между стадиями оставляет стадии рассогласованными, поэтому
    /// владелец после отмены обязан собрать окно целиком.
    internal TerrainCpuBuildResult Execute(
        TerrainCpuBuildRequest request,
        CancellationToken cancellationToken)
    {
        long start = Stopwatch.GetTimestamp();
        var result = new TerrainCpuBuildResultBuilder();
        int minX = request.Origin.x;
        int minY = request.Origin.y;
        TerrainCellSources sources = new(
            _cellCache,
            _precalc,
            _floodFill,
            request.WorldWidth,
            request.WorldHeight,
            request.Atlases);
        var precalculation = new TerrainPrecalculationInput(
            _cellCache,
            request.Size,
            new Vector2Int(request.WorldWidth, request.WorldHeight));

        long cacheStart = Stopwatch.GetTimestamp();
        _cellCache.ApplyPendingCapture();
        result.CacheMs = ElapsedMs(cacheStart);

        // Шаг из одних изменённых клеток кэш не двигал: полосы нет, и
        // приращение предрасчёта и заливки было бы пустым проходом.
        bool moved = request.BuildFull || request.ScrollDelta != Vector2Int.zero;
        if (moved)
        {
            RunWindowStages(request, precalculation, sources, result, cancellationToken);
        }

        for (int index = 0; index < request.DirtyRegions.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Прямоугольник снят в координатах окна до сдвига: после сдвига он
            // может частично или целиком выйти за окно. Вышедшая часть уехала
            // вместе с окном, вошедшая уже собрана полосой из свежего кэша.
            TerrainWindowCellRegion region = request.DirtyRegions[index].ToWindowLocal(
                request.Origin,
                request.Size,
                neighbourHalo: 1);
            if (region.IsEmpty)
            {
                continue;
            }

            long precalculateStart = Stopwatch.GetTimestamp();
            _precalc.PrecalculateRegion(
                precalculation,
                region);
            result.PrecalculateMs += ElapsedMs(precalculateStart);

            long floodStart = Stopwatch.GetTimestamp();
            _floodFill.UpdateLocalRegion(
                region.StartX, region.StartY, region.CountX, region.CountY, _cellCache);
            result.FloodFillMs += ElapsedMs(floodStart);

            long meshStart = Stopwatch.GetTimestamp();
            _cellBuilder.BuildRegion(
                sources,
                minX,
                minY,
                region.StartX,
                region.StartY,
                region.CountX,
                region.CountY);
            result.DoorsTouched |= _cellBuilder.DoorsTouched;
            result.AddBuilderStages(_cellBuilder);
            result.MeshMs += ElapsedMs(meshStart);
        }

        if (!request.TextureTypes.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long meshStart = Stopwatch.GetTimestamp();
            _cellBuilder.BuildTextureCells(request.TextureTypes, sources, minX, minY);
            result.DoorsTouched |= _cellBuilder.DoorsTouched;
            result.AddBuilderStages(_cellBuilder);
            result.MeshMs += ElapsedMs(meshStart);
        }

        result.ElapsedMs = ElapsedMs(start);
        return result.Build();
    }

    private void RunWindowStages(
        TerrainCpuBuildRequest request,
        in TerrainPrecalculationInput precalculation,
        in TerrainCellSources sources,
        TerrainCpuBuildResultBuilder result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Полная сборка текселей идёт по полному предрасчёту и заливке: после
        // пачки изменений приращение по сдвигу не покрыло бы изменённые
        // клетки, а рабочему потоку полный проход ничего не стоит в кадре.
        bool incremental = request.CacheScrolled && !request.BuildFull;
        long precalculateStart = Stopwatch.GetTimestamp();
        if (incremental)
        {
            _precalc.PrecalculateIncremental(
                precalculation,
                request.ScrollDelta);
        }
        else
        {
            _precalc.PrecalculateFull(
                precalculation);
        }

        result.PrecalculateMs += ElapsedMs(precalculateStart);
        cancellationToken.ThrowIfCancellationRequested();

        // Тем же сдвигом, что кэш и предрасчёт выше: иначе на каждом переходе
        // через границу региона заливка одна платила по площади за то, что
        // сдвинулось на кайму.
        long floodStart = Stopwatch.GetTimestamp();
        if (incremental)
        {
            _floodFill.ComputeScrolled(request.ScrollDelta.x, request.ScrollDelta.y, _cellCache);
        }
        else
        {
            _floodFill.ComputeFull(_cellCache);
        }

        result.FloodFillMs += ElapsedMs(floodStart);
        cancellationToken.ThrowIfCancellationRequested();

        // Тексели лежат по кольцевому адресу и при сдвиге не двигаются:
        // собирается только вошедшая полоса.
        long meshStart = Stopwatch.GetTimestamp();
        if (request.BuildFull)
        {
            _cellBuilder.BuildFull(sources, request.Origin.x, request.Origin.y, cancellationToken);
            result.DoorsTouched = true;
        }
        else
        {
            _cellBuilder.ScrollAndBuildBand(
                sources,
                request.Origin.x,
                request.Origin.y,
                request.ScrollDelta.x,
                request.ScrollDelta.y);
            result.DoorsTouched |= _cellBuilder.DoorsTouched;
        }

        result.AddBuilderStages(_cellBuilder);
        result.MeshMs += ElapsedMs(meshStart);
    }

    // Снимки атласов переиспользуются между шагами. Содержимое атласа
    // меняется только приездом текстуры, а каждый приезд попадает в шаг как
    // тип на перечитывание, — тогда снимок снимается заново. Прежний массив
    // не трогается: его может держать ещё не опубликованный шаг.
    private IReadOnlyList<IAtlasDescriptor> CaptureAtlases(
        IReadOnlyList<IAtlasDescriptor> atlases,
        bool contentMayHaveChanged)
    {
        bool sameSet = !contentMayHaveChanged && _snapshotSources.Count == atlases.Count;
        for (int index = 0; sameSet && index < atlases.Count; index++)
        {
            sameSet = ReferenceEquals(_snapshotSources[index], atlases[index]);
        }

        if (sameSet)
        {
            return _atlasSnapshots;
        }

        if (_atlasRevision == ulong.MaxValue)
        {
            throw new InvalidOperationException("Terrain atlas revision overflowed.");
        }

        var snapshots = new IAtlasDescriptor[atlases.Count];
        for (int index = 0; index < snapshots.Length; index++)
        {
            snapshots[index] = TerrainAtlasSnapshot.Capture(atlases[index]);
        }

        _snapshotSources.Clear();
        for (int index = 0; index < atlases.Count; index++)
        {
            _snapshotSources.Add(atlases[index]);
        }

        _atlasSnapshots = snapshots;
        _atlasRevision++;
        return snapshots;
    }

    internal bool IsAtlasSnapshotCurrent(
        TerrainCpuBuildRequest request,
        IReadOnlyList<IAtlasDescriptor> atlases)
    {
        if (request.AtlasRevision != _atlasRevision || atlases.Count != _snapshotSources.Count)
        {
            return false;
        }

        for (int index = 0; index < atlases.Count; index++)
        {
            if (!ReferenceEquals(_snapshotSources[index], atlases[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Главный поток после завершения шага: запомнить, чем он был.</summary>
    internal void RecordPublished(
        TerrainCpuBuildRequest request,
        TerrainCpuBuildResult result,
        float latencyMs)
    {
        LastBuildScrolled = request.CacheScrolled && request.ScrollDelta != Vector2Int.zero;
        LastScrollDelta = request.CacheScrolled ? request.ScrollDelta : Vector2Int.zero;
        TerrainBuildStepKind kind =
            request.BuildFull ? TerrainBuildStepKind.Full
            : request.ScrollDelta != Vector2Int.zero ? TerrainBuildStepKind.Scroll
            : request.DirtyRegions.Length > 0 ? TerrainBuildStepKind.Patch
            : TerrainBuildStepKind.Textures;
        LastWorkerCost = new TerrainWorkerCost(
            kind,
            result.CacheMs,
            result.PrecalculateMs,
            result.FloodFillMs,
            result.MeshMs,
            result.ScrollMs,
            result.WarmupMs,
            result.FillMs,
            result.FilledCells,
            result.QuadMs,
            result.PackMs,
            result.ElapsedMs,
            latencyMs);
    }

    /// <summary>
    /// Одна выгрузка текселей за кадр, после публикации шага. Начало окна
    /// публикуется вместе с ними: шейдер берёт по нему кольцевой адрес.
    /// </summary>
    public float Commit(int originX, int originY)
    {
        long start = Stopwatch.GetTimestamp();
        _cellBuilder.Commit(originX, originY);
        return ElapsedMs(start);
    }

    public void Dispose() => _cellBuilder.Dispose();

    private static float ElapsedMs(long startTimestamp) =>
        (float)((Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency);
}

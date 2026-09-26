#nullable enable

using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Interfaces.Diagnostics;
using Kern.World.Streaming;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>All inputs required to decide the active terrain window for one frame.</summary>
public readonly record struct TerrainFramePlanningInput(
    Camera Camera,
    Vector3 FocusPosition,
    float CellSize,
    int ViewportPadding,
    int RequiredLightingPadding,
    int StableRegionPadding,
    Vector2Int CommittedOrigin,
    int MeshWidth,
    int MeshHeight,
    bool IsInitialized,
    bool CellsCommitted,
    bool CpuBuildInFlight,
    float SpeedCellsPerSecond,
    float PreparationLatencySeconds,
    RectInt RetainedLightingViewport,
    bool AllowPartialAdvance,
    bool HoldingPublishedView,
    IWorldDataStorage? Storage,
    IMapDataProvider? MapData,
    IConnectionService? ConnectionService,
    IFrameTelemetry Telemetry);

/// <summary>
/// Решение о кадре: какое окно просит камера, какое уже собрано и с каким
/// кадр работает на самом деле.
/// </summary>
///
/// Собрано здесь в одном месте, потому что решение принимается до любой
/// работы и по трём независимым входам: размер окна (его диктует освещение, а
/// не зум), положение окна (его двигает governor стриминга) и наличие данных
/// на диске. Телеметрия плана пишется тут же — она описывает решение, а не его
/// последствия.
public sealed class TerrainFramePlanner
{
    private readonly TerrainViewportCalculator _viewport = new();
    private readonly TerrainResidencyProbe.FrameCache _residencyCache = new();
    private int _lastResidencyProbeCalls;
    private int _lastResidencyChunkReads;
    private int _lastResidencyCacheHits;
    private int _lastResidencyLruTouches;

    public StreamingPolicy Policy => _viewport.Policy;

    public int LastResidencyProbeCalls => _lastResidencyProbeCalls;

    public int LastResidencyChunkReads => _lastResidencyChunkReads;

    public int LastResidencyCacheHits => _lastResidencyCacheHits;

    public int LastResidencyLruTouches => _lastResidencyLruTouches;

    public TerrainFramePlan Plan(in TerrainFramePlanningInput input)
    {
        _viewport.CalculateDimensions(
            input.Camera,
            input.CellSize,
            input.ViewportPadding,
            input.RequiredLightingPadding,
            input.StableRegionPadding,
            input.MeshWidth,
            input.MeshHeight,
            input.IsInitialized,
            out int targetWidth,
            out int targetHeight,
            out int effectivePadding,
            out int requestedWidth,
            out int requestedHeight,
            out bool dimensionsChanged);

        Vector2Int requestedOrigin = _viewport.ResolveGridPosition(
            input.Camera,
            input.FocusPosition,
            input.CellSize,
            targetWidth,
            targetHeight,
            requestedWidth,
            requestedHeight,
            effectivePadding,
            dimensionsChanged,
            input.CommittedOrigin,
            input.SpeedCellsPerSecond,
            input.PreparationLatencySeconds,
            centerOnFocus: !input.AllowPartialAdvance,
            out int viewportMinX,
            out int viewportMinY,
            out int viewportWidth,
            out int viewportHeight);

        PublishStreamingTelemetry(input.Telemetry, _viewport.LastPlan);

        var requestedWindow = new StreamingWindow(
            requestedOrigin,
            new Vector2Int(targetWidth, targetHeight));
        var committedWindow = new StreamingWindow(
            input.CommittedOrigin,
            new Vector2Int(input.MeshWidth, input.MeshHeight));
        var cameraViewport = new RectInt(
            viewportMinX,
            viewportMinY,
            viewportWidth,
            viewportHeight);

        _lastResidencyProbeCalls = 1;
        _lastResidencyChunkReads = 0;
        _lastResidencyCacheHits = 0;
        _lastResidencyLruTouches = 0;
        bool isRequestedResident = TerrainResidencyProbe.IsWindowResident(
            input.Storage,
            input.MapData,
            input.ConnectionService,
            requestedWindow.Origin,
            requestedWindow.Size.x,
            requestedWindow.Size.y);

        // Запрошенное место ещё не приехало — значит едем настолько, насколько
        // приехало, а не стоим и не прыгаем потом целиком.
        //
        // Кроме перехода вида (телепорт): там промежуточное окно никому не
        // видно, а каждый шаг к нему — полная сборка впустую. Ждём место
        // назначения целиком.
        if (input.AllowPartialAdvance && !isRequestedResident &&
            input.CommittedOrigin.x != int.MinValue && !dimensionsChanged)
        {
            Vector2Int requestedSize = requestedWindow.Size;
            _residencyCache.BeginFrame(input.Storage, input.MapData, requestedSize.x, requestedSize.y);
            Vector2Int reachable = TerrainWindowAdvance.Resolve(
                input.CommittedOrigin,
                requestedWindow.Origin,
                requestedSize.x,
                requestedSize.y,
                _residencyCache.ResidencyCallback);
            if (reachable != input.CommittedOrigin)
            {
                requestedWindow = new StreamingWindow(reachable, requestedWindow.Size);
                isRequestedResident = true;
                TerrainResidencyProbe.TouchWindow(
                    input.Storage,
                    input.MapData,
                    reachable,
                    requestedSize.x,
                    requestedSize.y,
                    _residencyCache);
            }

            _lastResidencyProbeCalls += _residencyCache.ProbeCalls;
            _lastResidencyChunkReads = _residencyCache.ChunkReads;
            _lastResidencyCacheHits = _residencyCache.CacheHits;
            _lastResidencyLruTouches = _residencyCache.LruTouches;
        }

        return _viewport.SelectFramePlan(
            requestedWindow,
            committedWindow,
            cameraViewport,
            input.RetainedLightingViewport,
            isRequestedResident,
            TerrainResidencyProbe.HasAnyResidentData(
                storage,
                mapData,
                requestedWindow.Origin,
                requestedWindow.Size.x,
                requestedWindow.Size.y),
            dimensionsChanged,
            input.CellsCommitted,
            input.CpuBuildInFlight,
            input.HoldingPublishedView);
    }

    private static void PublishStreamingTelemetry(IFrameTelemetry telemetry, StreamingPlan plan)
    {
        telemetry.StreamingPlanKind = (int)plan.Kind;
        telemetry.StreamingWindowOriginX = plan.Target.Origin.x;
        telemetry.StreamingWindowOriginY = plan.Target.Origin.y;
        telemetry.StreamingWindowWidth = plan.Target.Size.x;
        telemetry.StreamingWindowHeight = plan.Target.Size.y;
        telemetry.StreamingDeltaX = plan.Delta.x;
        telemetry.StreamingDeltaY = plan.Delta.y;
    }
}

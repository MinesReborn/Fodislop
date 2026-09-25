#nullable enable

using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Interfaces.Diagnostics;
using Kern.World.Streaming;
using UnityEngine;

namespace Kern.World.Terrain;

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

    public StreamingPolicy Policy => _viewport.Policy;

    public TerrainFramePlan Plan(
        Camera camera,
        Vector3 focusPosition,
        float cellSize,
        int viewportPadding,
        int requiredLightingPadding,
        int stableRegionPadding,
        Vector2Int committedOrigin,
        int meshWidth,
        int meshHeight,
        bool isInitialized,
        bool cellsCommitted,
        bool cpuBuildInFlight,
        float speedCellsPerSecond,
        float preparationLatencySeconds,
        RectInt retainedLightingViewport,
        bool allowPartialAdvance,
        bool holdingPublishedView,
        IWorldDataStorage? storage,
        IMapDataProvider? mapData,
        IConnectionService? connectionService,
        IFrameTelemetry telemetry)
    {
        _viewport.CalculateDimensions(
            camera,
            cellSize,
            viewportPadding,
            requiredLightingPadding,
            stableRegionPadding,
            meshWidth,
            meshHeight,
            isInitialized,
            out int targetWidth,
            out int targetHeight,
            out int effectivePadding,
            out int requestedWidth,
            out int requestedHeight,
            out bool dimensionsChanged);

        Vector2Int requestedOrigin = _viewport.ResolveGridPosition(
            camera,
            focusPosition,
            cellSize,
            targetWidth,
            targetHeight,
            requestedWidth,
            requestedHeight,
            effectivePadding,
            dimensionsChanged,
            committedOrigin,
            speedCellsPerSecond,
            preparationLatencySeconds,
            centerOnFocus: !allowPartialAdvance,
            out int viewportMinX,
            out int viewportMinY,
            out int viewportWidth,
            out int viewportHeight);

        PublishStreamingTelemetry(telemetry, _viewport.LastPlan);

        var requestedWindow = new StreamingWindow(
            requestedOrigin,
            new Vector2Int(targetWidth, targetHeight));
        var committedWindow = new StreamingWindow(
            committedOrigin,
            new Vector2Int(meshWidth, meshHeight));
        var cameraViewport = new RectInt(
            viewportMinX,
            viewportMinY,
            viewportWidth,
            viewportHeight);

        bool isRequestedResident = TerrainResidencyProbe.IsWindowResident(
            storage,
            mapData,
            connectionService,
            requestedWindow.Origin,
            requestedWindow.Size.x,
            requestedWindow.Size.y);

        // Запрошенное место ещё не приехало — значит едем настолько, насколько
        // приехало, а не стоим и не прыгаем потом целиком.
        //
        // Кроме перехода вида (телепорт): там промежуточное окно никому не
        // видно, а каждый шаг к нему — полная сборка впустую. Ждём место
        // назначения целиком.
        if (allowPartialAdvance && !isRequestedResident && committedOrigin.x != int.MinValue && !dimensionsChanged)
        {
            Vector2Int reachable = TerrainWindowAdvance.Resolve(
                committedOrigin,
                requestedWindow.Origin,
                requestedWindow.Size.x,
                requestedWindow.Size.y,
                origin => TerrainResidencyProbe.IsWindowResident(
                    storage, mapData, origin, requestedWindow.Size.x, requestedWindow.Size.y));
            if (reachable != committedOrigin)
            {
                requestedWindow = new StreamingWindow(reachable, requestedWindow.Size);
                isRequestedResident = true;
            }
        }

        return _viewport.SelectFramePlan(
            requestedWindow,
            committedWindow,
            cameraViewport,
            retainedLightingViewport,
            isRequestedResident,
            dimensionsChanged,
            cellsCommitted,
            cpuBuildInFlight,
            holdingPublishedView);
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

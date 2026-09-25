#nullable enable

using Kern.Core;
using Kern.World.Streaming;
using UnityEngine;

namespace Kern.World.Terrain;

public sealed class TerrainViewportCalculator
{
    private readonly StreamingGovernor _streamingGovernor =
        new(StreamingPolicy.Default);
    private bool _invalidGridPositionLogged;

    public StreamingPolicy Policy => _streamingGovernor.Policy;

    public StreamingPlan LastPlan { get; private set; }

    public void CalculateDimensions(
        Camera camera,
        float cellSize,
        int baseViewportPadding,
        int requiredLightingPadding,
        int stableRegionPadding,
        int currentMeshWidth,
        int currentMeshHeight,
        bool isInitialized,
        out int meshWidth,
        out int meshHeight,
        out int effectivePadding,
        out int requestedWidth,
        out int requestedHeight,
        out bool dimensionsChanged)
    {
        StreamingPolicy policy = _streamingGovernor.Policy;
        effectivePadding = policy.ResolveEffectivePadding(
            baseViewportPadding,
            requiredLightingPadding,
            stableRegionPadding);

        // Сетка считается на кадр максимального отдаления, а не текущий кадр.
        // Она же — поле препятствий и регион освещения: при размере от зума
        // приближение давало сетку меньше области света, за её краем поле было
        // пустым, и свет перестраивался при каждой перепривязке сетки.
        float sizingOrthographicSize = Mathf.Max(
            camera.orthographicSize,
            ProjectRuntimeContracts.Camera.MaximumOrthographicSize);
        int rawRequestedWidth = Mathf.CeilToInt(
            (sizingOrthographicSize * 2 * camera.aspect) / cellSize) + (effectivePadding * 2);
        int rawRequestedHeight = Mathf.CeilToInt(
            (sizingOrthographicSize * 2) / cellSize) + (effectivePadding * 2);
        requestedWidth = policy.QuantizeDimensionWithHeadroom(rawRequestedWidth);
        requestedHeight = policy.QuantizeDimensionWithHeadroom(rawRequestedHeight);

        int targetWidth = policy.SelectWindowDimension(
            requestedWidth,
            currentMeshWidth,
            isInitialized);
        int targetHeight = policy.SelectWindowDimension(
            requestedHeight,
            currentMeshHeight,
            isInitialized);

        dimensionsChanged = targetWidth != currentMeshWidth || targetHeight != currentMeshHeight;
        meshWidth = targetWidth;
        meshHeight = targetHeight;
    }

    /// <param name="focusPosition">
    /// Центр кадра, под который ставится окно: камера, а во время перехода
    /// вида (телепорт) — место назначения, куда камера встанет.
    /// </param>
    public Vector2Int ResolveGridPosition(
        Camera camera,
        Vector3 focusPosition,
        float cellSize,
        int meshWidth,
        int meshHeight,
        int requestedWidth,
        int requestedHeight,
        int effectivePadding,
        bool dimensionsChanged,
        Vector2Int lastGridPos,
        float speedCellsPerSecond,
        float preparationLatencySeconds,
        bool centerOnFocus,
        out int viewportMinX,
        out int viewportMinY,
        out int viewportWidth,
        out int viewportHeight)
    {
        StreamingPolicy policy = _streamingGovernor.Policy;
        Vector3 camPos = focusPosition;
        Vector2Int desiredGridPos = new(
            Mathf.FloorToInt(camPos.x / cellSize) - (meshWidth / 2),
            Mathf.FloorToInt(camPos.y / cellSize) - (meshHeight / 2));

        // Видимое окно — реальный кадр камеры: рисуется только то, что на экране.
        viewportWidth = Mathf.Clamp(
            Mathf.CeilToInt((camera.orthographicSize * 2 * camera.aspect) / cellSize),
            2,
            Mathf.Max(2, requestedWidth - (effectivePadding * 2)));
        viewportHeight = Mathf.Clamp(
            Mathf.CeilToInt((camera.orthographicSize * 2) / cellSize),
            2,
            Mathf.Max(2, requestedHeight - (effectivePadding * 2)));
        viewportMinX = Mathf.FloorToInt(camPos.x / cellSize) - (viewportWidth / 2);
        viewportMinY = Mathf.FloorToInt(camPos.y / cellSize) - (viewportHeight / 2);

        // Переход вида: окно встаёт сразу на место назначения, а не
        // догоняет его по кванту за шаг, — но только если кадр назначения в
        // окне не помещается: доезжающий робот не должен перезапускать сборку.
        const int PresentationMarginCells = 4;
        bool focusOutsideWindow = lastGridPos.x == int.MinValue ||
            !policy.ContainsViewportWithMargin(
                new Vector2Int(meshWidth, meshHeight),
                new Vector2Int(viewportMinX, viewportMinY) - lastGridPos,
                new Vector2Int(viewportWidth, viewportHeight),
                PresentationMarginCells);
        Vector2Int targetOrigin = _streamingGovernor.SelectTargetOrigin(
            lastGridPos,
            desiredGridPos,
            new Vector2Int(viewportMinX, viewportMinY),
            new Vector2Int(viewportWidth, viewportHeight),
            new Vector2Int(meshWidth, meshHeight),
            dimensionsChanged || (centerOnFocus && focusOutsideWindow),
            reanchorMarginCells: policy.ResolvePrefetchMarginCells(
                new Vector2Int(meshWidth, meshHeight),
                new Vector2Int(viewportWidth, viewportHeight),
                policy.ResolveSpeedLeadCells(speedCellsPerSecond, preparationLatencySeconds)));
        var currentWindow = new StreamingWindow(
            lastGridPos,
            new Vector2Int(meshWidth, meshHeight));
        LastPlan = _streamingGovernor.Plan(
            currentWindow,
            targetOrigin,
            new Vector2Int(meshWidth, meshHeight),
            dimensionsChanged);
        Vector2Int currentGridPos = LastPlan.Target.Origin;

        if (currentGridPos.x == int.MinValue || currentGridPos.y == int.MinValue)
        {
            if (!_invalidGridPositionLogged)
            {
                _invalidGridPositionLogged = true;
                Debug.LogWarning(
                    $"[TerrainViewportCalculator] Invalid terrain grid position {currentGridPos}. " +
                    $"Camera position={camPos}; desired grid={desiredGridPos}; " +
                    $"last grid={lastGridPos}; dimensions={meshWidth}x{meshHeight}.");
            }

            currentGridPos = desiredGridPos;
            LastPlan = new StreamingPlan(
                StreamingPlanKind.FullRebuild,
                currentWindow,
                new StreamingWindow(currentGridPos, new Vector2Int(meshWidth, meshHeight)),
                currentGridPos - lastGridPos);
        }

        return currentGridPos;
    }

    public TerrainFramePlan SelectFramePlan(
        StreamingWindow requestedWindow,
        StreamingWindow committedWindow,
        RectInt cameraViewport,
        RectInt retainedLightingViewport,
        bool isRequestedResident,
        bool requestedDimensionsChanged,
        bool cellsCommitted,
        bool cpuBuildInFlight,
        bool holdingPublishedView)
    {
        if (cpuBuildInFlight && !cellsCommitted)
        {
            // The initial CPU build must be polled before there is a committed
            // window. Otherwise ShouldProcess stays false forever and the
            // completed task can never publish its result.
            return new TerrainFramePlan(
                requestedWindow,
                committedWindow,
                requestedWindow,
                cameraViewport,
                cameraViewport,
                DimensionsChanged: requestedDimensionsChanged,
                ShouldProcess: true);
        }

        if (!isRequestedResident || cpuBuildInFlight)
        {
            if (!cellsCommitted)
            {
                return new TerrainFramePlan(
                    requestedWindow,
                    committedWindow,
                    committedWindow,
                    cameraViewport,
                    retainedLightingViewport,
                    DimensionsChanged: false,
                    ShouldProcess: false);
            }

            // Окно опубликовано, а свет ещё ни разу не ставился: запрос
            // (новый размер или начало) не приехал в кадре первой
            // публикации. Отказ от обработки здесь не ждёт, а запирает:
            // без обработки свет не ставится никогда, меш показа тоже, и
            // экран остаётся чёрным, пока запрос не станет резидентным.
            // Свет ставится по кадру камеры внутри опубликованного окна.
            RectInt lightingViewport = TerrainLightingViewportPolicy.ResolveLightingViewport(
                cameraViewport,
                retainedLightingViewport,
                holdingPublishedView);
            if (lightingViewport.width <= 0 || lightingViewport.height <= 0)
            {
                lightingViewport = ClampInto(cameraViewport, committedWindow);
            }

            // Keep processing the committed window, including dirty terrain
            // and the dynamic light's exact position. Lighting follows the real
            // camera while a build is pending; only a held presentation retains
            // its previously committed lighting viewport.
            return new TerrainFramePlan(
                requestedWindow,
                committedWindow,
                committedWindow,
                cameraViewport,
                lightingViewport,
                DimensionsChanged: false,
                ShouldProcess: true);
        }

        return new TerrainFramePlan(
            requestedWindow,
            committedWindow,
            requestedWindow,
            cameraViewport,
            cameraViewport,
            DimensionsChanged: requestedDimensionsChanged,
            ShouldProcess: true);
    }

    // Прямоугольник сдвигается (а если не влезает — обрезается) так, чтобы
    // лежать внутри окна.
    private static RectInt ClampInto(RectInt rect, StreamingWindow window)
    {
        int width = Mathf.Min(rect.width, window.Size.x);
        int height = Mathf.Min(rect.height, window.Size.y);
        int x = Mathf.Clamp(rect.x, window.Origin.x, window.Origin.x + window.Size.x - width);
        int y = Mathf.Clamp(rect.y, window.Origin.y, window.Origin.y + window.Size.y - height);
        return new RectInt(x, y, width, height);
    }
}

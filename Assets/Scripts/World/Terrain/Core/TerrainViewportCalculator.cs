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

    public Vector2Int ResolveGridPosition(
        Camera camera,
        float cellSize,
        int meshWidth,
        int meshHeight,
        int requestedWidth,
        int requestedHeight,
        int effectivePadding,
        bool dimensionsChanged,
        Vector2Int lastGridPos,
        out int viewportMinX,
        out int viewportMinY,
        out int viewportWidth,
        out int viewportHeight)
    {
        StreamingPolicy policy = _streamingGovernor.Policy;
        Vector3 camPos = camera.transform.position;
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

        Vector2Int targetOrigin = _streamingGovernor.SelectTargetOrigin(
            lastGridPos,
            desiredGridPos,
            new Vector2Int(viewportMinX, viewportMinY),
            new Vector2Int(viewportWidth, viewportHeight),
            new Vector2Int(meshWidth, meshHeight),
            dimensionsChanged,
            reanchorMarginCells: policy.ResolvePrefetchMarginCells(
                Mathf.Min(meshWidth, meshHeight)));
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
        bool cellsCommitted)
    {
        if (!isRequestedResident)
        {
            if (!cellsCommitted || retainedLightingViewport.width <= 0 || retainedLightingViewport.height <= 0)
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

            // Keep processing the committed window, including dirty terrain
            // and the dynamic light's exact position. A pending streaming request must
            // neither resize its resources nor reanchor lighting onto it.
            return new TerrainFramePlan(
                requestedWindow,
                committedWindow,
                committedWindow,
                cameraViewport,
                retainedLightingViewport,
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
}

public readonly record struct TerrainFramePlan(
    StreamingWindow RequestedWindow,
    StreamingWindow CommittedWindow,
    StreamingWindow ActiveWindow,
    RectInt CameraViewport,
    RectInt LightingViewport,
    bool DimensionsChanged,
    bool ShouldProcess);

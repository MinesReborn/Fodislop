#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces.Diagnostics;
using Kern.Rendering;
using Kern.World.Lighting.Diagnostics;
using Kern.World.Lighting.Quality;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Lighting;

/// <summary>
/// Owns per-frame invalidation, command recording, execution and journal state.
/// </summary>
internal sealed class LightingUpdateCoordinator
{
    private static readonly ProfilerMarker s_updateMarker =
        new("Kern.Lighting.UpdateLighting.CPU");
    private static readonly AllocationLedger.Entry s_allocationEntry =
        AllocationLedger.Register("Свет — обновление");
    private static readonly ProfilerMarker s_buildCommandsMarker =
        new("Kern.Lighting.BuildCommands.CPU");
    private static readonly ProfilerMarker s_executeCommandsMarker =
        new("Kern.Lighting.ExecuteCommands.CPU");
    // Explicit dense reference for production differential tests. Ordinary
    // reanchors use the dependency-aware path, never the dormant band solver.
    internal static bool DiagnosticForceDenseReanchor { get; set; }

    private readonly LightingResourceManager _resources;
    private readonly LightingRuntimeState _state;
    private readonly LightingGPULifecycle _gpuLifecycle;
    private readonly LightingFrameExecutor _frameExecutor;
    private readonly LightingPresentation _presentation;
    private readonly LightingGeometryRegistry _geometryRegistry;
    private readonly DynamicLightManager _dynamicLightManager;
    private readonly IFrameTelemetry _telemetry;
    private readonly LightingInvalidationJournal _journal;
    private readonly LightingAmbientOcclusionUpdater _ambientOcclusionUpdater;
    private readonly List<string> _executedStages = new();

    public LightingUpdateCoordinator(
        LightingResourceManager resources,
        LightingRuntimeState state,
        LightingGPULifecycle gpuLifecycle,
        LightingFrameExecutor frameExecutor,
        LightingPresentation presentation,
        LightingGeometryRegistry geometryRegistry,
        DynamicLightManager dynamicLightManager,
        IFrameTelemetry telemetry,
        LightingInvalidationJournal journal)
    {
        _resources = resources;
        _state = state;
        _gpuLifecycle = gpuLifecycle;
        _frameExecutor = frameExecutor;
        _presentation = presentation;
        _geometryRegistry = geometryRegistry;
        _dynamicLightManager = dynamicLightManager;
        _telemetry = telemetry;
        _journal = journal;
        _ambientOcclusionUpdater = new(
            _resources,
            _state,
            _frameExecutor,
            _presentation,
            _geometryRegistry,
            _telemetry,
            _journal,
            _executedStages);
    }

    public void Update(
        int visibleMinX,
        int visibleMinY,
        int visibleWidth,
        int visibleHeight,
        Camera camera,
        Kern.Core.Interfaces.WorldLighting.ILightingGeometryContributor terrainGeometry,
        GraphicsQualitySettings qualitySettings,
        LightingQualityMode qualityMode,
        LightingEngine.DebugView debugView,
        bool bypassLightingCompute,
        bool ambientOcclusionOnly)
    {
        using var updateMarker = s_updateMarker.Auto();
        using var allocationScope = AllocationLedger.Measure(s_allocationEntry);
        if (terrainGeometry == null ||
            (terrainGeometry is UnityEngine.Object unityObject && unityObject == null))
        {
            throw new ArgumentNullException(nameof(terrainGeometry));
        }
        if (visibleWidth <= 0 || visibleHeight <= 0 || camera == null)
        {
            return;
        }

        if (!camera.orthographic)
        {
            return;
        }


        if (bypassLightingCompute || qualityMode == LightingQualityMode.Off)
        {
            if (!bypassLightingCompute && ambientOcclusionOnly)
            {
                _ambientOcclusionUpdater.Update(
                    visibleMinX,
                    visibleMinY,
                    visibleWidth,
                    visibleHeight,
                    terrainGeometry,
                    qualitySettings);
                return;
            }

            bool enteringBypass = !_state.WasLightingBypassed;
            _state.WasLightingBypassed = true;
            _presentation.PublishDisabled();
            if (enteringBypass)
            {
                _gpuLifecycle.ReleaseResources();
            }

            return;
        }

        if (_state.WasLightingBypassed || _presentation.IsDisabledStatePublished)
        {
            _state.WasLightingBypassed = false;
            _presentation.MarkEnabled();
            Shader.EnableKeyword(LightingPresentation.WorldLightingKeyword);
            InvalidateAll();
        }

        _gpuLifecycle.EnsurePipeline();
        Vector4 previousLightingRegion = _state.LastVisibleRegion;
        Vector4 lightingRegion = LightingRegionCalculator.GetStableLightingRegion(
            visibleMinX,
            visibleMinY,
            visibleWidth,
            visibleHeight,
            _state.LastVisibleRegion);
        bool regionChanged = lightingRegion != _state.LastVisibleRegion;
        if (regionChanged)
        {
            _telemetry.LightingRegionChangeCount++;
        }
        _state.LastVisibleRegion = lightingRegion;

        // Consume every pending geometry change in this frame. Transport
        // dependencies reduce work; an area cap would publish stale lighting.
        const int maxInvalidationAreaPerFrame = int.MaxValue;
        bool fieldWasDirty = _state.FieldDirty;
        _state.ActivatePendingRegionsBudgeted(
            new RectInt(
                Mathf.RoundToInt(lightingRegion.x) - 1,
                Mathf.RoundToInt(lightingRegion.y) - 1,
                Mathf.RoundToInt(lightingRegion.z) + 2,
                Mathf.RoundToInt(lightingRegion.w) + 2),
            maxInvalidationAreaPerFrame);

        int gridWidth = Mathf.RoundToInt(lightingRegion.z);
        int gridHeight = Mathf.RoundToInt(lightingRegion.w);
        bool resourcesResized = EnsureResources(
            gridWidth,
            gridHeight,
            camera,
            qualitySettings);
        if (resourcesResized)
        {
            FrameEventLog.Record($"свет: ресурсы пересозданы {gridWidth}×{gridHeight}");
            _state.ClearPendingRegionInvalidation();
            _state.FieldDirty = true;
            _state.HasRenderedLightState = false;
            _state.HasStaticRadianceState = false;
            _state.HasDynamicRadianceState = false;
        }

        Vector2Int regionDelta = regionChanged && !float.IsNaN(previousLightingRegion.x)
            ? new Vector2Int(
                Mathf.RoundToInt(lightingRegion.x - previousLightingRegion.x),
                Mathf.RoundToInt(lightingRegion.y - previousLightingRegion.y))
            : Vector2Int.zero;

        // A region reanchor must preserve the already solved overlap. The
        // solver validates the probe phase for every cascade; incompatible
        // deltas invalidate the corresponding cascade instead of mixing phases.
        bool canReuseStaticAtlas = !DiagnosticForceDenseReanchor &&
            _state.HasStaticRadianceState &&
            _state.LastContributorGeometryRevision == _geometryRegistry.GeometryRevision &&
            regionChanged &&
            !resourcesResized &&
            !float.IsNaN(previousLightingRegion.x) &&
            _frameExecutor.CanReuseStaticAtlas(regionDelta);

        LightingRegionInvalidationPolicy.OnRegionChanged(
            _state,
            regionChanged,
            canReuseStaticAtlas,
            lightingRegion);

        // The output (SDR/HDR, paper white) or the source count moved the
        // visibility bound: re-cull sources and re-trace every ray and horizon.
        if (LightingComputeBinder.UpdateInvisibleDynamicRadiance(_dynamicLightManager.Count))
        {
            _dynamicLightManager.MarkDirty();
            _frameExecutor.InvalidateDynamicTiles();
            _state.HasDynamicRadianceState = false;
        }

        bool dynamicLightsDirty = !_state.HasRenderedLightState || _dynamicLightManager.IsDirty;
        ulong contributorGeometryRevision = _geometryRegistry.GeometryRevision;
        bool contributorGeometryChanged =
            _state.LastContributorGeometryRevision != contributorGeometryRevision;
        bool geometryChanged =
            (_state.LastTerrainGeometryRevision != terrainGeometry.LightingGeometryRevision &&
                (_state.StagedTerrainGeometryRevision != terrainGeometry.LightingGeometryRevision ||
                 _state.ActiveRegionInvalidations.Count > 0)) ||
            contributorGeometryChanged;
        if (geometryChanged)
        {
            _telemetry.LightingGeometryChangeCount++;
        }
        const float cellSize = ProjectRuntimeContracts.World.CellSize;
        Vector4 worldRect = new(
            lightingRegion.x * cellSize,
            lightingRegion.y * cellSize,
            lightingRegion.z * cellSize,
            lightingRegion.w * cellSize);
        RectInt receiverRect = LightingReceiverCoverage.GetRect(camera, worldRect,
            _resources.LightWidth, _resources.LightHeight, cellSize);
        bool receiversChanged = _dynamicLightManager.Count > 0 && !_state.LastDynamicReceiverRect.Equals(receiverRect);
        if (!_state.FieldDirty && !regionChanged && !dynamicLightsDirty && !geometryChanged &&
            !_state.CompositeDirty && !receiversChanged)
        {
            _state.LastTerrainGeometryRevision = terrainGeometry.LightingGeometryRevision;
            return;
        }
        CommandBuffer commandBuffer = _resources.LightingCommandBuffer ??
            throw new InvalidOperationException(
                "Radiance Cascades command buffer is not initialized.");
        commandBuffer.Clear();
        int dynamicLightCount;
        bool dynamicLightsChanged;
        bool rebuildFields = _state.FieldDirty || regionChanged || geometryChanged;
        bool allowStaticDependencyMask = !resourcesResized &&
            (!regionChanged || canReuseStaticAtlas) &&
            !contributorGeometryChanged &&
            _state.ActiveRegionInvalidations.Count > 0;
        bool reuseStaticAtlas = canReuseStaticAtlas;
        if (rebuildFields)
        {
            _telemetry.LightingFieldRebuildCount++;
        }
        try
        {
            long buildStart = System.Diagnostics.Stopwatch.GetTimestamp();
            using (s_buildCommandsMarker.Auto())
            {
                commandBuffer.BeginSample("Kern.RadianceCascades");
                dynamicLightCount = _frameExecutor.UploadDynamicLights(
                    commandBuffer,
                    worldRect,
                    cellSize,
                    out dynamicLightsChanged);

                if (!rebuildFields && !dynamicLightsChanged &&
                    !_state.CompositeDirty && !receiversChanged)
                {
                    commandBuffer.EndSample("Kern.RadianceCascades");
                    RememberDynamicLightState();
                    return;
                }

                _frameExecutor.ConfigureSharedComputeParameters(
                    commandBuffer,
                    worldRect,
                    cellSize,
                    _resources.StaticEmissionField!,
                    qualityMode,
                    debugView);
                bool staticRadianceChanged = rebuildFields || !_state.HasStaticRadianceState;
                bool dynamicRadianceChanged = dynamicLightCount > 0 &&
                    (dynamicLightsChanged || staticRadianceChanged || receiversChanged || !_state.HasDynamicRadianceState);
                LightingInvalidationFlags invalidations = RecordLightingFrame(
                    commandBuffer,
                    worldRect,
                    cellSize,
                    dynamicLightCount,
                    rebuildFields,
                    dynamicLightsChanged,
                    staticRadianceChanged,
                    reuseStaticAtlas,
                    regionDelta,
                    _state.ActiveRegionInvalidations,
                    allowStaticDependencyMask,
                    LightingAmbientOcclusionUpdatePolicy.CanUpdatePartially(
                        _state, terrainGeometry.LightingGeometryRevision, fieldWasDirty,
                        resourcesResized, regionChanged, contributorGeometryChanged),
                    dynamicRadianceChanged,
                    qualityMode,
                    debugView,
                    terrainGeometry,
                    receiverRect);
                if (receiversChanged)
                {
                    invalidations |= LightingInvalidationFlags.ReceiverCoverageChanged;
                }
                _state.HasStaticRadianceState |= staticRadianceChanged;
                _state.HasDynamicRadianceState = dynamicLightCount > 0 &&
                    (dynamicRadianceChanged || _state.HasDynamicRadianceState);
                if (staticRadianceChanged)
                {
                    _telemetry.LightingStaticSolveCount++;
                    _telemetry.LightingStaticSolveFrameCount++;
                }

                if (dynamicRadianceChanged)
                {
                    _telemetry.LightingDynamicSolveCount++;
                }

                commandBuffer.EndSample("Kern.RadianceCascades");
                _telemetry.LightingBuildCommandsTimeMs =
                    (float)((System.Diagnostics.Stopwatch.GetTimestamp() - buildStart) *
                        1000.0 / System.Diagnostics.Stopwatch.Frequency);
                _telemetry.LightingCommandBufferBytes = commandBuffer.sizeInBytes;
                _telemetry.ActiveDynamicLights = dynamicLightCount;
                long executeStart = System.Diagnostics.Stopwatch.GetTimestamp();
                using (s_executeCommandsMarker.Auto())
                {
                    Graphics.ExecuteCommandBuffer(commandBuffer);
                }

                _telemetry.LightingExecuteCommandsTimeMs =
                    (float)((System.Diagnostics.Stopwatch.GetTimestamp() - executeStart) *
                        1000.0 / System.Diagnostics.Stopwatch.Frequency);
                _presentation.Publish(
                    debugView,
                    _state.LastVisibleRegion,
                    cellSize);
                string reason = rebuildFields
                    ? (reuseStaticAtlas ? "Region moved (scroll)" : "Geometry or region updated")
                    : dynamicLightsChanged
                        ? "Dynamic lights updated"
                        : receiversChanged ? "World receiver coverage changed" : "Lightmap refreshed";
                _journal.Record(
                    _state.SolveCount,
                    invalidations,
                    reason,
                    _executedStages,
                    Array.Empty<string>());
                _state.SolveCount++;
                _state.FieldDirty = false;
                _state.CompositeDirty = false;
                _state.LastTerrainGeometryRevision = terrainGeometry.LightingGeometryRevision;
                _state.LastContributorGeometryRevision = contributorGeometryRevision;
                _state.LastDynamicReceiverRect = receiverRect;
                _state.CompleteActiveRegionInvalidation();
                RememberDynamicLightState();
            }
        }
        catch
        {
            // A recording/execution failure cannot leave a swapped atlas marked current.
            LightingRuntimeInvalidation.ResetFieldAndRadiance(_state);
            throw;
        }
        finally
        {
            commandBuffer.Clear();
        }
    }

    private bool EnsureResources(
        int gridWidth,
        int gridHeight,
        Camera camera,
        GraphicsQualitySettings qualitySettings)
    {
        _state.RequestedPixelsPerCell = LightingQualityTuningController.FieldPixelsPerCell;
        bool textureDimensionLimited;
        bool cascadeBudgetLimited;
        bool resized = _gpuLifecycle.EnsureResources(
            gridWidth,
            gridHeight,
            camera,
            in qualitySettings,
            out textureDimensionLimited,
            out cascadeBudgetLimited,
            out int effectivePixelsPerCell);
        _state.TextureDimensionLimited = textureDimensionLimited;
        _state.CascadeBudgetLimited = cascadeBudgetLimited;
        _state.EffectivePixelsPerCell = effectivePixelsPerCell;
        _telemetry.LightingEstimatedCascadeRayWorkUnits =
            _resources.EstimatedCascadeRayWorkUnits;
        _telemetry.LightingEstimatedCascadeDispatchThreads =
            _resources.EstimatedCascadeDispatchThreads;
        return resized;
    }

    private LightingInvalidationFlags RecordLightingFrame(
        CommandBuffer commandBuffer,
        Vector4 worldRect,
        float cellSize,
        int dynamicLightCount,
        bool rebuildFields,
        bool dynamicLightsChanged,
        bool staticRadianceChanged,
        bool reuseStaticAtlas,
        Vector2Int regionDelta,
        IReadOnlyList<RectInt> dirtyRegions,
        bool allowStaticDependencyMask,
        bool allowPartialAmbientOcclusion,
        bool dynamicRadianceChanged,
        LightingQualityMode qualityMode,
        LightingEngine.DebugView debugView,
        Kern.Core.Interfaces.WorldLighting.ILightingGeometryContributor terrainGeometry,
        RectInt receiverRect)
    {
        LightingFrameResult result = _frameExecutor.Record(
            commandBuffer,
            new LightingFrameRequest(
                worldRect,
                cellSize,
                dynamicLightCount,
                rebuildFields,
                dynamicLightsChanged,
                staticRadianceChanged,
                reuseStaticAtlas,
                regionDelta,
                dirtyRegions,
                allowStaticDependencyMask,
                dynamicRadianceChanged,
                dynamicLightCount == 0 &&
                    (dynamicLightsChanged || staticRadianceChanged || _state.HasDynamicRadianceState),
                _state.CompositeDirty,
                qualityMode,
                debugView)
            {
                DynamicReceiverRect = receiverRect,
                AllowPartialAmbientOcclusion = allowPartialAmbientOcclusion,
            },
            terrainGeometry,
            _resources.StaticEmissionField!,
            _resources.StaticDirectTexture!);
        _executedStages.Clear();
        _executedStages.AddRange(result.ExecutedStages);
        return result.Invalidations |
            (_state.CompositeDirty ? LightingInvalidationFlags.CompositeDirty : LightingInvalidationFlags.None);
    }

    private void RememberDynamicLightState()
    {
        _state.HasRenderedLightState = true;
        _dynamicLightManager.ClearDirty();
    }

    private void InvalidateAll()
    {
        _state.FieldDirty = true;
        _state.CompositeDirty = true;
        _state.HasRenderedLightState = false;
        _state.HasStaticRadianceState = false;
        _state.HasDynamicRadianceState = false;
        _state.LastVisibleRegion = new Vector4(float.NaN, float.NaN, float.NaN, float.NaN);
    }

}

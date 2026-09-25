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
    private static readonly ProfilerMarker _UpdateMarker =
        new("Kern.Lighting.UpdateLighting.CPU");
    private static readonly AllocationLedger.Entry _AllocationEntry =
        AllocationLedger.Register("Свет — обновление");
    private static readonly ProfilerMarker _BuildCommandsMarker =
        new("Kern.Lighting.BuildCommands.CPU");
    private static readonly ProfilerMarker _ExecuteCommandsMarker =
        new("Kern.Lighting.ExecuteCommands.CPU");

    private readonly LightingResourceManager _resources;
    private readonly LightingRuntimeState _state;
    private readonly LightingGPULifecycle _gpuLifecycle;
    private readonly LightingFrameExecutor _frameExecutor;
    private readonly LightingPresentation _presentation;
    private readonly LightingGeometryRegistry _geometryRegistry;
    private readonly DynamicLightManager _dynamicLightManager;
    private readonly IFrameTelemetry _telemetry;
    private readonly LightingInvalidationJournal _journal;
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
        using var updateMarker = _UpdateMarker.Auto();
        using var allocationScope = AllocationLedger.Measure(_AllocationEntry);
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
                UpdateAmbientOcclusionOnly(
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

        const int maxInvalidationAreaPerFrame = 32 * 32 * 2; // Safe per-frame cascade budget
        _state.ActivatePendingRegionsBudgeted(
            new RectInt(
                visibleMinX,
                visibleMinY,
                visibleWidth,
                visibleHeight),
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

        // ROLLED BACK 2026-09-19: scroll reuse correlated with a heavy FPS
        // drop in playmode, cause not yet isolated (prime suspects: full-atlas
        // memmove cost on large fields, or a broken reuse path doing more work
        // than the full solve it replaces). The scroll/strip machinery in
        // StaticLightingSolver stays in place but dormant; re-enable by
        // restoring the condition below once the cause is measured.
        // Original: regionChanged && !resourcesResized.
        bool canReuseStaticAtlas = false;

        LightingRegionInvalidationPolicy.OnRegionChanged(
            _state,
            regionChanged,
            canReuseStaticAtlas,
            lightingRegion);

        bool dynamicLightsDirty = !_state.HasRenderedLightState || _dynamicLightManager.IsDirty;
        ulong contributorGeometryRevision = _geometryRegistry.GeometryRevision;
        bool contributorGeometryChanged =
            _state.LastContributorGeometryRevision != contributorGeometryRevision;
        bool geometryChanged =
            _state.LastTerrainGeometryRevision != terrainGeometry.LightingGeometryRevision ||
            contributorGeometryChanged;
        if (geometryChanged)
        {
            _telemetry.LightingGeometryChangeCount++;
        }
        if (!_state.FieldDirty && !regionChanged && !dynamicLightsDirty && !geometryChanged &&
            !_state.CompositeDirty)
        {
            return;
        }

        const float cellSize = ProjectRuntimeContracts.World.CellSize;
        Vector4 worldRect = new(
            lightingRegion.x * cellSize,
            lightingRegion.y * cellSize,
            lightingRegion.z * cellSize,
            lightingRegion.w * cellSize);
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
            using (_BuildCommandsMarker.Auto())
            {
                commandBuffer.BeginSample("Kern.RadianceCascades");
                dynamicLightCount = _frameExecutor.UploadDynamicLights(
                    commandBuffer,
                    worldRect,
                    cellSize,
                    out dynamicLightsChanged);

                if (!rebuildFields && !dynamicLightsChanged &&
                    !_state.CompositeDirty)
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
                    (dynamicLightsChanged || staticRadianceChanged || !_state.HasDynamicRadianceState);
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
                    dynamicRadianceChanged,
                    qualityMode,
                    debugView,
                    terrainGeometry);
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
                using (_ExecuteCommandsMarker.Auto())
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
                        : "Lightmap refreshed";
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
                _state.CompleteActiveRegionInvalidation();
                RememberDynamicLightState();
            }
        }
        finally
        {
            commandBuffer.Clear();
        }
    }

    private void UpdateAmbientOcclusionOnly(
        int visibleMinX,
        int visibleMinY,
        int visibleWidth,
        int visibleHeight,
        Kern.Core.Interfaces.WorldLighting.ILightingGeometryContributor terrainGeometry,
        GraphicsQualitySettings qualitySettings)
    {
        bool entering = _state.WasLightingBypassed || _presentation.IsDisabledStatePublished;
        _state.WasLightingBypassed = false;
        Vector4 previousRegion = _state.LastVisibleRegion;
        Vector4 region = LightingRegionCalculator.GetStableLightingRegion(
            visibleMinX,
            visibleMinY,
            visibleWidth,
            visibleHeight,
            previousRegion);
        bool regionChanged = region != previousRegion;
        _state.LastVisibleRegion = region;
        if (regionChanged)
        {
            _state.FieldDirty = true;
            _telemetry.LightingRegionChangeCount++;
        }

        if (entering || _state.FieldDirty)
        {
            _presentation.PublishDisabled();
        }

        bool resized = _resources.EnsureAmbientOcclusionOnlyResources(
            Mathf.RoundToInt(region.z),
            Mathf.RoundToInt(region.w),
            qualitySettings.LightingMaximumTextureDimension);
        ulong contributorRevision = _geometryRegistry.GeometryRevision;
        bool geometryChanged =
            _state.LastTerrainGeometryRevision != terrainGeometry.LightingGeometryRevision ||
            _state.LastContributorGeometryRevision != contributorRevision;
        if (geometryChanged)
        {
            _telemetry.LightingGeometryChangeCount++;
        }

        if (!entering && !resized && !regionChanged && !geometryChanged && !_state.FieldDirty)
        {
            return;
        }

        const float cellSize = ProjectRuntimeContracts.World.CellSize;
        Vector4 worldRect = new(
            region.x * cellSize,
            region.y * cellSize,
            region.z * cellSize,
            region.w * cellSize);
        RenderTexture field = _resources.AmbientOcclusionField ??
            throw new InvalidOperationException("Standard graphics has no AO field.");
        CommandBuffer commands = _resources.LightingCommandBuffer ??
            throw new InvalidOperationException("Standard graphics has no AO command buffer.");
        LightingInvalidationFlags invalidations =
            (regionChanged ? LightingInvalidationFlags.RegionChanged : LightingInvalidationFlags.None) |
            (geometryChanged ? LightingInvalidationFlags.GeometryChanged : LightingInvalidationFlags.None) |
            (_state.FieldDirty || resized || entering
                ? LightingInvalidationFlags.FieldDirty
                : LightingInvalidationFlags.None);
        _state.FieldDirty = true;
        FrameEventLog.Record(
            $"AO Стандарт: перестройка {field.width}×{field.height}, " +
            $"регион={regionChanged}, геометрия={geometryChanged}, ресурсы={resized}");
        commands.Clear();
        try
        {
            _frameExecutor.RecordAmbientOcclusionField(commands, terrainGeometry, worldRect);
            Graphics.ExecuteCommandBuffer(commands);
            _presentation.PublishAmbientOcclusionOnly(field, region, cellSize);
            _telemetry.LightingFieldRebuildCount++;
            _state.FieldDirty = false;
            _state.CompositeDirty = false;
            _state.LastTerrainGeometryRevision = terrainGeometry.LightingGeometryRevision;
            _state.LastContributorGeometryRevision = contributorRevision;
            _state.ClearPendingRegionInvalidation();
            _executedStages.Clear();
            _executedStages.Add("AmbientOcclusionField");
            _journal.Record(
                _state.SolveCount,
                invalidations,
                "Standard ambient occlusion updated",
                _executedStages,
                Array.Empty<string>());
            _state.SolveCount++;
        }
        finally
        {
            commands.Clear();
        }
    }

    private bool EnsureResources(
        int gridWidth,
        int gridHeight,
        Camera camera,
        GraphicsQualitySettings qualitySettings)
    {
        _state.RequestedPixelsPerCell = Mathf.Clamp(qualitySettings.LightingMinimumPixelsPerCell, 1, 16);
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
        bool dynamicRadianceChanged,
        LightingQualityMode qualityMode,
        LightingEngine.DebugView debugView,
        Kern.Core.Interfaces.WorldLighting.ILightingGeometryContributor terrainGeometry)
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
                debugView),
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

#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces.Diagnostics;
using Kern.Rendering;
using Kern.World.Lighting.Diagnostics;
using Kern.World.Lighting.Quality;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Lighting;

/// <summary>
/// Records and publishes the Standard-quality ambient-occlusion-only frame.
/// </summary>
internal sealed class LightingAmbientOcclusionUpdater
{
    private readonly LightingResourceManager _resources;
    private readonly LightingRuntimeState _state;
    private readonly LightingFrameExecutor _frameExecutor;
    private readonly LightingPresentation _presentation;
    private readonly LightingGeometryRegistry _geometryRegistry;
    private readonly IFrameTelemetry _telemetry;
    private readonly LightingInvalidationJournal _journal;
    private readonly List<string> _executedStages;

    public LightingAmbientOcclusionUpdater(
        LightingResourceManager resources,
        LightingRuntimeState state,
        LightingFrameExecutor frameExecutor,
        LightingPresentation presentation,
        LightingGeometryRegistry geometryRegistry,
        IFrameTelemetry telemetry,
        LightingInvalidationJournal journal,
        List<string> executedStages)
    {
        _resources = resources;
        _state = state;
        _frameExecutor = frameExecutor;
        _presentation = presentation;
        _geometryRegistry = geometryRegistry;
        _telemetry = telemetry;
        _journal = journal;
        _executedStages = executedStages;
    }

    public void Update(
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
            Mathf.RoundToInt(region.w));
        ulong contributorRevision = _geometryRegistry.GeometryRevision;
        bool fieldWasDirty = _state.FieldDirty;
        _state.ActivatePendingRegionIfVisible(new RectInt(
            Mathf.RoundToInt(region.x) - 1, Mathf.RoundToInt(region.y) - 1,
            Mathf.RoundToInt(region.z) + 2, Mathf.RoundToInt(region.w) + 2));
        bool geometryChanged =
            (_state.LastTerrainGeometryRevision != terrainGeometry.LightingGeometryRevision &&
                (_state.StagedTerrainGeometryRevision != terrainGeometry.LightingGeometryRevision ||
                 _state.ActiveRegionInvalidations.Count > 0)) ||
            _state.LastContributorGeometryRevision != contributorRevision;
        if (geometryChanged)
        {
            _telemetry.LightingGeometryChangeCount++;
        }

        if (!entering && !resized && !regionChanged && !geometryChanged && !_state.FieldDirty)
        {
            _state.LastTerrainGeometryRevision = terrainGeometry.LightingGeometryRevision;
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
        bool allowPartial = LightingAmbientOcclusionUpdatePolicy.CanUpdatePartially(
            _state, terrainGeometry.LightingGeometryRevision, fieldWasDirty,
            entering || resized, regionChanged,
            _state.LastContributorGeometryRevision != contributorRevision);
        RectInt? rasterRect = allowPartial
            ? LightingAmbientOcclusionUpdatePolicy.ResolveRasterRect(
                _state.ActiveRegionInvalidations,
                new RectInt(Mathf.RoundToInt(region.x), Mathf.RoundToInt(region.y),
                    Mathf.RoundToInt(region.z), Mathf.RoundToInt(region.w)),
                LightingConfigHolder.AmbientOcclusionPixelsPerCell)
            : null;
        _state.FieldDirty = true;
        FrameEventLog.Record(
            $"AO Стандарт: перестройка {field.width}×{field.height}, " +
            $"регион={regionChanged}, геометрия={geometryChanged}, ресурсы={resized}");
        commands.Clear();
        try
        {
            _frameExecutor.RecordAmbientOcclusionField(commands, terrainGeometry, worldRect, rasterRect);
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
}

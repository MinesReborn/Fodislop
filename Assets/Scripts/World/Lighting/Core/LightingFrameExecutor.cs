#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.World.Lighting.Quality;
using Kern.World.Terrain;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Lighting;

/// <summary>
/// Records the complete lighting transport order into one command buffer.
/// It owns per-transport cache state; resource lifetime remains external.
/// </summary>
internal sealed class LightingFrameExecutor
{
    private readonly LightingResourceManager _resources;
    private readonly GeometryLightingSolver _geometrySolver;
    private readonly StaticLightingSolver _staticSolver;
    private readonly DynamicLightingSolver _dynamicSolver;
    private readonly IndirectLightingSolver _indirectSolver;
    private readonly DynamicLightManager _dynamicLightManager;
    private readonly IFrameTelemetry _telemetry;
    private readonly LightingGeometryRegistry _geometryRegistry;
    private readonly List<string> _executedStages = new();
    private RectInt? _lastDynamicUnion;

    public LightingFrameExecutor(
        LightingResourceManager resources,
        GeometryLightingSolver geometrySolver,
        StaticLightingSolver staticSolver,
        DynamicLightingSolver dynamicSolver,
        IndirectLightingSolver indirectSolver,
        DynamicLightManager dynamicLightManager,
        LightingGeometryRegistry geometryRegistry,
        IFrameTelemetry telemetry)
    {
        _resources = resources;
        _geometrySolver = geometrySolver;
        _staticSolver = staticSolver;
        _dynamicSolver = dynamicSolver;
        _indirectSolver = indirectSolver;
        _dynamicLightManager = dynamicLightManager;
        _geometryRegistry = geometryRegistry;
        _telemetry = telemetry;
    }

    public void Release()
    {
        _dynamicSolver.Release();
        _dynamicLightManager.ResetUploadState();
        _lastDynamicUnion = null;
    }

    public void EnsureDynamicLightCapacity(int capacity)
    {
        _dynamicLightManager.EnsureCapacity(capacity);
    }

    public int UploadDynamicLights(
        CommandBuffer commandBuffer,
        Vector4 worldRect,
        float cellSize,
        out bool uploadedLightsChanged)
    {
        return _dynamicLightManager.UploadDynamicLights(
            commandBuffer,
            _resources.DynamicLightBuffer,
            worldRect,
            cellSize,
            out uploadedLightsChanged);
    }

    public void ClearDynamicDirect(CommandBuffer commandBuffer)
    {
        commandBuffer.SetRenderTarget(_resources.DirectTexture!);
        commandBuffer.ClearRenderTarget(
            clearDepth: false,
            clearColor: true,
            backgroundColor: Color.clear);
    }

    public void ConfigureSharedComputeParameters(
        CommandBuffer commandBuffer,
        Vector4 worldRect,
        float cellSize,
        RenderTexture emissionField,
        LightingQualityMode quality,
        LightingEngine.DebugView debugView)
    {
        LightingComputeBinder.BindSharedParameters(
            commandBuffer,
            _resources.LightingCompute!,
            _resources.FieldWidth,
            _resources.FieldHeight,
            _resources.BounceWidth,
            _resources.BounceHeight,
            worldRect,
            cellSize,
            quality,
            debugView,
            _resources.MaterialField!,
            emissionField,
            _resources.SolveCascadeKernel,
            _resources.ResolveDirectKernel,
            _resources.SolveDiffuseBounceKernel,
            _resources.CompositeLightingKernel,
            _resources.CellGridWidth,
            _resources.CellGridHeight);
    }

    public LightingFrameResult Record(
        CommandBuffer commandBuffer,
        LightingFrameRequest request,
        TerrainRenderer terrainRenderer,
        RenderTexture emissionField,
        RenderTexture staticDirectTexture)
    {
        _executedStages.Clear();
        LightingInvalidationFlags invalidations = BuildInvalidations(request);

        if (request.RebuildFields)
        {
            _geometrySolver.RecordMaterialField(
                commandBuffer,
                terrainRenderer,
                _geometryRegistry,
                request.WorldRect);
            _executedStages.Add("MaterialField");
            _geometrySolver.PrepareCaches(commandBuffer, materialFieldRebuilt: true);
            _executedStages.Add("GeometryCache");
        }

        bool staticRadianceChanged = request.StaticRadianceChanged;
        bool dynamicRadianceNeeded = request.DynamicRadianceChanged &&
            request.DynamicLightCount > 0;
        if (request.ClearDynamicRadiance)
        {
            ClearDynamicDirect(commandBuffer);
            _dynamicSolver.InvalidateTiles();
        }

        if (staticRadianceChanged &&
            LightingConfigHolder.EnabledFeatures.HasFlag(LightingFeatureFlags.StaticRC))
        {
            _staticSolver.RecordTrace(
                commandBuffer,
                emissionField,
                request.ReuseStaticAtlas,
                request.RegionDelta,
                request.DirtyRegions,
                request.AllowStaticDependencyMask,
                request.WorldRect);
            _executedStages.Add("CascadeTrace");
            _staticSolver.RecordResolve(
                commandBuffer,
                request.DebugView,
                emissionField,
                staticDirectTexture);
            _executedStages.Add("CascadeMerge");
        }

        RectInt dynamicDirtyUnion = default;
        if (dynamicRadianceNeeded &&
            LightingConfigHolder.EnabledFeatures.HasFlag(LightingFeatureFlags.DynamicLights))
        {
            _dynamicSolver.Record(
                commandBuffer,
                request.DynamicLightCount,
                request.WorldRect,
                request.CellSize,
                staticRadianceChanged || request.RebuildFields,
                request.DebugView,
                _telemetry,
                out dynamicDirtyUnion);
            _executedStages.Add("DynamicLighting");
        }

        // Dynamic-only frames keep every input except the dynamic tiles: bounce
        // and composite refresh the dynamic union plus gather margin instead of
        // the whole field. Any static, geometry or debug-view change keeps
        // the full path, so debug views stay bit-identical.
        //
        // CompositeDirty is intentionally NOT a full-path trigger: it is set
        // on every dynamic light move by LightingEngine.SetDynamicLight, which is
        // exactly the dynamic-only case this path exists for. BounceDirty is
        // only set by config/debug/reset/geometry paths, which force the full
        // path through the other conditions anyway.
        //
        // Removing the last source also goes partial: its previous union is
        // retained below, and the cleared area is exactly that union. Any
        // rebuild invalidates the retained union (stale texel space).
        if (request.RebuildFields || staticRadianceChanged)
        {
            _lastDynamicUnion = null;
        }

        RectInt? partialRect = null;
        if (!staticRadianceChanged &&
            !request.RebuildFields &&
            !request.BounceDirty &&
            request.DebugView == LightingEngine.DebugView.FinalLighting)
        {
            if (dynamicRadianceNeeded &&
                dynamicDirtyUnion.width > 0 &&
                dynamicDirtyUnion.height > 0)
            {
                partialRect = dynamicDirtyUnion;
                _lastDynamicUnion = dynamicDirtyUnion;
            }
            else if (request.ClearDynamicRadiance && _lastDynamicUnion.HasValue)
            {
                partialRect = _lastDynamicUnion;
                _lastDynamicUnion = null;
            }
        }

        bool bounceRequired = request.BounceDirty ||
            request.DynamicLightsChanged ||
            request.DynamicRadianceChanged ||
            staticRadianceChanged;
        bool bounceRequested = request.Quality == LightingQualityMode.PerPixelBilinearFixBounce ||
            request.DebugView == LightingEngine.DebugView.DiffuseBounce;
        if (bounceRequested &&
            LightingConfigHolder.BounceEnabled &&
            LightingConfigHolder.BounceStrength > 0f &&
            LightingConfigHolder.EnabledFeatures.HasFlag(LightingFeatureFlags.DiffuseBounce) &&
            bounceRequired)
        {
            _indirectSolver.RecordBounce(
                commandBuffer,
                partialRect,
                request.WorldRect,
                request.CellSize,
                _telemetry);
            _executedStages.Add("DiffuseBounce");
        }

        if (request.DynamicLightsChanged ||
            request.DynamicRadianceChanged ||
            staticRadianceChanged ||
            request.CompositeDirty)
        {
            _indirectSolver.RecordComposite(
                commandBuffer,
                partialRect,
                request.WorldRect,
                request.CellSize,
                _telemetry);
            _executedStages.Add("Composite");
        }

        return new LightingFrameResult(
            invalidations,
            staticRadianceChanged,
            dynamicRadianceNeeded,
            request.ClearDynamicRadiance,
            _executedStages.ToArray());
    }

    private static LightingInvalidationFlags BuildInvalidations(
        LightingFrameRequest request)
    {
        LightingInvalidationFlags invalidations = LightingInvalidationFlags.None;
        if (request.RebuildFields)
        {
            invalidations |=
                LightingInvalidationFlags.GeometryChanged |
                LightingInvalidationFlags.RegionChanged |
                LightingInvalidationFlags.FieldDirty;
        }

        if (request.DynamicLightsChanged)
        {
            invalidations |= LightingInvalidationFlags.DynamicLightsChanged;
        }

        if (request.StaticRadianceChanged)
        {
            invalidations |= LightingInvalidationFlags.StaticRadianceChanged;
        }

        if (request.DynamicRadianceChanged)
        {
            invalidations |= LightingInvalidationFlags.DynamicRadianceChanged;
        }

        return invalidations;
    }
}

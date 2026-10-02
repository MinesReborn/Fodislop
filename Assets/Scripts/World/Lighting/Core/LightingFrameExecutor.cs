#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.World.Lighting.Quality;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Lighting;

/// <summary>
/// Records the complete lighting transport order into one command buffer.
/// It owns per-transport cache state; resource lifetime remains external.
/// </summary>
internal sealed class LightingFrameExecutor
{
    internal static Action<string, AsyncGPUReadbackRequest>? DiagnosticMaterialReadback { get; set; }
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

    public void InvalidateDynamicQuality()
    {
        _dynamicSolver.Release();
        _dynamicLightManager.ResetUploadState();
        _lastDynamicUnion = null;
    }

    // Every dynamic ray, horizon and receiver tile is traced again; slots and
    // their composed rectangles survive, so the next solve refreshes exactly
    // the area those lights covered and cover.
    public void InvalidateDynamicTiles()
    {
        _dynamicSolver.InvalidateTiles();
    }

    public bool CanReuseStaticAtlas(Vector2Int regionDelta) =>
        _staticSolver.CanReuseStaticAtlas(regionDelta);

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

    public void RecordAmbientOcclusionField(
        CommandBuffer commandBuffer,
        Kern.Core.Interfaces.WorldLighting.ILightingGeometryContributor terrainGeometry,
        Vector4 worldRect,
        RectInt? rasterRect = null) =>
        _geometrySolver.RecordAmbientOcclusionField(
            commandBuffer,
            terrainGeometry,
            _geometryRegistry,
            worldRect,
            rasterRect);

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
            _resources.LightWidth,
            _resources.LightHeight,
            worldRect,
            cellSize,
            debugView,
            _resources.MaterialField!,
            emissionField,
            _resources.SolveCascadeKernel,
            _resources.ResolveDirectKernel,
            _resources.CompositeLightingKernel,
            _resources.CellGridWidth,
            _resources.CellGridHeight);
    }

    public LightingFrameResult Record(
        CommandBuffer commandBuffer,
        LightingFrameRequest request,
        Kern.Core.Interfaces.WorldLighting.ILightingGeometryContributor terrainGeometry,
        RenderTexture emissionField,
        RenderTexture staticDirectTexture)
    {
        _executedStages.Clear();
        LightingInvalidationFlags invalidations = BuildInvalidations(request);

        if (request.RebuildFields)
        {
            if (request.ReuseStaticAtlas)
            {
                _resources.EnsureReanchorFields();
                commandBuffer.CopyTexture(_resources.MaterialField!, _resources.ReanchorMaterial!);
                commandBuffer.CopyTexture(_resources.StaticEmissionField!, _resources.ReanchorEmission!);
            }

            _geometrySolver.RecordMaterialField(
                commandBuffer,
                terrainGeometry,
                _geometryRegistry,
                request.WorldRect);
            _executedStages.Add("MaterialField");
            RecordMaterialReadback(commandBuffer, "MaterialField");
            _geometrySolver.PrepareCaches(commandBuffer, materialFieldRebuilt: true);
            _executedStages.Add("GeometryCache");
            RecordMaterialReadback(commandBuffer, "GeometryCache");
            RectInt? aoRasterRect = request.AllowPartialAmbientOcclusion
                ? LightingAmbientOcclusionUpdatePolicy.ResolveRasterRect(
                    request.DirtyRegions,
                    new RectInt(
                        Mathf.RoundToInt(request.WorldRect.x / request.CellSize),
                        Mathf.RoundToInt(request.WorldRect.y / request.CellSize),
                        Mathf.RoundToInt(request.WorldRect.z / request.CellSize),
                        Mathf.RoundToInt(request.WorldRect.w / request.CellSize)),
                    LightingConfigHolder.AmbientOcclusionPixelsPerCell)
                : null;
            RecordAmbientOcclusionField(
                commandBuffer,
                terrainGeometry,
                request.WorldRect,
                aoRasterRect);
            _executedStages.Add("AmbientOcclusionField");
            RecordMaterialReadback(commandBuffer, "AmbientOcclusionField");
        }

        bool staticRadianceChanged = request.StaticRadianceChanged;
        bool dynamicRadianceNeeded = request.DynamicRadianceChanged &&
            request.DynamicLightCount > 0;
        if (request.ClearDynamicRadiance)
        {
            ClearDynamicDirect(commandBuffer);
            _dynamicSolver.Release();
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
        bool dynamicRecorded = false;
        if (dynamicRadianceNeeded &&
            LightingConfigHolder.EnabledFeatures.HasFlag(LightingFeatureFlags.DynamicLights))
        {
            dynamicRecorded = true;
            _dynamicSolver.Record(
                commandBuffer,
                request.DynamicLightCount,
                request.WorldRect,
                request.CellSize,
                staticRadianceChanged || request.RebuildFields,
                request.DebugView,
                _telemetry,
                out dynamicDirtyUnion,
                request.DynamicReceiverRect);
            _executedStages.Add("DynamicLighting");
        }

        // Dynamic-only frames keep every input except the dynamic tiles:
        // composite refreshes the dynamic union plus gather margin instead of
        // the whole field. Any static, geometry or debug-view change keeps
        // the full path, so debug views stay bit-identical.
        //
        // Source edits request upload-set evaluation. DynamicLightsChanged
        // reflects the resulting GPU inputs; culled-only edits never reach
        // this recorder. CompositeDirty remains an explicit refresh request.
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

        // A global refresh must cover pixels outside the dynamic dirty union.
        // Retain the updated union above for subsequent dynamic-only frames.
        if (request.CompositeDirty)
        {
            partialRect = null;
        }

        // A dynamic-only solve that changed no DirectTexture pixel (moved
        // receivers kept every light's rectangle, culled sources changed)
        // leaves the composite exactly as it is.
        bool dynamicUnchanged = dynamicRecorded &&
            _dynamicSolver.LastSolveUnchanged &&
            !staticRadianceChanged &&
            !request.RebuildFields &&
            !request.CompositeDirty &&
            !request.ClearDynamicRadiance &&
            request.DebugView == LightingEngine.DebugView.FinalLighting;
        if (!dynamicUnchanged &&
            (request.DynamicLightsChanged ||
            request.DynamicRadianceChanged ||
            staticRadianceChanged ||
            request.CompositeDirty))
        {
            _indirectSolver.RecordComposite(
                commandBuffer,
                partialRect,
                request.WorldRect,
                request.CellSize,
                _telemetry);
            _executedStages.Add("Composite");
        }

        if (request.RebuildFields)
        {
            RecordMaterialReadback(commandBuffer, "FrameComplete");
        }

        return new LightingFrameResult(
            invalidations,
            staticRadianceChanged,
            dynamicRadianceNeeded,
            request.ClearDynamicRadiance,
            _executedStages);
    }

    private void RecordMaterialReadback(CommandBuffer commandBuffer, string stage)
    {
        Action<string, AsyncGPUReadbackRequest>? observer = DiagnosticMaterialReadback;
        if (observer != null)
        {
            commandBuffer.RequestAsyncReadback(_resources.MaterialField!,
                request => observer(stage, request));
        }
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

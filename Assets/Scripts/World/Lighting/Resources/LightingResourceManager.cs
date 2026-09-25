#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Rendering;
using Kern.World.Lighting.Quality;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Lighting;
internal sealed class LightingResourceManager
{
    // Static solves happen as one frame-sized burst when the lighting region
    // reanchors. Bound the complete cascade ray-step cost before allocating
    // the field, then use the largest quality that fits that bound.
    private const int MaximumStaticCascadeDirections = 64;
    // A region reanchor records the complete static transport graph in one
    // command buffer. Keep that burst below the shared conservative DDA
    // budget; the quality selector lowers pixels-per-cell before it lowers
    // angular resolution further.
    private const long MaximumStaticCascadeRayWork =
        LightingPerformanceBudget.MaximumStaticCascadeRayWorkUnits;

    private readonly CascadeBufferManager _buffers = new();
    private RenderTexture? _materialField;
    private RenderTexture? _staticEmissionField;
    private RenderTexture? _directTexture;
    private RenderTexture? _staticDirectTexture;
    private RenderTexture? _lightmapTexture;
    private RenderTexture? _cellSolidMask;
    private RenderTexture? _surfaceAirCache;
    private RenderTexture? _ambientOcclusionField;

    public LightingResources Registry { get; } = new();
    public ComputeShader? LightingCompute { get; private set; }
    public CommandBuffer? LightingCommandBuffer { get; private set; }
    public RenderTexture? MaterialField => _materialField;
    public RenderTexture? StaticEmissionField => _staticEmissionField;
    public RenderTexture? DirectTexture => _directTexture;
    public RenderTexture? StaticDirectTexture => _staticDirectTexture;
    public RenderTexture? LightmapTexture => _lightmapTexture;
    public ComputeBuffer? RadianceAtlas => _buffers.RadianceAtlas;
    public ComputeBuffer? RadianceScratchAtlas => _buffers.RadianceScratchAtlas;
    public ComputeBuffer? DirtyRegions => _buffers.DirtyRegions;
    public ComputeBuffer? CascadeChangedMask => _buffers.CascadeChangedMask;
    public ComputeBuffer? DynamicLightBuffer => _buffers.DynamicLightBuffer;
    public ComputeBuffer? DynamicReachBuffer => _buffers.DynamicReachBuffer;
    public ComputeBuffer? LightingCounters => _buffers.LightingCounters;

    // Geometry caches: depend only on the material field and are rebuilt with
    // it (see WorldLighting.compute). Recreated together with the field
    // textures, which invalidates them.
    public RenderTexture? CellSolidMask => _cellSolidMask;
    public RenderTexture? SurfaceAirCache => _surfaceAirCache;
    public RenderTexture? AmbientOcclusionField => _ambientOcclusionField;
    public bool GeometryCachesValid { get; set; }
    public int CellGridWidth { get; private set; }
    public int CellGridHeight { get; private set; }

    public int SolveCascadeKernel { get; private set; }
    public int ScrollRadianceAtlasKernel { get; private set; }
    public int ClearCascadeChangedMaskKernel { get; private set; }
    public int SolveDynamicLightingKernel { get; private set; }
    public int ComposeDynamicLightingKernel { get; private set; }
    public int TraceDynamicPolarKernel { get; private set; }
    public int ClearDynamicDirectKernel { get; private set; }
    public int ResolveDirectKernel { get; private set; }
    public int ResolveTransmissionDebugKernel { get; private set; }
    public int CompositeLightingKernel { get; private set; }
    public int BuildCellSolidMaskKernel { get; private set; }
    public int BuildSurfaceAirCacheKernel { get; private set; }
    public int FieldWidth { get; private set; }
    public int FieldHeight { get; private set; }
    public int AmbientOcclusionWidth { get; private set; }
    public int AmbientOcclusionHeight { get; private set; }
    public int AtlasCapacity => _buffers.AtlasCapacity;
    public int AtlasEntryCount { get; private set; }
    public long EstimatedCascadeRayWorkUnits { get; private set; }
    public long EstimatedCascadeDispatchThreads { get; private set; }

    public readonly List<CascadeLayout> Cascades = new();

    public bool GPUPipelineInitialized { get; set; }

    public void EnsureGPUPipelineInitialized()
    {
        if (GPUPipelineInitialized)
        {
            return;
        }

        LightingShaderValidator.LoadedLightingCompute loaded = LightingShaderValidator.LoadComputeShader();
        LightingCompute = loaded.Compute;
        SolveCascadeKernel = loaded.SolveCascadeKernel;
        ScrollRadianceAtlasKernel = loaded.ScrollRadianceAtlasKernel;
        ClearCascadeChangedMaskKernel = loaded.ClearCascadeChangedMaskKernel;
        SolveDynamicLightingKernel = loaded.SolveDynamicLightingKernel;
        ComposeDynamicLightingKernel = loaded.ComposeDynamicLightingKernel;
        TraceDynamicPolarKernel = loaded.TraceDynamicPolarKernel;
        ClearDynamicDirectKernel = loaded.ClearDynamicDirectKernel;
        ResolveDirectKernel = loaded.ResolveDirectKernel;
        ResolveTransmissionDebugKernel = loaded.ResolveTransmissionDebugKernel;
        CompositeLightingKernel = loaded.CompositeLightingKernel;
        BuildCellSolidMaskKernel = loaded.BuildCellSolidMaskKernel;
        BuildSurfaceAirCacheKernel = loaded.BuildSurfaceAirCacheKernel;
        LightingShaderValidator.ValidateGpuRequirements();
        LightingShaderValidator.ValidateTerrainFieldPasses(LightingTexturePool.DestroyLightingObject);
        LightingCommandBuffer ??= new CommandBuffer
        {
            name = "Kern Radiance Cascades",
        };
        GPUPipelineInitialized = true;
        Shader.EnableKeyword("KERN_WORLD_LIGHTING");
    }

    public void ReleaseGPUPipeline()
    {
        ReleaseResources();

        LightingCommandBuffer?.Release();
        LightingCommandBuffer = null;
        LightingCompute = null;
        GPUPipelineInitialized = false;
    }

    public bool EnsureAmbientOcclusionOnlyResources(
        int gridWidth,
        int gridHeight,
        int maximumTextureDimension)
    {
        int scale = Mathf.Max(
            1,
            Mathf.Min(maximumTextureDimension / gridWidth, maximumTextureDimension / gridHeight));
        int width = gridWidth * scale;
        int height = gridHeight * scale;
        if (_materialField == null && _ambientOcclusionField != null &&
            AmbientOcclusionWidth == width && AmbientOcclusionHeight == height &&
            CellGridWidth == gridWidth && CellGridHeight == gridHeight)
        {
            return false;
        }

        ReleaseResources();
        AmbientOcclusionWidth = width;
        AmbientOcclusionHeight = height;
        CellGridWidth = gridWidth;
        CellGridHeight = gridHeight;
        _ambientOcclusionField = LightingTexturePool.CreateTexture(
            width,
            height,
            RenderTextureFormat.ARGB32,
            randomWrite: false,
            FilterMode.Bilinear,
            "_LightingAmbientOcclusionField",
            useMipMap: false);
        LightingCommandBuffer ??= new CommandBuffer
        {
            name = "Kern Ambient Occlusion",
        };
        SyncRegistry();
        return true;
    }

    public void EnsureResources(
        int gridWidth,
        int gridHeight,
        Camera camera,
        in GraphicsQualitySettings qualitySettings,
        out bool textureDimensionLimited,
        out bool cascadeBudgetLimited,
        out int effectivePixelsPerCell)
    {
        if (!camera.orthographic)
        {
            throw new InvalidOperationException(
                "Radiance Cascades requires an orthographic base camera.");
        }

        if (camera.pixelWidth <= 0 || camera.pixelHeight <= 0 ||
            camera.orthographicSize <= 0f || camera.aspect <= 0f)
        {
            throw new InvalidOperationException(
                $"Radiance Cascades received invalid camera metrics: " +
                $"pixels={camera.pixelWidth}x{camera.pixelHeight}, " +
                $"orthographicSize={camera.orthographicSize}, aspect={camera.aspect}.");
        }

        int requestedPixelsPerCell = Mathf.Clamp(qualitySettings.LightingMinimumPixelsPerCell, 1, 16);

        int requestedScale = Mathf.Max(1, Mathf.FloorToInt(requestedPixelsPerCell));
        int scale = CascadeLayoutBuilder.SelectStablePixelsPerCell(
            gridWidth,
            gridHeight,
            requestedScale,
            qualitySettings.LightingMaximumTextureDimension,
            qualitySettings.LightingCascadeAtlasLimit,
            MaximumStaticCascadeDirections,
            MaximumStaticCascadeRayWork);

        int maximumTextureScale = Mathf.Max(
            0,
            Mathf.Min(
                qualitySettings.LightingMaximumTextureDimension / gridWidth,
            qualitySettings.LightingMaximumTextureDimension / gridHeight));

        textureDimensionLimited = maximumTextureScale < requestedScale;
        cascadeBudgetLimited = scale < Mathf.Min(requestedScale, maximumTextureScale);
        effectivePixelsPerCell = scale;

        int fieldWidth = gridWidth * scale;
        int fieldHeight = gridHeight * scale;
        int ambientOcclusionScale = Mathf.Max(
            1,
            Mathf.Min(
                qualitySettings.LightingMaximumTextureDimension / gridWidth,
                qualitySettings.LightingMaximumTextureDimension / gridHeight));
        int ambientOcclusionWidth = gridWidth * ambientOcclusionScale;
        int ambientOcclusionHeight = gridHeight * ambientOcclusionScale;
        int maximumCascadeDirections = CascadeLayoutBuilder.SelectMaximumCascadeDirections(
            fieldWidth,
            fieldHeight,
            qualitySettings.LightingCascadeAtlasLimit,
            MaximumStaticCascadeDirections,
            MaximumStaticCascadeRayWork);

        const FilterMode lightmapFilterMode = FilterMode.Bilinear;

        if (FieldWidth == fieldWidth && FieldHeight == fieldHeight &&
            AmbientOcclusionWidth == ambientOcclusionWidth &&
            AmbientOcclusionHeight == ambientOcclusionHeight &&
            CellGridWidth == gridWidth && CellGridHeight == gridHeight &&
            _materialField != null &&
            _ambientOcclusionField != null &&
            RadianceAtlas != null)
        {
            if (_lightmapTexture != null && _lightmapTexture.filterMode != lightmapFilterMode)
            {
                _lightmapTexture.filterMode = lightmapFilterMode;
            }

            return;
        }

        ReleaseFieldTextures();
        FieldWidth = fieldWidth;
        FieldHeight = fieldHeight;
        AmbientOcclusionWidth = ambientOcclusionWidth;
        AmbientOcclusionHeight = ambientOcclusionHeight;

        // Transport reads mip0 only. Terrain AO owns a separate high-resolution
        // geometry field so lighting quality cannot change its silhouette.
        _materialField = LightingTexturePool.CreateTexture(
            fieldWidth,
            fieldHeight,
            RenderTextureFormat.ARGB32,
            randomWrite: false,
            FilterMode.Bilinear,
            "_LightingMaterialField",
            useMipMap: false);
        _staticEmissionField = LightingTexturePool.CreateTexture(
            fieldWidth,
            fieldHeight,
            RenderTextureFormat.ARGBHalf,
            randomWrite: false,
            FilterMode.Bilinear,
            "_StaticEmissionField",
            useMipMap: false);
        _directTexture = LightingTexturePool.CreateTexture(
            fieldWidth,
            fieldHeight,
            RenderTextureFormat.ARGBHalf,
            randomWrite: true,
            FilterMode.Bilinear,
            "_RadianceDirect");
        _staticDirectTexture = LightingTexturePool.CreateTexture(
            fieldWidth,
            fieldHeight,
            RenderTextureFormat.ARGBHalf,
            randomWrite: true,
            FilterMode.Bilinear,
            "_RadianceDirectStatic");
        _lightmapTexture = LightingTexturePool.CreateTexture(
            fieldWidth,
            fieldHeight,
            RenderTextureFormat.ARGBHalf,
            randomWrite: true,
            lightmapFilterMode,
            "_WorldLightTexture");
        CellGridWidth = gridWidth;
        CellGridHeight = gridHeight;
        _cellSolidMask = LightingTexturePool.CreateTexture(
            gridWidth,
            gridHeight,
            RenderTextureFormat.ARGBHalf,
            randomWrite: true,
            FilterMode.Point,
            "_LightingCellSolidMask");
        _surfaceAirCache = LightingTexturePool.CreateTexture(
            fieldWidth,
            fieldHeight,
            RenderTextureFormat.ARGBHalf,
            randomWrite: true,
            FilterMode.Point,
            "_LightingSurfaceAirCache");
        // AO имеет своё поле геометрии: один тексель освещения на клетку не
        // умеет ни скруглённый силуэт, ни дырку в текстуре, поэтому это поле
        // берёт всё пространственное разрешение из бюджета текстур, не
        // увеличивая работу транспорта.
        _ambientOcclusionField = LightingTexturePool.CreateTexture(
            ambientOcclusionWidth,
            ambientOcclusionHeight,
            RenderTextureFormat.ARGB32,
            randomWrite: false,
            FilterMode.Bilinear,
            "_LightingAmbientOcclusionField",
            useMipMap: false);
        GeometryCachesValid = false;

        CascadeLayoutBuilder.BuildCascadeLayouts(
            fieldWidth,
            fieldHeight,
            qualitySettings.LightingCascadeAtlasLimit,
            Cascades,
            maximumCascadeDirections);
        AtlasEntryCount = Cascades[^1].Offset + Cascades[^1].EntryCount;
        EstimatedCascadeRayWorkUnits = CascadeCostCalculator.EstimateRayWorkUnits(Cascades);
        EstimatedCascadeDispatchThreads = 0;
        foreach (CascadeLayout cascade in Cascades)
        {
            EstimatedCascadeDispatchThreads += cascade.EntryCount;
        }
        EnsurePersistentBuffers(
            qualitySettings.LightingCascadeAtlasLimit,
            qualitySettings.LightingMaximumLightCount);
        SyncRegistry();
    }

    public void ReleaseResources()
    {
        _buffers.ReleaseBuffers();
        AtlasEntryCount = 0;
        EstimatedCascadeRayWorkUnits = 0;
        EstimatedCascadeDispatchThreads = 0;
        ReleaseFieldTextures();
        Registry.ClearReferences();
    }

    private void SyncRegistry()
    {
        Registry.Compute = LightingCompute;
        Registry.CommandBuffer = LightingCommandBuffer;
        Registry.FieldWidth = FieldWidth;
        Registry.FieldHeight = FieldHeight;

        Registry.Geometry.Material = _materialField;
        Registry.Geometry.StaticEmission = _staticEmissionField;
        Registry.Geometry.CellSolidMask = _cellSolidMask;
        Registry.Geometry.SurfaceAirCache = _surfaceAirCache;
        Registry.Geometry.AmbientOcclusion = _ambientOcclusionField;
        Registry.Geometry.AmbientOcclusionWidth = AmbientOcclusionWidth;
        Registry.Geometry.AmbientOcclusionHeight = AmbientOcclusionHeight;
        Registry.Geometry.CellGridWidth = CellGridWidth;
        Registry.Geometry.CellGridHeight = CellGridHeight;
        Registry.Geometry.CachesValid = GeometryCachesValid;

        Registry.Cascade.Atlas = RadianceAtlas;
        Registry.Cascade.AtlasCapacity = AtlasCapacity;
        Registry.Cascade.AtlasEntryCount = AtlasEntryCount;
        Registry.Cascade.Layouts.Clear();
        Registry.Cascade.Layouts.AddRange(Cascades);

        Registry.Direct.Static = _staticDirectTexture;
        Registry.Direct.Dynamic = _directTexture;
        Registry.Direct.DynamicLightsBuffer = DynamicLightBuffer;

        Registry.Output.Lightmap = _lightmapTexture;
    }

    public void ReleaseFieldTextures()
    {
        LightingTexturePool.ReleaseTexture(ref _materialField);
        LightingTexturePool.ReleaseTexture(ref _staticEmissionField);
        LightingTexturePool.ReleaseTexture(ref _directTexture);
        LightingTexturePool.ReleaseTexture(ref _staticDirectTexture);
        LightingTexturePool.ReleaseTexture(ref _lightmapTexture);
        LightingTexturePool.ReleaseTexture(ref _cellSolidMask);
        LightingTexturePool.ReleaseTexture(ref _surfaceAirCache);
        LightingTexturePool.ReleaseTexture(ref _ambientOcclusionField);
        GeometryCachesValid = false;
        CellGridWidth = 0;
        CellGridHeight = 0;
        FieldWidth = 0;
        FieldHeight = 0;
        AmbientOcclusionWidth = 0;
        AmbientOcclusionHeight = 0;
        Cascades.Clear();
    }

    public void EnsurePersistentBuffers(long atlasDimension, int maximumLightCount) =>
        _buffers.EnsurePersistentBuffers(AtlasEntryCount, atlasDimension, maximumLightCount);

    public void EnsureDirtyRegionCapacity(int capacity) =>
        _buffers.EnsureDirtyRegionCapacity(capacity);

    public void SwapRadianceAtlases()
    {
        _buffers.SwapRadianceAtlases();
        Registry.Cascade.Atlas = RadianceAtlas;
    }

    public void EnsureScratchAtlas() => _buffers.EnsureScratchAtlas();
}

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

    private static readonly uint[] EmptyLightingCounters = new uint[3];
    private readonly ComputeBuffer?[] _lightingCounterBuffers = new ComputeBuffer?[2];
    private int _activeLightingCounterBuffer;
    private RenderTexture? _materialField;
    private RenderTexture? _staticEmissionField;
    private RenderTexture? _directTexture;
    private RenderTexture? _staticDirectTexture;
    private RenderTexture? _bounceTexture;
    private RenderTexture? _lightmapTexture;
    private RenderTexture? _cellSolidMask;

    public LightingResources Registry { get; } = new();
    public ComputeShader? LightingCompute { get; private set; }
    public CommandBuffer? LightingCommandBuffer { get; private set; }
    public RenderTexture? MaterialField => _materialField;
    public RenderTexture? StaticEmissionField => _staticEmissionField;
    public RenderTexture? DirectTexture => _directTexture;
    public RenderTexture? StaticDirectTexture => _staticDirectTexture;
    public RenderTexture? BounceTexture => _bounceTexture;
    public RenderTexture? LightmapTexture => _lightmapTexture;
    public ComputeBuffer? RadianceAtlas { get; private set; }
    public ComputeBuffer? RadianceScratchAtlas { get; private set; }
    public ComputeBuffer? DirtyRegions { get; private set; }
    public ComputeBuffer? CascadeChangedMask { get; private set; }
    public ComputeBuffer? DynamicLightBuffer { get; private set; }
    public ComputeBuffer? LightingCounters => _lightingCounterBuffers[_activeLightingCounterBuffer];

    // Geometry caches: depend only on the material field and are rebuilt with
    // it (see WorldLighting.compute). Recreated together with the field
    // textures, which invalidates them.
    public RenderTexture? CellSolidMask => _cellSolidMask;
    public ComputeBuffer? BounceTaps { get; private set; }
    public ComputeBuffer? BounceFilterWeights { get; private set; }
    public bool GeometryCachesValid { get; set; }
    public int CellGridWidth { get; private set; }
    public int CellGridHeight { get; private set; }

    public int SolveCascadeKernel { get; private set; }
    public int ScrollRadianceAtlasKernel { get; private set; }
    public int SolveDynamicLightingKernel { get; private set; }
    public int ComposeDynamicLightingKernel { get; private set; }
    public int TraceDynamicPolarKernel { get; private set; }
    public int ClearDynamicDirectKernel { get; private set; }
    public int ResolveDirectKernel { get; private set; }
    public int ResolveTransmissionDebugKernel { get; private set; }
    public int SolveDiffuseBounceKernel { get; private set; }
    public int CompositeLightingKernel { get; private set; }
    public int BuildCellSolidMaskKernel { get; private set; }
    public int BuildBounceTapsKernel { get; private set; }
    public int BuildBounceFilterKernel { get; private set; }

    public int FieldWidth { get; private set; }
    public int FieldHeight { get; private set; }
    public int BounceWidth { get; private set; }
    public int BounceHeight { get; private set; }
    public int AtlasCapacity { get; private set; }
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
        SolveDynamicLightingKernel = loaded.SolveDynamicLightingKernel;
        ComposeDynamicLightingKernel = loaded.ComposeDynamicLightingKernel;
        TraceDynamicPolarKernel = loaded.TraceDynamicPolarKernel;
        ClearDynamicDirectKernel = loaded.ClearDynamicDirectKernel;
        ResolveDirectKernel = loaded.ResolveDirectKernel;
        ResolveTransmissionDebugKernel = loaded.ResolveTransmissionDebugKernel;
        SolveDiffuseBounceKernel = loaded.SolveDiffuseBounceKernel;
        CompositeLightingKernel = loaded.CompositeLightingKernel;
        BuildCellSolidMaskKernel = loaded.BuildCellSolidMaskKernel;
        BuildBounceTapsKernel = loaded.BuildBounceTapsKernel;
        BuildBounceFilterKernel = loaded.BuildBounceFilterKernel;

        LightingShaderValidator.ValidateGpuRequirements();
        LightingShaderValidator.ValidateMaterialFieldPass(LightingTexturePool.DestroyLightingObject);
        LightingCommandBuffer = new CommandBuffer
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

    public void EnsureResources(
        int gridWidth,
        int gridHeight,
        Camera camera,
        in GraphicsQualitySettings qualitySettings,
        LightingQualityMode qualityMode,
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

        int requestedPixelsPerCell = qualityMode == LightingQualityMode.PerBlock
            ? 1
            : Mathf.Clamp(qualitySettings.LightingMinimumPixelsPerCell, 1, 16);

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
        int maximumCascadeDirections = CascadeLayoutBuilder.SelectMaximumCascadeDirections(
            fieldWidth,
            fieldHeight,
            qualitySettings.LightingCascadeAtlasLimit,
            MaximumStaticCascadeDirections,
            MaximumStaticCascadeRayWork);
        int bounceWidth = Mathf.Max(1, Mathf.CeilToInt(fieldWidth * 0.5f));
        int bounceHeight = Mathf.Max(1, Mathf.CeilToInt(fieldHeight * 0.5f));

        FilterMode lightmapFilterMode = qualityMode == LightingQualityMode.PerBlock
            ? FilterMode.Point
            : FilterMode.Bilinear;

        if (FieldWidth == fieldWidth && FieldHeight == fieldHeight &&
            CellGridWidth == gridWidth && CellGridHeight == gridHeight &&
            _materialField != null &&
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
        BounceWidth = bounceWidth;
        BounceHeight = bounceHeight;

        _materialField = LightingTexturePool.CreateTexture(
            fieldWidth,
            fieldHeight,
            RenderTextureFormat.ARGB32,
            randomWrite: false,
            FilterMode.Bilinear,
            "_LightingMaterialField",
            useMipMap: true);
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
        _bounceTexture = LightingTexturePool.CreateTexture(
            bounceWidth,
            bounceHeight,
            RenderTextureFormat.ARGBHalf,
            randomWrite: true,
            FilterMode.Bilinear,
            "_RadianceBounce");
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
        BounceTaps = new ComputeBuffer(
            bounceWidth * bounceHeight * 16,
            sizeof(float) * 4,
            ComputeBufferType.Structured);
        BounceFilterWeights = new ComputeBuffer(
            fieldWidth * fieldHeight * 4,
            sizeof(float) * 4,
            ComputeBufferType.Structured);
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
        DynamicLightBuffer?.Release();
        DynamicLightBuffer = null;
        for (int index = 0; index < _lightingCounterBuffers.Length; index++)
        {
            _lightingCounterBuffers[index]?.Release();
            _lightingCounterBuffers[index] = null;
        }
        _activeLightingCounterBuffer = 0;
        RadianceAtlas?.Release();
        RadianceAtlas = null;
        RadianceScratchAtlas?.Release();
        RadianceScratchAtlas = null;
        DirtyRegions?.Release();
        DirtyRegions = null;
        CascadeChangedMask?.Release();
        CascadeChangedMask = null;
        AtlasCapacity = 0;
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

        Registry.Bounce.Texture = _bounceTexture;
        Registry.Bounce.Taps = BounceTaps;
        Registry.Bounce.FilterWeights = BounceFilterWeights;
        Registry.Bounce.Width = BounceWidth;
        Registry.Bounce.Height = BounceHeight;

        Registry.Output.Lightmap = _lightmapTexture;
    }

    public void ReleaseFieldTextures()
    {
        LightingTexturePool.ReleaseTexture(ref _materialField);
        LightingTexturePool.ReleaseTexture(ref _staticEmissionField);
        LightingTexturePool.ReleaseTexture(ref _directTexture);
        LightingTexturePool.ReleaseTexture(ref _staticDirectTexture);
        LightingTexturePool.ReleaseTexture(ref _bounceTexture);
        LightingTexturePool.ReleaseTexture(ref _lightmapTexture);
        LightingTexturePool.ReleaseTexture(ref _cellSolidMask);
        BounceTaps?.Release();
        BounceTaps = null;
        BounceFilterWeights?.Release();
        BounceFilterWeights = null;
        GeometryCachesValid = false;
        CellGridWidth = 0;
        CellGridHeight = 0;
        FieldWidth = 0;
        FieldHeight = 0;
        BounceWidth = 0;
        BounceHeight = 0;
        Cascades.Clear();
    }

    public void EnsurePersistentBuffers(long atlasDimension, int maximumLightCount)
    {
        long maximumCapacity = atlasDimension * atlasDimension * 4;

        if (maximumCapacity <= 0 || maximumCapacity > int.MaxValue)
        {
            throw new InvalidOperationException(
                "Radiance cascade atlas capacity exceeds the supported structured-buffer size.");
        }

        if (AtlasEntryCount > maximumCapacity)
        {
            throw new InvalidOperationException(
                "Radiance cascade layout exceeds the configured atlas capacity.");
        }

        int requiredCapacity = Mathf.Max(1, AtlasEntryCount);

        if (RadianceAtlas == null || AtlasCapacity < requiredCapacity)
        {
            RadianceAtlas?.Release();
            RadianceAtlas = new ComputeBuffer(
                requiredCapacity,
                sizeof(uint) * 3,
                ComputeBufferType.Structured);
            AtlasCapacity = requiredCapacity;
        }

        if (CascadeChangedMask == null || CascadeChangedMask.count < requiredCapacity)
        {
            CascadeChangedMask?.Release();
            CascadeChangedMask = new ComputeBuffer(
                requiredCapacity,
                sizeof(uint),
                ComputeBufferType.Structured);
        }

        int clampedLightCount = Mathf.Max(1, maximumLightCount);

        if (DynamicLightBuffer == null || DynamicLightBuffer.count != clampedLightCount)
        {
            DynamicLightBuffer?.Release();
            DynamicLightBuffer = new ComputeBuffer(
                clampedLightCount,
                sizeof(float) * 8,
                ComputeBufferType.Structured);
        }

        if (_lightingCounterBuffers[0] == null || _lightingCounterBuffers[0]!.count != 3 ||
            _lightingCounterBuffers[1] == null || _lightingCounterBuffers[1]!.count != 3)
        {
            for (int index = 0; index < _lightingCounterBuffers.Length; index++)
            {
                _lightingCounterBuffers[index]?.Release();
                _lightingCounterBuffers[index] = new ComputeBuffer(
                    3,
                    sizeof(uint),
                    ComputeBufferType.Structured);
            }
        }
    }

    public void EnsureDirtyRegionCapacity(int capacity)
    {
        int requiredCapacity = Mathf.Max(1, capacity);
        if (DirtyRegions != null && DirtyRegions.count >= requiredCapacity)
        {
            return;
        }

        DirtyRegions?.Release();
        DirtyRegions = new ComputeBuffer(
            requiredCapacity,
            sizeof(int) * 4,
            ComputeBufferType.Structured);
    }

    public void SwapRadianceAtlases()
    {
        EnsureScratchAtlas();
        (RadianceAtlas, RadianceScratchAtlas) =
            (RadianceScratchAtlas, RadianceAtlas);
        Registry.Cascade.Atlas = RadianceAtlas;
    }

    // The atlas scroll path is disabled (see LightingUpdateCoordinator), so
    // its scratch duplicate is allocated lazily on first scroll use instead
    // of pinning a full atlas in VRAM forever. Re-enabling scroll needs no
    // other change: RecordScroll reaches this through SwapRadianceAtlases.
    public void EnsureScratchAtlas()
    {
        if (RadianceScratchAtlas != null && RadianceScratchAtlas.count == AtlasCapacity && AtlasCapacity > 0)
        {
            return;
        }

        RadianceScratchAtlas?.Release();
        RadianceScratchAtlas = AtlasCapacity > 0
            ? new ComputeBuffer(AtlasCapacity, sizeof(uint) * 3, ComputeBufferType.Structured)
            : null;
    }
}

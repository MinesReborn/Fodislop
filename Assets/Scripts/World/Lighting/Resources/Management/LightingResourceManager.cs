#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Diagnostics;
using Kern.Core.Interfaces;
using Kern.Rendering;
using Kern.World.Lighting.Quality;
using UnityEngine;
using UnityEngine.Rendering;

using Kern.Diagnostics.Memory;

namespace Kern.World.Lighting;
internal sealed class LightingResourceManager
{
    private readonly LightingDynamicDistanceResources _dynamicDistance = new();
    private readonly LightingAmbientOcclusionResources _ambientOcclusion = new();
    public bool DynamicDistanceFieldValid
    {
        get => _dynamicDistance.DynamicDistanceFieldValid;
        set => _dynamicDistance.DynamicDistanceFieldValid = value;
    }
    public Material AmbientOcclusionClearMaterial => _ambientOcclusion.AmbientOcclusionClearMaterial;
    public void EnsureDynamicDistanceField() => _dynamicDistance.EnsureDynamicDistanceField(FieldWidth, FieldHeight);
    public void ReleaseDynamicDistanceField() => _dynamicDistance.ReleaseDynamicDistanceField();
    public static void ValidateDynamicDistanceFieldRequest(int width, int height) =>
        LightingDynamicDistanceResources.ValidateDynamicDistanceFieldRequest(width, height);

    // Authored transport quality is independent of field density and camera
    // coverage. Cost estimates are diagnostics, never an automatic LOD.

    private readonly CascadeBufferManager _buffers = new();
    private RenderTexture? _materialField;
    private RenderTexture? _staticGlowField;
    private RenderTexture? _directTexture;
    private RenderTexture? _staticDirectTexture;
    private RenderTexture? _lightmapTexture;
    private RenderTexture? _cellSolidMask;
    private RenderTexture? _surfaceAirCache;
    private ComputeBuffer? _cleanCellRows;
    private ComputeBuffer? _cleanCellPrefix;
    private GraphicsQualitySettings _allocatedQuality;
    private int _allocatedMaximumCascadeDirections;
    private int _allocatedProbePixelsPerCell;

    private RenderTexture? _reanchorMaterial;
    private RenderTexture? _reanchorGlow;
    private ComputeBuffer? _reanchorRows;
    private ComputeBuffer? _reanchorChanges;

    public RenderTexture? ReanchorMaterial => _reanchorMaterial;
    public RenderTexture? ReanchorGlow => _reanchorGlow;
    public ComputeBuffer? ReanchorRows => _reanchorRows;
    public ComputeBuffer? ReanchorChanges => _reanchorChanges;

    // SolveCascade declares this binding even when region reuse is disabled.
    // An empty change stream needs one zero entry, not full saved fields and
    // two dense prefix-sum buffers on every dynamic frame.
    public void EnsureReanchorChangeBinding()
    {
        if (_reanchorChanges != null)
        {
            return;
        }
        _reanchorChanges = new ComputeBuffer(1, sizeof(uint));
        _reanchorChanges.SetData(new uint[] { 0 });
    }

    public void EnsureReanchorFields()
    {
        if (_reanchorMaterial != null && _reanchorMaterial.width == FieldWidth &&
            _reanchorMaterial.height == FieldHeight)
        {
            return;
        }

        MemoryAllocationGuard.Require("Lighting reanchor generation",
            LightingAllocationEstimate.TextureBytes(FieldWidth, FieldHeight, 1, 20));
        ReleaseReanchorFields();
        _reanchorMaterial = LightingTexturePool.CreateTexture(FieldWidth, FieldHeight,
            RenderTextureFormat.ARGB32, false, FilterMode.Point, "Lighting.ReanchorMaterial");
        _reanchorGlow = LightingTexturePool.CreateTexture(FieldWidth, FieldHeight,
            RenderTextureFormat.ARGBHalf, false, FilterMode.Point, "Lighting.ReanchorGlow");
        _reanchorRows = new ComputeBuffer(checked(FieldWidth * FieldHeight), sizeof(uint));
        _reanchorChanges = new ComputeBuffer(checked(FieldWidth * FieldHeight), sizeof(uint));
    }

    private void ReleaseReanchorFields()
    {
        LightingTexturePool.ReleaseTexture(ref _reanchorMaterial);
        LightingTexturePool.ReleaseTexture(ref _reanchorGlow);
        _reanchorRows?.Release();
        _reanchorRows = null;
        _reanchorChanges?.Release();
        _reanchorChanges = null;
    }

    public LightingResources Registry { get; } = new();
    public ComputeShader? LightingCompute { get; private set; }
    public CommandBuffer? LightingCommandBuffer { get; private set; }
    public RenderTexture? MaterialField => _materialField;
    public RenderTexture? StaticGlowField => _staticGlowField;
    public RenderTexture? DirectTexture => _directTexture;
    public RenderTexture? StaticDirectTexture => _staticDirectTexture;
    public RenderTexture? LightmapTexture => _lightmapTexture;
    public ComputeBuffer? RadianceAtlas => _buffers.RadianceAtlas;
    public ComputeBuffer? RadianceScratchAtlas => _buffers.RadianceScratchAtlas;
    public ComputeBuffer? DirtyRegions => _buffers.DirtyRegions;
    public ComputeBuffer? CascadeChangedMask => _buffers.CascadeChangedMask;
    public ComputeBuffer? DynamicLightBuffer => _buffers.DynamicLightBuffer;
    public ComputeBuffer? LightingCounters => _buffers.LightingCounters;

    // Geometry caches: depend only on the material field and are rebuilt with
    // it (see WorldLighting.compute). Recreated together with the field
    // textures, which invalidates them.
    public RenderTexture? CellSolidMask => _cellSolidMask;
    public RenderTexture? SurfaceAirCache => _surfaceAirCache;
    // Summed-area table of cells that are not clean air; scratch rows + result.
    public ComputeBuffer? CleanCellRows => _cleanCellRows;
    public ComputeBuffer? CleanCellPrefix => _cleanCellPrefix;
    public RenderTexture? DynamicDistanceField => _dynamicDistance.Field;
    public ComputeBuffer? DynamicDistanceSeedsA => _dynamicDistance.SeedsA;
    public ComputeBuffer? DynamicDistanceSeedsB => _dynamicDistance.SeedsB;
    public RenderTexture? AmbientOcclusionField => _ambientOcclusion.Field;
    public bool HasAmbientOcclusionField => _ambientOcclusion.Field != null;

    /// <summary>
    /// Переносит уже посчитанное AO-поле на новое начало светового региона.
    /// Перекрытие снимается в буфер и возвращается со сдвигом; полоса,
    /// вошедшая в регион, затем пересчитывается как обычная региональная
    /// инвалидация, вместо полной очистки всего поля.
    /// </summary>
    public void RecordAmbientOcclusionScroll(CommandBuffer commandBuffer, Vector2Int regionDelta)
    {
        RenderTexture field = _ambientOcclusion.Field ??
            throw new InvalidOperationException("Ambient occlusion field is not allocated.");
        int scale = LightingConfigHolder.AmbientOcclusionPixelsPerCell;
        int shiftX = -regionDelta.x * scale;
        int shiftY = (Kern.Core.Interfaces.WorldLighting.LightingFieldOrientation.RowsTopDown
            ? regionDelta.y
            : -regionDelta.y) * scale;
        if (shiftX == 0 && shiftY == 0)
        {
            return;
        }

        RenderTexture scratch = _ambientOcclusion.EnsureShiftScratch(field.width, field.height);
        commandBuffer.CopyTexture(field, 0, 0, scratch, 0, 0);
        int sourceX0 = Mathf.Max(0, -shiftX);
        int sourceX1 = Mathf.Min(field.width, field.width - shiftX);
        int sourceY0 = Mathf.Max(0, -shiftY);
        int sourceY1 = Mathf.Min(field.height, field.height - shiftY);
        int copyWidth = sourceX1 - sourceX0;
        int copyHeight = sourceY1 - sourceY0;
        if (copyWidth <= 0 || copyHeight <= 0)
        {
            return;
        }

        commandBuffer.CopyTexture(
            scratch, 0, 0,
            sourceX0, sourceY0, copyWidth, copyHeight,
            field, 0, 0,
            sourceX0 + shiftX, sourceY0 + shiftY);
        Kern.Diagnostics.Frames.FrameEventLog.Record(
            $"AO: перенос {field.width}×{field.height}, сдвиг {regionDelta.x},{regionDelta.y} клеток");
    }

    public bool GeometryCachesValid { get; set; }
    public int CellGridWidth { get; private set; }
    public int CellGridHeight { get; private set; }

    public int SolveCascadeKernel { get; private set; }
    public int ScrollRadianceAtlasKernel { get; private set; }
    public int ClearCascadeChangedMaskKernel { get; private set; }
    public int BuildReanchorChangeRowsKernel { get; private set; }
    public int BuildReanchorChangeColumnsKernel { get; private set; }
    public int SolveDynamicLightingKernel { get; private set; }
    public int ComposeDynamicLightingKernel { get; private set; }
    public int TraceDynamicPolarKernel { get; private set; }
    public int TraceDynamicPolarBatchKernel { get; private set; }
    public int SolveDynamicLightingBatchKernel { get; private set; }
    public int ClearDynamicDirectKernel { get; private set; }
    public int ResolveDirectKernel { get; private set; }
    public int ResolveTransmissionDebugKernel { get; private set; }
    public int CompositeLightingKernel { get; private set; }
    public int BuildCellSolidMaskKernel { get; private set; }
    public int BuildSurfaceAirCacheKernel { get; private set; }
    public int BuildCleanCellRowsKernel { get; private set; }
    public int BuildCleanCellColumnsKernel { get; private set; }
    public int SeedDynamicDistanceFieldKernel { get; private set; }
    public int JumpFloodDynamicDistanceFieldKernel { get; private set; }
    public int ResolveDynamicDistanceFieldKernel { get; private set; }
    public int FieldWidth { get; private set; }
    public int FieldHeight { get; private set; }
    // Receiver lattice of static/dynamic direct, surface cache and lightmap.
    public int LightWidth { get; private set; }
    public int LightHeight { get; private set; }
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
        BuildReanchorChangeRowsKernel = loaded.Compute.FindKernel("BuildReanchorChangeRows");
        BuildReanchorChangeColumnsKernel = loaded.Compute.FindKernel("BuildReanchorChangeColumns");
        SolveCascadeKernel = loaded.SolveCascadeKernel;
        ScrollRadianceAtlasKernel = loaded.ScrollRadianceAtlasKernel;
        ClearCascadeChangedMaskKernel = loaded.ClearCascadeChangedMaskKernel;
        SolveDynamicLightingKernel = loaded.SolveDynamicLightingKernel;
        ComposeDynamicLightingKernel = loaded.ComposeDynamicLightingKernel;
        TraceDynamicPolarKernel = loaded.TraceDynamicPolarKernel;
        TraceDynamicPolarBatchKernel = loaded.TraceDynamicPolarBatchKernel;
        SolveDynamicLightingBatchKernel = loaded.SolveDynamicLightingBatchKernel;
        ClearDynamicDirectKernel = loaded.ClearDynamicDirectKernel;
        ResolveDirectKernel = loaded.ResolveDirectKernel;
        ResolveTransmissionDebugKernel = loaded.ResolveTransmissionDebugKernel;
        CompositeLightingKernel = loaded.CompositeLightingKernel;
        BuildCellSolidMaskKernel = loaded.BuildCellSolidMaskKernel;
        BuildSurfaceAirCacheKernel = loaded.BuildSurfaceAirCacheKernel;
        BuildCleanCellRowsKernel = loaded.Compute.FindKernel("BuildCleanCellRows");
        BuildCleanCellColumnsKernel = loaded.Compute.FindKernel("BuildCleanCellColumns");
        SeedDynamicDistanceFieldKernel = loaded.SeedDynamicDistanceFieldKernel;
        JumpFloodDynamicDistanceFieldKernel = loaded.JumpFloodDynamicDistanceFieldKernel;
        ResolveDynamicDistanceFieldKernel = loaded.ResolveDynamicDistanceFieldKernel;
        LightingShaderValidator.ValidateGPURequirements();
        LightingShaderValidator.ValidateTerrainFieldPasses(LightingTexturePool.DestroyLightingObject);
        LightingFieldOrientationValidator.EnsureValidated();
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
        int gridHeight)
    {
        int scale = LightingAmbientOcclusionResources.AmbientOcclusionScale(gridWidth, gridHeight);
        int width = gridWidth * scale;
        int height = gridHeight * scale;
        if (_materialField == null && _ambientOcclusion.Field != null &&
            AmbientOcclusionWidth == width && AmbientOcclusionHeight == height &&
            CellGridWidth == gridWidth && CellGridHeight == gridHeight)
        {
            return false;
        }

        ReleaseResources();
        // AO-only fields use the same raster transform and the same readers.
        LightingFieldOrientationValidator.EnsureValidated();
        _ambientOcclusion.EnsureAmbientOcclusionClearMaterial();
        AmbientOcclusionWidth = width;
        AmbientOcclusionHeight = height;
        CellGridWidth = gridWidth;
        CellGridHeight = gridHeight;
        _ambientOcclusion.Allocate(width, height);
        LightingCommandBuffer ??= new CommandBuffer
        {
            name = "Kern Ambient Occlusion",
        };
        SyncRegistry();
        return true;
    }

    public void ValidateRequestedQuality(LightingQualityTuning quality, in GraphicsQualitySettings settings) =>
        LightingResourceQuality.ValidateRequestedQuality(CellGridWidth, CellGridHeight, quality, settings);

    public bool TryApplyQualityTuning(LightingQualityTuning quality, in GraphicsQualitySettings settings, out string rejection) =>
        LightingResourceQuality.TryApplyQualityTuning(CellGridWidth, CellGridHeight, quality, settings, out rejection);

    public void EnsureResources(
        int gridWidth,
        int gridHeight,
        Camera camera,
        in GraphicsQualitySettings qualitySettings,
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

        effectivePixelsPerCell = LightingQualityTuningController.FieldPixelsPerCell;
        _ambientOcclusion.EnsureAmbientOcclusionClearMaterial();
        // Resource identity depends on coverage and settings, never camera pose.
        // Cache the selected probe layout as well as textures: repeating the
        // builder allocates scratch lists even when the layout is unchanged.
        if (CellGridWidth == gridWidth && CellGridHeight == gridHeight &&
            _allocatedQuality.Equals(qualitySettings) &&
            FieldWidth == checked(gridWidth * effectivePixelsPerCell) &&
            FieldHeight == checked(gridHeight * effectivePixelsPerCell) &&
            LightWidth == checked(gridWidth * LightingQualityTuningController.LightPixelsPerCell) &&
            LightHeight == checked(gridHeight * LightingQualityTuningController.LightPixelsPerCell) &&
            _allocatedMaximumCascadeDirections == LightingQualityTuningController.MaximumStaticCascadeDirections &&
            _allocatedProbePixelsPerCell == LightingQualityTuningController.CascadeProbePixelsPerCell &&
            _materialField != null && _ambientOcclusion.Field != null && RadianceAtlas != null)
        {
            return;
        }

        int probeScale = LightingQualityTuningController.CascadeProbePixelsPerCell;

        int fieldWidth = checked(gridWidth * LightingQualityTuningController.FieldPixelsPerCell);
        int fieldHeight = checked(gridHeight * LightingQualityTuningController.FieldPixelsPerCell);
        int lightWidth = checked(gridWidth * LightingQualityTuningController.LightPixelsPerCell);
        int lightHeight = checked(gridHeight * LightingQualityTuningController.LightPixelsPerCell);
        int ambientOcclusionScale = LightingAmbientOcclusionResources.AmbientOcclusionScale(gridWidth, gridHeight);
        int ambientOcclusionWidth = gridWidth * ambientOcclusionScale;
        int ambientOcclusionHeight = gridHeight * ambientOcclusionScale;

        const FilterMode lightmapFilterMode = FilterMode.Bilinear;

        // Keep the requested sparse lattice and angular progression. A dense
        // geometry grid increases the conservative texel-step estimate; it
        // must not silently replace 4 probes/cell with 1 probe/cell.
        // Validate a candidate without mutating the currently published layout.
        // The same preflight serves the manual tuning panel.
        var candidate = new List<CascadeLayout>();
        LightingResourceLayout.Validate(gridWidth, gridHeight, LightingQualityTuningController.Current,
            qualitySettings, candidate);
        long candidateEntries = (long)candidate[^1].Offset + candidate[^1].EntryCount;
        MemoryAllocationGuard.Require("Lighting fields and cascade buffers",
            LightingAllocationEstimate.FieldBytes(fieldWidth, fieldHeight, lightWidth, lightHeight,
                ambientOcclusionWidth, ambientOcclusionHeight,
                gridWidth, gridHeight, candidateEntries, qualitySettings.LightingMaximumLightCount));
        _allocatedQuality = qualitySettings;
        _allocatedMaximumCascadeDirections = LightingQualityTuningController.MaximumStaticCascadeDirections;
        _allocatedProbePixelsPerCell = probeScale;

        ReleaseFieldTextures();
        FieldWidth = fieldWidth;
        FieldHeight = fieldHeight;
        LightWidth = lightWidth;
        LightHeight = lightHeight;
        AmbientOcclusionWidth = ambientOcclusionWidth;
        AmbientOcclusionHeight = ambientOcclusionHeight;

        // Transport reads mip0 only. Terrain AO owns a separate high-resolution
        // geometry field so lighting quality cannot change its silhouette.
        _materialField = LightingTexturePool.CreateTexture(
            fieldWidth,
            fieldHeight,
            RenderTextureFormat.ARGB32,
            randomWrite: false,
            FilterMode.Point,
            "_LightingMaterialField",
            useMipMap: false);
        _staticGlowField = LightingTexturePool.CreateTexture(
            fieldWidth,
            fieldHeight,
            RenderTextureFormat.ARGBHalf,
            randomWrite: false,
            FilterMode.Bilinear,
            "_StaticGlowField",
            useMipMap: false);
        _directTexture = LightingTexturePool.CreateTexture(
            lightWidth,
            lightHeight,
            RenderTextureFormat.ARGBHalf,
            randomWrite: true,
            FilterMode.Bilinear,
            "_RadianceDirect");
        _staticDirectTexture = LightingTexturePool.CreateTexture(
            lightWidth,
            lightHeight,
            RenderTextureFormat.ARGBHalf,
            randomWrite: true,
            FilterMode.Bilinear,
            "_RadianceDirectStatic");
        _lightmapTexture = LightingTexturePool.CreateTexture(
            lightWidth,
            lightHeight,
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
        _cleanCellRows = new ComputeBuffer(checked(gridWidth * gridHeight), sizeof(uint) * 2, ComputeBufferType.Structured);
        _cleanCellPrefix = new ComputeBuffer(checked(gridWidth * gridHeight), sizeof(uint) * 2, ComputeBufferType.Structured);
        _surfaceAirCache = LightingTexturePool.CreateTexture(
            lightWidth,
            lightHeight,
            RenderTextureFormat.ARGBHalf,
            randomWrite: true,
            FilterMode.Point,
            "_LightingSurfaceAirCache");
        // AO is calculated directly at world raster density and has an
        // independent lifecycle in the AO-only graphics preset.
        _ambientOcclusion.Allocate(ambientOcclusionWidth, ambientOcclusionHeight);
        GeometryCachesValid = false;

        // ReleaseFieldTextures clears the old cascade publication. Commit the
        // validated candidate only after the replacement fields are acquired.
        Cascades.AddRange(candidate);
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
        _ambientOcclusion.ReleaseMaterial();

        ReleaseReanchorFields();
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
        Registry.LightWidth = LightWidth;
        Registry.LightHeight = LightHeight;

        Registry.Geometry.Material = _materialField;
        Registry.Geometry.StaticGlow = _staticGlowField;
        Registry.Geometry.CellSolidMask = _cellSolidMask;
        Registry.Geometry.SurfaceAirCache = _surfaceAirCache;
        Registry.Geometry.AmbientOcclusion = _ambientOcclusion.Field;
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
        ReleaseReanchorFields();
        LightingTexturePool.ReleaseTexture(ref _materialField);
        LightingTexturePool.ReleaseTexture(ref _staticGlowField);
        LightingTexturePool.ReleaseTexture(ref _directTexture);
        LightingTexturePool.ReleaseTexture(ref _staticDirectTexture);
        LightingTexturePool.ReleaseTexture(ref _lightmapTexture);
        LightingTexturePool.ReleaseTexture(ref _cellSolidMask);
        _cleanCellRows?.Release();
        _cleanCellRows = null;
        _cleanCellPrefix?.Release();
        _cleanCellPrefix = null;
        ReleaseDynamicDistanceField();
        LightingTexturePool.ReleaseTexture(ref _surfaceAirCache);
        _ambientOcclusion.ReleaseField();
        GeometryCachesValid = false;
        CellGridWidth = 0;
        CellGridHeight = 0;
        FieldWidth = 0;
        FieldHeight = 0;
        LightWidth = 0;
        LightHeight = 0;
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

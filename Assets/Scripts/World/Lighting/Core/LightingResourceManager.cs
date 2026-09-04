#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Rendering;
using Fodinae.World.Lighting.Pipeline;
using Fodinae.World.Lighting.Pipeline.Stages;
using Fodinae.World.Lighting.Quality;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.World.Lighting
{
    /// <summary>
    /// Owns all GPU resources for the lighting engine: render textures,
    /// compute buffers, cascade layouts, and their lifecycle.
    /// </summary>
    internal sealed class LightingResourceManager
    {
        private RenderTexture? _materialField;
        private RenderTexture? _staticEmissionField;
        private RenderTexture? _dynamicEmissionField;
        private RenderTexture? _automaticNormalField;
        private RenderTexture? _directTexture;
        private RenderTexture? _staticDirectTexture;
        private RenderTexture? _bounceTexture;
        private RenderTexture? _lightmapTexture;

        public ComputeShader? LightingCompute { get; private set; }
        public CommandBuffer? LightingCommandBuffer { get; private set; }
        public RenderTexture? MaterialField => _materialField;
        public RenderTexture? StaticEmissionField => _staticEmissionField;
        public RenderTexture? DynamicEmissionField => _dynamicEmissionField;
        public Material? DynamicEmissionMaterial { get; private set; }
        public RenderTexture? AutomaticNormalField => _automaticNormalField;
        public RenderTexture? DirectTexture => _directTexture;
        public RenderTexture? StaticDirectTexture => _staticDirectTexture;
        public RenderTexture? BounceTexture => _bounceTexture;
        public RenderTexture? LightmapTexture => _lightmapTexture;
        public ComputeBuffer? RadianceAtlas { get; private set; }
        public ComputeBuffer? DynamicLightBuffer { get; private set; }

        public int SolveCascadeKernel { get; private set; }
        public int SolveAutomaticNormalsKernel { get; private set; }
        public int ResolveDirectKernel { get; private set; }
        public int SolveDiffuseBounceKernel { get; private set; }
        public int CompositeLightingKernel { get; private set; }

        public int FieldWidth { get; private set; }
        public int FieldHeight { get; private set; }
        public int BounceWidth { get; private set; }
        public int BounceHeight { get; private set; }
        public int AtlasCapacity { get; private set; }
        public int AtlasEntryCount { get; private set; }

        public readonly List<CascadeLayout> Cascades = new();
        public LightingPipeline? CompositePipeline { get; private set; }
        public LightingPipeline? AutomaticNormalsPipeline { get; private set; }
        public LightingPipeline? DiffuseBouncePipeline { get; private set; }
        public LightingPipeline? DynamicEmissionCompositionPipeline { get; private set; }
        public LightingPipeline? MaterialFieldPipeline { get; private set; }

        public bool GpuPipelineInitialized { get; set; }
        public bool LightingDisabledStatePublished { get; set; }

        public void EnsureGpuPipelineInitialized()
        {
            if (GpuPipelineInitialized)
            {
                return;
            }

            LoadComputeShaderOrThrow();
            ValidateGpuRequirements();
            ValidateMaterialFieldPass();
            LightingCommandBuffer = new CommandBuffer
            {
                name = "Fodinae Radiance Cascades",
            };
            GpuPipelineInitialized = true;
            LightingDisabledStatePublished = false;
            Shader.EnableKeyword("FODINAE_WORLD_LIGHTING");
        }

        public void ReleaseGpuPipeline()
        {
            ReleaseResources();

            if (DynamicEmissionMaterial != null)
            {
                DestroyLightingObject(DynamicEmissionMaterial);
                DynamicEmissionMaterial = null;
            }

            LightingCommandBuffer?.Release();
            LightingCommandBuffer = null;
            LightingCompute = null;
            CompositePipeline = null;
            AutomaticNormalsPipeline = null;
            DiffuseBouncePipeline = null;
            DynamicEmissionCompositionPipeline = null;
            MaterialFieldPipeline = null;
            GpuPipelineInitialized = false;
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
                qualitySettings.LightingCascadeAtlasLimit);

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
            int bounceWidth = Mathf.Max(1, Mathf.CeilToInt(fieldWidth * 0.5f));
            int bounceHeight = Mathf.Max(1, Mathf.CeilToInt(fieldHeight * 0.5f));

            if (FieldWidth == fieldWidth && FieldHeight == fieldHeight &&
                _materialField != null &&
                RadianceAtlas != null)
            {
                return;
            }

            ReleaseFieldTextures();
            FieldWidth = fieldWidth;
            FieldHeight = fieldHeight;
            BounceWidth = bounceWidth;
            BounceHeight = bounceHeight;

            _materialField = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.ARGB32,
                randomWrite: false,
                FilterMode.Bilinear,
                "_LightingMaterialField",
                useMipMap: true);
            _staticEmissionField = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.ARGBHalf,
                randomWrite: false,
                FilterMode.Bilinear,
                "_StaticEmissionField",
                useMipMap: false);
            _dynamicEmissionField = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.ARGBHalf,
                randomWrite: false,
                FilterMode.Bilinear,
                "_DynamicEmissionField",
                useMipMap: false);
            _automaticNormalField = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.ARGBHalf,
                randomWrite: true,
                FilterMode.Point,
                "_AutomaticNormalField");
            _directTexture = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.ARGBHalf,
                randomWrite: true,
                FilterMode.Bilinear,
                "_RadianceDirect");
            _staticDirectTexture = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.ARGBHalf,
                randomWrite: true,
                FilterMode.Bilinear,
                "_RadianceDirectStatic");
            _bounceTexture = CreateTexture(
                bounceWidth,
                bounceHeight,
                RenderTextureFormat.ARGBHalf,
                randomWrite: true,
                FilterMode.Bilinear,
                "_RadianceBounce");
            _lightmapTexture = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.ARGBHalf,
                randomWrite: true,
                FilterMode.Bilinear,
                "_WorldLightTexture");

            CascadeLayoutBuilder.BuildCascadeLayouts(
                fieldWidth,
                fieldHeight,
                qualitySettings.LightingCascadeAtlasLimit,
                Cascades);
            AtlasEntryCount = Cascades[^1].Offset + Cascades[^1].EntryCount;
            EnsurePersistentBuffers(
                qualitySettings.LightingCascadeAtlasLimit,
                qualitySettings.LightingMaximumLightCount);
        }

        public void ReleaseResources()
        {
            DynamicLightBuffer?.Release();
            DynamicLightBuffer = null;
            RadianceAtlas?.Release();
            RadianceAtlas = null;
            AtlasCapacity = 0;
            AtlasEntryCount = 0;
            ReleaseFieldTextures();
        }

        public void ReleaseFieldTextures()
        {
            ReleaseTexture(ref _materialField);
            ReleaseTexture(ref _staticEmissionField);
            ReleaseTexture(ref _dynamicEmissionField);
            ReleaseTexture(ref _automaticNormalField);
            ReleaseTexture(ref _directTexture);
            ReleaseTexture(ref _staticDirectTexture);
            ReleaseTexture(ref _bounceTexture);
            ReleaseTexture(ref _lightmapTexture);
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

            int clampedLightCount = Mathf.Max(1, maximumLightCount);

            if (DynamicLightBuffer == null || DynamicLightBuffer.count != clampedLightCount)
            {
                DynamicLightBuffer?.Release();
                DynamicLightBuffer = new ComputeBuffer(
                    clampedLightCount,
                    sizeof(float) * 8,
                    ComputeBufferType.Structured);
            }
        }

        private static RenderTexture CreateTexture(
            int width,
            int height,
            RenderTextureFormat format,
            bool randomWrite,
            FilterMode filterMode,
            string name,
            bool useMipMap = false)
        {
            var texture = new RenderTexture(
                width,
                height,
                0,
                format,
                RenderTextureReadWrite.Linear)
            {
                enableRandomWrite = randomWrite,
                useMipMap = useMipMap,
                autoGenerateMips = false,
                filterMode = filterMode,
                wrapMode = TextureWrapMode.Clamp,
                name = name,
            };

            if (!texture.Create())
            {
                DestroyLightingObject(texture);
                throw new InvalidOperationException($"Failed to create required lighting target '{name}'.");
            }

            return texture;
        }

        private static void ReleaseTexture(ref RenderTexture? texture)
        {
            if (texture == null)
            {
                return;
            }

            texture.Release();
            DestroyLightingObject(texture);
            texture = null;
        }

        private static void DestroyLightingObject(UnityEngine.Object target)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private void LoadComputeShaderOrThrow()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                throw new NotSupportedException("Radiance Cascades requires compute shader support.");
            }

            LightingCompute = Resources.Load<ComputeShader>(
                ProjectRuntimeContracts.ResourcePaths.WorldLightingCompute) ??
                throw new InvalidOperationException(
                    "Required compute shader Resources/Shaders/Lighting/WorldLighting.compute is missing.");

            (string Name, Action<int> SetIndex)[] requiredKernels =
            [
                (ProjectRuntimeContracts.ComputeKernelNames.SolveCascade, k => SolveCascadeKernel = k),
                (ProjectRuntimeContracts.ComputeKernelNames.SolveAutomaticNormals, k => SolveAutomaticNormalsKernel = k),
                (ProjectRuntimeContracts.ComputeKernelNames.ResolveDirect, k => ResolveDirectKernel = k),
                (ProjectRuntimeContracts.ComputeKernelNames.SolveDiffuseBounce, k => SolveDiffuseBounceKernel = k),
                (ProjectRuntimeContracts.ComputeKernelNames.CompositeLighting, k => CompositeLightingKernel = k),
            ];

            foreach (var (kernelName, setIndex) in requiredKernels)
            {
                if (!LightingCompute.HasKernel(kernelName))
                {
                    throw new InvalidOperationException(
                        $"Radiance Cascades compute shader is missing kernel '{kernelName}'.");
                }

                int kernelIndex = LightingCompute.FindKernel(kernelName);
                ValidateKernelSupportOrThrow(kernelName, kernelIndex);
                setIndex(kernelIndex);
            }

            CompositePipeline = new LightingPipeline(
                new CompositeStage(CompositeLightingKernel));
            AutomaticNormalsPipeline = new LightingPipeline(
                new AutomaticNormalsStage(SolveAutomaticNormalsKernel));
            DiffuseBouncePipeline = new LightingPipeline(
                new DiffuseBounceStage(SolveDiffuseBounceKernel));
            DynamicEmissionCompositionPipeline = new LightingPipeline(
                new DynamicEmissionCompositionStage());
            MaterialFieldPipeline = new LightingPipeline(
                new MaterialFieldStage());

            LoadDynamicEmissionMaterialOrThrow();
        }

        private void LoadDynamicEmissionMaterialOrThrow()
        {
            if (DynamicEmissionMaterial != null)
            {
                return;
            }

            Shader shader = Shader.Find(ProjectRuntimeContracts.ShaderNames.DynamicEmission) ??
                throw new InvalidOperationException(
                    $"Required shader '{ProjectRuntimeContracts.ShaderNames.DynamicEmission}' is missing. " +
                    "Dynamic light sources cannot be rasterized into the emission field.");

            DynamicEmissionMaterial = new Material(shader)
            {
                name = "FodinaeDynamicEmission",
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        private void ValidateKernelSupportOrThrow(string kernelName, int kernelIndex)
        {
            if (LightingCompute?.IsSupported(kernelIndex) != true)
            {
                throw new InvalidOperationException(
                    $"Radiance Cascades kernel '{kernelName}' failed to compile for {SystemInfo.graphicsDeviceType}.");
            }
        }

        private static void ValidateGpuRequirements()
        {
            if (SystemInfo.supportedRenderTargetCount < 2 ||
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB32) ||
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) ||
                !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBHalf))
            {
                throw new NotSupportedException(
                    "Radiance Cascades requires two MRTs, RGBA8 material, and random-write lighting targets.");
            }
        }

        private static void ValidateMaterialFieldPass()
        {
            Shader terrainShader = Shader.Find(ProjectRuntimeContracts.ShaderNames.Terrain) ??
                throw new InvalidOperationException("The terrain shader required by lighting is missing.");

            var validationMaterial = new Material(terrainShader);

            try
            {
                if (validationMaterial.FindPass(
                        ProjectRuntimeContracts.ShaderPassNames.LightingMaterialField) < 0)
                {
                    throw new InvalidOperationException(
                        "The terrain shader is missing the LightingMaterialField pass.");
                }
            }
            finally
            {
                DestroyLightingObject(validationMaterial);
            }
        }
    }
}

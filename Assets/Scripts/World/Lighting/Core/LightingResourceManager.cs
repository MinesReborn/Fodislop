#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Rendering;
using Fodinae.World.Lighting.Pipeline;
using Fodinae.World.Lighting.Pipeline.Stages;
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
        public ComputeShader? LightingCompute { get; private set; }
        public CommandBuffer? LightingCommandBuffer { get; private set; }
        public RenderTexture? MaterialField { get; private set; }
        public RenderTexture? StaticEmissionField { get; private set; }
        public RenderTexture? DynamicEmissionField { get; private set; }
        public Material? DynamicEmissionMaterial { get; private set; }
        public RenderTexture? AutomaticNormalField { get; private set; }
        public RenderTexture? DirectTexture { get; private set; }
        public RenderTexture? StaticDirectTexture { get; private set; }
        public RenderTexture? AmbientOcclusionTexture { get; private set; }
        public RenderTexture? BounceTexture { get; private set; }
        public RenderTexture? LightmapTexture { get; private set; }
        public ComputeBuffer? RadianceAtlas { get; private set; }
        public ComputeBuffer? DynamicLightBuffer { get; private set; }

        public int SolveCascadeKernel { get; private set; }
        public int SolveAutomaticNormalsKernel { get; private set; }
        public int SolveContactOcclusionKernel { get; private set; }
        public int ResolveDirectKernel { get; private set; }
        public int SolveDiffuseBounceKernel { get; private set; }
        public int CompositeLightingKernel { get; private set; }
        public int ResolveAndCompositeKernel { get; private set; }

        public int FieldWidth { get; private set; }
        public int FieldHeight { get; private set; }
        public int BounceWidth { get; private set; }
        public int BounceHeight { get; private set; }
        public int AtlasCapacity { get; private set; }
        public int AtlasEntryCount { get; private set; }

        public readonly System.Collections.Generic.List<CascadeLayout> Cascades = new();
        public LightingPipeline? ContactOcclusionPipeline { get; private set; }
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
            ContactOcclusionPipeline = null;
            CompositePipeline = null;
            AutomaticNormalsPipeline = null;
            DiffuseBouncePipeline = null;
            DynamicEmissionCompositionPipeline = null;
            MaterialFieldPipeline = null;
            GpuPipelineInitialized = false;
        }

        public void EnsureResources(int gridWidth, int gridHeight, Camera camera)
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

            int requestedScale = Mathf.Max(1, Mathf.FloorToInt(1f));
            int scale = SelectStablePixelsPerCell(gridWidth, gridHeight, requestedScale);
            int maximumTextureScale = Mathf.Max(
                0,
                Mathf.Min(
                    8192 / gridWidth,
                    8192 / gridHeight));
            int fieldWidth = gridWidth * scale;
            int fieldHeight = gridHeight * scale;
            int bounceWidth = Mathf.Max(1, Mathf.CeilToInt(fieldWidth * 0.5f));
            int bounceHeight = Mathf.Max(1, Mathf.CeilToInt(fieldHeight * 0.5f));

            if (FieldWidth == fieldWidth && FieldHeight == fieldHeight &&
                MaterialField != null && AmbientOcclusionTexture != null &&
                RadianceAtlas != null)
            {
                return;
            }

            ReleaseFieldTextures();
            FieldWidth = fieldWidth;
            FieldHeight = fieldHeight;
            BounceWidth = bounceWidth;
            BounceHeight = bounceHeight;
            MaterialField = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.ARGB32,
                randomWrite: false,
                FilterMode.Bilinear,
                "_LightingMaterialField",
                useMipMap: true);
            StaticEmissionField = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.ARGBHalf,
                randomWrite: false,
                FilterMode.Bilinear,
                "_StaticEmissionField",
                useMipMap: false);
            DynamicEmissionField = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.ARGBHalf,
                randomWrite: false,
                FilterMode.Bilinear,
                "_DynamicEmissionField",
                useMipMap: false);
            AutomaticNormalField = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.ARGBHalf,
                randomWrite: true,
                FilterMode.Point,
                "_AutomaticNormalField");
            DirectTexture = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.ARGBHalf,
                randomWrite: true,
                FilterMode.Bilinear,
                "_RadianceDirect");
            StaticDirectTexture = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.ARGBHalf,
                randomWrite: true,
                FilterMode.Bilinear,
                "_RadianceDirectStatic");
            AmbientOcclusionTexture = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.RHalf,
                randomWrite: true,
                FilterMode.Bilinear,
                "_ContactOcclusion");
            BounceTexture = CreateTexture(
                bounceWidth,
                bounceHeight,
                RenderTextureFormat.ARGBHalf,
                randomWrite: true,
                FilterMode.Bilinear,
                "_RadianceBounce");
            LightmapTexture = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.ARGBHalf,
                randomWrite: true,
                FilterMode.Bilinear,
                "_WorldLightTexture");

            BuildCascadeLayouts(fieldWidth, fieldHeight);
            AtlasEntryCount = Cascades[^1].Offset + Cascades[^1].EntryCount;
            EnsurePersistentBuffers();
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
            ReleaseTexture(MaterialField);
            MaterialField = null;
            ReleaseTexture(StaticEmissionField);
            StaticEmissionField = null;
            ReleaseTexture(DynamicEmissionField);
            DynamicEmissionField = null;
            ReleaseTexture(AutomaticNormalField);
            AutomaticNormalField = null;
            ReleaseTexture(DirectTexture);
            DirectTexture = null;
            ReleaseTexture(StaticDirectTexture);
            StaticDirectTexture = null;
            ReleaseTexture(AmbientOcclusionTexture);
            AmbientOcclusionTexture = null;
            ReleaseTexture(BounceTexture);
            BounceTexture = null;
            ReleaseTexture(LightmapTexture);
            LightmapTexture = null;
            FieldWidth = 0;
            FieldHeight = 0;
            BounceWidth = 0;
            BounceHeight = 0;
            Cascades.Clear();
        }

        private void EnsurePersistentBuffers()
        {
            long atlasDimension = 8192L;
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

            int maximumLightCount = Mathf.Max(1, 256);
            if (DynamicLightBuffer == null || DynamicLightBuffer.count != maximumLightCount)
            {
                DynamicLightBuffer?.Release();
                DynamicLightBuffer = new ComputeBuffer(
                    maximumLightCount,
                    sizeof(float) * 8,
                    ComputeBufferType.Structured);
            }
        }

        private void BuildCascadeLayouts(int width, int height)
        {
            Cascades.Clear();
            float requiredDistance = Mathf.Sqrt((width * width) + (height * height));
            int maxCascades = GetMaximumCascadeCount(8192L);
            int offset = 0;
            int spacing = 1;
            int directions = 4;
            float intervalStart = 0f;
            float intervalEnd = 1f;
            while (true)
            {
                int probeWidth = Mathf.CeilToInt(width / (float)spacing);
                int probeHeight = Mathf.CeilToInt(height / (float)spacing);
                long entryCountLong = (long)probeWidth * probeHeight * directions;
                if (entryCountLong > int.MaxValue - offset)
                {
                    throw new InvalidOperationException("Radiance cascade atlas exceeds the supported buffer size.");
                }

                int entryCount = (int)entryCountLong;
                Cascades.Add(new CascadeLayout(
                    offset,
                    entryCount,
                    probeWidth,
                    probeHeight,
                    spacing,
                    directions,
                    intervalStart,
                    intervalEnd));
                offset += entryCount;
                if (Cascades.Count >= maxCascades || intervalEnd >= requiredDistance)
                {
                    break;
                }

                spacing *= 2;
                directions = Mathf.Min(256, directions * 4);
                intervalStart = intervalEnd;
                intervalEnd *= 4f;
            }
        }

        private static int GetMaximumCascadeCount(long atlasDimension)
        {
            return atlasDimension <= 256 ? 3 : 4;
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

        private static void ReleaseTexture(RenderTexture? texture)
        {
            if (texture == null)
            {
                return;
            }

            texture.Release();
            DestroyLightingObject(texture);
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

        private static int SelectStablePixelsPerCell(
            int gridWidth,
            int gridHeight,
            int requestedScale)
        {
            int maximumTextureDimension = 8192;
            long atlasDimension = 8192L;
            long maximumEntryCount = atlasDimension * atlasDimension * 4;
            for (int scale = requestedScale; scale >= 1; scale--)
            {
                int width = checked(gridWidth * scale);
                int height = checked(gridHeight * scale);
                if (width > maximumTextureDimension ||
                    height > maximumTextureDimension)
                {
                    continue;
                }

                int maximumCascadeCount = GetMaximumCascadeCount(atlasDimension);
                long requiredEntryCount = CalculateCascadeEntryCount(
                    width,
                    height,
                    maximumCascadeCount);
                if (requiredEntryCount <= maximumEntryCount)
                {
                    return scale;
                }
            }

            throw new InvalidOperationException(
                $"Radiance cascade region {gridWidth}x{gridHeight} cannot fit at " +
                $"one texel per cell within texture limit {maximumTextureDimension} " +
                $"and atlas limit {atlasDimension}.");
        }

        private static long CalculateCascadeEntryCount(
            int width,
            int height,
            int maximumCascadeCount)
        {
            if (maximumCascadeCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumCascadeCount));
            }

            float requiredDistance = Mathf.Sqrt((width * width) + (height * height));
            long entryCount = 0;
            int spacing = 1;
            int directions = 4;
            float intervalEnd = 1f;
            while (true)
            {
                int probeWidth = Mathf.CeilToInt(width / (float)spacing);
                int probeHeight = Mathf.CeilToInt(height / (float)spacing);
                entryCount += (long)probeWidth * probeHeight * directions;
                if (intervalEnd >= requiredDistance ||
                    maximumCascadeCount == 1)
                {
                    return entryCount;
                }

                maximumCascadeCount--;
                spacing *= 2;
                directions = Mathf.Min(256, directions * 4);
                intervalEnd *= 4f;
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
            string[] requiredKernels =
            {
                ProjectRuntimeContracts.ComputeKernelNames.SolveCascade,
                ProjectRuntimeContracts.ComputeKernelNames.SolveAutomaticNormals,
                ProjectRuntimeContracts.ComputeKernelNames.SolveContactOcclusion,
                ProjectRuntimeContracts.ComputeKernelNames.ResolveDirect,
                ProjectRuntimeContracts.ComputeKernelNames.SolveDiffuseBounce,
                ProjectRuntimeContracts.ComputeKernelNames.CompositeLighting,
                ProjectRuntimeContracts.ComputeKernelNames.ResolveAndComposite,
            };
            foreach (string kernelName in requiredKernels)
            {
                if (!LightingCompute.HasKernel(kernelName))
                {
                    throw new InvalidOperationException(
                        $"Radiance Cascades compute shader is missing kernel '{kernelName}'.");
                }
            }

            SolveCascadeKernel = LightingCompute.FindKernel(
                ProjectRuntimeContracts.ComputeKernelNames.SolveCascade);
            SolveAutomaticNormalsKernel = LightingCompute.FindKernel(
                ProjectRuntimeContracts.ComputeKernelNames.SolveAutomaticNormals);
            SolveContactOcclusionKernel = LightingCompute.FindKernel(
                ProjectRuntimeContracts.ComputeKernelNames.SolveContactOcclusion);
            ResolveDirectKernel = LightingCompute.FindKernel(
                ProjectRuntimeContracts.ComputeKernelNames.ResolveDirect);
            SolveDiffuseBounceKernel = LightingCompute.FindKernel(
                ProjectRuntimeContracts.ComputeKernelNames.SolveDiffuseBounce);
            CompositeLightingKernel = LightingCompute.FindKernel(
                ProjectRuntimeContracts.ComputeKernelNames.CompositeLighting);
            ResolveAndCompositeKernel = LightingCompute.FindKernel(
                ProjectRuntimeContracts.ComputeKernelNames.ResolveAndComposite);
            ValidateKernelSupportOrThrow(
                ProjectRuntimeContracts.ComputeKernelNames.SolveCascade,
                SolveCascadeKernel);
            ValidateKernelSupportOrThrow(
                ProjectRuntimeContracts.ComputeKernelNames.SolveAutomaticNormals,
                SolveAutomaticNormalsKernel);
            ValidateKernelSupportOrThrow(
                ProjectRuntimeContracts.ComputeKernelNames.SolveContactOcclusion,
                SolveContactOcclusionKernel);
            ValidateKernelSupportOrThrow(
                ProjectRuntimeContracts.ComputeKernelNames.ResolveDirect,
                ResolveDirectKernel);
            ValidateKernelSupportOrThrow(
                ProjectRuntimeContracts.ComputeKernelNames.SolveDiffuseBounce,
                SolveDiffuseBounceKernel);
            ValidateKernelSupportOrThrow(
                ProjectRuntimeContracts.ComputeKernelNames.CompositeLighting,
                CompositeLightingKernel);
            ValidateKernelSupportOrThrow(
                ProjectRuntimeContracts.ComputeKernelNames.ResolveAndComposite,
                ResolveAndCompositeKernel);
            ContactOcclusionPipeline = new LightingPipeline(
                new ContactOcclusionStage(SolveContactOcclusionKernel));
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
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RHalf) ||
                !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.RHalf) ||
                !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBHalf))
            {
                throw new NotSupportedException(
                    "Radiance Cascades requires two MRTs, RGBA8 material, R16F contact AO, and random-write lighting targets.");
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

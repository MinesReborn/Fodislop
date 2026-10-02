#nullable enable

using System;
using System.Diagnostics;
using Kern.Core.Interfaces.WorldLighting;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Kern.Rendering.PostProcessing
{
    /// <summary>
    /// Scene-linear bloom on absolute world-pixel lattices. RenderGraph owns all
    /// small textures; the renderer feature owns this pass and its blend material.
    /// Lighting publication is borrowed when recording, never retained.
    /// </summary>
    internal sealed class WorldBloomRenderPass : ScriptableRenderPass2D, IDisposable
    {
        private static readonly int s_worldLightRectId = Shader.PropertyToID("_WorldLightRect");
        private static readonly int s_worldEmissionTextureId = Shader.PropertyToID("_WorldEmissionTexture");
        private static readonly int s_sceneId = Shader.PropertyToID("_Scene");
        private static readonly int s_emissionId = Shader.PropertyToID("_Emission");
        private static readonly int s_sourceId = Shader.PropertyToID("_Source");
        private static readonly int s_baseId = Shader.PropertyToID("_Base");
        private static readonly int s_outputId = Shader.PropertyToID("_Output");
        private static readonly int s_sizeId = Shader.PropertyToID("_OutputSize");
        private static readonly int s_sourceUvId = Shader.PropertyToID("_SourceUv");
        private static readonly int s_texelId = Shader.PropertyToID("_SourceTexelSize");
        private static readonly int s_emissionUvId = Shader.PropertyToID("_EmissionUv");
        private static readonly int s_thresholdId = Shader.PropertyToID("_Threshold");
        private static readonly int s_kneeId = Shader.PropertyToID("_SoftKnee");
        private static readonly int s_radiusId = Shader.PropertyToID("_Radius");
        private static readonly int s_scatterId = Shader.PropertyToID("_Scatter");
        private static readonly int s_tintId = Shader.PropertyToID("_Tint");
        private static readonly int s_sceneBlendId = Shader.PropertyToID("_WorldBloomSceneBlend");
        private static readonly int s_intensityId = Shader.PropertyToID("_WorldBloomIntensity");
        private static readonly ProfilerMarker s_prefilter = new(ProfilerCategory.Render, "Kern.PostProcess.Bloom.Prefilter", MarkerFlags.SampleGPU);
        private static readonly ProfilerMarker s_downsample = new(ProfilerCategory.Render, "Kern.PostProcess.Bloom.Downsample", MarkerFlags.SampleGPU);
        private static readonly ProfilerMarker s_upsample = new(ProfilerCategory.Render, "Kern.PostProcess.Bloom.Upsample", MarkerFlags.SampleGPU);
        private static readonly ProfilerMarker s_add = new(ProfilerCategory.Render, "Kern.PostProcess.Bloom.Add", MarkerFlags.SampleGPU);
        private static readonly string[] s_downNames = ["_WorldBloom0", "_WorldBloom1", "_WorldBloom2", "_WorldBloom3"];
        private static readonly string[] s_upNames = ["_WorldBloomUp0", "_WorldBloomUp1", "_WorldBloomUp2"];
        private readonly WorldBloomLevel[] _levels = new WorldBloomLevel[4];
        private readonly TextureHandle[] _down = new TextureHandle[4];
        private readonly TextureHandle[] _up = new TextureHandle[3];
        private readonly PostProcessWorkload _workload = new();
        private readonly PostProcessWorkload _diagnosticWorkload = new();
        private readonly ComputeShader _shader;
        private readonly Material _material;
        private readonly int _prefilterKernel;
        private readonly int _downKernel;
        private readonly int _upKernel;

        internal bool IsAlive => _shader != null && _material != null;
        internal PostProcessWorkloadSnapshot? LatestWorkload =>
            PostProcessRuntimeState.DiagnosticOffscreenCamera != null &&
            PostProcessRuntimeState.DiagnosticOffscreenCamera != PostProcessRuntimeState.MainCamera
                ? _diagnosticWorkload.Latest : _workload.Latest;

        internal WorldBloomRenderPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            renderPassEvent2D = RenderPassEvent2D.BeforeRenderingPostProcessing;
            _shader = Resources.Load<ComputeShader>("Shaders/PostProcessing/WorldBloom")
                ?? throw new InvalidOperationException("World bloom requires WorldBloom.compute.");
            Shader blendShader = Resources.Load<Shader>("Shaders/PostProcessing/WorldBloomAdd")
                ?? throw new InvalidOperationException("World bloom requires WorldBloomAdd.shader.");
            _material = CoreUtils.CreateEngineMaterial(blendShader);
            _prefilterKernel = _shader.FindKernel("Prefilter");
            _downKernel = _shader.FindKernel("Downsample");
            _upKernel = _shader.FindKernel("Upsample");
        }

        private sealed class Frame
        {
            public ComputeShader Shader = null!;
            public Material Material = null!;
            public WorldBloomLevel Scene;
            public WorldBloomLevel[] Levels = null!;
            public TextureHandle SceneColor;
            public TextureHandle[] Down = null!;
            public TextureHandle[] Up = null!;
            public int Count;
            public int PrefilterKernel;
            public int DownKernel;
            public int UpKernel;
            public float Threshold;
            public float Knee;
            public float Radius;
            public float Scatter;
            public Vector4 Tint;
            public float Intensity;
            public EntityId CameraId;
            public bool DebugBloom;
            public Vector4 EmissionUv;
            public int TextureCount;
            public long TextureBytes;
            public PostProcessWorkload Workload = null!;
            public PostProcessWorkloadAccumulator Accumulator;
            public long ComputeCPUTicks;
        }

        private sealed class Observation
        {
            public EntityId CameraId;
            public TextureHandle Color;
            public WorldBloomLevel Scene;
        }

        private sealed class BlendFrame
        {
            public Frame Compute = null!;
            public TextureHandle Bloom;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer context)
        {
            UniversalCameraData camera = context.Get<UniversalCameraData>();
            Renderer2DWorldGridData grid = context.Get<Renderer2DWorldGridData>();
            if (!grid.Active ||
                (camera.camera != PostProcessRuntimeState.MainCamera &&
                 camera.camera != PostProcessRuntimeState.DiagnosticOffscreenCamera) ||
                PostProcessRuntimeState.BypassPostProcessEffects || PostProcessRuntimeState.TemporaryBypass)
            {
                return;
            }

            BloomComponent bloom = VolumeManager.instance.stack.GetComponent<BloomComponent>()
                ?? throw new InvalidOperationException("World bloom requires BloomComponent in the volume stack.");
            bool debugBloom = PostProcessRuntimeState.DebugView == PostProcessDebugView.Bloom;
            if ((!bloom.active || !bloom.IsActive()) && !debugBloom)
            {
                return;
            }

            UniversalResourceData resources = context.Get<UniversalResourceData>();
            TextureHandle sceneColor = resources.activeColorTexture;
            if (!sceneColor.IsValid())
            {
                throw new InvalidOperationException("World bloom requires the world HDR color target.");
            }

            var layout = grid.Layout;
            int minX = Mathf.RoundToInt(layout.WorldRect.x * WorldRenderGrid.PixelsPerCell);
            int minY = Mathf.RoundToInt(layout.WorldRect.y * WorldRenderGrid.PixelsPerCell);
            var scene = new WorldBloomLevel(minX, minY, layout.Width, layout.Height, 1);
            int count = 1;
            _levels[0] = WorldBloomLevel.Cover(minX, minY, layout.Width, layout.Height, 4);
            for (int i = 1; i < _levels.Length; i++)
            {
                WorldBloomLevel next = WorldBloomLevel.Cover(minX, minY, layout.Width, layout.Height, 4 << i);
                if (Math.Min(next.Width, next.Height) < 8)
                {
                    break;
                }
                _levels[i] = next;
                count++;
            }

            long bytes = 0;
            int textureCount = 0;
            TextureDesc descriptor = sceneColor.GetDescriptor(graph);
            descriptor.sizeMode = TextureSizeMode.Explicit;
            descriptor.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            descriptor.depthBufferBits = DepthBits.None;
            descriptor.msaaSamples = MSAASamples.None;
            descriptor.bindTextureMS = false;
            descriptor.enableRandomWrite = true;
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;
            descriptor.filterMode = FilterMode.Bilinear;
            descriptor.clearBuffer = false;
            descriptor.useDynamicScale = false;
            for (int i = 0; i < count; i++)
            {
                descriptor.width = _levels[i].Width;
                descriptor.height = _levels[i].Height;
                descriptor.name = s_downNames[i];
                _down[i] = graph.CreateTexture(descriptor);
                textureCount++;
                bytes += (long)descriptor.width * descriptor.height * 8;
                if (i < count - 1)
                {
                    descriptor.name = s_upNames[i];
                    _up[i] = graph.CreateTexture(descriptor);
                    textureCount++;
                    bytes += (long)descriptor.width * descriptor.height * 8;
                }
            }

            Vector4 lightRect = Shader.GetGlobalVector(s_worldLightRectId);
            if (lightRect.z <= 0 || lightRect.w <= 0)
            {
                throw new InvalidOperationException("World bloom requires a coherent emission world rectangle.");
            }
            Vector4 emissionUv = new(layout.WorldRect.z / lightRect.z, layout.WorldRect.w / lightRect.w,
                (layout.WorldRect.x - lightRect.x) / lightRect.z, (layout.WorldRect.y - lightRect.y) / lightRect.w);
            if (LightingFieldOrientation.RowsTopDown)
            {
                emissionUv.y = -emissionUv.y;
                emissionUv.w = 1f - emissionUv.w;
            }

            PostProcessRuntimeState.RecordDiagnosticPass(camera.camera, false);
            Frame frame;
            using (var builder = graph.AddUnsafePass<Frame>("World bloom pyramid", out frame))
            {
                frame.Accumulator = default;
                frame.ComputeCPUTicks = 0;
                frame.Shader = _shader;
                frame.Material = _material;
                frame.Scene = scene;
                frame.Levels = _levels;
                frame.SceneColor = sceneColor;
                frame.Down = _down;
                frame.Up = _up;
                frame.Count = count;
                frame.PrefilterKernel = _prefilterKernel;
                frame.DownKernel = _downKernel;
                frame.UpKernel = _upKernel;
                frame.Threshold = bloom.threshold.value;
                frame.Knee = bloom.softKnee.value;
                frame.Radius = bloom.radius.value;
                frame.Scatter = bloom.scatter.value;
                Color tint = bloom.tint.value;
                frame.Tint = new Vector4(tint.r, tint.g, tint.b, tint.a);
                frame.Intensity = bloom.active && bloom.IsActive() ? bloom.intensity.value : 0f;
                frame.DebugBloom = debugBloom;
                frame.EmissionUv = emissionUv;
                frame.TextureCount = textureCount;
                frame.TextureBytes = bytes;
                frame.CameraId = camera.camera.GetEntityId();
                frame.Workload = camera.camera == PostProcessRuntimeState.DiagnosticOffscreenCamera &&
                    camera.camera != PostProcessRuntimeState.MainCamera ? _diagnosticWorkload : _workload;
                builder.UseTexture(sceneColor, AccessFlags.Read);
                for (int i = 0; i < count; i++)
                {
                    builder.UseTexture(_down[i], AccessFlags.ReadWrite);
                    if (i < count - 1)
                    {
                        builder.UseTexture(_up[i], AccessFlags.ReadWrite);
                    }
                }
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (Frame data, UnsafeGraphContext ctx) => RecordPyramid(data, ctx));
            }

            TextureHandle reconstructed = count == 1 ? _down[0] : _up[0];
            using (var builder = graph.AddRasterRenderPass<BlendFrame>("World bloom additive", out var blend))
            {
                blend.Compute = frame;
                blend.Bloom = reconstructed;
                builder.UseTexture(reconstructed, AccessFlags.Read);
                builder.SetRenderAttachment(sceneColor, 0, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (BlendFrame data, RasterGraphContext ctx) => RecordBlend(data, ctx));
            }
            if (PostProcessRuntimeState.DiagnosticBloomImage != null &&
                camera.camera == PostProcessRuntimeState.DiagnosticOffscreenCamera)
            {
                using var observer = graph.AddUnsafePass<Observation>("World bloom diagnostic readback", out var observation);
                observation.Color = sceneColor;
                observation.CameraId = camera.camera.GetEntityId();
                observation.Scene = scene;
                observer.UseTexture(sceneColor, AccessFlags.Read);
                observer.AllowPassCulling(false);
                observer.SetRenderFunc(static (Observation data, UnsafeGraphContext ctx) =>
                {
                    CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                    Observe(cmd, "after", data.Color, data.Scene, data.CameraId);
                });
            }
        }

        private static Vector4 Mapping(WorldBloomLevel destination, WorldBloomLevel source)
        {
            var mapping = destination.MapTo(source);
            return new Vector4((float)mapping.X, (float)mapping.Y, (float)mapping.OffsetX, (float)mapping.OffsetY);
        }

        private static void SetupLevel(CommandBuffer cmd, Frame data, WorldBloomLevel destination, WorldBloomLevel source)
        {
            cmd.SetComputeVectorParam(data.Shader, s_sizeId, new Vector4(destination.Width, destination.Height,
                1f / destination.Width, 1f / destination.Height));
            cmd.SetComputeVectorParam(data.Shader, s_texelId, new Vector4(1f / source.Width, 1f / source.Height, source.Width, source.Height));
            cmd.SetComputeVectorParam(data.Shader, s_sourceUvId, Mapping(destination, source));
        }

        private static void Dispatch(CommandBuffer cmd, Frame data, int kernel, WorldBloomLevel level, TextureHandle target)
        {
            cmd.SetComputeTextureParam(data.Shader, kernel, s_outputId, target);
            cmd.DispatchCompute(data.Shader, kernel, (level.Width + 7) / 8, (level.Height + 7) / 8, 1);
            data.Accumulator.RecordDispatch(level.Width, level.Height, 8, 8);
        }

        private static void RecordPyramid(Frame data, UnsafeGraphContext context)
        {
            long started = Stopwatch.GetTimestamp();
            CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            // This field is produced outside RenderGraph, before camera rendering.
            // Borrow it for this recording call; never cache a texture/RTHandle.
            Texture emission = Shader.GetGlobalTexture(s_worldEmissionTextureId)
                ?? throw new InvalidOperationException("World emission publication is missing.");
            Observe(cmd, "before", data.SceneColor, data.Scene, data.CameraId);
            cmd.SetComputeFloatParam(data.Shader, s_thresholdId, data.Threshold);
            cmd.SetComputeFloatParam(data.Shader, s_kneeId, data.Knee);
            cmd.SetComputeFloatParam(data.Shader, s_radiusId, data.Radius);
            cmd.SetComputeFloatParam(data.Shader, s_scatterId, data.Scatter);
            cmd.SetComputeVectorParam(data.Shader, s_tintId, data.Tint);
            cmd.SetComputeVectorParam(data.Shader, s_emissionUvId, data.EmissionUv);
            SetupLevel(cmd, data, data.Levels[0], data.Scene);
            cmd.SetComputeTextureParam(data.Shader, data.PrefilterKernel, s_sceneId, data.SceneColor);
            cmd.SetComputeTextureParam(data.Shader, data.PrefilterKernel, s_emissionId, emission);
            cmd.BeginSample(s_prefilter);
            Dispatch(cmd, data, data.PrefilterKernel, data.Levels[0], data.Down[0]);
            cmd.EndSample(s_prefilter);
            Observe(cmd, "prefilter", data.Down[0], data.Levels[0], data.CameraId);
            cmd.BeginSample(s_downsample);
            for (int i = 1; i < data.Count; i++)
            {
                SetupLevel(cmd, data, data.Levels[i], data.Levels[i - 1]);
                cmd.SetComputeTextureParam(data.Shader, data.DownKernel, s_sourceId, data.Down[i - 1]);
                Dispatch(cmd, data, data.DownKernel, data.Levels[i], data.Down[i]);
            }
            cmd.EndSample(s_downsample);
            TextureHandle current = data.Down[data.Count - 1];
            cmd.BeginSample(s_upsample);
            for (int i = data.Count - 2; i >= 0; i--)
            {
                SetupLevel(cmd, data, data.Levels[i], data.Levels[i + 1]);
                cmd.SetComputeTextureParam(data.Shader, data.UpKernel, s_sourceId, current);
                cmd.SetComputeTextureParam(data.Shader, data.UpKernel, s_baseId, data.Down[i]);
                Dispatch(cmd, data, data.UpKernel, data.Levels[i], data.Up[i]);
                current = data.Up[i];
            }
            cmd.EndSample(s_upsample);
            data.ComputeCPUTicks = Stopwatch.GetTimestamp() - started;
            PostProcessRuntimeState.RecordBloomDispatches(2 * data.Count - 1, data.CameraId);
        }

        private static void RecordBlend(BlendFrame blend, RasterGraphContext context)
        {
            Frame data = blend.Compute;
            long started = Stopwatch.GetTimestamp() - data.ComputeCPUTicks;
            float energy = 0f;
            float weight = 1f;
            for (int i = 0; i < data.Count; i++)
            {
                energy += weight;
                weight *= data.Scatter;
            }
            data.Material.SetFloat(s_sceneBlendId, (float)(data.DebugBloom ? BlendMode.Zero : BlendMode.One));
            data.Material.SetFloat(s_intensityId, data.Intensity / Mathf.Max(energy, 1e-4f));
            Vector4 mapping = Mapping(data.Scene, data.Levels[0]);
            context.cmd.BeginSample(s_add);
            Blitter.BlitTexture(context.cmd, blend.Bloom, mapping, data.Material, 0);
            context.cmd.EndSample(s_add);
            data.Accumulator.RecordRaster(data.Scene.Width, data.Scene.Height);
            data.Workload.Publish(data.Accumulator.Complete(Time.frameCount, data.Scene.Width, data.Scene.Height,
                1, data.TextureCount, data.TextureBytes, started));
        }

        private static void Observe(CommandBuffer cmd, string stage, TextureHandle texture, WorldBloomLevel level, EntityId cameraId)
        {
            if (PostProcessRuntimeState.DiagnosticBloomImage is not { } observer ||
                PostProcessRuntimeState.DiagnosticOffscreenCamera == null ||
                cameraId != PostProcessRuntimeState.DiagnosticOffscreenCamera.GetEntityId())
            {
                return;
            }
            RTHandle target = texture;
            observer(cmd, stage, target.rt, new Vector4(level.MinX / 32f, level.MinY / 32f,
                level.Width * level.Stride / 32f, level.Height * level.Stride / 32f));
        }

        public void Dispose()
        {
            CoreUtils.Destroy(_material);
        }
    }
}

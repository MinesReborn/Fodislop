#nullable enable

using System;
using Kern.Core;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using static Kern.Rendering.PostProcessing.PostProcessShaderConstants;

namespace Kern.Rendering.PostProcessing
{
    public class PostProcessRenderPass : ScriptableRenderPass2D
    {
        private static readonly int s_finalVignetteId = Shader.PropertyToID("_KernFinalVignette");
        private static readonly int s_finalVignetteColorId = Shader.PropertyToID("_KernFinalVignetteColor");
        private static readonly int s_finalVignetteAspectId = Shader.PropertyToID("_KernFinalVignetteAspect");
        private readonly PostProcessWorkload _workload = new();
        private readonly PostProcessWorkload _diagnosticWorkload = new();
        internal PostProcessWorkloadSnapshot? LatestWorkload =>
            PostProcessRuntimeState.DiagnosticOffscreenCamera != null &&
            PostProcessRuntimeState.DiagnosticOffscreenCamera != PostProcessRuntimeState.MainCamera
                ? _diagnosticWorkload.Latest : _workload.Latest;
        private readonly ComputeShader _postProcessCS;
        private readonly int _kernelComposite;
        private VolumeStack? _cachedVolumeStack;
        private VignetteComponent? _vignette;
        private EigengrauComponent? _eigengrau;

        private void RefreshVolumeComponents(VolumeStack stack)
        {
            if (ReferenceEquals(_cachedVolumeStack, stack))
            {
                return;
            }

            _cachedVolumeStack = stack;
            _vignette = stack.GetComponent<VignetteComponent>();
            _eigengrau = stack.GetComponent<EigengrauComponent>();
        }

        private static T RequireComponent<T>(T? component, string componentName)
            where T : VolumeComponent
        {
            return component ?? throw new InvalidOperationException(
                $"Post-process VolumeStack is missing required component '{componentName}'.");
        }

        internal bool IsShaderAlive => _postProcessCS != null;

        public PostProcessRenderPass(ComputeShader postProcessCS)
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            renderPassEvent2D = RenderPassEvent2D.AfterRenderingPostProcessing;
            _postProcessCS = postProcessCS;
            _kernelComposite = _postProcessCS.FindKernel("DisplayFinal");
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // Копия шейдера не сохранена в ассет, и сборка плеера выгружает её
            // вместе с неиспользуемыми объектами, пока проход ещё жив. Редактор
            // рисовал Game view на уничтоженном ComputeShader и падал в
            // HDROutputUtils.ConfigureHDROutput. Проход без шейдера пропускается,
            // а фича пересоздаёт его по IsShaderAlive в EnsurePassCreated.
            if (_postProcessCS == null)
            {
                return;
            }

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            if (cameraData.renderType != CameraRenderType.Base ||
                cameraData.camera.cameraType != CameraType.Game ||
                (cameraData.camera != PostProcessRuntimeState.MainCamera &&
                 cameraData.camera != PostProcessRuntimeState.DiagnosticOffscreenCamera))
            {
                return;
            }

            var stack = VolumeManager.instance.stack;
            RefreshVolumeComponents(stack);
            VignetteComponent vignette = RequireComponent(_vignette, nameof(VignetteComponent));
            EigengrauComponent eigengrau = RequireComponent(
                _eigengrau,
                nameof(EigengrauComponent));

            // Обход не трогает статику: правится только то, что уходит в кадр.
            bool bypass = PostProcessRuntimeState.BypassPostProcessEffects ||
                PostProcessRuntimeState.TemporaryBypass;

            bool vignetteActive = !bypass && vignette.active && vignette.IsActive();
            bool eigengrauActive = !bypass && eigengrau.active && eigengrau.IsActive();

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            var activeColor = resourceData.activeColorTexture;
            if (!activeColor.IsValid())
            {
                return;
            }

            TextureDesc activeColorDesc = activeColor.GetDescriptor(renderGraph);
            PostProcessPassDataAssembler.BuildPassDescriptors(
                activeColorDesc,
                cameraData.cameraTargetDescriptor,
                out int width,
                out int height,
                out TextureDesc desc);
            Renderer2DWorldGridData worldGrid = frameData.Get<Renderer2DWorldGridData>();
            bool scaleWorld = worldGrid.Active;
            // Display effects execute once per world pixel. The renderer's
            // existing final blit owns the guarded viewport crop and scaling.
            // This keeps grain, LUT and vignette on the same lattice as geometry.

            Tonemapping output = stack.GetComponent<Tonemapping>();
            bool HDROutput = cameraData.isHDROutputActive;

            // HDR display getters throw when HDR support is disabled in Player Settings.
            ColorGamut HDRGamut = HDROutput ? cameraData.hdrDisplayColorGamut : ColorGamut.sRGB;
            // Unity can report an HDR output before its calibration values are
            // populated. Zero here would turn the DisplayFinal normalization
            // into NaN/Inf and poison the whole frame.
            float paperWhite = HDROutput
                ? Mathf.Max(output.paperWhite.value, DisplaySettings.DefaultPaperWhite)
                : 1f;
            float peakNits = HDROutput
                ? Mathf.Max(output.maxNits.value, paperWhite)
                : 0f;
            DisplayOutputPrecision.Publish(HDROutput, paperWhite);

            // The display pass runs only for presentation or active display effects.
            bool lutActive = !bypass && PostProcessRuntimeState.LUT != null;
            bool diagnosticsActive = PostProcessRuntimeState.DebugView != PostProcessDebugView.None ||
                PostProcessRuntimeState.CompareMode != CompareMode.Off;
            bool passNeeded = scaleWorld || diagnosticsActive || vignetteActive || eigengrauActive || lutActive;
            if (!passNeeded)
            {
                return;
            }

            PostProcessRuntimeState.RecordDiagnosticPass(cameraData.camera, displayPass: true);

            desc.name = "_PPIntermediateColor";
            desc.filterMode = FilterMode.Point;
            TextureHandle intermediateTexture = renderGraph.CreateTexture(desc);
            int textureCount = 1;
            long texturePayloadBytes = TexturePayloadBytes(desc);

            using (var builder = renderGraph.AddUnsafePass<PostProcessPassData>(PassName, out var passData, profilingSampler))
            {
                passData.CameraId = cameraData.camera.GetEntityId();
                passData.Workload = cameraData.camera == PostProcessRuntimeState.DiagnosticOffscreenCamera &&
                    cameraData.camera != PostProcessRuntimeState.MainCamera ? _diagnosticWorkload : _workload;
                passData.CreatedTextureCount = textureCount;
                passData.CreatedTexturePayloadBytes = texturePayloadBytes;
                passData.DisplaySourceUv = new Vector4(1, 1, 0, 0);
                // Keep the crop in canonical Y-up coordinates. The executor resolves
                // each resource's RenderGraph orientation, which is independent of
                // the graphics API's preferred origin (imported camera targets
                // and compute outputs can have different origins).
                passData.ScalesWorldGrid = false;
                passData.WorldGridRect = worldGrid.Active ? worldGrid.Layout.WorldRect : Vector4.zero;
                if (scaleWorld)
                {
                    Vector4 crop = worldGrid.Layout.ViewportToWorldUv;
                    passData.DisplayWorldToViewportUv = new Vector4(1f / crop.x, 1f / crop.y,
                        -crop.z / crop.x, -crop.w / crop.y);
                }
                passData.DisplayViewportAspect = cameraData.camera.aspect;

                passData.DisplayLinearFilter = false;
                passData.PostProcessCS = _postProcessCS;
                passData.KernelComposite = _kernelComposite;
                passData.ColorTexture = activeColor;
                passData.IntermediateTexture = intermediateTexture;
                passData.Width = width;
                passData.Height = height;

                passData.DiagnosticsActive = diagnosticsActive;

                PostProcessPassDataAssembler.FillPassComponents(
                    passData,
                    vignette,
                    eigengrau,
                    vignetteActive,
                    eigengrauActive,
                    HDROutput,
                    HDRGamut,
                    paperWhite,
                    peakNits);
                passData.PostDebugView = (int)PostProcessRuntimeState.DebugView;
                passData.CompareSplit = PostProcessRuntimeState.CompareSplit;
                passData.CompareMode = (int)PostProcessRuntimeState.CompareMode;
                passData.CompareBefore = PostProcessRuntimeState.CompareBefore;
                PostProcessPassDataAssembler.FillLUT(passData, lutActive);

                passData.FrameIndex = Time.frameCount;
                passData.CalibrationPattern = (int)PostProcessRuntimeState.CalibrationMode;
                passData.CalibrationValue = PostProcessRuntimeState.CalibrationValue;
                passData.VignetteInFinalBlit = scaleWorld;
                if (scaleWorld)
                {
                    // The vignette belongs to the display: the final blit draws it
                    // per screen pixel on the scene sample. Calibration patterns
                    // and diagnostic views replace the frame and stay unvignetted.
                    bool screenVignette = passData.VignetteActive && passData.CalibrationPattern == 0 &&
                        passData.PostDebugView == 0;
                    Shader.SetGlobalVector(s_finalVignetteId, new Vector4(
                        screenVignette ? passData.VignetteIntensity : 0f, passData.VignetteSmoothness,
                        passData.VignetteCenter.x, passData.VignetteCenter.y));
                    Shader.SetGlobalVector(s_finalVignetteColorId,
                        (Vector4)(passData.VignetteColor * Mathf.Max(paperWhite, 1f)));
                    Shader.SetGlobalFloat(s_finalVignetteAspectId, cameraData.camera.aspect);
                }

                // Готовый кадр лежит в промежуточной текстуре. Если цель камеры —
                // не экран, копировать его обратно не нужно: промежуточная
                // текстура сама становится цветом камеры. Это одно полноэкранное
                // копирование для прохода вывода.
                passData.SwapColor = !resourceData.isActiveTargetBackBuffer;
                builder.UseTexture(
                    passData.ColorTexture,
                    passData.SwapColor ? AccessFlags.Read : AccessFlags.ReadWrite);
                builder.UseTexture(passData.IntermediateTexture, AccessFlags.ReadWrite);

                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PostProcessPassData data, UnsafeGraphContext context) => PostProcessPassExecutor.Render(data, context));
                if (passData.SwapColor)
                {
                    resourceData.cameraColor = intermediateTexture;
                }
            }
        }

        private static long TexturePayloadBytes(TextureDesc desc)
        {
            return checked((long)desc.width * desc.height * GraphicsFormatUtility.GetBlockSize(desc.colorFormat));
        }
    }
}

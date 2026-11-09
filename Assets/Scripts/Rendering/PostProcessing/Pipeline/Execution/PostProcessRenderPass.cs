#nullable enable

using System;
using Kern.Core;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using static Kern.Rendering.PostProcessing.PostProcessShaderConstants;

using Kern.Rendering.Display;
using Kern.Rendering.WorldGrid;

namespace Kern.Rendering.PostProcessing
{
    public class PostProcessRenderPass : ScriptableRenderPass2D
    {
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
        private ChromaticAberrationComponent? _chromaticAberration;

        private void RefreshVolumeComponents(VolumeStack stack)
        {
            if (ReferenceEquals(_cachedVolumeStack, stack))
            {
                return;
            }

            _cachedVolumeStack = stack;
            _vignette = stack.GetComponent<VignetteComponent>();
            _eigengrau = stack.GetComponent<EigengrauComponent>();
            _chromaticAberration = stack.GetComponent<ChromaticAberrationComponent>();
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
            // Компонент необязательный: тестовые профили собираются без него,
            // и тогда кадр идёт без аберрации, а не падает на RequireComponent.
            ChromaticAberrationComponent? chromaticAberration = _chromaticAberration;
            bool chromaticAberrationActive = !bypass && chromaticAberration != null &&
                chromaticAberration.active && chromaticAberration.IsActive();

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
            int colorVisionMode = bypass ? 0 : PostProcessRuntimeState.ColorVisionMode;
            // Калибровка дисплея: узор и ползунки уровня чёрного и яркости SDR (яркость в HDR не действует)
            float blackLift = DisplayAdjustment.BlackLift(PostProcessRuntimeState.DisplayBlackLevel);
            float gamma = HDROutput ? 1f : DisplayAdjustment.Gamma(PostProcessRuntimeState.DisplayBrightness);
            bool calibrationActive = PostProcessRuntimeState.CalibrationMode != CalibrationPattern.Off ||
                blackLift != 0f || gamma != 1f;
            bool passNeeded = diagnosticsActive || vignetteActive || eigengrauActive ||
                chromaticAberrationActive || lutActive || colorVisionMode != 0 || calibrationActive ||
                PostProcessRuntimeState.DiagnosticWorldImage != null;
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
                WorldRenderGrid grid = WorldRenderGridCamera.Capture(cameraData.camera).Grid;
                passData.WorldGridRect = new Vector4(
                    (float)grid.WorldMinX,
                    (float)grid.WorldMinY,
                    (float)grid.WorldWidth,
                    (float)grid.WorldHeight);
                passData.DisplayViewportAspect = cameraData.camera.aspect;
                passData.DisplayLinearFilter = false;
                passData.PostProcessCS = _postProcessCS;
                passData.KernelComposite = _kernelComposite;
                passData.ColorTexture = activeColor;
                passData.IntermediateTexture = intermediateTexture;
                passData.Width = width;
                passData.Height = height;

                passData.DiagnosticsActive = diagnosticsActive;
                passData.ColorVisionMode = colorVisionMode;
                passData.DisplayBlackLift = blackLift;
                passData.DisplayGamma = gamma;

                PostProcessPassDataAssembler.FillPassComponents(
                    passData,
                    vignette,
                    eigengrau,
                    chromaticAberration,
                    vignetteActive,
                    eigengrauActive,
                    chromaticAberrationActive,
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
                passData.CalibrationRect0 = PostProcessRuntimeState.CalibrationRect0;
                passData.CalibrationRect1 = PostProcessRuntimeState.CalibrationRect1;
                passData.CalibrationRect2 = PostProcessRuntimeState.CalibrationRect2;
                passData.VignetteInFinalBlit = false;

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

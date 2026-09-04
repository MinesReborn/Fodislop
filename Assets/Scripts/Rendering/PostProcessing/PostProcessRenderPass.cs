#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using static Fodinae.Rendering.PostProcessing.PostProcessShaderConstants;

namespace Fodinae.Rendering.PostProcessing
{
    public class PostProcessRenderPass : ScriptableRenderPass2D
    {
        private readonly ComputeShader _postProcessCS;
        private readonly int _kernelPrefilter;
        private readonly int _kernelDownsample;
        private readonly int _kernelUpsample;
        private readonly int _kernelComposite;
        private readonly TextureHandle[] _bloomDownTextures = new TextureHandle[1];
        private readonly TextureHandle[] _bloomUpTextures = new TextureHandle[1];
        private VolumeStack? _cachedVolumeStack;
        private BloomComponent? _bloom;
        private VignetteComponent? _vignette;
        private ChromaticAberrationComponent? _chromaticAberration;
        private ColorGradingComponent? _colorGrading;
        private EigengrauComponent? _eigengrau;
        private MotionBlurComponent? _motionBlur;
        private RTHandle? _historyTexture;
        private GraphicsFormat _historyFormat;
        private bool _historyValid;
        private bool _temporalWasActive;
        private uint _observedCameraGeneration;
        private Matrix4x4 _lastViewProjection;
        private bool _hasViewProjection;

        private static Camera? _mainCamera;
        private static uint _cameraGeneration;

        // Set by PostProcessController from the active graphics preset. Static
        // for the same reason _mainCamera is: a ScriptableRenderPass is owned by
        // the renderer asset, not by the scene, so there is no injection path
        // into it - the controller pushes, the pass reads.
        //
        // Выключить постпроцесс нельзя ничем: ни настройкой, ни отладочным
        // байпасом, ни ожиданием конфига. Тонмап сжимает HDR каскадного света
        private static AdvancedPostProcessSnapshot _advanced;

        private static float _displayGamma = DisplaySettings.DefaultGamma;
        private static float _displayPaperWhiteNits = DisplaySettings.DefaultPaperWhite;
        private static float _displayPeakBrightnessNits = DisplaySettings.DefaultPeakBrightness;

        /// <summary>
        /// Флаг полного отключения эффектов конвейера для бисекции/отладки через GUI.
        /// По умолчанию выключен, чтобы все настройки графики и эффектов действовали.
        /// </summary>
        public static bool BypassPostProcessEffects { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForDomainReload()
        {
            _mainCamera = null;
            _cameraGeneration = 0;
            _advanced = default;
            _displayGamma = DisplaySettings.DefaultGamma;
            _displayPaperWhiteNits = DisplaySettings.DefaultPaperWhite;
            _displayPeakBrightnessNits = DisplaySettings.DefaultPeakBrightness;
            BypassPostProcessEffects = false;
        }

        public static void SetDisplayCalibration(float gamma, float paperWhiteNits, float peakBrightnessNits)
        {
            _displayGamma = gamma > 0.1f ? gamma : DisplaySettings.DefaultGamma;
            _displayPaperWhiteNits = paperWhiteNits > 10f ? paperWhiteNits : DisplaySettings.DefaultPaperWhite;
            _displayPeakBrightnessNits = peakBrightnessNits > 100f ? peakBrightnessNits : DisplaySettings.DefaultPeakBrightness;
        }

        public static void SetAdvancedSettings(AdvancedPostProcessSnapshot settings)
        {
            _advanced = settings;
        }

        private void RefreshVolumeComponents(VolumeStack stack)
        {
            if (ReferenceEquals(_cachedVolumeStack, stack))
            {
                return;
            }

            _cachedVolumeStack = stack;
            _bloom = stack.GetComponent<BloomComponent>();
            _vignette = stack.GetComponent<VignetteComponent>();
            _chromaticAberration = stack.GetComponent<ChromaticAberrationComponent>();
            _colorGrading = stack.GetComponent<ColorGradingComponent>();
            _eigengrau = stack.GetComponent<EigengrauComponent>();
            _motionBlur = stack.GetComponent<MotionBlurComponent>();
        }

        private static T RequireComponent<T>(T? component, string componentName)
            where T : VolumeComponent
        {
            return component ?? throw new InvalidOperationException(
                $"Post-process VolumeStack is missing required component '{componentName}'.");
        }

        public static void SetMainCamera(Camera? camera)
        {
            if (_mainCamera != camera)
            {
                // Смена камеры обесценивает историю временных эффектов: она
                // снята с другого ракурса. Поколение сбрасывает её, не трогая
                // сам проход.
                _cameraGeneration++;
            }

            _mainCamera = camera;
        }

        public PostProcessRenderPass(ComputeShader postProcessCS)
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            renderPassEvent2D = RenderPassEvent2D.BeforeRenderingPostProcessing;
            _postProcessCS = postProcessCS;
            _kernelPrefilter = _postProcessCS.FindKernel("BloomPrefilter");
            _kernelDownsample = _postProcessCS.FindKernel("BloomDownsample");
            _kernelUpsample = _postProcessCS.FindKernel("BloomUpsample");
            _kernelComposite = _postProcessCS.FindKernel("CompositeFinal");
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_observedCameraGeneration != _cameraGeneration)
            {
                _observedCameraGeneration = _cameraGeneration;
                _historyValid = false;
            }

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            if (cameraData.renderType != CameraRenderType.Base ||
                cameraData.camera.cameraType != CameraType.Game ||
                cameraData.camera != _mainCamera)
            {
                return;
            }

            Matrix4x4 viewProjection =
                cameraData.camera.projectionMatrix * cameraData.camera.worldToCameraMatrix;
            if (!_hasViewProjection || _lastViewProjection != viewProjection)
            {
                // History has no motion-vector reprojection. Reusing it after
                // the camera moves blends unrelated screen pixels and produces
                // full-frame trails, especially around high-contrast UI and
                // terrain edges.
                _lastViewProjection = viewProjection;
                _hasViewProjection = true;
                _historyValid = false;
            }

            var stack = VolumeManager.instance.stack;
            RefreshVolumeComponents(stack);
            BloomComponent bloom = RequireComponent(_bloom, nameof(BloomComponent));
            VignetteComponent vignette = RequireComponent(_vignette, nameof(VignetteComponent));
            ChromaticAberrationComponent ca = RequireComponent(
                _chromaticAberration,
                nameof(ChromaticAberrationComponent));
            ColorGradingComponent cg = RequireComponent(
                _colorGrading,
                nameof(ColorGradingComponent));
            EigengrauComponent eigengrau = RequireComponent(
                _eigengrau,
                nameof(EigengrauComponent));
            MotionBlurComponent mb = RequireComponent(_motionBlur, nameof(MotionBlurComponent));

            bool bloomActive =
                (bloom.active && bloom.IsActive()) ||
                _advanced.RequiresBloomTexture;
            bool vignetteActive = vignette.active && vignette.IsActive();
            bool caActive = ca.active && ca.IsActive();
            bool cgActive = cg.active && cg.IsActive();
            bool eigengrauActive = eigengrau.active && eigengrau.IsActive();
            bool mbActive = mb.active && mb.IsActive();

            if (BypassPostProcessEffects)
            {
                bloomActive = false;
                vignetteActive = false;
                caActive = false;
                cgActive = false;
                eigengrauActive = false;
                mbActive = false;
                _advanced = default;
                _displayGamma = DisplaySettings.DefaultGamma;
            }

            // Досрочного выхода по «ни одного включённого эффекта» здесь нет и
            // быть не может. Тонмап работает в обоих режимах вывода и не
            // выключается ничем: он сжимает HDR каскадного света под диапазон
            // дисплея, и кадр без него не дешевле, а неверен — всё ярче белой
            // точки срезается в плоский белый. Раньше на этом месте стояла
            // проверка, первым слагаемым которой было константное `true`:
            // условие никогда не выполнялось, но читалось как живое.

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            var activeColor = resourceData.activeColorTexture;
            if (!activeColor.IsValid())
            {
                return;
            }

            var activeColorDesc = resourceData.activeColorTexture.GetDescriptor(renderGraph);
            var desc = cameraData.cameraTargetDescriptor;
            desc.graphicsFormat = activeColorDesc.colorFormat;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.bindMS = false;
            desc.enableRandomWrite = true;

            bool temporalActive =
                _advanced.TemporalPersistenceIntensity > 0f ||
                _advanced.LightStability > 0f ||
                mbActive;
            if (temporalActive && !_temporalWasActive)
            {
                _historyValid = false;
            }

            _temporalWasActive = temporalActive;
            TextureHandle historyTexture = default;
            if (temporalActive)
            {
                EnsureHistoryTexture(desc);
                historyTexture = renderGraph.ImportTexture(
                    _historyTexture ?? throw new InvalidOperationException(
                        "Post-process history texture allocation failed."));
            }

            TextureHandle intermediateTexture = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph,
                desc,
                "_PPIntermediateColor",
                false,
                FilterMode.Point);

            TextureHandle bloomPrefilterTexture = default;
            if (bloomActive)
            {
                var bloomDesc = desc;
                bloomDesc.width = Mathf.Max(1, bloomDesc.width / 2);
                bloomDesc.height = Mathf.Max(1, bloomDesc.height / 2);
                bloomPrefilterTexture = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph,
                    bloomDesc,
                    "_PPBloomPrefilter",
                    false,
                    FilterMode.Bilinear);

                for (int i = 0; i < _bloomDownTextures.Length; i++)
                {
                    bloomDesc.width = Mathf.Max(1, bloomDesc.width / 2);
                    bloomDesc.height = Mathf.Max(1, bloomDesc.height / 2);
                    _bloomDownTextures[i] = UniversalRenderer.CreateRenderGraphTexture(
                        renderGraph,
                        bloomDesc,
                        BloomDownNames[i],
                        false,
                        FilterMode.Bilinear);
                }

                for (int i = 0; i < _bloomUpTextures.Length; i++)
                {
                    var bloomUpDesc = desc;
                    bloomUpDesc.width = Mathf.Max(1, bloomUpDesc.width >> (i + 1));
                    bloomUpDesc.height = Mathf.Max(1, bloomUpDesc.height >> (i + 1));
                    _bloomUpTextures[i] = UniversalRenderer.CreateRenderGraphTexture(
                        renderGraph,
                        bloomUpDesc,
                        BloomUpNames[i],
                        false,
                        FilterMode.Bilinear);
                }
            }

            using (var builder = renderGraph.AddUnsafePass<PostProcessPassData>(PassName, out var passData, profilingSampler))
            {
                passData.PostProcessCS = _postProcessCS;
                passData.KernelPrefilter = _kernelPrefilter;
                passData.KernelDownsample = _kernelDownsample;
                passData.KernelUpsample = _kernelUpsample;
                passData.KernelComposite = _kernelComposite;

                passData.ColorTexture = activeColor;
                passData.IntermediateTexture = intermediateTexture;
                passData.BloomPrefilterTexture = bloomPrefilterTexture;
                passData.BloomDownTextures = _bloomDownTextures;
                passData.BloomUpTextures = _bloomUpTextures;
                passData.Descriptor = desc;
                passData.HistoryTexture = historyTexture;

                passData.BloomActive = bloomActive;
                passData.BloomThreshold = bloom.threshold.value;
                passData.BloomSoftKnee = bloom.softKnee.value;
                passData.BloomRadius = bloom.radius.value;
                passData.BloomScatter = bloom.scatter.value;
                passData.BloomTint = bloom.tint.value;
                passData.BloomIntensity = bloom.intensity.value;

                passData.VignetteActive = vignetteActive;
                passData.VignetteIntensity = vignette.intensity.value;
                passData.VignetteColor = vignette.color.value;
                passData.VignetteSmoothness = vignette.smoothness.value;
                passData.VignetteCenter = vignette.center.value;

                passData.CaActive = caActive;
                passData.CaIntensity = ca.intensity.value;

                passData.CgActive = cgActive;
                passData.Exposure = cg.exposure.value;
                passData.ColorFilter = cg.colorFilter.value;
                passData.Contrast = cg.contrast.value;
                passData.Saturation = cg.saturation.value;
                passData.Gamma = _displayGamma;

                if (cameraData.isHDROutputActive)
                {
                    HDROutputSettings output = HDROutputSettings.main;
                    float nativePaperWhite = output.available && output.paperWhiteNits > 10f
                        ? output.paperWhiteNits
                        : DisplaySettings.DefaultPaperWhite;
                    passData.HdrPaperWhiteScale = _displayPaperWhiteNits / nativePaperWhite;
                    passData.ToneMappingWhitePoint = Mathf.Max(0.5f, _displayPeakBrightnessNits / Mathf.Max(10f, _displayPaperWhiteNits));
                    passData.ToneMappingEnabled = !BypassPostProcessEffects && cg.toneMapping.value;
                }
                else
                {
                    passData.HdrPaperWhiteScale = 1f;
                    passData.ToneMappingWhitePoint = BypassPostProcessEffects
                        ? PostProcessSettings.DefaultToneMappingWhitePoint
                        : cg.toneMappingWhitePoint.value;
                    passData.ToneMappingEnabled = !BypassPostProcessEffects && cg.toneMapping.value;
                }

                passData.EigengrauActive = eigengrauActive;
                passData.EigengrauIntensity = eigengrau.intensity.value;
                passData.EigengrauColor = eigengrau.color.value;
                passData.EigengrauDarknessThreshold = eigengrau.darknessThreshold.value;
                passData.EigengrauNoiseScale = eigengrau.noiseScale.value;
                passData.EigengrauAnimationSpeed = eigengrau.animationSpeed.value;

                passData.Advanced0 = new Vector4(
                    _advanced.LocalContrastIntensity,
                    _advanced.LensDirtIntensity,
                    _advanced.LensDirtScale,
                    _advanced.AnamorphicIntensity);
                passData.Advanced1 = new Vector4(
                    _advanced.AnamorphicLength,
                    _advanced.ChromaticDiffractionIntensity,
                    _advanced.HeatRefractionIntensity,
                    _advanced.HeatRefractionScale);
                passData.Advanced2 = new Vector4(
                    _advanced.GlintIntensity,
                    _advanced.GlintThreshold,
                    _advanced.VolumetricDustIntensity,
                    _advanced.VolumetricDustScale);
                passData.Advanced3 = new Vector4(
                    _advanced.VolumetricDustSpeed,
                    _advanced.PhosphorMaskIntensity,
                    _advanced.DitheringIntensity,
                    0f);
                passData.HistoryValid = _historyValid;
                passData.Temporal = passData.HistoryValid
                    ? new Vector4(
                        _advanced.TemporalPersistenceIntensity,
                        _advanced.TemporalPersistenceDecay,
                        _advanced.LightStability,
                        mbActive ? mb.intensity.value : 0f)
                    : Vector4.zero;
                passData.TemporalActive = temporalActive;
                passData.TimeSeconds = Time.time;

                builder.UseTexture(passData.ColorTexture, AccessFlags.ReadWrite);
                builder.UseTexture(passData.IntermediateTexture, AccessFlags.ReadWrite);
                if (passData.TemporalActive)
                {
                    builder.UseTexture(passData.HistoryTexture, AccessFlags.ReadWrite);
                }

                if (passData.BloomActive)
                {
                    builder.UseTexture(passData.BloomPrefilterTexture, AccessFlags.ReadWrite);
                    for (int i = 0; i < passData.BloomDownTextures.Length; i++)
                    {
                        builder.UseTexture(passData.BloomDownTextures[i], AccessFlags.ReadWrite);
                    }

                    for (int i = 0; i < passData.BloomUpTextures.Length; i++)
                    {
                        builder.UseTexture(passData.BloomUpTextures[i], AccessFlags.ReadWrite);
                    }
                }

                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PostProcessPassData data, UnsafeGraphContext context) => PostProcessPassExecutor.Render(data, context));
            }

            if (temporalActive)
            {
                _historyValid = true;
            }
        }

        private void EnsureHistoryTexture(RenderTextureDescriptor descriptor)
        {
            if (_historyTexture != null &&
                _historyTexture.rt.width == descriptor.width &&
                _historyTexture.rt.height == descriptor.height &&
                _historyFormat == descriptor.graphicsFormat)
            {
                return;
            }

            _historyTexture?.Release();
            _historyTexture = RTHandles.Alloc(
                descriptor,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                name: "_PPTemporalHistory");
            _historyFormat = descriptor.graphicsFormat;
            _historyValid = false;
        }

        public void Dispose()
        {
            _historyTexture?.Release();
            _historyTexture = null;
            _historyValid = false;
            _temporalWasActive = false;
            _hasViewProjection = false;
        }
    }
}

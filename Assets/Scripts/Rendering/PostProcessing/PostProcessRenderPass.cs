#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Fodinae.Rendering.PostProcessing
{
    public class PostProcessRenderPass : ScriptableRenderPass2D
    {
        private const string PASS_NAME = "ComputePostProcessPass";
        private static readonly int InputTexID = Shader.PropertyToID("_InputTex");
        private static readonly int SourceTexID = Shader.PropertyToID("_SourceTex");
        private static readonly int BaseTexID = Shader.PropertyToID("_BaseTex");
        private static readonly int BloomTexID = Shader.PropertyToID("_BloomTex");
        private static readonly int DestTexID = Shader.PropertyToID("_DestTex");
        private static readonly int OutputTexID = Shader.PropertyToID("_OutputTex");
        private static readonly int ScreenSizeID = Shader.PropertyToID("_ScreenSize");
        private static readonly int SourceTexelSizeID = Shader.PropertyToID("_SourceTexelSize");

        private static readonly int BloomThresholdID = Shader.PropertyToID("_BloomThreshold");
        private static readonly int BloomSoftKneeID = Shader.PropertyToID("_BloomSoftKnee");
        private static readonly int BloomRadiusID = Shader.PropertyToID("_BloomRadius");
        private static readonly int BloomScatterID = Shader.PropertyToID("_BloomScatter");
        private static readonly int BloomTintID = Shader.PropertyToID("_BloomTint");
        private static readonly int BloomIntensityID = Shader.PropertyToID("_BloomIntensity");

        private static readonly int VignetteIntensityID = Shader.PropertyToID("_VignetteIntensity");
        private static readonly int VignetteColorID = Shader.PropertyToID("_VignetteColor");
        private static readonly int VignetteSmoothnessID = Shader.PropertyToID("_VignetteSmoothness");
        private static readonly int VignetteCenterID = Shader.PropertyToID("_VignetteCenter");

        private static readonly int ChromaticAberrationIntensityID = Shader.PropertyToID("_ChromaticAberrationIntensity");

        private static readonly int ExposureID = Shader.PropertyToID("_Exposure");
        private static readonly int ColorFilterID = Shader.PropertyToID("_ColorFilter");
        private static readonly int ContrastID = Shader.PropertyToID("_Contrast");
        private static readonly int SaturationID = Shader.PropertyToID("_Saturation");
        private static readonly int GammaID = Shader.PropertyToID("_Gamma");
        private static readonly int HdrPaperWhiteScaleID = Shader.PropertyToID("_HdrPaperWhiteScale");
        private static readonly int ToneMappingEnabledID = Shader.PropertyToID("_ToneMappingEnabled");
        private static readonly int ToneMappingWhitePointID = Shader.PropertyToID("_ToneMappingWhitePoint");

        private static readonly int EigengrauIntensityID = Shader.PropertyToID("_EigengrauIntensity");
        private static readonly int EigengrauColorID = Shader.PropertyToID("_EigengrauColor");
        private static readonly int EigengrauDarknessThresholdID = Shader.PropertyToID("_EigengrauDarknessThreshold");
        private static readonly int EigengrauNoiseScaleID = Shader.PropertyToID("_EigengrauNoiseScale");
        private static readonly int EigengrauAnimationSpeedID = Shader.PropertyToID("_EigengrauAnimationSpeed");
        private static readonly int TimeID = Shader.PropertyToID("_Time");

        private static readonly int Advanced0ID = Shader.PropertyToID("_Advanced0");
        private static readonly int Advanced1ID = Shader.PropertyToID("_Advanced1");
        private static readonly int Advanced2ID = Shader.PropertyToID("_Advanced2");
        private static readonly int Advanced3ID = Shader.PropertyToID("_Advanced3");
        private static readonly int HistoryTexID = Shader.PropertyToID("_HistoryTex");
        private static readonly int TemporalID = Shader.PropertyToID("_Temporal");
        private static readonly string[] BloomDownNames =
        {
            "_PPBloomDown_0",
        };
        private static readonly string[] BloomUpNames =
        {
            "_PPBloomUp_0",
        };

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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForDomainReload()
        {
            _mainCamera = null;
            _cameraGeneration = 0;
            _advanced = default;
            _displayGamma = DisplaySettings.DefaultGamma;
            _displayPaperWhiteNits = DisplaySettings.DefaultPaperWhite;
            _displayPeakBrightnessNits = DisplaySettings.DefaultPeakBrightness;
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

        private class PassData
        {
            public ComputeShader PostProcessCS = null!;
            public int KernelPrefilter;
            public int KernelDownsample;
            public int KernelUpsample;
            public int KernelComposite;

            public TextureHandle ColorTexture;
            public TextureHandle IntermediateTexture;
            public TextureHandle BloomPrefilterTexture;
            public TextureHandle[] BloomDownTextures = null!;
            public TextureHandle[] BloomUpTextures = null!;
            public TextureHandle HistoryTexture;
            public RenderTextureDescriptor Descriptor;

            public bool BloomActive;
            public float BloomThreshold;
            public float BloomSoftKnee;
            public float BloomRadius;
            public float BloomScatter;
            public Vector4 BloomTint;
            public float BloomIntensity;

            public bool VignetteActive;
            public float VignetteIntensity;
            public Vector4 VignetteColor;
            public float VignetteSmoothness;
            public Vector2 VignetteCenter;

            public bool CaActive;
            public float CaIntensity;

            public bool CgActive;
            public float Exposure;
            public Vector4 ColorFilter;
            public float Contrast;
            public float Saturation;
            public float Gamma;
            public float HdrPaperWhiteScale;
            public bool ToneMappingEnabled;
            public float ToneMappingWhitePoint;

            public bool EigengrauActive;
            public float EigengrauIntensity;
            public Vector4 EigengrauColor;
            public float EigengrauDarknessThreshold;
            public float EigengrauNoiseScale;
            public float EigengrauAnimationSpeed;

            public Vector4 Advanced0;
            public Vector4 Advanced1;
            public Vector4 Advanced2;
            public Vector4 Advanced3;
            public Vector4 Temporal;
            public bool HistoryValid;
            public bool TemporalActive;
            public float TimeSeconds;
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

            using (var builder = renderGraph.AddUnsafePass<PassData>(PASS_NAME, out var passData, profilingSampler))
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
                    passData.ToneMappingEnabled = true;
                }
                else
                {
                    passData.HdrPaperWhiteScale = 1f;
                    passData.ToneMappingWhitePoint = cg.toneMappingWhitePoint.value;
                    passData.ToneMappingEnabled = true;
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

                builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    int width = data.Descriptor.width;
                    int height = data.Descriptor.height;

                    cmd.SetComputeVectorParam(data.PostProcessCS, ScreenSizeID, new Vector4(width, height, 1f / width, 1f / height));

                    if (data.BloomActive)
                    {
                        cmd.SetComputeFloatParam(data.PostProcessCS, BloomThresholdID, data.BloomThreshold);
                        cmd.SetComputeFloatParam(data.PostProcessCS, BloomSoftKneeID, data.BloomSoftKnee);
                        cmd.SetComputeFloatParam(data.PostProcessCS, BloomRadiusID, data.BloomRadius);
                        cmd.SetComputeFloatParam(data.PostProcessCS, BloomScatterID, data.BloomScatter);
                        cmd.SetComputeVectorParam(data.PostProcessCS, BloomTintID, data.BloomTint);
                        cmd.SetComputeFloatParam(data.PostProcessCS, BloomIntensityID, data.BloomIntensity);

                        int prefilterWidth = Mathf.Max(1, width / 2);
                        int prefilterHeight = Mathf.Max(1, height / 2);
                        cmd.SetComputeVectorParam(
                            data.PostProcessCS,
                            ScreenSizeID,
                            new Vector4(
                                prefilterWidth,
                                prefilterHeight,
                                1f / prefilterWidth,
                                1f / prefilterHeight));
                        cmd.SetComputeVectorParam(
                            data.PostProcessCS,
                            SourceTexelSizeID,
                            new Vector4(1f / width, 1f / height, width, height));
                        cmd.BeginSample("Fodinae.PostProcess.Bloom.Prefilter");
                        cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelPrefilter, InputTexID, data.ColorTexture);
                        cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelPrefilter, DestTexID, data.BloomPrefilterTexture);
                        cmd.DispatchCompute(
                            data.PostProcessCS,
                            data.KernelPrefilter,
                            Mathf.CeilToInt(prefilterWidth / 8f),
                            Mathf.CeilToInt(prefilterHeight / 8f),
                            1);
                        cmd.EndSample("Fodinae.PostProcess.Bloom.Prefilter");

                        int downWidth = prefilterWidth;
                        int downHeight = prefilterHeight;
                        int sourceWidth = prefilterWidth;
                        int sourceHeight = prefilterHeight;
                        TextureHandle currentSource = data.BloomPrefilterTexture;
                        cmd.BeginSample("Fodinae.PostProcess.Bloom.Downsample");
                        for (int i = 0; i < data.BloomDownTextures.Length; i++)
                        {
                            downWidth = Mathf.Max(1, downWidth / 2);
                            downHeight = Mathf.Max(1, downHeight / 2);
                            cmd.SetComputeVectorParam(
                                data.PostProcessCS,
                                ScreenSizeID,
                                new Vector4(downWidth, downHeight, 1f / downWidth, 1f / downHeight));
                            cmd.SetComputeVectorParam(
                                data.PostProcessCS,
                                SourceTexelSizeID,
                                new Vector4(1f / sourceWidth, 1f / sourceHeight, sourceWidth, sourceHeight));
                            cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelDownsample, SourceTexID, currentSource);
                            cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelDownsample, DestTexID, data.BloomDownTextures[i]);
                            cmd.DispatchCompute(
                                data.PostProcessCS,
                                data.KernelDownsample,
                                Mathf.CeilToInt(downWidth / 8f),
                                Mathf.CeilToInt(downHeight / 8f),
                                1);
                            currentSource = data.BloomDownTextures[i];
                            sourceWidth = downWidth;
                            sourceHeight = downHeight;
                        }

                        cmd.EndSample("Fodinae.PostProcess.Bloom.Downsample");

                        TextureHandle currentUp = data.BloomDownTextures[^1];
                        int currentUpWidth = downWidth;
                        int currentUpHeight = downHeight;
                        cmd.BeginSample("Fodinae.PostProcess.Bloom.Upsample");
                        for (int i = data.BloomUpTextures.Length - 1; i >= 0; i--)
                        {
                            int upWidth = Mathf.Max(1, width >> (i + 1));
                            int upHeight = Mathf.Max(1, height >> (i + 1));
                            TextureHandle baseTexture = i == 0
                                ? data.BloomPrefilterTexture
                                : data.BloomDownTextures[i - 1];
                            cmd.SetComputeVectorParam(
                                data.PostProcessCS,
                                ScreenSizeID,
                                new Vector4(upWidth, upHeight, 1f / upWidth, 1f / upHeight));
                            cmd.SetComputeVectorParam(
                                data.PostProcessCS,
                                SourceTexelSizeID,
                                new Vector4(1f / currentUpWidth, 1f / currentUpHeight, currentUpWidth, currentUpHeight));
                            cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelUpsample, SourceTexID, currentUp);
                            cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelUpsample, BaseTexID, baseTexture);
                            cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelUpsample, DestTexID, data.BloomUpTextures[i]);
                            cmd.DispatchCompute(
                                data.PostProcessCS,
                                data.KernelUpsample,
                                Mathf.CeilToInt(upWidth / 8f),
                                Mathf.CeilToInt(upHeight / 8f),
                                1);
                            currentUp = data.BloomUpTextures[i];
                            currentUpWidth = upWidth;
                            currentUpHeight = upHeight;
                        }

                        cmd.EndSample("Fodinae.PostProcess.Bloom.Upsample");

                        cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelComposite, BloomTexID, currentUp);
                    }
                    else
                    {
                        cmd.SetComputeFloatParam(data.PostProcessCS, BloomIntensityID, 0f);
                        cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelComposite, BloomTexID, Texture2D.blackTexture);
                    }

                    cmd.SetComputeVectorParam(data.PostProcessCS, ScreenSizeID, new Vector4(width, height, 1f / width, 1f / height));

                    cmd.SetComputeFloatParam(data.PostProcessCS, VignetteIntensityID, data.VignetteActive ? data.VignetteIntensity : 0f);
                    if (data.VignetteActive)
                    {
                        cmd.SetComputeVectorParam(data.PostProcessCS, VignetteColorID, data.VignetteColor);
                        cmd.SetComputeFloatParam(data.PostProcessCS, VignetteSmoothnessID, data.VignetteSmoothness);
                        cmd.SetComputeVectorParam(data.PostProcessCS, VignetteCenterID, data.VignetteCenter);
                    }

                    cmd.SetComputeFloatParam(data.PostProcessCS, ChromaticAberrationIntensityID, data.CaActive ? data.CaIntensity : 0f);

                    cmd.SetComputeFloatParam(data.PostProcessCS, ExposureID, data.CgActive ? data.Exposure : 0f);
                    cmd.SetComputeVectorParam(data.PostProcessCS, ColorFilterID, data.CgActive ? data.ColorFilter : Color.white);
                    cmd.SetComputeFloatParam(data.PostProcessCS, ContrastID, data.CgActive ? data.Contrast : 0f);
                    cmd.SetComputeFloatParam(data.PostProcessCS, SaturationID, data.CgActive ? data.Saturation : 1f);
                    cmd.SetComputeFloatParam(data.PostProcessCS, GammaID, data.Gamma);
                    cmd.SetComputeFloatParam(data.PostProcessCS, HdrPaperWhiteScaleID, data.HdrPaperWhiteScale);
                    cmd.SetComputeIntParam(
                        data.PostProcessCS,
                        ToneMappingEnabledID,
                        data.ToneMappingEnabled ? 1 : 0);
                    cmd.SetComputeFloatParam(
                        data.PostProcessCS,
                        ToneMappingWhitePointID,
                        data.ToneMappingWhitePoint);

                    cmd.SetComputeFloatParam(data.PostProcessCS, EigengrauIntensityID, data.EigengrauActive ? data.EigengrauIntensity : 0f);
                    if (data.EigengrauActive)
                    {
                        cmd.SetComputeVectorParam(data.PostProcessCS, EigengrauColorID, data.EigengrauColor);
                        cmd.SetComputeFloatParam(data.PostProcessCS, EigengrauDarknessThresholdID, data.EigengrauDarknessThreshold);
                        cmd.SetComputeFloatParam(data.PostProcessCS, EigengrauNoiseScaleID, data.EigengrauNoiseScale);
                        cmd.SetComputeFloatParam(data.PostProcessCS, EigengrauAnimationSpeedID, data.EigengrauAnimationSpeed);
                        cmd.SetComputeFloatParam(data.PostProcessCS, TimeID, data.TimeSeconds);
                    }

                    cmd.SetComputeVectorParam(data.PostProcessCS, Advanced0ID, data.Advanced0);
                    cmd.SetComputeVectorParam(data.PostProcessCS, Advanced1ID, data.Advanced1);
                    cmd.SetComputeVectorParam(data.PostProcessCS, Advanced2ID, data.Advanced2);
                    cmd.SetComputeVectorParam(data.PostProcessCS, Advanced3ID, data.Advanced3);
                    cmd.SetComputeFloatParam(data.PostProcessCS, TimeID, data.TimeSeconds);
                    cmd.SetComputeVectorParam(data.PostProcessCS, TemporalID, data.Temporal);
                    if (data.TemporalActive && data.HistoryValid)
                    {
                        cmd.SetComputeTextureParam(
                            data.PostProcessCS,
                            data.KernelComposite,
                            HistoryTexID,
                            data.HistoryTexture);
                    }
                    else
                    {
                        cmd.SetComputeTextureParam(
                            data.PostProcessCS,
                            data.KernelComposite,
                            HistoryTexID,
                            Texture2D.blackTexture);
                    }

                    cmd.BeginSample("Fodinae.PostProcess.Composite");
                    cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelComposite, InputTexID, data.ColorTexture);
                    cmd.SetComputeTextureParam(data.PostProcessCS, data.KernelComposite, OutputTexID, data.IntermediateTexture);
                    cmd.DispatchCompute(data.PostProcessCS, data.KernelComposite, Mathf.CeilToInt(width / 8f), Mathf.CeilToInt(height / 8f), 1);
                    cmd.EndSample("Fodinae.PostProcess.Composite");

                    cmd.BeginSample("Fodinae.PostProcess.BlitBack");
                    Blitter.BlitCameraTexture(cmd, data.IntermediateTexture, data.ColorTexture);
                    if (data.TemporalActive)
                    {
                        cmd.CopyTexture(data.IntermediateTexture, data.HistoryTexture);
                    }
                    cmd.EndSample("Fodinae.PostProcess.BlitBack");
                });
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

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
        private readonly bool _displayPass;
        private readonly ComputeShader _postProcessCS;
        private readonly int _kernelPrefilter;
        private readonly int _kernelDownsample;
        private readonly int _kernelUpsample;
        private readonly int _kernelComposite;
        private readonly int _kernelUpsampleComposite;
        // Размеры берутся из списков имён: две константы, обязанные совпадать,
        // разъезжались бы молча.
        private readonly TextureHandle[] _bloomDownTextures = new TextureHandle[BloomDownNames.Length];
        private readonly TextureHandle[] _bloomUpTextures = new TextureHandle[BloomUpNames.Length];
        private VolumeStack? _cachedVolumeStack;
        private BloomComponent? _bloom;
        private VignetteComponent? _vignette;
        private EigengrauComponent? _eigengrau;

        private void RefreshVolumeComponents(VolumeStack stack)
        {
            if (ReferenceEquals(_cachedVolumeStack, stack))
            {
                return;
            }

            _cachedVolumeStack = stack;
            _bloom = stack.GetComponent<BloomComponent>();
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

        public PostProcessRenderPass(ComputeShader postProcessCS, bool displayPass = false)
        {
            _displayPass = displayPass;
            renderPassEvent = displayPass ? RenderPassEvent.AfterRenderingPostProcessing : RenderPassEvent.BeforeRenderingPostProcessing;
            renderPassEvent2D = displayPass ? RenderPassEvent2D.AfterRenderingPostProcessing : RenderPassEvent2D.BeforeRenderingPostProcessing;
            _postProcessCS = postProcessCS;
            _kernelPrefilter = _postProcessCS.FindKernel("BloomPrefilter");
            _kernelDownsample = _postProcessCS.FindKernel("BloomDownsample");
            _kernelUpsample = _postProcessCS.FindKernel("BloomUpsample");
            _kernelUpsampleComposite = _postProcessCS.FindKernel("BloomUpsampleComposite");
            _kernelComposite = _postProcessCS.FindKernel(displayPass ? "DisplayFinal" : "CompositeFinal");
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
                cameraData.camera != PostProcessRuntimeState.MainCamera)
            {
                return;
            }

            var stack = VolumeManager.instance.stack;
            RefreshVolumeComponents(stack);
            BloomComponent bloom = RequireComponent(_bloom, nameof(BloomComponent));
            VignetteComponent vignette = RequireComponent(_vignette, nameof(VignetteComponent));
            EigengrauComponent eigengrau = RequireComponent(
                _eigengrau,
                nameof(EigengrauComponent));

            // Обход не трогает статику: правится только то, что уходит в кадр.
            bool bypass = PostProcessRuntimeState.BypassPostProcessEffects ||
                PostProcessRuntimeState.TemporaryBypass;

            bool bloomActive = !bypass && !_displayPass &&
                bloom.active && bloom.IsActive();
            bool vignetteActive = !bypass && vignette.active && vignette.IsActive();
            bool eigengrauActive = !bypass && eigengrau.active && eigengrau.IsActive();

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

            TextureDesc activeColorDesc = activeColor.GetDescriptor(renderGraph);
            PostProcessPassDataAssembler.BuildPassDescriptors(
                activeColorDesc,
                cameraData.cameraTargetDescriptor,
                out int width,
                out int height,
                out TextureDesc desc);

            Tonemapping output = stack.GetComponent<Tonemapping>();
            bool hdrOutput = cameraData.isHDROutputActive;

            // HDR display getters throw when HDR support is disabled in Player Settings.
            ColorGamut hdrGamut = hdrOutput ? cameraData.hdrDisplayColorGamut : ColorGamut.sRGB;
            // Unity can report an HDR output before its calibration values are
            // populated. Zero here would turn the DisplayFinal normalization
            // into NaN/Inf and poison the whole frame.
            float paperWhite = hdrOutput
                ? Mathf.Max(output.paperWhite.value, DisplaySettings.DefaultPaperWhite)
                : 1f;
            float peakNits = hdrOutput
                ? Mathf.Max(output.maxNits.value, paperWhite)
                : 0f;

            // Проход, который ничего не меняет, не запускается вовсе. Каждый из
            // двух проходов — полноэкранный compute на полном разрешении кадра,
            // и при нулевых эффектах они стоили ~20 fps на 3420×1890 впустую.
            bool lutActive = !bypass && PostProcessRuntimeState.Lut != null;
            bool diagnosticsActive = PostProcessRuntimeState.DebugView != PostProcessDebugView.None ||
                PostProcessRuntimeState.CompareMode != CompareMode.Off;
            // Глубина пирамиды блума считается ДО раннего выхода: на
            // вырожденном размере окна уровней не остаётся, блум отключается, и
            // тогда творческий проход может оказаться не нужен вовсе. Нижние
            // уровни вырождаются в несколько пикселей и ореола не добавляют,
            // зато продолжают стоить dispatch и барьер.
            int bloomLevels = 0;
            if (bloomActive)
            {
                int smallestSide = Mathf.Max(1, Mathf.Min(width, height) / 2);
                while (bloomLevels < _bloomDownTextures.Length &&
                    (smallestSide >> (bloomLevels + 1)) >= 8)
                {
                    bloomLevels++;
                }

                bloomActive = bloomLevels > 0;
            }

            // Сценический проход теперь несёт только блум; LUT, виньетка,
            // зерно и диагностика живут в проходе вывода.
            bool passNeeded = _displayPass
                ? diagnosticsActive || vignetteActive || eigengrauActive || lutActive
                : bloomActive;
            if (!passNeeded)
            {
                return;
            }

            PostProcessRuntimeState.RecordDiagnosticPass(cameraData.camera, _displayPass);

            desc.name = "_PPIntermediateColor";
            desc.filterMode = FilterMode.Point;
            TextureHandle intermediateTexture = renderGraph.CreateTexture(desc);

            TextureHandle bloomPrefilterTexture = default;
            if (bloomActive)
            {
                var bloomDesc = desc;
                bloomDesc.width = Mathf.Max(1, bloomDesc.width / 2);
                bloomDesc.height = Mathf.Max(1, bloomDesc.height / 2);
                bloomDesc.name = "_PPBloomPrefilter";
                bloomDesc.filterMode = FilterMode.Bilinear;
                bloomPrefilterTexture = renderGraph.CreateTexture(bloomDesc);

                for (int i = 0; i < bloomLevels; i++)
                {
                    bloomDesc.width = Mathf.Max(1, bloomDesc.width / 2);
                    bloomDesc.height = Mathf.Max(1, bloomDesc.height / 2);
                    bloomDesc.name = BloomDownNames[i];
                    _bloomDownTextures[i] = renderGraph.CreateTexture(bloomDesc);
                }

                for (int i = PostProcessRuntimeState.DiagnosticUnfusedBloom ? 0 : 1; i < bloomLevels; i++)
                {
                    var bloomUpDesc = desc;
                    bloomUpDesc.width = Mathf.Max(1, bloomUpDesc.width >> (i + 1));
                    bloomUpDesc.height = Mathf.Max(1, bloomUpDesc.height >> (i + 1));
                    bloomUpDesc.name = BloomUpNames[i];
                    bloomUpDesc.filterMode = FilterMode.Bilinear;
                    _bloomUpTextures[i] = renderGraph.CreateTexture(bloomUpDesc);
                }
            }

            Vector4 screenToEmission = PostProcessPassDataAssembler.ComputeScreenToEmission(cameraData.camera, bloomActive);

            using (var builder = renderGraph.AddUnsafePass<PostProcessPassData>(PassName, out var passData, profilingSampler))
            {
                passData.PostProcessCS = _postProcessCS;
                passData.KernelPrefilter = _kernelPrefilter;
                passData.KernelDownsample = _kernelDownsample;
                passData.KernelUpsample = _kernelUpsample;
                passData.KernelComposite = _kernelComposite;
                passData.KernelUpsampleComposite = _kernelUpsampleComposite;
                passData.UnfusedBloom = PostProcessRuntimeState.DiagnosticUnfusedBloom;
                passData.BloomStorageFormat = desc.colorFormat switch
                {
                    GraphicsFormat.R16G16B16A16_SFloat => 1,
                    GraphicsFormat.B10G11R11_UFloatPack32 => 2,
                    GraphicsFormat.R32G32B32A32_SFloat => 0,
                    _ when !bloomActive => 0,
                    _ => throw new InvalidOperationException($"Unsupported bloom storage format: {desc.colorFormat}"),
                };

                passData.ColorTexture = activeColor;
                passData.IntermediateTexture = intermediateTexture;
                passData.BloomPrefilterTexture = bloomPrefilterTexture;
                passData.BloomDownTextures = _bloomDownTextures;
                passData.BloomUpTextures = _bloomUpTextures;
                passData.Width = width;
                passData.Height = height;

                passData.IsDisplayPass = _displayPass;
                passData.DiagnosticsActive = diagnosticsActive;
                passData.ScreenToEmission = screenToEmission;

                PostProcessPassDataAssembler.FillPassComponents(
                    passData,
                    bloom,
                    vignette,
                    eigengrau,
                    bloomActive,
                    bloomLevels,
                    vignetteActive,
                    eigengrauActive,
                    _displayPass,
                    hdrOutput,
                    hdrGamut,
                    paperWhite,
                    peakNits);
                passData.PostDebugView = _displayPass ? (int)PostProcessRuntimeState.DebugView : 0;
                passData.CompareSplit = PostProcessRuntimeState.CompareSplit;
                passData.CompareMode = (int)PostProcessRuntimeState.CompareMode;
                passData.CompareBefore = PostProcessRuntimeState.CompareBefore;
                PostProcessPassDataAssembler.FillLut(passData, lutActive);

                passData.FrameIndex = Time.frameCount;
                passData.CalibrationPattern = (int)PostProcessRuntimeState.CalibrationMode;
                passData.CalibrationValue = PostProcessRuntimeState.CalibrationValue;

                // Готовый кадр лежит в промежуточной текстуре. Если цель камеры —
                // не экран, копировать его обратно не нужно: промежуточная
                // текстура сама становится цветом камеры. Это одно полноэкранное
                // копирование на каждый из двух проходов.
                passData.SwapColor = !resourceData.isActiveTargetBackBuffer;
                builder.UseTexture(
                    passData.ColorTexture,
                    passData.SwapColor ? AccessFlags.Read : AccessFlags.ReadWrite);
                builder.UseTexture(passData.IntermediateTexture, AccessFlags.ReadWrite);

                if (passData.BloomActive)
                {
                    builder.UseTexture(passData.BloomPrefilterTexture, AccessFlags.ReadWrite);
                    for (int i = 0; i < passData.BloomLevels; i++)
                    {
                        builder.UseTexture(passData.BloomDownTextures[i], AccessFlags.ReadWrite);
                        if (i > 0 || passData.UnfusedBloom)
                        {
                            builder.UseTexture(passData.BloomUpTextures[i], AccessFlags.ReadWrite);
                        }
                    }
                }

                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PostProcessPassData data, UnsafeGraphContext context) => PostProcessPassExecutor.Render(data, context));
                if (passData.SwapColor)
                {
                    resourceData.cameraColor = intermediateTexture;
                }
            }
        }
    }
}

#nullable enable

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Kern.Rendering.PostProcessing.Scopes;

internal sealed class ScopesRenderPass : ScriptableRenderPass2D
{
    private const string PassName = "ComputeScopesPass";
    private const float CaptureIntervalSeconds = 0.2f;

    private readonly ComputeShader _scopesCS;
    private readonly int _kernelClear;
    private readonly int _kernelGather;
    private readonly int _kernelHistogram;
    private readonly int _kernelWaveform;
    private readonly int _kernelVectorscope;
    private readonly ScopeResources _resources = new();
    private float _nextCaptureTime;
    private string? _failure;

    private static bool s_enabled;
    private static ScopesSourceMode s_sourceMode = ScopesSourceMode.After;
    private static ScopeWaveformMode s_waveformMode = ScopeWaveformMode.Overlay;
    private static int s_histogramMode;
    private static float s_vectorscopeScale = 1f;
    private static bool s_showSkinToneLine = true;

    // Проход не резолвится контейнером — он принадлежит renderer asset.
    // Состояние в него ТОЛКАЮТ (Enabled, режимы), но
    // приборы надо ТЯНУТЬ: их считает GPU, а показывает интерфейс. Поэтому
    // живой проход публикует себя здесь. Это не синглтон-точка доступа к
    // логике: наружу видны только три текстуры, и записать сюда нельзя.
    private static ScopesRenderPass? s_live;

    public ScopesRenderPass(ComputeShader scopesCS)
    {
        ApplyRenderPassEvent();
        _scopesCS = scopesCS;
        _kernelClear = _scopesCS.FindKernel("ScopesClear");
        _kernelGather = _scopesCS.FindKernel("ScopesGather");
        _kernelHistogram = _scopesCS.FindKernel("HistogramResolve");
        _kernelWaveform = _scopesCS.FindKernel("WaveformResolve");
        _kernelVectorscope = _scopesCS.FindKernel("VectorscopeResolve");
        s_live = this;
    }

    public static RenderTexture? LiveHistogram => s_live?._resources.HistogramTexture;

    public static RenderTexture? LiveWaveform => s_live?._resources.WaveformTexture;

    public static RenderTexture? LiveVectorscope => s_live?._resources.VectorscopeTexture;

    public static bool Available => s_live != null && s_live._failure == null;

    public static string? FailureMessage => s_live?._failure;

    public static uint ClippedBlackSamples => s_live?._resources.ClippedBlackSamples ?? 0u;

    public static uint ClippedHighlightSamples => s_live?._resources.ClippedHighlightSamples ?? 0u;

    public static float MedianExposureStops => s_live?._resources.MedianExposureStops ?? float.NaN;

    public static float P95ExposureStops => s_live?._resources.P95ExposureStops ?? float.NaN;

    public static ScopesSourceMode SourceMode
    {
        get => s_sourceMode;
        set
        {
            s_sourceMode = value is ScopesSourceMode.Before or ScopesSourceMode.After
                ? value
                : ScopesSourceMode.After;
            s_live?.ApplyRenderPassEvent();
        }
    }

    public static ScopeWaveformMode WaveformMode
    {
        get => s_waveformMode;
        set => s_waveformMode = value is ScopeWaveformMode.Overlay or ScopeWaveformMode.Parade or ScopeWaveformMode.Luma
            ? value
            : ScopeWaveformMode.Overlay;
    }

    public static int HistogramMode
    {
        get => s_histogramMode;
        set => s_histogramMode = Mathf.Clamp(value, 0, 2);
    }

    public static float VectorscopeScale
    {
        get => s_vectorscopeScale;
        set => s_vectorscopeScale = Mathf.Clamp(value, 0.5f, 2f);
    }

    public static bool ShowSkinToneLine
    {
        get => s_showSkinToneLine;
        set => s_showSkinToneLine = value;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlaySession()
    {
        s_enabled = false;
        s_sourceMode = ScopesSourceMode.After;
        s_waveformMode = ScopeWaveformMode.Overlay;
        s_histogramMode = 0;
        s_vectorscopeScale = 1f;
        s_showSkinToneLine = true;
        if (s_live != null)
        {
            s_live._failure = null;
            s_live._nextCaptureTime = 0f;
            s_live._resources.Dispose();
        }

        s_live = null;
    }

    public static bool Enabled
    {
        get => s_enabled;
        set
        {
            if (s_enabled == value)
            {
                return;
            }

            s_enabled = value;
            if (value && s_live != null)
            {
                s_live._failure = null;
                s_live._nextCaptureTime = 0f;
            }
            else if (!value && s_live != null)
            {
                s_live._resources.Dispose();
            }
        }
    }

    private void ApplyRenderPassEvent()
    {
        // After samples Kern's DisplayFinal result. In HDR it is still linear
        // absolute-nit color; URP's final PQ/scRGB encoding and the physical
        // display are downstream, so this is a signal meter, not a photometer.
        renderPassEvent = s_sourceMode == ScopesSourceMode.Before
            ? RenderPassEvent.BeforeRenderingPostProcessing
            : RenderPassEvent.AfterRenderingPostProcessing;
        renderPassEvent2D = s_sourceMode == ScopesSourceMode.Before
            ? RenderPassEvent2D.BeforeRenderingPostProcessing
            : RenderPassEvent2D.AfterRenderingPostProcessing;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (!s_enabled)
        {
            return;
        }

        // При отключённом Domain Reload статическое поле очищается через
        // SubsystemRegistration, а экземпляр renderer feature может
        // пережить вход в Play Mode. Возвращаем живую ссылку до чтения
        // текстур интерфейсом.
        s_live = this;
        if (_failure != null)
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

        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
        TextureHandle activeColor = resourceData.activeColorTexture;
        if (!activeColor.IsValid())
        {
            return;
        }

        // Ограничитель частоты ставится только после выбора настоящей
        // игровой камеры. Scene View или overlay-камера могут пройти
        // через feature раньше Base-камеры; если занять слот на них,
        // приборы будут пропускать валидный кадр и выглядеть зависшими.
        float now = Time.unscaledTime;
        if (now < _nextCaptureTime)
        {
            return;
        }

        _nextCaptureTime = now + CaptureIntervalSeconds;

        try
        {
            _resources.EnsureAllocated();
        }
        catch (System.Exception exception)
        {
            _failure = exception.Message;
            Debug.LogError(
                $"[ScopesRenderPass] Приборы остановлены: {exception.Message}");
            return;
        }

        using var builder = renderGraph.AddUnsafePass<ScopesPassData>(
            PassName, out ScopesPassData passData, profilingSampler);

        passData.ScopesCS = _scopesCS;
        passData.KernelClear = _kernelClear;
        passData.KernelGather = _kernelGather;
        passData.KernelHistogram = _kernelHistogram;
        passData.KernelWaveform = _kernelWaveform;
        passData.KernelVectorscope = _kernelVectorscope;
        passData.Resources = _resources;
        passData.SourceTexture = activeColor;
        passData.WaveformMode = (int)s_waveformMode;
        passData.HistogramMode = s_histogramMode;
        passData.VectorscopeScale = s_vectorscopeScale;
        passData.ShowSkinToneLine = s_showSkinToneLine;
        passData.HDROutput = cameraData.isHDROutputActive;
        passData.HDRGamut = passData.HDROutput ? cameraData.hdrDisplayColorGamut : ColorGamut.sRGB;
        TextureDesc sourceDescriptor = activeColor.GetDescriptor(renderGraph);
        RenderTextureDescriptor cameraDescriptor = cameraData.cameraTargetDescriptor;
        passData.SourceWidth = Mathf.Max(
            1,
            sourceDescriptor.sizeMode == TextureSizeMode.Explicit
                ? sourceDescriptor.width
                : cameraDescriptor.width);
        passData.SourceHeight = Mathf.Max(
            1,
            sourceDescriptor.sizeMode == TextureSizeMode.Explicit
                ? sourceDescriptor.height
                : cameraDescriptor.height);
        if (passData.HDROutput)
        {
            Tonemapping output = VolumeManager.instance.stack.GetComponent<Tonemapping>();
            float maxNits = Mathf.Max(1f, output.maxNits.value);
            float paperWhite = Mathf.Max(1f, output.paperWhite.value);
            float postExposure = Mathf.Pow(2f, VolumeManager.instance.stack
                .GetComponent<ColorAdjustments>().postExposure.value);
            passData.SignalScale = s_sourceMode == ScopesSourceMode.Before
                ? paperWhite * postExposure / maxNits
                : 1f / maxNits;
            passData.ExposureScale = s_sourceMode == ScopesSourceMode.Before
                ? postExposure
                : 1f / paperWhite;
        }
        else
        {
            float postExposure = Mathf.Pow(2f, VolumeManager.instance.stack
                .GetComponent<ColorAdjustments>().postExposure.value);
            passData.SignalScale = s_sourceMode == ScopesSourceMode.Before ? postExposure : 1f;
            passData.ExposureScale = passData.SignalScale;
        }

        builder.UseTexture(activeColor, AccessFlags.Read);
        builder.AllowPassCulling(false);
        builder.SetRenderFunc(
            static (ScopesPassData data, UnsafeGraphContext context) =>
                ScopesPassExecutor.Render(data, context));
    }

    public void Dispose()
    {
        if (ReferenceEquals(s_live, this))
        {
            s_live = null;
        }

        _resources.Dispose();
    }
}

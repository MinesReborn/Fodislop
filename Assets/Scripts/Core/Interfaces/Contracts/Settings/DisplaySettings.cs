#nullable enable

using System;
using Kern.Rendering.PostProcessing;

namespace Kern.Core;

[Serializable]
public sealed class DisplaySettings
{
    public const float PaperWhiteMin = 100f;
    public const float PaperWhiteMax = 400f;
    public const float PeakBrightnessMin = 400f;
    public const float PeakBrightnessMax = 2000f;

    // Дефолти — авторська калібровка, єдиний дім у PostProcessLook.
    public const float DefaultPaperWhite = PostProcessLook.DisplayCalibration.PaperWhiteNits;
    public const float DefaultPeakBrightness = PostProcessLook.DisplayCalibration.PeakBrightnessNits;

    // Ноль означает «разрешение не выбрано, взять родное». Диапазон поэтому
    // не отрезок, а «ноль либо 320..16384», и проверяется отдельно в
    // ClientConfigValidator вместе с парностью ширины и высоты.
    [SettingUnbounded("Ноль или 320..16384; проверяется в паре с высотой.")]
    [SettingConsumer(SettingConsumerTarget.DisplayManager, "DisplayManager.SetResolution / Screen.SetResolution")]
    public int ResolutionWidth;

    [SettingUnbounded("Ноль или 320..16384; проверяется в паре с шириной.")]
    [SettingConsumer(SettingConsumerTarget.DisplayManager, "DisplayManager.SetResolution / Screen.SetResolution")]
    public int ResolutionHeight;

    [SettingRange(0f, 1000f)]
    [SettingConsumer(SettingConsumerTarget.DisplayManager, "DisplayManager.SetResolution / Screen.SetResolution")]
    public int RefreshRate;

    [SettingUnbounded("Индекс режима окна FullScreenMode.")]
    [SettingLabel("menu.settings.fullscreen")]
    [SettingConsumer(SettingConsumerTarget.DisplayManager, "DisplayManager.SetResolution / Screen.fullScreenMode")]
    public int FullScreenMode = 1;

    [SettingUnbounded("Тумблер вертикальной синхронизации.")]
    [SettingLabel("menu.settings.vsync")]
    [SettingConsumer(SettingConsumerTarget.DisplayManager, "DisplayManager.SetVSync / QualitySettings.vSyncCount")]
    public bool VSync = true;

    [SettingUnbounded("Тумблер HDR-вывода.")]
    [SettingLabel("menu.settings.hdr")]
    [SettingConsumer(SettingConsumerTarget.DisplayManager, "DisplayManager.SetHDREnabled / HDROutput")]
    public bool HDREnabled = ProjectRuntimeContracts.ClientConfiguration.DefaultHDREnabled;

    // −1 означает «без ограничения». Отрезком это не выражается.
    [SettingUnbounded("Минус единица либо 30..1000; проверяется отдельно.")]
    [SettingConsumer(SettingConsumerTarget.DisplayManager, "DisplayManager.SetVSync / Application.targetFrameRate")]
    public int TargetFrameRate = -1;

    [SettingUnbounded("Режим выборки — перечисление; проверяется на определённость.")]
    [SettingConsumer(SettingConsumerTarget.DisplayManager, "DisplayManager.SetPixelSamplingMode -> CameraFollow + Shader.SetGlobalFloat(_PixelArtFiltering)")]
    public PixelSamplingMode PixelSampling = PixelSamplingMode.SmoothFiltered;

    [SettingRange(PaperWhiteMin, PaperWhiteMax)]
    [SettingLabel("settings.display.paper_white")]
    [SettingConsumer(SettingConsumerTarget.DisplayManager, "DisplayManager.SetPaperWhiteNits / PostProcessRuntimeState.SetDisplayCalibration")]
    public float PaperWhiteNits = DefaultPaperWhite;

    [SettingRange(PeakBrightnessMin, PeakBrightnessMax)]
    [SettingLabel("settings.display.peak_brightness")]
    [SettingConsumer(SettingConsumerTarget.DisplayManager, "DisplayManager.SetPeakBrightnessNits / PostProcessRuntimeState.SetDisplayCalibration")]
    public float PeakBrightnessNits = DefaultPeakBrightness;
}

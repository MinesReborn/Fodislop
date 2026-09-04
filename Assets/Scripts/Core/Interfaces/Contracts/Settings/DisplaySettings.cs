#nullable enable

using System;
using UnityEngine.Serialization;

namespace Fodinae.Core;

/// <summary>Экран: режим, кадры, калибровка вывода.</summary>
[Serializable]
public sealed class DisplaySettings
{
    public const float GammaMin = 1.8f;
    public const float GammaMax = 2.6f;
    public const float DefaultGamma = 2.2f;
    public const float PaperWhiteMin = 100f;
    public const float PaperWhiteMax = 500f;
    public const float DefaultPaperWhite = 200f;
    public const float PeakBrightnessMin = 400f;
    public const float PeakBrightnessMax = 2000f;
    public const float DefaultPeakBrightness = 1000f;

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
    [FormerlySerializedAs("HdrEnabled")]
    [SettingConsumer(SettingConsumerTarget.DisplayManager, "DisplayManager.SetHDREnabled / HDROutput")]
    public bool HDREnabled = ProjectRuntimeContracts.ClientConfiguration.DefaultHDREnabled;

    // −1 означает «без ограничения». Отрезком это не выражается.
    [SettingUnbounded("Минус единица либо 30..1000; проверяется отдельно.")]
    [SettingConsumer(SettingConsumerTarget.DisplayManager, "DisplayManager.SetVSync / Application.targetFrameRate")]
    public int TargetFrameRate = -1;

    [SettingRange(GammaMin, GammaMax)]
    [SettingLabel("settings.display.gamma")]
    [SettingConsumer(SettingConsumerTarget.DisplayManager, "DisplayManager.SetGamma / PostProcessRenderPass.SetDisplayCalibration")]
    public float Gamma = DefaultGamma;

    [SettingRange(PaperWhiteMin, PaperWhiteMax)]
    [SettingLabel("settings.display.paper_white")]
    [SettingConsumer(SettingConsumerTarget.DisplayManager, "DisplayManager.SetPaperWhiteNits / PostProcessRenderPass.SetDisplayCalibration")]
    public float PaperWhiteNits = DefaultPaperWhite;

    [SettingRange(PeakBrightnessMin, PeakBrightnessMax)]
    [SettingLabel("settings.display.peak_brightness")]
    [SettingConsumer(SettingConsumerTarget.DisplayManager, "DisplayManager.SetPeakBrightnessNits / PostProcessRenderPass.SetDisplayCalibration")]
    public float PeakBrightnessNits = DefaultPeakBrightness;
}

#nullable enable

using System;
using Kern.Rendering.PostProcessing;

namespace Kern.Core;

[Serializable]
public sealed class PostProcessSettings
{
    public const float ExposureMin = -2f;
    public const float ExposureMax = 2f;
    public const float ContrastMin = -0.5f;
    public const float ContrastMax = 0.5f;
    public const float SaturationMin = 0f;
    public const float SaturationMax = 2f;

    // Дефолти — авторський вигляд, єдиний дім у PostProcessLook.
    public const float DefaultExposure = PostProcessLook.ColorGrading.Exposure;
    public const float DefaultContrast = PostProcessLook.ColorGrading.Contrast;
    public const float DefaultSaturation = PostProcessLook.ColorGrading.Saturation;

    [SettingRange(ExposureMin, ExposureMax)]
    [SettingLabel("settings.effects.exposure")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "PostProcessController.Exposure")]
    public float Exposure = DefaultExposure;

    [SettingRange(ContrastMin, ContrastMax)]
    [SettingLabel("settings.effects.contrast")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "PostProcessController.Contrast")]
    public float Contrast = DefaultContrast;

    [SettingRange(SaturationMin, SaturationMax)]
    [SettingLabel("settings.effects.saturation")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "PostProcessController.Saturation")]
    public float Saturation = DefaultSaturation;
}

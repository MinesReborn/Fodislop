#nullable enable

using System;

namespace Fodinae.Core;

/// <summary>
/// Калибровка вывода: экспозиция и цветовой отклик.
/// </summary>
/// <remarks>
/// Это не художественные параметры эффектов — те авторские и лежат в
/// <c>PostProcessLook</c>. Здесь ровно четыре величины, которыми игрок
/// подгоняет кадр под свой монитор. Нейтральные значения — точный no-op.
/// </remarks>
[Serializable]
public sealed class PostProcessSettings
{
    public const float ExposureMin = -2f;
    public const float ExposureMax = 2f;
    public const float ContrastMin = -0.5f;
    public const float ContrastMax = 0.5f;
    public const float SaturationMin = 0f;
    public const float SaturationMax = 2f;
    public const float ToneMappingWhitePointMin = 0.25f;
    public const float ToneMappingWhitePointMax = 8f;
    public const float DefaultExposure = 0f;
    public const float DefaultContrast = 0f;
    public const float DefaultSaturation = 1f;
    public const float DefaultToneMappingWhitePoint = 1f;

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

    [SettingRange(ToneMappingWhitePointMin, ToneMappingWhitePointMax)]
    [SettingLabel("settings.effects.tone_mapping_white_point")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "PostProcessController.ToneMappingWhitePoint")]
    public float ToneMappingWhitePoint = DefaultToneMappingWhitePoint;
}

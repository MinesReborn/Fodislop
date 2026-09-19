#nullable enable

using System;

namespace Kern.Core;

[Serializable]
public sealed class AccessibilitySettings
{
    [SettingRange(0f, 4f)]
    [SettingLabel("gateway.onb.colorblind_label")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "PostProcessRenderPass color grading lut")]
    public int ColorblindMode;

    [SettingUnbounded("Тумблер снижения светочувствительной нагрузки.")]
    [SettingConsumer(SettingConsumerTarget.PostProcessController, "PostProcessController.ApplyClientConfig photosensitivity clamp")]
    public bool ReducePhotosensitivity;
}

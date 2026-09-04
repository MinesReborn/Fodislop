#nullable enable

using System;

namespace Fodinae.Core;

/// <summary>Интерфейс: масштаб, язык, схема управления.</summary>
[Serializable]
public sealed class InterfaceSettings
{
    public const float UIScaleMin = 0.5f;
    public const float UIScaleMax = 2f;

    [SettingRange(UIScaleMin, UIScaleMax)]
    [SettingLabel("menu.settings.ui_scale")]
    [SettingConsumer(SettingConsumerTarget.UserInterface, "PauseMenu / UIDocument panelSettings.scale")]
    public float UIScale = 1f;

    [SettingUnbounded("Код языка из перечня локализаций; проверяется списком.")]
    [SettingLabel("settings.interface.language")]
    [SettingConsumer(SettingConsumerTarget.LocalizationService, "LocalizationService.SetLanguage")]
    public string Language = "ru";

    [SettingRange(0f, 1f)]
    [SettingLabel("gateway.onb.controls_scheme_label")]
    [SettingConsumer(SettingConsumerTarget.Gameplay, "Controls / Onboarding scheme")]
    public int ControlScheme;
}

#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Localization;
using Fodinae.Rendering;
using Fodinae.Rendering.PostProcessing;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI;

/// <summary>
/// Builds the Effects tab in the Pause Menu.
/// </summary>
internal sealed class PauseMenuEffectsTabBuilder
{
    private readonly GraphicsSettingsController _graphicsSettings;
    private readonly PostProcessController _postProcessController;
    private readonly IClientConfigManager _clientConfig;
    private readonly ICollection<Action> _refreshers;
    private readonly ILocalizationService _loc;

    public PauseMenuEffectsTabBuilder(
        GraphicsSettingsController graphicsSettings,
        PostProcessController postProcessController,
        IClientConfigManager clientConfig,
        ICollection<Action> refreshers,
        ILocalizationService loc)
    {
        _graphicsSettings = graphicsSettings;
        _postProcessController = postProcessController;
        _clientConfig = clientConfig;
        _refreshers = refreshers;
        _loc = loc;
    }

    public VisualElement Build(ScrollView effectsScroll)
    {
        VisualElement postProcessSection = effectsScroll.Q<VisualElement>("EffectsSection") ??
            throw new InvalidOperationException("[PauseMenu] EffectsSection is missing from PauseMenu.uxml.");
        VisualElement bloomGroup = effectsScroll.Q<VisualElement>("EffectsGroupBloom") ??
            throw new InvalidOperationException("[PauseMenu] EffectsGroupBloom is missing from PauseMenu.uxml.");
        VisualElement cameraGroup = effectsScroll.Q<VisualElement>("EffectsGroupCamera") ??
            throw new InvalidOperationException("[PauseMenu] EffectsGroupCamera is missing from PauseMenu.uxml.");
        VisualElement detailGroup = effectsScroll.Q<VisualElement>("EffectsGroupDetail") ??
            throw new InvalidOperationException("[PauseMenu] EffectsGroupDetail is missing from PauseMenu.uxml.");
        VisualElement opticsGroup = effectsScroll.Q<VisualElement>("EffectsGroupOptics") ??
            throw new InvalidOperationException("[PauseMenu] EffectsGroupOptics is missing from PauseMenu.uxml.");
        VisualElement atmosphereGroup = effectsScroll.Q<VisualElement>("EffectsGroupAtmosphere") ??
            throw new InvalidOperationException("[PauseMenu] EffectsGroupAtmosphere is missing from PauseMenu.uxml.");
        VisualElement displayGroup = effectsScroll.Q<VisualElement>("EffectsGroupDisplay") ??
            throw new InvalidOperationException("[PauseMenu] EffectsGroupDisplay is missing from PauseMenu.uxml.");
        VisualElement temporalGroup = effectsScroll.Q<VisualElement>("EffectsGroupTemporal") ??
            throw new InvalidOperationException("[PauseMenu] EffectsGroupTemporal is missing from PauseMenu.uxml.");

        _postProcessController.EnsureVolumeSetup();

        // Базовая калибровка вывода остаётся компактной и настраиваемой.
        // Сила отдельных художественных эффектов задаётся PostProcessLook,
        // а игрок решает, платить ли за эффект, через тумблеры ниже.
        Toggle Switch(string key, Func<bool> read, Action<ClientConfig, bool> write) =>
            PauseMenuUIFactory.CreateBoundToggle(
                _loc.Get(key),
                read,
                value => _graphicsSettings.UpdatePostProcessSettings(
                    config => write(config, value)),
                _refreshers);

        ClientConfig Cfg() => _clientConfig.Config;

        BindSlider(
            cameraGroup,
            "PostProcessExposure",
            "PostProcessExposureLabel",
            "settings.effects.exposure",
            () => Cfg().PostProcess.Exposure,
            (config, value) => config.PostProcess.Exposure = value);
        BindSlider(
            cameraGroup,
            "PostProcessContrast",
            "PostProcessContrastLabel",
            "settings.effects.contrast",
            () => Cfg().PostProcess.Contrast,
            (config, value) => config.PostProcess.Contrast = value);
        BindSlider(
            cameraGroup,
            "PostProcessSaturation",
            "PostProcessSaturationLabel",
            "settings.effects.saturation",
            () => Cfg().PostProcess.Saturation,
            (config, value) => config.PostProcess.Saturation = value);
        BindSlider(
            cameraGroup,
            "PostProcessWhitePoint",
            "PostProcessWhitePointLabel",
            "settings.effects.tone_mapping_white_point",
            () => Cfg().PostProcess.ToneMappingWhitePoint,
            (config, value) => config.PostProcess.ToneMappingWhitePoint = value);

        bloomGroup.Add(Switch(
            "settings.effects.bloom",
            () => Cfg().BloomEnabled,
            (config, value) => config.BloomEnabled = value));

        cameraGroup.Add(Switch(
            "settings.effects.vignette",
            () => Cfg().VignetteEnabled,
            (config, value) => config.VignetteEnabled = value));
        cameraGroup.Add(Switch(
            "settings.effects.chromatic_aberration",
            () => Cfg().ChromaticAberrationEnabled,
            (config, value) => config.ChromaticAberrationEnabled = value));
        cameraGroup.Add(Switch(
            "settings.effects.grain",
            () => Cfg().FilmGrainEnabled,
            (config, value) => config.FilmGrainEnabled = value));
        cameraGroup.Add(Switch(
            "settings.effects.motion_blur",
            () => Cfg().MotionBlurEnabled,
            (config, value) => config.MotionBlurEnabled = value));

        detailGroup.Add(Switch(
            "settings.effects.local_sharpness",
            () => Cfg().LocalContrastEnabled,
            (config, value) => config.LocalContrastEnabled = value));

        opticsGroup.Add(Switch(
            "settings.effects.anamorphic_beams",
            () => Cfg().LensEffectsEnabled,
            (config, value) => config.LensEffectsEnabled = value));

        atmosphereGroup.Add(Switch(
            "settings.effects.glow_dust",
            () => Cfg().AtmosphereEnabled,
            (config, value) => config.AtmosphereEnabled = value));

        displayGroup.Add(Switch(
            "settings.effects.phosphor_pattern",
            () => Cfg().DisplayPhysicsEnabled,
            (config, value) => config.DisplayPhysicsEnabled = value));

        temporalGroup.Add(Switch(
            "settings.effects.phosphor_afterglow",
            () => Cfg().TemporalEnabled,
            (config, value) => config.TemporalEnabled = value));

        return effectsScroll;
    }

    private void BindSlider(
        VisualElement root,
        string sliderName,
        string labelName,
        string localizationKey,
        Func<float> read,
        Action<ClientConfig, float> write)
    {
        Slider slider = root.Q<Slider>(sliderName) ??
            throw new InvalidOperationException(
                $"[PauseMenu] {sliderName} is missing from PauseMenu.uxml.");
        Label label = root.Q<Label>(labelName) ??
            throw new InvalidOperationException(
                $"[PauseMenu] {labelName} is missing from PauseMenu.uxml.");
        string localizedLabel = _loc.Get(localizationKey);

        void Refresh()
        {
            float value = PauseMenuUIFactory.SnapValue(read(), slider.lowValue, slider.highValue);
            slider.SetValueWithoutNotify(value);
            label.text = $"{localizedLabel}: {value:F2}";
        }

        slider.RegisterValueChangedCallback(evt =>
        {
            float snapped = PauseMenuUIFactory.SnapValue(evt.newValue, slider.lowValue, slider.highValue);
            if (!Mathf.Approximately(snapped, evt.newValue))
            {
                slider.SetValueWithoutNotify(snapped);
            }

            label.text = $"{localizedLabel}: {snapped:F2}";
            _graphicsSettings.UpdatePostProcessSettings(
                config => write(config, snapped));
        });
        _refreshers.Add(Refresh);
        Refresh();
    }
}

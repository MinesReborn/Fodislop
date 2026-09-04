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
        Toggle Switch(string fieldName, Func<bool> read, Action<ClientConfig, bool> write) =>
            PauseMenuUIFactory.CreateBoundToggle(
                _loc.Get(SettingSchema.LabelOf<EffectSettings>(fieldName)),
                read,
                value => _graphicsSettings.UpdatePostProcessSettings(
                    config => write(config, value)),
                _refreshers);

        ClientConfig Cfg() => _clientConfig.Config;

        VisualElement BoundCameraSlider(
            string propertyName,
            Func<float> read,
            Action<ClientConfig, float> write) =>
            PauseMenuUIFactory.CreateBoundSlider<PostProcessSettings>(
                propertyName,
                _loc,
                read,
                value => _graphicsSettings.UpdatePostProcessSettings(config => write(config, value)),
                _refreshers);

        cameraGroup.Add(BoundCameraSlider(
            nameof(PostProcessSettings.Exposure),
            () => Cfg().PostProcess.Exposure,
            (config, value) => config.PostProcess.Exposure = value));
        cameraGroup.Add(BoundCameraSlider(
            nameof(PostProcessSettings.Contrast),
            () => Cfg().PostProcess.Contrast,
            (config, value) => config.PostProcess.Contrast = value));
        cameraGroup.Add(BoundCameraSlider(
            nameof(PostProcessSettings.Saturation),
            () => Cfg().PostProcess.Saturation,
            (config, value) => config.PostProcess.Saturation = value));
        cameraGroup.Add(Switch(
            nameof(EffectSettings.ToneMappingEnabled),
            () => Cfg().Effects.ToneMappingEnabled,
            (config, value) => config.Effects.ToneMappingEnabled = value));
        cameraGroup.Add(BoundCameraSlider(
            nameof(PostProcessSettings.ToneMappingWhitePoint),
            () => Cfg().PostProcess.ToneMappingWhitePoint,
            (config, value) => config.PostProcess.ToneMappingWhitePoint = value));

        bloomGroup.Add(Switch(
            nameof(EffectSettings.BloomEnabled),
            () => Cfg().Effects.BloomEnabled,
            (config, value) => config.Effects.BloomEnabled = value));

        cameraGroup.Add(Switch(
            nameof(EffectSettings.VignetteEnabled),
            () => Cfg().Effects.VignetteEnabled,
            (config, value) => config.Effects.VignetteEnabled = value));
        cameraGroup.Add(Switch(
            nameof(EffectSettings.ChromaticAberrationEnabled),
            () => Cfg().Effects.ChromaticAberrationEnabled,
            (config, value) => config.Effects.ChromaticAberrationEnabled = value));
        cameraGroup.Add(Switch(
            nameof(EffectSettings.FilmGrainEnabled),
            () => Cfg().Effects.FilmGrainEnabled,
            (config, value) => config.Effects.FilmGrainEnabled = value));
        cameraGroup.Add(Switch(
            nameof(EffectSettings.MotionBlurEnabled),
            () => Cfg().Effects.MotionBlurEnabled,
            (config, value) => config.Effects.MotionBlurEnabled = value));

        detailGroup.Add(Switch(
            nameof(EffectSettings.LocalContrastEnabled),
            () => Cfg().Effects.LocalContrastEnabled,
            (config, value) => config.Effects.LocalContrastEnabled = value));

        opticsGroup.Add(Switch(
            nameof(EffectSettings.LensEffectsEnabled),
            () => Cfg().Effects.LensEffectsEnabled,
            (config, value) => config.Effects.LensEffectsEnabled = value));

        atmosphereGroup.Add(Switch(
            nameof(EffectSettings.AtmosphereEnabled),
            () => Cfg().Effects.AtmosphereEnabled,
            (config, value) => config.Effects.AtmosphereEnabled = value));

        displayGroup.Add(Switch(
            nameof(EffectSettings.DisplayPhysicsEnabled),
            () => Cfg().Effects.DisplayPhysicsEnabled,
            (config, value) => config.Effects.DisplayPhysicsEnabled = value));

        temporalGroup.Add(Switch(
            nameof(EffectSettings.TemporalEnabled),
            () => Cfg().Effects.TemporalEnabled,
            (config, value) => config.Effects.TemporalEnabled = value));

        return effectsScroll;
    }
}

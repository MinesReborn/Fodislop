#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Localization;
using Fodinae.Game;
using Fodinae.Rendering;
using Fodinae.World.Lighting;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI;

/// <summary>
/// Builds the Advanced tab in the Pause Menu.
/// </summary>
internal sealed class PauseMenuAdvancedTabBuilder
{
    private readonly GraphicsSettingsController _graphicsSettings;
    private readonly LightingEngine _lightingEngine;
    private readonly IClientConfigManager _clientConfig;
    private readonly ILocalPlayerState _localPlayer;
    private readonly ICollection<Action> _refreshers;
    private readonly ILocalizationService _loc;
    private readonly Action _markGraphicsCustom;
#if UNITY_EDITOR || UNITY_ENABLE_CHECKS
    private readonly Action _addLightingDebugControls;
#endif

    public PauseMenuAdvancedTabBuilder(
        GraphicsSettingsController graphicsSettings,
        LightingEngine lightingEngine,
        IClientConfigManager clientConfig,
        ILocalPlayerState localPlayer,
        ICollection<Action> refreshers,
        ILocalizationService loc,
        Action markGraphicsCustom
#if UNITY_EDITOR || UNITY_ENABLE_CHECKS
        , Action addLightingDebugControls
#endif
    )
    {
        _graphicsSettings = graphicsSettings;
        _lightingEngine = lightingEngine;
        _clientConfig = clientConfig;
        _localPlayer = localPlayer;
        _refreshers = refreshers;
        _loc = loc;
        _markGraphicsCustom = markGraphicsCustom;
#if UNITY_EDITOR || UNITY_ENABLE_CHECKS
        _addLightingDebugControls = addLightingDebugControls;
#endif
    }

    public VisualElement Build(ScrollView advancedScroll)
    {
        Foldout advancedGraphicsSection = advancedScroll.Q<Foldout>("AdvancedLightingSection") ??
            throw new InvalidOperationException("[PauseMenu] AdvancedLightingSection is missing from PauseMenu.uxml.");
        VisualElement ambientGroup = advancedGraphicsSection.Q<VisualElement>("AdvancedGroupAmbient") ??
            throw new InvalidOperationException("[PauseMenu] AdvancedGroupAmbient is missing from PauseMenu.uxml.");
        VisualElement dynamicGroup = advancedGraphicsSection.Q<VisualElement>("AdvancedGroupDynamic") ??
            throw new InvalidOperationException("[PauseMenu] AdvancedGroupDynamic is missing from PauseMenu.uxml.");
        VisualElement extinctionGroup = advancedGraphicsSection.Q<VisualElement>("AdvancedGroupExtinction") ??
            throw new InvalidOperationException("[PauseMenu] AdvancedGroupExtinction is missing from PauseMenu.uxml.");
        VisualElement bounceGroup = advancedGraphicsSection.Q<VisualElement>("AdvancedGroupBounce") ??
            throw new InvalidOperationException("[PauseMenu] AdvancedGroupBounce is missing from PauseMenu.uxml.");
        VisualElement aoGroup = advancedGraphicsSection.Q<VisualElement>("AdvancedGroupAO") ??
            throw new InvalidOperationException("[PauseMenu] AdvancedGroupAO is missing from PauseMenu.uxml.");
        VisualElement boundsGroup = advancedGraphicsSection.Q<VisualElement>("AdvancedGroupBounds") ??
            throw new InvalidOperationException("[PauseMenu] AdvancedGroupBounds is missing from PauseMenu.uxml.");
        VisualElement worldMaterialsSection = advancedScroll.Q<VisualElement>("WorldMaterialsSection") ??
            throw new InvalidOperationException("[PauseMenu] WorldMaterialsSection is missing from PauseMenu.uxml.");

        void ApplyLightingSetting(
            float value,
            Action<LightingEngine, float> apply)
        {
            _markGraphicsCustom();
            apply(_lightingEngine, value);
        }

        float GetLightingValue(Func<LightingEngine, float> actualValue)
        {
            return actualValue(_lightingEngine);
        }

        void ApplyLightingColor(
            Color value,
            Action<LightingEngine, Color> apply)
        {
            _markGraphicsCustom();
            apply(_lightingEngine, value);
        }

        ambientGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.advanced.ambient_intensity"),
            () => GetLightingValue(static engine => engine.AmbientIntensity),
            value => ApplyLightingSetting(
                value,
                static (engine, setting) => engine.SetAmbientIntensity(setting)),
            0f,
            1f,
            _refreshers));
        ambientGroup.Add(PauseMenuUIFactory.CreateBoundColorControls(
            _loc.Get("settings.advanced.ambient_color"),
            () => _lightingEngine.AmbientColor,
            value => ApplyLightingColor(
                value,
                static (engine, setting) => engine.SetAmbientColor(setting)),
            0f,
            4f,
            _refreshers));
        ambientGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.advanced.emission_power"),
            () => GetLightingValue(static engine => engine.EmissionScale),
            value => ApplyLightingSetting(
                value,
                static (engine, setting) => engine.SetEmissionScale(setting)),
            0.1f,
            8f,
            _refreshers));

        dynamicGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.advanced.player_emission_power"),
            () => ResolveLocalRobot()?.DynamicLightIntensity ?? 0f,
            value =>
            {
                _markGraphicsCustom();
                ResolveLocalRobot()?.SetDynamicLightIntensity(value);
            },
            0f,
            4f,
            _refreshers));
        dynamicGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.advanced.dynamic_emission_rate"),
            () => _lightingEngine.DynamicLightUpdatesPerSecond,
            value =>
            {
                _markGraphicsCustom();
                _lightingEngine.SetDynamicLightUpdatesPerSecond(value);
            },
            1f,
            LightingConfigLimits.DynamicLightUpdatesPerSecond,
            _refreshers));

        dynamicGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.advanced.light_red"),
            () => ResolveLocalRobot()?.DynamicLightColor.r ?? 0f,
            value =>
            {
                _markGraphicsCustom();
                Robot? localRobot = ResolveLocalRobot();
                if (localRobot == null)
                {
                    return;
                }

                Color color = localRobot.DynamicLightColor;
                localRobot.SetDynamicLightColor(new Color(value, color.g, color.b, 1f));
            },
            0f,
            1f,
            _refreshers));
        dynamicGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.advanced.light_green"),
            () => ResolveLocalRobot()?.DynamicLightColor.g ?? 0f,
            value =>
            {
                _markGraphicsCustom();
                Robot? localRobot = ResolveLocalRobot();
                if (localRobot == null)
                {
                    return;
                }

                Color color = localRobot.DynamicLightColor;
                localRobot.SetDynamicLightColor(new Color(color.r, value, color.b, 1f));
            },
            0f,
            1f,
            _refreshers));
        dynamicGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.advanced.light_blue"),
            () => ResolveLocalRobot()?.DynamicLightColor.b ?? 0f,
            value =>
            {
                _markGraphicsCustom();
                Robot? localRobot = ResolveLocalRobot();
                if (localRobot == null)
                {
                    return;
                }

                Color color = localRobot.DynamicLightColor;
                localRobot.SetDynamicLightColor(new Color(color.r, color.g, value, 1f));
            },
            0f,
            1f,
            _refreshers));

        extinctionGroup.Add(PauseMenuUIFactory.CreateBoundColorControls(
            _loc.Get("settings.advanced.empty_extinction"),
            () => _lightingEngine.EmptyExtinctionRgb,
            value => ApplyLightingColor(
                value,
                static (engine, setting) => engine.SetEmptyExtinctionColor(setting)),
            0f,
            4f,
            _refreshers));
        extinctionGroup.Add(PauseMenuUIFactory.CreateBoundColorControls(
            _loc.Get("settings.advanced.solid_extinction"),
            () => _lightingEngine.SolidExtinctionRgb,
            value => ApplyLightingColor(
                value,
                static (engine, setting) => engine.SetSolidExtinctionColor(setting)),
            0f,
            4f,
            _refreshers));
        extinctionGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.advanced.empty_extinction_falloff"),
            () => GetLightingValue(static engine => engine.EmptyExtinctionMultiplier),
            value => ApplyLightingSetting(
                value,
                static (engine, setting) =>
                    engine.SetEmptyExtinctionMultiplier(setting)),
            0f,
            2f,
            _refreshers));
        extinctionGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.advanced.solid_extinction_falloff"),
            () => GetLightingValue(static engine => engine.SolidExtinctionMultiplier),
            value => ApplyLightingSetting(
                value,
                static (engine, setting) =>
                    engine.SetSolidExtinctionMultiplier(setting)),
            0.25f,
            2f,
            _refreshers));
        bounceGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.advanced.bounce_strength"),
            () => GetLightingValue(static engine => engine.BounceStrength),
            value => ApplyLightingSetting(
                value,
                static (engine, setting) => engine.SetBounceStrength(setting)),
            0f,
            1f,
            _refreshers));
        aoGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.advanced.ao_radius"),
            () => GetLightingValue(static engine => engine.AmbientOcclusionRadiusCells),
            value => ApplyLightingSetting(
                value,
                static (engine, setting) =>
                    engine.SetAmbientOcclusionRadius(setting)),
            0.5f,
            8f,
            _refreshers));
        aoGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.advanced.ao_strength"),
            () => GetLightingValue(static engine => engine.AmbientOcclusionStrength),
            value => ApplyLightingSetting(
                value,
                static (engine, setting) =>
                    engine.SetAmbientOcclusionStrength(setting)),
            0.1f,
            8f,
            _refreshers));
        VisualElement maximumLightMultiplierSlider = PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.advanced.max_light_multiplier"),
            () => GetLightingValue(static engine => engine.MaximumLightMultiplier),
            value => ApplyLightingSetting(
                value,
                static (engine, setting) =>
                    engine.SetMaximumLightMultiplier(setting)),
            0.25f,
            LightingConfigLimits.MaximumLightMultiplier,
            _refreshers);
        void RefreshMaximumLightMultiplierState()
        {
            maximumLightMultiplierSlider.SetEnabled(_lightingEngine.EnableFinalLightingClamp);
        }

        _refreshers.Add(RefreshMaximumLightMultiplierState);
        RefreshMaximumLightMultiplierState();
        boundsGroup.Add(maximumLightMultiplierSlider);
#if UNITY_EDITOR || UNITY_ENABLE_CHECKS
        boundsGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.advanced.transmittance_debug"),
            () => GetLightingValue(static engine => engine.TransmittanceDebugDistanceCells),
            value => ApplyLightingSetting(
                value,
                static (engine, setting) =>
                    engine.SetTransmittanceDebugDistance(setting)),
            2f,
            32f,
            _refreshers));
#endif
        boundsGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.advanced.min_transmission"),
            () => GetLightingValue(static engine => engine.MinimumTransmission),
            value => ApplyLightingSetting(
                value,
                static (engine, setting) => engine.SetMinimumTransmission(setting)),
            0.0001f,
            0.1f,
            _refreshers));
        boundsGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.advanced.light_safe_border"),
            () => _lightingEngine.LightSafeBorder,
            value => ApplyLightingSetting(
                value,
                static (engine, setting) => engine.SetLightSafeBorder(setting)),
            0f,
            8f,
            _refreshers));
        Toggle finalLightingClampToggle = PauseMenuUIFactory.CreateBoundToggle(
            _loc.Get("settings.advanced.clamp_final_light"),
            () => _lightingEngine.EnableFinalLightingClamp,
            value =>
            {
                _markGraphicsCustom();
                _lightingEngine.SetFinalLightingClampEnabled(value);
                RefreshMaximumLightMultiplierState();
            },
            _refreshers);
        boundsGroup.Add(finalLightingClampToggle);

        void SaveShaderSetting(Action<ClientConfig> update)
        {
            _graphicsSettings.UpdateCustomWorldMaterialSettings(update);
        }

        worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.world.shimmer_speed"),
            () => _clientConfig.Config.TerrainShimmerSpeedScale,
            value => SaveShaderSetting(
                config => config.TerrainShimmerSpeedScale = value),
            0f,
            10f,
            _refreshers));
        worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundColorControls(
            _loc.Get("settings.world.shimmer_color"),
            () => _clientConfig.Config.TerrainShimmerColor,
            value => SaveShaderSetting(config => config.TerrainShimmerColor = value),
            0f,
            8f,
            _refreshers));
        worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.world.pulse_speed"),
            () => _clientConfig.Config.TerrainPulseSpeedScale,
            value => SaveShaderSetting(config => config.TerrainPulseSpeedScale = value),
            0f,
            10f,
            _refreshers));
        worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.world.surface_emission"),
            () => _clientConfig.Config.TransitEmissionStrength,
            value => SaveShaderSetting(config => config.TransitEmissionStrength = value),
            0f,
            8f,
            _refreshers));
        worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundColorControls(
            _loc.Get("settings.world.surface_emission_color"),
            () => _clientConfig.Config.TransitEmissionColor,
            value => SaveShaderSetting(config => config.TransitEmissionColor = value),
            0f,
            8f,
            _refreshers));
        worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.world.far_surface_emission"),
            () => _clientConfig.Config.PerspectiveEmissionStrength,
            value => SaveShaderSetting(
                config => config.PerspectiveEmissionStrength = value),
            0f,
            8f,
            _refreshers));
        worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundColorControls(
            _loc.Get("settings.world.far_surface_color"),
            () => _clientConfig.Config.PerspectiveEmissionColor,
            value => SaveShaderSetting(
                config => config.PerspectiveEmissionColor = value),
            0f,
            8f,
            _refreshers));

#if UNITY_EDITOR || UNITY_ENABLE_CHECKS
        _addLightingDebugControls();
#endif

        return advancedScroll;
    }

    private Robot? ResolveLocalRobot()
    {
        return _localPlayer.Current?.GetComponent<Robot>();
    }
}

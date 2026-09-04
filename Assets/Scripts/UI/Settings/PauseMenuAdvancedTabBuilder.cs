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

        ambientGroup.Add(PauseMenuUIFactory.CreateBoundSlider<WorldLightingSettings>(
            nameof(WorldLightingSettings.AmbientIntensity),
            _loc,
            () => GetLightingValue(static engine => engine.AmbientIntensity),
            value => ApplyLightingSetting(
                value,
                static (engine, setting) => engine.SetAmbientIntensity(setting)),
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
        ambientGroup.Add(PauseMenuUIFactory.CreateBoundSlider<WorldLightingSettings>(
            nameof(WorldLightingSettings.EmissionScale),
            _loc,
            () => GetLightingValue(static engine => engine.EmissionScale),
            value => ApplyLightingSetting(
                value,
                static (engine, setting) => engine.SetEmissionScale(setting)),
            _refreshers));

        dynamicGroup.Add(PauseMenuUIFactory.CreateBoundSlider<WorldLightingSettings>(
            nameof(WorldLightingSettings.DynamicLightIntensity),
            _loc,
            () => _lightingEngine.DynamicLightIntensity,
            value =>
            {
                _markGraphicsCustom();
                _lightingEngine.SetDynamicLightSettings(value, _lightingEngine.DynamicLightColor);
                ResolveLocalRobot()?.SetDynamicLightIntensity(value);
            },
            _refreshers));
        dynamicGroup.Add(PauseMenuUIFactory.CreateBoundSlider<WorldLightingSettings>(
            nameof(WorldLightingSettings.DynamicLightUpdatesPerSecond),
            _loc,
            () => _lightingEngine.DynamicLightUpdatesPerSecond,
            value =>
            {
                _markGraphicsCustom();
                _lightingEngine.SetDynamicLightUpdatesPerSecond(value);
            },
            _refreshers));

        dynamicGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.advanced.light_red"),
            () => _lightingEngine.DynamicLightColor.r,
            value =>
            {
                _markGraphicsCustom();
                Color c = _lightingEngine.DynamicLightColor;
                Color newColor = new Color(value, c.g, c.b, 1f);
                _lightingEngine.SetDynamicLightSettings(_lightingEngine.DynamicLightIntensity, newColor);
                ResolveLocalRobot()?.SetDynamicLightColor(newColor);
            },
            0f,
            1f,
            _refreshers));
        dynamicGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.advanced.light_green"),
            () => _lightingEngine.DynamicLightColor.g,
            value =>
            {
                _markGraphicsCustom();
                Color c = _lightingEngine.DynamicLightColor;
                Color newColor = new Color(c.r, value, c.b, 1f);
                _lightingEngine.SetDynamicLightSettings(_lightingEngine.DynamicLightIntensity, newColor);
                ResolveLocalRobot()?.SetDynamicLightColor(newColor);
            },
            0f,
            1f,
            _refreshers));
        dynamicGroup.Add(PauseMenuUIFactory.CreateBoundSlider(
            _loc.Get("settings.advanced.light_blue"),
            () => _lightingEngine.DynamicLightColor.b,
            value =>
            {
                _markGraphicsCustom();
                Color c = _lightingEngine.DynamicLightColor;
                Color newColor = new Color(c.r, c.g, value, 1f);
                _lightingEngine.SetDynamicLightSettings(_lightingEngine.DynamicLightIntensity, newColor);
                ResolveLocalRobot()?.SetDynamicLightColor(newColor);
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
        extinctionGroup.Add(PauseMenuUIFactory.CreateBoundSlider<WorldLightingSettings>(
            nameof(WorldLightingSettings.EmptyExtinctionMultiplier),
            _loc,
            () => GetLightingValue(static engine => engine.EmptyExtinctionMultiplier),
            value => ApplyLightingSetting(
                value,
                static (engine, setting) =>
                    engine.SetEmptyExtinctionMultiplier(setting)),
            _refreshers));
        extinctionGroup.Add(PauseMenuUIFactory.CreateBoundSlider<WorldLightingSettings>(
            nameof(WorldLightingSettings.SolidExtinctionMultiplier),
            _loc,
            () => GetLightingValue(static engine => engine.SolidExtinctionMultiplier),
            value => ApplyLightingSetting(
                value,
                static (engine, setting) =>
                    engine.SetSolidExtinctionMultiplier(setting)),
            _refreshers));
        bounceGroup.Add(PauseMenuUIFactory.CreateBoundSlider<WorldLightingSettings>(
            nameof(WorldLightingSettings.BounceStrength),
            _loc,
            () => GetLightingValue(static engine => engine.BounceStrength),
            value => ApplyLightingSetting(
                value,
                static (engine, setting) => engine.SetBounceStrength(setting)),
            _refreshers));
        VisualElement maximumLightMultiplierSlider = PauseMenuUIFactory.CreateBoundSlider<WorldLightingSettings>(
            nameof(WorldLightingSettings.MaximumLightMultiplier),
            _loc,
            () => GetLightingValue(static engine => engine.MaximumLightMultiplier),
            value => ApplyLightingSetting(
                value,
                static (engine, setting) =>
                    engine.SetMaximumLightMultiplier(setting)),
            _refreshers);
        void RefreshMaximumLightMultiplierState()
        {
            maximumLightMultiplierSlider.SetEnabled(_lightingEngine.EnableFinalLightingClamp);
        }

        _refreshers.Add(RefreshMaximumLightMultiplierState);
        RefreshMaximumLightMultiplierState();
        boundsGroup.Add(maximumLightMultiplierSlider);
#if UNITY_EDITOR || UNITY_ENABLE_CHECKS
        boundsGroup.Add(PauseMenuUIFactory.CreateBoundSlider<WorldLightingSettings>(
            nameof(WorldLightingSettings.TransmittanceDebugDistanceCells),
            _loc,
            () => GetLightingValue(static engine => engine.TransmittanceDebugDistanceCells),
            value => ApplyLightingSetting(
                value,
                static (engine, setting) =>
                    engine.SetTransmittanceDebugDistance(setting)),
            _refreshers));
#endif
        boundsGroup.Add(PauseMenuUIFactory.CreateBoundSlider<WorldLightingSettings>(
            nameof(WorldLightingSettings.MinimumTransmission),
            _loc,
            () => GetLightingValue(static engine => engine.MinimumTransmission),
            value => ApplyLightingSetting(
                value,
                static (engine, setting) => engine.SetMinimumTransmission(setting)),
            _refreshers));
        boundsGroup.Add(PauseMenuUIFactory.CreateBoundSlider<WorldLightingSettings>(
            nameof(WorldLightingSettings.LightSafeBorder),
            _loc,
            () => _lightingEngine.LightSafeBorder,
            value => ApplyLightingSetting(
                value,
                static (engine, setting) => engine.SetLightSafeBorder(setting)),
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
            _markGraphicsCustom();
            _graphicsSettings.UpdateCustomWorldMaterialSettings(update);
        }

        worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundSlider<TerrainSettings>(
            nameof(TerrainSettings.ShimmerSpeedScale),
            _loc,
            () => _clientConfig.Config.Terrain.ShimmerSpeedScale,
            value => SaveShaderSetting(
                config => config.Terrain.ShimmerSpeedScale = value),
            _refreshers));
        worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundColorControls(
            _loc.Get("settings.world.shimmer_color"),
            () => _clientConfig.Config.Terrain.ShimmerColor,
            value => SaveShaderSetting(config => config.Terrain.ShimmerColor = value),
            0f,
            8f,
            _refreshers));
        worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundSlider<TerrainSettings>(
            nameof(TerrainSettings.PulseSpeedScale),
            _loc,
            () => _clientConfig.Config.Terrain.PulseSpeedScale,
            value => SaveShaderSetting(config => config.Terrain.PulseSpeedScale = value),
            _refreshers));
        worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundSlider<TerrainSettings>(
            nameof(TerrainSettings.TransitEmissionStrength),
            _loc,
            () => _clientConfig.Config.Terrain.TransitEmissionStrength,
            value => SaveShaderSetting(config => config.Terrain.TransitEmissionStrength = value),
            _refreshers));
        worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundColorControls(
            _loc.Get("settings.world.surface_emission_color"),
            () => _clientConfig.Config.Terrain.TransitEmissionColor,
            value => SaveShaderSetting(config => config.Terrain.TransitEmissionColor = value),
            0f,
            8f,
            _refreshers));
        worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundSlider<TerrainSettings>(
            nameof(TerrainSettings.PerspectiveEmissionStrength),
            _loc,
            () => _clientConfig.Config.Terrain.PerspectiveEmissionStrength,
            value => SaveShaderSetting(
                config => config.Terrain.PerspectiveEmissionStrength = value),
            _refreshers));
        worldMaterialsSection.Add(PauseMenuUIFactory.CreateBoundColorControls(
            _loc.Get("settings.world.far_surface_color"),
            () => _clientConfig.Config.Terrain.PerspectiveEmissionColor,
            value => SaveShaderSetting(
                config => config.Terrain.PerspectiveEmissionColor = value),
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

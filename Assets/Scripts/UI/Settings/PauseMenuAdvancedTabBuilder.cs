#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Game;
using Kern.Rendering;
using Kern.World.Lighting;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI;

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

        // Освещение — константы, не настраивается

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

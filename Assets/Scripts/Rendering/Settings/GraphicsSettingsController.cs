#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game;
using Fodinae.Player.Logic;
using Fodinae.Rendering.PostProcessing;
using Fodinae.World;
using Fodinae.World.Lighting;
using Fodinae.World.Terrain;

namespace Fodinae.Rendering;

public sealed class GraphicsSettingsController
{
    private readonly IClientConfigManager _clientConfig;
    private readonly LightingEngine _lightingEngine;
    private readonly PostProcessController _postProcessController;
    private readonly TerrainRenderer _terrainRenderer;
    private readonly SurfaceRenderer _surfaceRenderer;
    private readonly ILocalPlayerState _localPlayer;

    public GraphicsSettingsController(
        IClientConfigManager clientConfig,
        LightingEngine lightingEngine,
        PostProcessController postProcessController,
        TerrainRenderer terrainRenderer,
        SurfaceRenderer surfaceRenderer,
        ILocalPlayerState localPlayer)
    {
        _clientConfig = clientConfig;
        _lightingEngine = lightingEngine;
        _postProcessController = postProcessController;
        _terrainRenderer = terrainRenderer;
        _surfaceRenderer = surfaceRenderer;
        _localPlayer = localPlayer;
    }

    public GraphicsPreset SelectedPreset => _clientConfig.SelectedGraphicsPreset;

    public GraphicsQualitySettings CustomSettings => _clientConfig.Config.GraphicsQualitySettings;

    public void MarkCustom()
    {
        _clientConfig.MarkGraphicsAsCustom();
    }

    public void SelectStandardPreset(GraphicsPreset preset)
    {
        _clientConfig.SelectGraphicsPreset(preset);
        ApplyAll();
        _clientConfig.Save();
    }

    public void SelectCustomPreset()
    {
        _clientConfig.MarkGraphicsAsCustom();
        ApplyAll();
        _clientConfig.Save();
    }

    public void SetCustomSettings(GraphicsQualitySettings settings)
    {
        _clientConfig.SetCustomGraphicsSettings(settings);
        ApplyAll();
        _clientConfig.Save();
    }

    public void UpdatePostProcessSettings(Action<ClientConfig> update)
    {
        _clientConfig.UpdatePostProcessAndSave(update);
        _postProcessController.ApplyClientConfig();
    }

    public void UpdateAccessibilitySettings(Action<AccessibilitySettings> update)
    {
        _clientConfig.UpdateAccessibility(update);
        _postProcessController.ApplyClientConfig();
    }

    public void UpdateCustomWorldMaterialSettings(Action<ClientConfig> update)
    {
        MarkCustom();
        _clientConfig.UpdateAndSave(update);
        _terrainRenderer.ApplyClientConfig();
        _surfaceRenderer.ApplyClientConfig();
    }

    private void ApplyAll()
    {
        _lightingEngine.ApplyClientConfig();
        _postProcessController.ApplyClientConfig();
        _terrainRenderer.ApplyClientConfig();
        _surfaceRenderer.ApplyClientConfig();
        _localPlayer.Current?
            .GetComponent<Robot>()?
            .ResetDynamicLightPreferences();
    }
}

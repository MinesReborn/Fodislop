#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Game;
using Kern.Player.Logic;
using Kern.Rendering.PostProcessing;
using Kern.World;
using Kern.World.Lighting;
using Kern.World.Terrain;
using UnityEngine;

namespace Kern.Rendering;

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

    public void SelectPreset(GraphicsPreset preset)
    {
        Debug.Log($"[GraphicsSettingsController] Selecting preset: {preset}");
        _clientConfig.SelectGraphicsPreset(preset);
        ApplyAll();
        _clientConfig.Save();
    }

    public void UpdatePostProcessSettings(Action<ClientConfig> update)
    {
        Debug.Log("[GraphicsSettingsController] Updating post-process settings");
        _clientConfig.UpdatePostProcessAndSave(update);
        _postProcessController.ApplyClientConfig();
    }

    public void UpdateAccessibilitySettings(Action<AccessibilitySettings> update)
    {
        Debug.Log("[GraphicsSettingsController] Updating accessibility settings");
        _clientConfig.UpdateSection(config => config.Accessibility, update);
        _postProcessController.ApplyClientConfig();
    }

    public void UpdateWorldMaterialSettings(Action<ClientConfig> update)
    {
        Debug.Log("[GraphicsSettingsController] Updating world material settings");
        _clientConfig.UpdateAndSave(update);
        _terrainRenderer.ApplyClientConfig();
        _surfaceRenderer.ApplyClientConfig();
    }

    private void ApplyAll()
    {
        Debug.Log("[GraphicsSettingsController] ApplyAll: applying config across Lighting, PostProcess, Terrain, Surface");
        _lightingEngine.ApplyClientConfig();
        _postProcessController.ApplyClientConfig();
        _terrainRenderer.ApplyClientConfig();
        _surfaceRenderer.ApplyClientConfig();
        _localPlayer.Current?
            .GetComponent<Robot>()?
            .ResetDynamicLightPreferences();
    }
}

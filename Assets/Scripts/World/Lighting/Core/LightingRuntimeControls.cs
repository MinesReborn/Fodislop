#nullable enable

using System;
using Kern.Core.Interfaces;
using Kern.Rendering;
using Kern.World.Lighting.Quality;
using UnityEngine;

namespace Kern.World.Lighting;

internal sealed class LightingRuntimeControls(
    IClientConfigManager clientConfig,
    LightingQualityController qualityController,
    LightingRuntimeState runtimeState,
    DynamicLightManager dynamicLightManager,
    Action publishTerrainRequirements)
{
    public void ApplyClientConfig()
    {
        qualityController.Apply(
            clientConfig.Config.GraphicsPreset,
            clientConfig.Config.GraphicsQualitySettings);
        publishTerrainRequirements();
        LightingRuntimeInvalidation.ResetFieldAndRadiance(runtimeState);
        dynamicLightManager.IncrementGeneration();
        dynamicLightManager.MarkDirty();
        Debug.Log(
            $"[LightingEngine] Applied client config (Preset={clientConfig.Config.GraphicsPreset})");
    }

    public void SetDebugView(
        ref LightingEngine.DebugView current,
        LightingEngine.DebugView requested)
    {
        if (current == requested)
        {
            return;
        }

        current = requested;
        runtimeState.HasRenderedLightState = false;
        runtimeState.HasStaticRadianceState = false;
        runtimeState.HasDynamicRadianceState = false;
        runtimeState.CompositeDirty = true;
        Debug.Log($"[LightingEngine] SetDebugView: {requested}");
    }

    public void InvalidateRadiance() =>
        LightingRuntimeInvalidation.ResetRadiance(runtimeState);

    public void ResetRuntimeLightingPreferences()
    {
        qualityController.Apply(
            clientConfig.Config.GraphicsPreset,
            clientConfig.Config.GraphicsQualitySettings);
        publishTerrainRequirements();
        LightingRuntimeInvalidation.ResetFieldAndRadiance(runtimeState);
    }
}

#nullable enable

using Kern.World.Lighting.Quality;
using Kern.Rendering;
using UnityEngine;

namespace Kern.World.Lighting;

/// <summary>
/// Owns GPU resource sizing and release transitions for the lighting runtime.
/// </summary>
internal sealed class LightingGPULifecycle
{
    private readonly LightingResourceManager _resources;
    private readonly LightingFrameExecutor _frameExecutor;

    public LightingGPULifecycle(
        LightingResourceManager resources,
        LightingFrameExecutor frameExecutor)
    {
        _resources = resources;
        _frameExecutor = frameExecutor;
    }

    public bool EnsureResources(
        int gridWidth,
        int gridHeight,
        Camera camera,
        in GraphicsQualitySettings qualitySettings,
        out bool textureDimensionLimited,
        out bool cascadeBudgetLimited,
        out int effectivePixelsPerCell)
    {
        RenderTexture? oldMaterial = _resources.MaterialField;
        int oldFieldWidth = _resources.FieldWidth;
        int oldFieldHeight = _resources.FieldHeight;
        int oldAoWidth = _resources.AmbientOcclusionWidth;
        int oldAoHeight = _resources.AmbientOcclusionHeight;

        _resources.EnsureResources(
            gridWidth,
            gridHeight,
            camera,
            in qualitySettings,
            out textureDimensionLimited,
            out cascadeBudgetLimited,
            out effectivePixelsPerCell);

        _frameExecutor.EnsureDynamicLightCapacity(
            Mathf.Max(1, qualitySettings.LightingMaximumLightCount));

        return oldMaterial != _resources.MaterialField ||
            oldFieldWidth != _resources.FieldWidth ||
            oldFieldHeight != _resources.FieldHeight ||
            oldAoWidth != _resources.AmbientOcclusionWidth ||
            oldAoHeight != _resources.AmbientOcclusionHeight;
    }

    public void EnsurePipeline()
    {
        _resources.EnsureGPUPipelineInitialized();
    }

    public void ReleasePipeline()
    {
        _resources.ReleaseGPUPipeline();
        _frameExecutor.Release();
    }

    public void ReleaseResources()
    {
        _resources.ReleaseResources();
        _frameExecutor.Release();
    }
}

#nullable enable

using System;
using Kern.World.Common.Rendering;
using Kern.World.Lighting.Quality;
using UnityEngine;

namespace Kern.World.Lighting;

/// <summary>
/// Publishes the lighting result and disabled fallback to world shaders.
/// </summary>
internal sealed class LightingPresentation
{
    public const string WorldLightingKeyword = "KERN_WORLD_LIGHTING";

    private static readonly int _worldLightTextureID = Shader.PropertyToID("_WorldLightTexture");
    private static readonly int _worldLightRectID = Shader.PropertyToID("_WorldLightRect");
    private static readonly int _worldLightDebugViewID = Shader.PropertyToID("_WorldLightDebugView");
    private static readonly int _worldLightTextureSizeID = Shader.PropertyToID("_WorldLightTextureSize");
    private static readonly int _worldEmissionScaleID = Shader.PropertyToID("_WorldEmissionScale");
    private static readonly int _worldAmbientOcclusionTextureID =
        Shader.PropertyToID("_WorldAmbientOcclusionTexture");
    private static readonly int _worldAmbientOcclusionYFlipID =
        Shader.PropertyToID("_WorldAmbientOcclusionYFlip");
    private static readonly int _worldEmissionTextureID = Shader.PropertyToID("_WorldEmissionTexture");

    private readonly LightingResourceManager _resources;
    private bool _disabledStatePublished;

    public LightingPresentation(LightingResourceManager resources)
    {
        _resources = resources;
    }

    public bool IsDisabledStatePublished => _disabledStatePublished;

    public void MarkEnabled()
    {
        _disabledStatePublished = false;
    }

    public void PublishDisabled()
    {
        if (_disabledStatePublished)
        {
            return;
        }

        Shader.DisableKeyword(WorldLightingKeyword);
        Shader.SetGlobalTexture(_worldLightTextureID, Texture2D.whiteTexture);
        Shader.SetGlobalVector(_worldLightRectID, new Vector4(-1000f, -1000f, 2000f, 2000f));
        Shader.SetGlobalVector(_worldLightTextureSizeID, new Vector4(1, 1, 1, 1));
        Shader.SetGlobalInteger(_worldLightDebugViewID, 0);
        Shader.SetGlobalTexture(_worldAmbientOcclusionTextureID, Texture2D.blackTexture);
        Shader.SetGlobalInteger(_worldAmbientOcclusionYFlipID, 0);
        Shader.SetGlobalFloat(_worldEmissionScaleID, LightingConfigHolder.EmissionScale);
        Shader.SetGlobalTexture(_worldEmissionTextureID, Texture2D.blackTexture);
        _disabledStatePublished = true;
    }

    public void PublishAmbientOcclusionOnly(
        RenderTexture ambientOcclusion,
        Vector4 visibleRegion,
        float cellSize)
    {
        if (!ambientOcclusion.IsCreated() ||
            float.IsNaN(visibleRegion.x) || float.IsInfinity(visibleRegion.x) ||
            float.IsNaN(visibleRegion.y) || float.IsInfinity(visibleRegion.y) ||
            float.IsNaN(visibleRegion.z) || float.IsInfinity(visibleRegion.z) || visibleRegion.z <= 0f ||
            float.IsNaN(visibleRegion.w) || float.IsInfinity(visibleRegion.w) || visibleRegion.w <= 0f ||
            float.IsNaN(cellSize) || float.IsInfinity(cellSize) || cellSize <= 0f)
        {
            throw new InvalidOperationException(
                "Standard graphics cannot publish an invalid ambient-occlusion field or region.");
        }

        Shader.EnableKeyword(WorldLightingKeyword);
        _disabledStatePublished = false;
        Shader.SetGlobalTexture(_worldLightTextureID, Texture2D.whiteTexture);
        Shader.SetGlobalTexture(_worldAmbientOcclusionTextureID, ambientOcclusion);
        Shader.SetGlobalTexture(_worldEmissionTextureID, Texture2D.blackTexture);
        Shader.SetGlobalInteger(
            _worldAmbientOcclusionYFlipID,
            SystemInfo.graphicsUVStartsAtTop ? 1 : 0);
        Shader.SetGlobalInteger(_worldLightDebugViewID, 0);
        Shader.SetGlobalVector(_worldLightTextureSizeID, new Vector4(1f, 1f, 1f, 1f));
        Shader.SetGlobalFloat(_worldEmissionScaleID, LightingConfigHolder.EmissionScale);
        Shader.SetGlobalVector(
            _worldLightRectID,
            new Vector4(
                visibleRegion.x * cellSize,
                visibleRegion.y * cellSize,
                visibleRegion.z * cellSize,
                visibleRegion.w * cellSize));
        TerrainSurfaceShaderGlobals.ApplyShaderGlobals();
    }

    public void Publish(
        LightingEngine.DebugView debugView,
        Vector4 visibleRegion,
        float cellSize)
    {
        RenderTexture lightmap = _resources.LightmapTexture ??
            throw new InvalidOperationException(
                "Enabled world lighting cannot publish before its lightmap exists.");
        RenderTexture ambientOcclusion = _resources.AmbientOcclusionField ??
            throw new InvalidOperationException(
                "Enabled world lighting cannot publish before its ambient-occlusion field exists.");
        if (float.IsNaN(visibleRegion.x) || float.IsInfinity(visibleRegion.x) ||
            float.IsNaN(visibleRegion.y) || float.IsInfinity(visibleRegion.y) ||
            float.IsNaN(visibleRegion.z) || float.IsInfinity(visibleRegion.z) || visibleRegion.z <= 0f ||
            float.IsNaN(visibleRegion.w) || float.IsInfinity(visibleRegion.w) || visibleRegion.w <= 0f ||
            float.IsNaN(cellSize) || float.IsInfinity(cellSize) || cellSize <= 0f)
        {
            throw new InvalidOperationException(
                "Enabled world lighting cannot publish AO mapping from an invalid world region or cell size.");
        }

        Shader.EnableKeyword(WorldLightingKeyword);
        _disabledStatePublished = false;
        Shader.SetGlobalTexture(_worldLightTextureID, lightmap);
        Shader.SetGlobalTexture(_worldAmbientOcclusionTextureID, ambientOcclusion);
        Shader.SetGlobalTexture(
            _worldEmissionTextureID,
            (Texture?)_resources.StaticEmissionField ?? Texture2D.blackTexture);
        Shader.SetGlobalInteger(
            _worldAmbientOcclusionYFlipID,
            SystemInfo.graphicsUVStartsAtTop ? 1 : 0);
        TerrainSurfaceShaderGlobals.ApplyShaderGlobals();

        Shader.SetGlobalInteger(_worldLightDebugViewID, (int)debugView);
        Shader.SetGlobalFloat(_worldEmissionScaleID, LightingConfigHolder.EmissionScale);
        Shader.SetGlobalVector(
            _worldLightTextureSizeID,
            new Vector4(
                lightmap.width,
                lightmap.height,
                1f / lightmap.width,
                1f / lightmap.height));
        Shader.SetGlobalVector(
            _worldLightRectID,
            new Vector4(
                visibleRegion.x * cellSize,
                visibleRegion.y * cellSize,
                visibleRegion.z * cellSize,
                visibleRegion.w * cellSize));
    }
}

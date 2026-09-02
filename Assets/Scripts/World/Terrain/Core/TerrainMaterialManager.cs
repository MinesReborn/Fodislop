#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.World.Terrain;

/// <summary>
/// Manages terrain materials and shader parameters based on client configuration and active atlases.
/// </summary>
public sealed class TerrainMaterialManager
{
    private static readonly int BaseMapPropertyId = Shader.PropertyToID("_BaseMap");
    private static readonly int FlowMapPropertyId = Shader.PropertyToID("_FlowMap");
    private static readonly int FlowScalePropertyId = Shader.PropertyToID("_FlowScale");
    private static readonly int ShimmerSpeedScalePropertyId = Shader.PropertyToID("_ShimmerSpeedScale");
    private static readonly int PulseSpeedScalePropertyId = Shader.PropertyToID("_PulseSpeedScale");
    private static readonly int ShimmerColorPropertyId = Shader.PropertyToID("_ShimmerColor");
    private static readonly int DebugColorPropertyId = Shader.PropertyToID("_DebugColor");
    private static readonly int DebugModePropertyId = Shader.PropertyToID("_DebugMode");
    private static readonly int WorldLightTexturePropertyId = Shader.PropertyToID("_WorldLightTexture");
    private static readonly int WorldLightRectPropertyId = Shader.PropertyToID("_WorldLightRect");

    private Material[] _materials = Array.Empty<Material>();
    private List<int>[] _subMeshIndices = Array.Empty<List<int>>();
    private Shader? _terrainShader;
    private int _lastAtlasCount = -1;
    private bool _lightingBindingValidated;

    public Material[] Materials => _materials;

    public List<int>[] SubMeshIndices => _subMeshIndices;

    public bool LightingBindingValidated => _lightingBindingValidated;

    public Shader? TerrainShader
    {
        get => _terrainShader;
        set => _terrainShader = value;
    }

    public void InitializeShader()
    {
        if (_terrainShader == null)
        {
            _terrainShader = Shader.Find(ProjectRuntimeContracts.ShaderNames.Terrain);
            if (_terrainShader == null || !_terrainShader.isSupported)
            {
                throw new InvalidOperationException(
                    $"Required terrain shader '{ProjectRuntimeContracts.ShaderNames.Terrain}' " +
                    "is missing or unsupported. World lighting cannot run without it.");
            }
        }
    }

    public void ApplyClientConfig(ClientConfig config)
    {
        if (_materials.Length == 0)
        {
            return;
        }

        foreach (Material material in _materials)
        {
            material.SetVector(FlowScalePropertyId, config.TerrainFlowScale);
            material.SetFloat(ShimmerSpeedScalePropertyId, config.TerrainShimmerSpeedScale);
            material.SetFloat(PulseSpeedScalePropertyId, config.TerrainPulseSpeedScale);
            material.SetColor(ShimmerColorPropertyId, config.TerrainShimmerColor);
            material.SetColor(DebugColorPropertyId, config.TerrainDebugColor);
            material.SetFloat(DebugModePropertyId, config.TerrainDebugMode ? 1f : 0f);
        }
    }

    public bool EnsureMaterials(
        IReadOnlyList<IAtlasDescriptor> atlases,
        int meshWidth,
        int meshHeight,
        IClientConfigManager clientConfigManager,
        TerrainCellCache cellCache)
    {
        bool materialsChanged = false;
        if (atlases.Count != _lastAtlasCount)
        {
            IClientConfigManager cfgManager = clientConfigManager ??
                throw new InvalidOperationException(
                    "TerrainRenderer requires IClientConfigManager injection.");
            ClientConfig clientConfig = cfgManager.Config ??
                throw new InvalidOperationException(
                    "TerrainRenderer requires an initialized ClientConfig.");

            _lastAtlasCount = atlases.Count;
            _lightingBindingValidated = false;
            cellCache.ClearCaches();
            CleanupMaterials();

            _subMeshIndices = new List<int>[atlases.Count];
            _materials = new Material[atlases.Count];
            int estimatedPerAtlas = (meshWidth * meshHeight * 2 * 6 / atlases.Count) + 16;
            for (int i = 0; i < atlases.Count; i++)
            {
                _subMeshIndices[i] = new List<int>(estimatedPerAtlas);
                Shader shader = _terrainShader ??
                    throw new InvalidOperationException(
                        "Terrain shader was not initialized before atlas material creation.");
                _materials[i] = new Material(shader)
                {
                    name = $"Terrain Atlas Material {i}",
                    hideFlags = HideFlags.HideAndDontSave,
                };
                RequireShaderProperties(_materials[i]);
                _materials[i].SetVector(FlowScalePropertyId, clientConfig.TerrainFlowScale);
                _materials[i].SetFloat(ShimmerSpeedScalePropertyId, clientConfig.TerrainShimmerSpeedScale);
                _materials[i].SetFloat(PulseSpeedScalePropertyId, clientConfig.TerrainPulseSpeedScale);
                _materials[i].SetColor(ShimmerColorPropertyId, clientConfig.TerrainShimmerColor);
                _materials[i].SetColor(DebugColorPropertyId, clientConfig.TerrainDebugColor);
                _materials[i].SetFloat(DebugModePropertyId, clientConfig.TerrainDebugMode ? 1f : 0f);

                if (_materials[i].FindPass("Universal2D") < 0 ||
                    _materials[i].FindPass(
                        ProjectRuntimeContracts.ShaderPassNames.LightingMaterialField) < 0)
                {
                    throw new InvalidOperationException(
                        $"Terrain material '{_materials[i].name}' is missing required " +
                        "world-lighting properties or passes.");
                }
            }

            materialsChanged = true;
        }
        else
        {
            int estimatedPerAtlas =
                (meshWidth * meshHeight * 2 * 6 / _subMeshIndices.Length) + 16;
            foreach (var list in _subMeshIndices)
            {
                list.Clear();
                if (list.Capacity < estimatedPerAtlas)
                {
                    list.Capacity = estimatedPerAtlas;
                }
            }
        }

        return materialsChanged;
    }

    public void BindAtlasTextures(
        IReadOnlyList<IAtlasDescriptor> atlases,
        ITextureService textureService,
        Mesh mesh)
    {
        for (int i = 0; i < atlases.Count; i++)
        {
            var atlasTex = atlases[i].Texture;
            if (_materials[i].GetTexture(BaseMapPropertyId) != atlasTex)
            {
                _materials[i].SetTexture(BaseMapPropertyId, atlasTex);
            }

            if (_materials[i].GetTexture(FlowMapPropertyId) != textureService.FlowMapTexture)
            {
                _materials[i].SetTexture(FlowMapPropertyId, textureService.FlowMapTexture);
            }

            mesh.SetIndices(_subMeshIndices[i], MeshTopology.Triangles, i, false, 0);
        }
    }

    public void ValidateLightingBinding()
    {
        if (_lightingBindingValidated || _materials.Length == 0)
        {
            return;
        }

        for (int materialIndex = 0; materialIndex < _materials.Length; materialIndex++)
        {
            Material material = _materials[materialIndex];
            if (material.FindPass("Universal2D") < 0 ||
                material.FindPass(
                    ProjectRuntimeContracts.ShaderPassNames.LightingMaterialField) < 0)
            {
                throw new InvalidOperationException(
                    $"Terrain material '{material.name}' is missing world-lighting passes.");
            }
        }

        Texture globalTexture = Shader.GetGlobalTexture(WorldLightTexturePropertyId);
        Vector4 globalRect = Shader.GetGlobalVector(WorldLightRectPropertyId);
        if (globalTexture == null || globalRect.z <= 0f || globalRect.w <= 0f)
        {
            throw new InvalidOperationException(
                "Radiance Cascades completed without publishing a valid world light texture and rect.");
        }

        _lightingBindingValidated = true;
        Debug.Log(
            $"[TerrainLighting] Bound {globalTexture.name} " +
            $"({globalTexture.width}x{globalTexture.height}) to {_materials.Length} terrain material(s).");
    }

    public void CleanupMaterials()
    {
        if (_materials != null)
        {
            foreach (var mat in _materials)
            {
                if (mat != null)
                {
                    if (Application.isPlaying)
                    {
                        UnityEngine.Object.Destroy(mat);
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(mat, allowDestroyingAssets: true);
                    }
                }
            }
        }
    }

    private static void RequireShaderProperties(Material material)
    {
        string[] requiredProperties =
        [
            "_BaseMap",
            "_FlowMap",
            "_FlowScale",
            "_ShimmerSpeedScale",
            "_PulseSpeedScale",
            "_ShimmerColor",
            "_DebugColor",
            "_DebugMode",
        ];
        foreach (string propertyName in requiredProperties)
        {
            if (!material.HasProperty(propertyName))
            {
                throw new InvalidOperationException(
                    $"Terrain shader '{material.shader.name}' is missing required property " +
                    $"'{propertyName}'. Client graphics settings cannot be applied.");
            }
        }
    }
}

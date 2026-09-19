#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain;

public sealed class TerrainMaterialManager
{
    private static readonly int _BaseMapPropertyID = Shader.PropertyToID("_BaseMap");
    private static readonly int _FlowMapPropertyID = Shader.PropertyToID("_FlowMap");
    private static readonly int _FlowScalePropertyID = Shader.PropertyToID("_FlowScale");
    private static readonly int _ShimmerSpeedScalePropertyID = Shader.PropertyToID("_ShimmerSpeedScale");
    private static readonly int _PulseSpeedScalePropertyID = Shader.PropertyToID("_PulseSpeedScale");
    private static readonly int _ShimmerColorPropertyID = Shader.PropertyToID("_ShimmerColor");
    private static readonly int _DebugColorPropertyID = Shader.PropertyToID("_DebugColor");
    private static readonly int _DebugModePropertyID = Shader.PropertyToID("_DebugMode");
    private static readonly int _WorldLightTexturePropertyID = Shader.PropertyToID("_WorldLightTexture");
    private static readonly int _WorldLightRectPropertyID = Shader.PropertyToID("_WorldLightRect");

    private Material[] _materials = [];
    private Material[] _overlayMaterials = [];
    private Material[] _cellMaterials = [];

    // Единственный материал террейна: меш идентификаторов, все атласы разом.
    public Material[] CellMaterials => _cellMaterials;
    private Shader? _terrainShader;
    private readonly List<IAtlasDescriptor> _lastAtlases = new();
    private bool _lightingBindingValidated;

    public Material[] Materials => _materials;

    // Материалы накладки дверей: те же, но без режима клеток — накладка
    // остаётся обычным мешем вершин.
    public Material[] OverlayMaterials => _overlayMaterials;
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

        foreach (Material material in AllMaterials())
        {
            material.SetVector(_FlowScalePropertyID, config.Terrain.FlowScale);
            material.SetFloat(_ShimmerSpeedScalePropertyID, config.Terrain.ShimmerSpeedScale);
            material.SetFloat(_PulseSpeedScalePropertyID, config.Terrain.PulseSpeedScale);
            material.SetColor(_ShimmerColorPropertyID, config.Terrain.ShimmerColor);
            material.SetColor(_DebugColorPropertyID, config.Terrain.DebugColor);
            material.SetFloat(_DebugModePropertyID, config.Terrain.DebugMode ? 1f : 0f);
        }
    }

    public bool EnsureMaterials(
        IReadOnlyList<IAtlasDescriptor> atlases,
        int meshWidth,
        int meshHeight,
        IClientConfigManager clientConfigManager,
        TerrainCellCache cellCache)
    {
        if (AtlasRefsEqual(atlases, _lastAtlases))
        {
            return false;
        }

        IClientConfigManager cfgManager = clientConfigManager ??
            throw new InvalidOperationException(
                "TerrainRenderer requires IClientConfigManager injection.");
        ClientConfig clientConfig = cfgManager.Config ??
            throw new InvalidOperationException(
                "TerrainRenderer requires an initialized ClientConfig.");

        // Рост в конец: новые текстуры стримятся по мере исследования мира.
        // Старые индексы атласов в текселях при этом валидны, поэтому кеши,
        // материалы и привязка света не трогаются — создаются только новые
        // материалы. Раньше любой рост валил ClearCaches + BuildFull + полный
        // static solve прямо посреди движения по новому контенту.
        if (IsAtlasAppend(atlases, _lastAtlases) && _cellMaterials.Length > 0)
        {
            int startIndex = _lastAtlases.Count;
            Array.Resize(ref _materials, atlases.Count);
            Array.Resize(ref _overlayMaterials, atlases.Count);
            for (int i = startIndex; i < atlases.Count; i++)
            {
                CreateAtlasMaterials(i, clientConfig);
            }

            SnapshotAtlasRefs(atlases);
            return false;
        }

        _lightingBindingValidated = false;
        cellCache.ClearCaches();
        CleanupMaterials();
        _cellMaterials = [];

        _materials = new Material[atlases.Count];
        _overlayMaterials = new Material[atlases.Count];
        for (int i = 0; i < atlases.Count; i++)
        {
            CreateAtlasMaterials(i, clientConfig);
        }

        if (atlases.Count > _TerrainAtlasPropertyIDs.Length)
        {
            throw new InvalidOperationException(
                $"Terrain cell material holds {_TerrainAtlasPropertyIDs.Length} atlases, got {atlases.Count}.");
        }

        // Один материал на все атласы рисует меш идентификаторов: при
        // материале на атлас каждый проходил бы все вершины сетки.
        _cellMaterials =
        [
            new Material(_materials[0])
            {
                name = "Terrain Cell Material",
                hideFlags = HideFlags.HideAndDontSave,
            },
        ];
        _cellMaterials[0].EnableKeyword(CellModeKeyword);

        SnapshotAtlasRefs(atlases);
        return true;
    }

    private static bool AtlasRefsEqual(
        IReadOnlyList<IAtlasDescriptor> atlases,
        List<IAtlasDescriptor> previous)
    {
        if (atlases.Count != previous.Count)
        {
            return false;
        }

        for (int i = 0; i < atlases.Count; i++)
        {
            if (!ReferenceEquals(atlases[i], previous[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAtlasAppend(
        IReadOnlyList<IAtlasDescriptor> atlases,
        List<IAtlasDescriptor> previous)
    {
        if (atlases.Count <= previous.Count)
        {
            return false;
        }

        for (int i = 0; i < previous.Count; i++)
        {
            if (!ReferenceEquals(atlases[i], previous[i]))
            {
                return false;
            }
        }

        return true;
    }

    private void SnapshotAtlasRefs(IReadOnlyList<IAtlasDescriptor> atlases)
    {
        _lastAtlases.Clear();
        _lastAtlases.AddRange(atlases);
    }

    private void CreateAtlasMaterials(int index, ClientConfig clientConfig)
    {
        Shader shader = _terrainShader ??
            throw new InvalidOperationException(
                "Terrain shader was not initialized before atlas material creation.");
        _materials[index] = new Material(shader)
        {
            name = $"Terrain Atlas Material {index}",
            hideFlags = HideFlags.HideAndDontSave,
        };
        RequireShaderProperties(_materials[index]);
        _materials[index].SetVector(_FlowScalePropertyID, clientConfig.Terrain.FlowScale);
        _materials[index].SetFloat(_ShimmerSpeedScalePropertyID, clientConfig.Terrain.ShimmerSpeedScale);
        _materials[index].SetFloat(_PulseSpeedScalePropertyID, clientConfig.Terrain.PulseSpeedScale);
        _materials[index].SetColor(_ShimmerColorPropertyID, clientConfig.Terrain.ShimmerColor);
        _materials[index].SetColor(_DebugColorPropertyID, clientConfig.Terrain.DebugColor);
        _materials[index].SetFloat(_DebugModePropertyID, clientConfig.Terrain.DebugMode ? 1f : 0f);

        if (_materials[index].FindPass("Universal2D") < 0 ||
            _materials[index].FindPass(
                ProjectRuntimeContracts.ShaderPassNames.LightingMaterialField) < 0)
        {
            throw new InvalidOperationException(
                $"Terrain material '{_materials[index].name}' is missing required " +
                "world-lighting properties or passes.");
        }

        _overlayMaterials[index] = new Material(_materials[index])
        {
            name = $"Terrain Door Overlay Material {index}",
            hideFlags = HideFlags.HideAndDontSave,
        };
    }

    public void BindAtlasTextures(
        IReadOnlyList<IAtlasDescriptor> atlases,
        ITextureService textureService)
    {
        for (int i = 0; i < atlases.Count && i < _materials.Length; i++)
        {
            BindAtlas(_materials[i], atlases[i].Texture, textureService.FlowMapTexture);
            BindAtlas(_overlayMaterials[i], atlases[i].Texture, textureService.FlowMapTexture);
            if (_cellMaterials.Length > 0 && i < _TerrainAtlasPropertyIDs.Length &&
                _cellMaterials[0].GetTexture(_TerrainAtlasPropertyIDs[i]) != atlases[i].Texture)
            {
                _cellMaterials[0].SetTexture(_TerrainAtlasPropertyIDs[i], atlases[i].Texture);
            }
        }

        if (_cellMaterials.Length > 0)
        {
            BindAtlas(_cellMaterials[0], atlases[0].Texture, textureService.FlowMapTexture);
        }
    }

    // Атлас или карта потока могут быть ещё не загружены: пустой слот
    // материала — штатное состояние до загрузки, SetTexture принимает null.
    private static void BindAtlas(Material material, Texture? atlas, Texture? flowMap)
    {
        if (material.GetTexture(_BaseMapPropertyID) != atlas)
        {
            material.SetTexture(_BaseMapPropertyID, atlas);
        }

        if (material.GetTexture(_FlowMapPropertyID) != flowMap)
        {
            material.SetTexture(_FlowMapPropertyID, flowMap);
        }
    }

    private System.Collections.Generic.IEnumerable<Material> AllMaterials()
    {
        foreach (Material material in _materials)
        {
            yield return material;
        }

        foreach (Material material in _overlayMaterials)
        {
            yield return material;
        }

        foreach (Material material in _cellMaterials)
        {
            yield return material;
        }
    }

    private static readonly int[] _TerrainAtlasPropertyIDs =
    [
        Shader.PropertyToID("_TerrainAtlas0"),
        Shader.PropertyToID("_TerrainAtlas1"),
        Shader.PropertyToID("_TerrainAtlas2"),
        Shader.PropertyToID("_TerrainAtlas3"),
        Shader.PropertyToID("_TerrainAtlas4"),
        Shader.PropertyToID("_TerrainAtlas5"),
        Shader.PropertyToID("_TerrainAtlas6"),
        Shader.PropertyToID("_TerrainAtlas7"),
    ];
    private const string CellModeKeyword = "KERN_TERRAIN_CELLS";

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

        Texture globalTexture = Shader.GetGlobalTexture(_WorldLightTexturePropertyID);
        Vector4 globalRect = Shader.GetGlobalVector(_WorldLightRectPropertyID);
        if (globalTexture == null || globalRect.z <= 0f || globalRect.w <= 0f)
        {
            throw new InvalidOperationException(
                "Radiance Cascades completed without publishing a valid world light texture and rect.");
        }

        _lightingBindingValidated = true;
    }

    public void CleanupMaterials()
    {
        if (_materials != null)
        {
            foreach (var mat in AllMaterials())
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

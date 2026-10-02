#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using MinesServer.Data;
using UnityEngine;
using Kern.Core.Interfaces.Diagnostics;
using Kern.Core.Interfaces.WorldLighting;

namespace Kern.World.Terrain;

public sealed class TerrainMaterialManager
{
    private static readonly int s_baseMapPropertyId = Shader.PropertyToID("_BaseMap");
    private static readonly int s_prismaticFlowMapPropertyId = Shader.PropertyToID("_PrismaticFlowMap");
    private static readonly int s_flowMapPropertyId = Shader.PropertyToID("_FlowMap");
    private static readonly int s_terrainDecalAtlasPropertyId = Shader.PropertyToID("_TerrainDecalAtlas");
    private static readonly int s_terrainDecalStoneAtlasPropertyId = Shader.PropertyToID("_TerrainDecalStoneAtlas");
    private static readonly int s_flowScalePropertyId = Shader.PropertyToID("_FlowScale");
    private static readonly int s_shimmerSpeedScalePropertyId = Shader.PropertyToID("_ShimmerSpeedScale");
    private static readonly int s_pulseSpeedScalePropertyId = Shader.PropertyToID("_PulseSpeedScale");
    private static readonly int s_shimmerColorPropertyId = Shader.PropertyToID("_ShimmerColor");
    private static readonly int s_worldLightTexturePropertyId = Shader.PropertyToID("_WorldLightTexture");
    private static readonly int s_worldLightRectPropertyId = Shader.PropertyToID("_WorldLightRect");

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
            material.SetVector(s_flowScalePropertyId, config.Terrain.FlowScale);
            material.SetFloat(s_shimmerSpeedScalePropertyId, config.Terrain.ShimmerSpeedScale);
            material.SetFloat(s_pulseSpeedScalePropertyId, config.Terrain.PulseSpeedScale);
            material.SetColor(s_shimmerColorPropertyId, config.Terrain.ShimmerColor);
            // Вид поверхности авторский: декали, кайма, глинт и
            // призматик берут числа из TerrainConfigHolder.
            TerrainMaterialTuning.Apply(material);
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

        // Слотов атласа в шейдере ровно восемь, и девятый не даёт ни ошибки,
        // ни чёрного: TerrainSampleAtlas на неизвестном слоте уходит в
        // `default` и читает нулевой атлас чужим прямоугольником — клетка
        // получает правдоподобный, но не свой рисунок.
        //
        // Проверка стоит ДО обеих веток. Раньше она была только в ветке полной
        // пересборки, а набор атласов растёт добавлением в конец — то есть до
        // неё дело не доходило никогда, и девятый атлас проезжал молча.
        if (atlases.Count > s_terrainAtlasPropertyIds.Length)
        {
            throw new InvalidOperationException(
                $"Terrain cell material holds {s_terrainAtlasPropertyIds.Length} atlases, got {atlases.Count}.");
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
            FrameEventLog.Record($"террейн: материалы для атласов {startIndex}..{atlases.Count - 1}");
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
        FrameEventLog.Record($"террейн: материалы пересозданы, атласов {atlases.Count}");
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
        _materials[index].SetVector(s_flowScalePropertyId, clientConfig.Terrain.FlowScale);
        _materials[index].SetFloat(s_shimmerSpeedScalePropertyId, clientConfig.Terrain.ShimmerSpeedScale);
        _materials[index].SetFloat(s_pulseSpeedScalePropertyId, clientConfig.Terrain.PulseSpeedScale);
        _materials[index].SetColor(s_shimmerColorPropertyId, clientConfig.Terrain.ShimmerColor);
        // Вид поверхности авторский: декали, кайма, глинт и
        // призматик берут числа из TerrainConfigHolder.
        TerrainMaterialTuning.Apply(_materials[index]);

        if (_materials[index].FindPass("Universal2D") < 0 ||
            _materials[index].FindPass(
                ProjectRuntimeContracts.ShaderPassNames.LightingMaterialField) < 0 ||
            _materials[index].FindPass(
                ProjectRuntimeContracts.ShaderPassNames.LightingAmbientOcclusionField) < 0)
        {
            throw new InvalidOperationException(
                $"Terrain material '{_materials[index].name}' is missing a required " +
                "world-lighting pass.");
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
            BindAtlas(
                _materials[i],
                atlases[i].Texture,
                textureService.FlowMapTexture,
                textureService.PrismaticFlowMapTexture,
                textureService.TerrainDecalAtlasTexture,
                textureService.TerrainDecalStoneAtlasTexture);
            BindAtlas(
                _overlayMaterials[i],
                atlases[i].Texture,
                textureService.FlowMapTexture,
                textureService.PrismaticFlowMapTexture,
                textureService.TerrainDecalAtlasTexture,
                textureService.TerrainDecalStoneAtlasTexture);
            if (_cellMaterials.Length > 0 && i < s_terrainAtlasPropertyIds.Length &&
                _cellMaterials[0].GetTexture(s_terrainAtlasPropertyIds[i]) != atlases[i].Texture)
            {
                _cellMaterials[0].SetTexture(s_terrainAtlasPropertyIds[i], atlases[i].Texture);
            }
        }

        if (_cellMaterials.Length > 0)
        {
            BindAtlas(
                _cellMaterials[0],
                atlases[0].Texture,
                textureService.FlowMapTexture,
                textureService.PrismaticFlowMapTexture,
                textureService.TerrainDecalAtlasTexture,
                textureService.TerrainDecalStoneAtlasTexture);
        }
    }

    // Атлас или карта потока могут быть ещё не загружены: пустой слот
    // материала — штатное состояние до загрузки, SetTexture принимает null.
    private static void BindAtlas(
        Material material,
        Texture? atlas,
        Texture? flowMap,
        Texture? prismaticFlowMap,
        Texture? terrainDecalAtlas,
        Texture? terrainDecalStoneAtlas)
    {
        if (material.GetTexture(s_baseMapPropertyId) != atlas)
        {
            material.SetTexture(s_baseMapPropertyId, atlas);
        }

        if (material.GetTexture(s_flowMapPropertyId) != flowMap)
        {
            material.SetTexture(s_flowMapPropertyId, flowMap);
        }

        if (material.GetTexture(s_prismaticFlowMapPropertyId) != prismaticFlowMap)
        {
            material.SetTexture(s_prismaticFlowMapPropertyId, prismaticFlowMap);
        }

        if (material.GetTexture(s_terrainDecalAtlasPropertyId) != terrainDecalAtlas)
        {
            material.SetTexture(s_terrainDecalAtlasPropertyId, terrainDecalAtlas);
        }

        if (material.GetTexture(s_terrainDecalStoneAtlasPropertyId) != terrainDecalStoneAtlas)
        {
            material.SetTexture(s_terrainDecalStoneAtlasPropertyId, terrainDecalStoneAtlas);
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

    private static readonly int[] s_terrainAtlasPropertyIds =
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

    public void ValidateLightingBinding(in LightingOutputSnapshot output)
    {
        if (output.State == LightingOutputState.Disabled || _lightingBindingValidated || _materials.Length == 0)
        {
            return;
        }

        if (output.State != LightingOutputState.Published ||
            output.WorldRectCells.width <= 0 || output.WorldRectCells.height <= 0)
        {
            throw new InvalidOperationException("Lighting output snapshot is invalid for Terrain binding.");
        }

        for (int materialIndex = 0; materialIndex < _materials.Length; materialIndex++)
        {
            Material material = _materials[materialIndex];
            if (material.FindPass("Universal2D") < 0 ||
                material.FindPass(
                    ProjectRuntimeContracts.ShaderPassNames.LightingMaterialField) < 0 ||
                material.FindPass(
                    ProjectRuntimeContracts.ShaderPassNames.LightingAmbientOcclusionField) < 0)
            {
                throw new InvalidOperationException(
                    $"Terrain material '{material.name}' is missing world-lighting passes.");
            }
        }

        Texture globalTexture = Shader.GetGlobalTexture(s_worldLightTexturePropertyId);
        Vector4 globalRect = Shader.GetGlobalVector(s_worldLightRectPropertyId);
        if (globalTexture == null || globalRect.z <= 0f || globalRect.w <= 0f)
        {
            throw new InvalidOperationException(
                    "Radiance Cascades completed without publishing a valid world light texture and rect.");
        }

        float cellSize = ProjectRuntimeContracts.World.CellSize;
        var expectedRect = new Vector4(
            output.WorldRectCells.x * cellSize,
            output.WorldRectCells.y * cellSize,
            output.WorldRectCells.width * cellSize,
            output.WorldRectCells.height * cellSize);
        if (globalRect != expectedRect)
        {
            throw new InvalidOperationException(
                $"Lighting output rectangle {globalRect} does not match published cell rectangle " +
                $"{output.WorldRectCells}.");
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
            "_PrismaticFlowMap",
            "_TerrainDecalAtlas",
            "_FlowScale",
            "_ShimmerSpeedScale",
            "_PulseSpeedScale",
            "_ShimmerColor",
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

#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using MinesServer.Data;
using UnityEngine;
using Kern.Core.Interfaces.Diagnostics;
using Kern.Core.Interfaces.WorldLighting;

using Kern.Diagnostics.Frames;

namespace Kern.World.Terrain;

public sealed class TerrainMaterialManager
{
    private static readonly int s_prismaticFlowMapPropertyId = Shader.PropertyToID("_PrismaticFlowMap");
    private static readonly int s_flowMapPropertyId = Shader.PropertyToID("_FlowMap");
    private static readonly int s_terrainDecalAtlasArrayPropertyId = Shader.PropertyToID("_TerrainDecalAtlasArray");
    private static readonly int s_flowScalePropertyId = Shader.PropertyToID("_FlowScale");
    private static readonly int s_shimmerSpeedScalePropertyId = Shader.PropertyToID("_ShimmerSpeedScale");
    private static readonly int s_blinkingSpeedScalePropertyId = Shader.PropertyToID("_BlinkingSpeedScale");
    private static readonly int s_shimmerColorPropertyId = Shader.PropertyToID("_ShimmerColor");
    private static readonly int s_worldLightTexturePropertyId = Shader.PropertyToID("_WorldLightTexture");
    private static readonly int s_worldLightRectPropertyId = Shader.PropertyToID("_WorldLightRect");

    private Material[] _cellMaterials = Array.Empty<Material>();
    private Material[] _presentationMaterials = Array.Empty<Material>();

    public Material[] PresentationMaterials => _presentationMaterials;

    private static readonly int s_depthPixelsId = Shader.PropertyToID("_TerrainDepthPixels");
    private static readonly int s_depthDarkeningId = Shader.PropertyToID("_TerrainDepthDarkening");

    public void ApplyDepthConfig(ClientConfig config)
    {
        foreach (Material material in _cellMaterials)
        {
            ApplyDepth(material, config);
        }
    }

    private static void ApplyDepth(Material material, ClientConfig config)
    {
        material.SetFloat(s_depthPixelsId, config.Terrain.DepthEnabled
            ? Mathf.Round(Mathf.Clamp(config.Terrain.DepthPixels, 0f, 16f)) : 0f);
        material.SetFloat(s_depthDarkeningId, Mathf.Clamp01(config.Terrain.DepthDarkening));
    }

    // Единственный материал террейна: меш идентификаторов и накладка дверей,
    // все атласы разом.
    public Material[] CellMaterials => _cellMaterials;
    private Shader? _terrainShader;
    private readonly List<IAtlasDescriptor> _lastAtlases = new();
    private bool _lightingBindingValidated;

    public Shader? TerrainShader
    {
        get => _terrainShader;
        set => _terrainShader = value;
    }

    public void InitializeShader()
    {
        if (_terrainShader == null)
        {
            _terrainShader = Shader.Find(ShaderNames.Terrain);
            if (_terrainShader == null || !_terrainShader.isSupported)
            {
                throw new InvalidOperationException(
                    $"Required terrain shader '{ShaderNames.Terrain}' " +
                    "is missing or unsupported. World lighting cannot run without it.");
            }
        }
    }

    public void ApplyClientConfig(ClientConfig config)
    {
        foreach (Material material in _cellMaterials)
        {
            material.SetVector(s_flowScalePropertyId, config.Terrain.FlowScale);
            material.SetFloat(s_shimmerSpeedScalePropertyId, config.Terrain.ShimmerSpeedScale);
            material.SetFloat(s_blinkingSpeedScalePropertyId, config.Terrain.BlinkingSpeedScale);
            material.SetColor(s_shimmerColorPropertyId, config.Terrain.ShimmerColor);
            // Вид поверхности авторский: декали, кайма, глинт и
            // призматик берут числа из TerrainConfigHolder.
            TerrainMaterialTuning.Apply(material, config.Terrain.EnableRim);
            ApplyDepth(material, config);
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
            SnapshotAtlasRefs(atlases);
            FrameEventLog.Record($"террейн: атласы {startIndex}..{atlases.Count - 1} добавлены");
            return false;
        }

        _lightingBindingValidated = false;
        cellCache.ClearCaches();
        CleanupMaterials();
        _cellMaterials = new[] { CreateCellMaterial(clientConfig) };
        Material surface = _cellMaterials[0];
        // Видимый фон — один сабмеш. Стенки и передний план блоков живут в
        // отдельном меше со своим рендерером и той же поверхностной
        // материей (см. TerrainPresentationWindow).
        _presentationMaterials = new[] { surface };

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

    private Material CreateCellMaterial(ClientConfig clientConfig)
    {
        Shader shader = _terrainShader ??
            throw new InvalidOperationException(
                "Terrain shader was not initialized before material creation.");
        var material = new Material(shader)
        {
            name = "Terrain Cell Material",
            hideFlags = HideFlags.HideAndDontSave,
        };
        RequireShaderProperties(material);
        material.SetVector(s_flowScalePropertyId, clientConfig.Terrain.FlowScale);
        material.SetFloat(s_shimmerSpeedScalePropertyId, clientConfig.Terrain.ShimmerSpeedScale);
        material.SetFloat(s_blinkingSpeedScalePropertyId, clientConfig.Terrain.BlinkingSpeedScale);
        material.SetColor(s_shimmerColorPropertyId, clientConfig.Terrain.ShimmerColor);
        // Вид поверхности авторский: декали, кайма, глинт и
        // призматик берут числа из TerrainConfigHolder.
        TerrainMaterialTuning.Apply(material, clientConfig.Terrain.EnableRim);
        ApplyDepth(material, clientConfig);
        RequireLightingPasses(material);
        return material;
    }

    private static void RequireLightingPasses(Material material)
    {
        string[] requiredPasses = new string[]
        {
            "Universal2D",
            ShaderPassNames.LightingMaterialField,
            ShaderPassNames.LightingAmbientOcclusionField,
        };
        List<string> missingPasses = new(requiredPasses.Length);
        foreach (string passName in requiredPasses)
        {
            if (material.FindPass(passName) < 0)
            {
                missingPasses.Add(passName);
            }
        }

        if (missingPasses.Count > 0)
        {
            throw new InvalidOperationException(
                $"Terrain material '{material.name}' using shader '{material.shader.name}' " +
                $"is missing required pass(es): {string.Join(", ", missingPasses)}.");
        }
    }

    public void BindAtlasTextures(
        IReadOnlyList<IAtlasDescriptor> atlases,
        ITextureService textureService)
    {
        if (_cellMaterials.Length == 0)
        {
            return;
        }

        // Атлас или карта потока могут быть ещё не загружены: пустой слот
        // материала — штатное состояние до загрузки, SetTexture принимает null.
        Material material = _cellMaterials[0];
        for (int i = 0; i < atlases.Count && i < s_terrainAtlasPropertyIds.Length; i++)
        {
            SetTextureIfChanged(material, s_terrainAtlasPropertyIds[i], atlases[i].Texture);
        }

        SetTextureIfChanged(material, s_flowMapPropertyId, textureService.FlowMapTexture);
        SetTextureIfChanged(material, s_prismaticFlowMapPropertyId, textureService.PrismaticFlowMapTexture);
        SetTextureIfChanged(material, s_terrainDecalAtlasArrayPropertyId, textureService.TerrainDecalAtlasArray);
    }

    private static void SetTextureIfChanged(Material material, int propertyId, Texture? texture)
    {
        if (material.GetTexture(propertyId) != texture)
        {
            material.SetTexture(propertyId, texture);
        }
    }

    private static readonly int[] s_terrainAtlasPropertyIds = new int[]
    {
        Shader.PropertyToID("_TerrainAtlas0"),
        Shader.PropertyToID("_TerrainAtlas1"),
        Shader.PropertyToID("_TerrainAtlas2"),
        Shader.PropertyToID("_TerrainAtlas3"),
        Shader.PropertyToID("_TerrainAtlas4"),
        Shader.PropertyToID("_TerrainAtlas5"),
        Shader.PropertyToID("_TerrainAtlas6"),
        Shader.PropertyToID("_TerrainAtlas7"),
    };

    public void ValidateLightingBinding(in LightingOutputSnapshot output)
    {
        if (output.State == LightingOutputState.Disabled || _lightingBindingValidated || _cellMaterials.Length == 0)
        {
            return;
        }

        if (output.State != LightingOutputState.Published ||
            output.WorldRectCells.width <= 0 || output.WorldRectCells.height <= 0)
        {
            throw new InvalidOperationException("Lighting output snapshot is invalid for Terrain binding.");
        }

        RequireLightingPasses(_cellMaterials[0]);

        Texture globalTexture = Shader.GetGlobalTexture(s_worldLightTexturePropertyId);
        Vector4 globalRect = Shader.GetGlobalVector(s_worldLightRectPropertyId);
        if (globalTexture == null || globalRect.z <= 0f || globalRect.w <= 0f)
        {
            throw new InvalidOperationException(
                    "Radiance Cascades completed without publishing a valid world light texture and rect.");
        }

        float cellSize = WorldGridDefaults.CellSize;
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
        foreach (Material material in _cellMaterials)
        {
            if (material == null)
            {
                continue;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(material);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(material, allowDestroyingAssets: true);
            }
        }

        _cellMaterials = Array.Empty<Material>();
        _presentationMaterials = Array.Empty<Material>();
    }

    private static void RequireShaderProperties(Material material)
    {
        string[] requiredProperties = new string[]
        {
            "_FlowMap",
            "_PrismaticFlowMap",
            "_TerrainDecalAtlasArray",
            "_FlowScale",
            "_ShimmerSpeedScale",
            "_BlinkingSpeedScale",
            "_ShimmerColor",
            "_TerrainRimEnabled",
            "_TerrainDepthPixels",
            "_TerrainDepthDarkening",
            "_RimFalloff",
            "_RimDistanceScale",
            "_EmissionBrightnessStart",
            "_EmissionBrightnessEnd",
            "_SurfaceEmissionStrength",
        };
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

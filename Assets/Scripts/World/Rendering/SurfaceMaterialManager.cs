#nullable enable

using System;
using Kern.Core;
using Kern.Game;
using UnityEngine;

namespace Kern.World;

public sealed class SurfaceMaterialManager
{
    public static Color HorizonSkyColor => new(0.35f, 0.55f, 0.75f, 1f);

    private const string SurfaceShaderName = ProjectRuntimeContracts.ShaderNames.WorldSurface;
    private const string RedRockKeyword = "KERN_SURFACE_REDROCK";
    private const string TransitKeyword = "KERN_SURFACE_TRANSIT";
    private const string PerspectiveKeyword = "KERN_SURFACE_PERSPECTIVE";
    private const string HorizonKeyword = "KERN_SURFACE_HORIZON";
    private const float PerspectiveReferencePixelsPerCell = 30f;

    private static readonly int s_baseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int s_emissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int s_emissionStrengthId = Shader.PropertyToID("_EmissionStrength");
    private static readonly int s_occupancyId = Shader.PropertyToID("_Occupancy");
    private static readonly int s_baseMapTileCountId = Shader.PropertyToID("_BaseMapTileCount");
    private static readonly int s_worldSizeId = Shader.PropertyToID("_WorldSize");
    private static readonly int s_surfaceProjectionId = Shader.PropertyToID("_SurfaceProjection");
    private static readonly int s_skyColorId = Shader.PropertyToID("_SkyColor");
    private static readonly int s_surfaceFieldThresholdId = Shader.PropertyToID("_SurfaceFieldThreshold");

    private static bool s_surfaceFieldThresholdApplied;

    public enum SurfaceKind
    {
        RedRock,
        Transit,
        Perspective,
        Horizon,
    }

    public Material CreateSurfaceMaterial(
        Texture2D texture,
        Color emissionColor,
        float emissionStrength,
        float occupancy,
        Vector2 baseMapTileCount,
        Vector2 worldSize,
        SurfaceKind kind,
        string materialName)
    {
        Shader surfaceShader = Shader.Find(SurfaceShaderName);
        if (surfaceShader == null || !surfaceShader.isSupported)
        {
            throw new InvalidOperationException(
                $"Required surface shader '{SurfaceShaderName}' is missing or unsupported.");
        }

        var material = new Material(surfaceShader)
        {
            name = materialName,
            hideFlags = HideFlags.DontSave,
        };
        RequireShaderProperties(material);
        ApplySurfaceFieldThreshold();
        material.SetTexture(s_baseMapId, texture);
        material.SetColor(s_emissionColorId, emissionColor);
        material.SetFloat(s_emissionStrengthId, emissionStrength);
        material.SetFloat(s_occupancyId, occupancy);
        material.SetVector(
            s_baseMapTileCountId,
            new Vector4(baseMapTileCount.x, baseMapTileCount.y, 0f, 0f));
        material.SetVector(
            s_worldSizeId,
            new Vector4(worldSize.x, worldSize.y, 0f, 0f));
        material.EnableKeyword(kind switch
        {
            SurfaceKind.RedRock => RedRockKeyword,
            SurfaceKind.Transit => TransitKeyword,
            SurfaceKind.Perspective => PerspectiveKeyword,
            SurfaceKind.Horizon => HorizonKeyword,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown surface kind."),
        });
        return material;
    }

    public void SetPerspectiveProjection(Material material, Camera camera)
    {
        float width = Mathf.Max(1f, camera.pixelWidth * (2f / PerspectiveReferencePixelsPerCell));
        material.SetVector(
            s_surfaceProjectionId,
            new Vector4(camera.transform.position.x, 1f / width, 0f, 0f));
    }

    public void SetHorizonSkyColor(Material material, Color skyColor) =>
        material.SetColor(s_skyColorId, skyColor);

    public void ApplyMaterialConfig(
        Material material,
        Color emissionColor,
        float emissionStrength,
        float occupancy)
    {
        material.SetColor(s_emissionColorId, emissionColor);
        material.SetFloat(s_emissionStrengthId, emissionStrength);
        material.SetFloat(s_occupancyId, occupancy);
    }

    public void SetMaterialWorldSize(
        Material? transitMaterial,
        Material? perspectiveMaterial,
        Material? redRockMaterial,
        int worldWidth,
        int worldHeight)
    {
        if (transitMaterial == null || perspectiveMaterial == null || redRockMaterial == null)
        {
            throw new InvalidOperationException("SurfaceRenderer materials must be initialized.");
        }

        Vector4 worldSize = new(worldWidth, worldHeight, 0f, 0f);
        transitMaterial.SetVector(s_worldSizeId, worldSize);
        perspectiveMaterial.SetVector(s_worldSizeId, worldSize);
        redRockMaterial.SetVector(s_worldSizeId, worldSize);
    }

    public Vector2 GetTerrainSheetTileCount(Texture2D texture)
    {
        const int tileSize = RenderingConstants.CELL_SIZE;
        if (texture.width <= 0 || texture.height <= 0 ||
            texture.width % tileSize != 0 || texture.height % tileSize != 0)
        {
            throw new InvalidOperationException(
                $"Surface terrain sheet '{texture.name}' dimensions " +
                $"{texture.width}x{texture.height} must be positive multiples " +
                $"of the terrain tile size {tileSize}.");
        }

        return new Vector2(texture.width / tileSize, texture.height / tileSize);
    }

    private static void RequireShaderProperties(Material material)
    {
        string[] requiredProperties =
        [
            "_BaseMap",
            "_EmissionColor",
            "_EmissionStrength",
            "_Occupancy",
            "_BaseMapTileCount",
            "_WorldSize",
            "_SurfaceProjection",
            "_SkyColor",
        ];
        foreach (string propertyName in requiredProperties)
        {
            if (!material.HasProperty(propertyName))
            {
                throw new InvalidOperationException(
                    $"World surface shader '{material.shader.name}' is missing required property " +
                    $"'{propertyName}'. Client graphics settings cannot be applied.");
            }
        }
    }

    // Порог поля поверхности одинаков для всех материалов, поэтому это
    // глобальная юниформа, а не свойство материала: кладём её один раз.
    private static void ApplySurfaceFieldThreshold()
    {
        if (s_surfaceFieldThresholdApplied)
        {
            return;
        }

        Shader.SetGlobalFloat(s_surfaceFieldThresholdId, WorldRenderConfigHolder.SurfaceFieldThreshold);
        s_surfaceFieldThresholdApplied = true;
    }
}

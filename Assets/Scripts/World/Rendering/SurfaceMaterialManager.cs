#nullable enable

using System;
using Kern.Core;
using UnityEngine;

namespace Kern.World;

public sealed class SurfaceMaterialManager
{
    private const string SurfaceShaderName = ProjectRuntimeContracts.ShaderNames.WorldSurface;
    private const string RedRockKeyword = "KERN_SURFACE_REDROCK";
    private const string TransitKeyword = "KERN_SURFACE_TRANSIT";
    private const string PerspectiveKeyword = "KERN_SURFACE_PERSPECTIVE";

    private static readonly int _BaseMapID = Shader.PropertyToID("_BaseMap");
    private static readonly int _EmissionColorID = Shader.PropertyToID("_EmissionColor");
    private static readonly int _EmissionStrengthID = Shader.PropertyToID("_EmissionStrength");
    private static readonly int _OccupancyID = Shader.PropertyToID("_Occupancy");
    private static readonly int _BaseMapTileCountID = Shader.PropertyToID("_BaseMapTileCount");
    private static readonly int _WorldSizeID = Shader.PropertyToID("_WorldSize");

    public enum SurfaceKind
    {
        RedRock,
        Transit,
        Perspective,
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
        material.SetTexture(_BaseMapID, texture);
        material.SetColor(_EmissionColorID, emissionColor);
        material.SetFloat(_EmissionStrengthID, emissionStrength);
        material.SetFloat(_OccupancyID, occupancy);
        material.SetVector(
            _BaseMapTileCountID,
            new Vector4(baseMapTileCount.x, baseMapTileCount.y, 0f, 0f));
        material.SetVector(
            _WorldSizeID,
            new Vector4(worldSize.x, worldSize.y, 0f, 0f));
        material.EnableKeyword(kind switch
        {
            SurfaceKind.RedRock => RedRockKeyword,
            SurfaceKind.Transit => TransitKeyword,
            SurfaceKind.Perspective => PerspectiveKeyword,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown surface kind."),
        });
        return material;
    }

    public void ApplyMaterialConfig(
        Material material,
        Color emissionColor,
        float emissionStrength,
        float occupancy)
    {
        material.SetColor(_EmissionColorID, emissionColor);
        material.SetFloat(_EmissionStrengthID, emissionStrength);
        material.SetFloat(_OccupancyID, occupancy);
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
        transitMaterial.SetVector(_WorldSizeID, worldSize);
        perspectiveMaterial.SetVector(_WorldSizeID, worldSize);
        redRockMaterial.SetVector(_WorldSizeID, worldSize);
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
}

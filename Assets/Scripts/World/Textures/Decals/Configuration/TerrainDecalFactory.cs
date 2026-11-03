#nullable enable

using UnityEngine;

namespace Kern.World.Textures;

internal static class TerrainDecalFactory
{
    public const int TextureWidth = 512;
    public const int TextureHeight = 32;
    public const int VariantColumns = 16;
    public const int VariantRows = 1;
    public const int TileWidth = 32;
    public const int TileHeight = 32;
    public const byte PlacementPercent = 20;
    public const byte PlacementOffset = 32;
    public const float Strength = 1f;

    public static Vector4 CreateShaderInfo() => new(
        TileWidth,
        TileHeight,
        VariantColumns,
        VariantRows);
}

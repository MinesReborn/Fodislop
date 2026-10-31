#ifndef KERN_TERRAIN_DECALS_INCLUDED
#define KERN_TERRAIN_DECALS_INCLUDED

#include "Assets/Shaders/Terrain/Cells/Format/TerrainCellFormat.hlsl"

TEXTURE2D_ARRAY(_TerrainDecalAtlasArray);
SAMPLER(sampler_TerrainDecalAtlasArray);

float2 TerrainTransformDecalUV(float2 uv, uint rotation, bool mirror)
{
    if (mirror)
    {
        uv.x = 1.0 - uv.x;
    }

    if (rotation == 1u)
    {
        return float2(uv.y, 1.0 - uv.x);
    }

    if (rotation == 2u)
    {
        return 1.0 - uv;
    }

    if (rotation == 3u)
    {
        return float2(1.0 - uv.y, uv.x);
    }

    return uv;
}

// Код декали — DecalCode в TerrainCellFormat.hlsl.

// All atlases use hard light on linear surface color before illumination.

float3 ApplyTerrainDecal(float3 baseColor, float2 localUV, float packedTextureIndex, float packedPlacement)
{
    if (packedPlacement < 0.5)
    {
        return baseColor;
    }

    uint decalTextureIndex = (uint)round(packedTextureIndex);
    if (decalTextureIndex == 0u)
    {
        return baseColor;
    }

    uint code = (uint)round(packedPlacement) - 1u;

    uint variant   = code & KERN_TERRAIN_DECAL_VARIANT_MASK;
    uint rotation  = (code >> KERN_TERRAIN_DECAL_ROTATION_SHIFT) & 3u;
    bool mirror    = ((code >> KERN_TERRAIN_DECAL_MIRROR_SHIFT) & 1u) != 0u;
    uint offsetX   = (code >> KERN_TERRAIN_DECAL_OFFSET_X_SHIFT) & 3u;
    uint offsetY   = (code >> KERN_TERRAIN_DECAL_OFFSET_Y_SHIFT) & 3u;

    float2 transformedUV = TerrainTransformDecalUV(localUV, rotation, mirror);
    float2 placementOffset = float2(offsetX, offsetY) / 3.0 - 0.5;
    float4 atlasInfo = _TerrainDecalAtlasInfo[decalTextureIndex];
    float4 effect = _TerrainDecalEffects[decalTextureIndex];
    transformedUV += placementOffset * effect.y;

    uint columns = max((uint)atlasInfo.z, 1u);
    uint rows = max((uint)atlasInfo.w, 1u);
    uint variantCount = columns * rows;
    variant %= variantCount;
    // AtlasInfo.xy is the fixed client tile size; zw is the fixed grid layout.
    float2 tileSize = atlasInfo.xy;
    float2 atlasSize = tileSize * float2(columns, rows);
    float2 slotOrigin = float2(variant % columns, variant / columns) * tileSize;
    float2 pixel = slotOrigin + clamp(saturate(transformedUV) * tileSize, 0.5, tileSize - 0.5);
    float2 atlasUV = pixel / atlasSize;
    half4 decal = SAMPLE_TEXTURE2D_ARRAY_LOD(
        _TerrainDecalAtlasArray,
        sampler_TerrainDecalAtlasArray,
        atlasUV,
        decalTextureIndex,
        0.0);

    float strength = effect.x;
    float3 darken = baseColor * (2.0 * decal.rgb);
    float3 lighten = 1.0 - (1.0 - baseColor) * (2.0 - 2.0 * decal.rgb);
    float3 hardLight = lerp(darken, lighten, step(0.5, decal.rgb));
    return lerp(baseColor, hardLight, decal.a * strength);
}

#endif

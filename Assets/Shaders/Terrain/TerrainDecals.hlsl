#ifndef KERN_TERRAIN_DECALS_INCLUDED
#define KERN_TERRAIN_DECALS_INCLUDED

TEXTURE2D(_TerrainDecalAtlas);
SAMPLER(sampler_TerrainDecalAtlas);

TEXTURE2D(_TerrainDecalStoneAtlas);
SAMPLER(sampler_TerrainDecalStoneAtlas);

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

// Бит 12 (= 4096) кодирует «использовать stone-атлас».
// Биты 0..10 — variant/rotation/mirror/offsetX/offsetY — идентичны ground.
// Ground-код доходит до 2048, поэтому бит 11 не свободен: он уже занят
// старшей ground-декалью, и флаг на нём уводил бы её в чужой атлас.
static const uint STONE_ATLAS_BIT = 4096u;

// Ground-декали — пыль по полу: только осветляют и лишь на треть. Силу
// приносит свойство _GroundDecalStrength.

// Stone-декали лежат на красноскале и черноскале — очень тёмной основе.
// Там одно осветление на трети силы давало прибавку в пару единиц из 255:
// трещину нельзя было нарисовать трещиной. Поэтому у камня hard light:
// тёмный пиксель атласа затемняет (трещина), светлый осветляет (скол, блик
// руды), серый 0.5 нейтрален — и сила своя, свойство _StoneDecalStrength.

float3 TerrainDecalHardLight(float3 baseColor, float3 decal)
{
    float3 darken = baseColor * (2.0 * decal);
    float3 lighten = 1.0 - (1.0 - baseColor) * (2.0 - 2.0 * decal);
    return lerp(darken, lighten, step(0.5, decal));
}

float3 ApplyTerrainDecal(float3 baseColor, float2 localUV, float packedPlacement)
{
    if (packedPlacement < 0.5)
    {
        return baseColor;
    }

    uint rawCode   = (uint)round(packedPlacement);
    bool useStone  = (rawCode & STONE_ATLAS_BIT) != 0u;
    uint code      = (rawCode & ~STONE_ATLAS_BIT) - 1u;

    uint variant   = code & 15u;
    uint rotation  = (code >> 4u) & 3u;
    bool mirror    = ((code >> 6u) & 1u) != 0u;
    uint offsetX   = (code >> 7u) & 3u;
    uint offsetY   = (code >> 9u) & 3u;

    float2 transformedUV = TerrainTransformDecalUV(localUV, rotation, mirror);
    float2 placementOffset = float2(offsetX, offsetY) / 3.0 - 0.5;
    transformedUV += placementOffset * _DecalPlacementOffset;

    // Sixteen horizontal 32x32 slots in a 512x32 atlas. Sampling texel centres
    // prevents a transformed edge from crossing into the neighbouring slot.
    float2 pixel   = float2(variant * 32.0, 0.0) +
        clamp(saturate(transformedUV) * 32.0, 0.5, 31.5);
    float2 atlasUV = pixel / float2(512.0, 32.0);

    half4 decal;
    if (useStone)
    {
        decal = SAMPLE_TEXTURE2D_LOD(
            _TerrainDecalStoneAtlas,
            sampler_TerrainDecalStoneAtlas,
            atlasUV,
            0);
    }
    else
    {
        decal = SAMPLE_TEXTURE2D_LOD(
            _TerrainDecalAtlas,
            sampler_TerrainDecalAtlas,
            atlasUV,
            0);
    }

    if (useStone)
    {
        float3 hardLight = TerrainDecalHardLight(baseColor, decal.rgb);
        return lerp(baseColor, hardLight, decal.a * _StoneDecalStrength);
    }

    float3 screen = 1.0 - (1.0 - baseColor) * (1.0 - decal.rgb);
    return lerp(baseColor, screen, decal.a * _GroundDecalStrength);
}

#endif

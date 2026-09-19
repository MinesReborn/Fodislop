#ifndef KERN_TERRAIN_TILE_ADDRESSING_INCLUDED
#define KERN_TERRAIN_TILE_ADDRESSING_INCLUDED

static const float KernTileAddressEpsilon = 0.0001;

float KernPositiveModulo(float value, float modulus)
{
    return fmod(fmod(value, modulus) + modulus, modulus);
}

float2 KernResolveTerrainTileIndex(
    float2 gridPosition,
    float2 tileCount,
    float tileGroupColumn,
    float useTileGroupColumn)
{
    float2 integerGridPosition = floor(
        gridPosition + KernTileAddressEpsilon);
    float tileX = useTileGroupColumn > 0.5
        ? floor(tileGroupColumn + KernTileAddressEpsilon)
        : floor(
            KernPositiveModulo(integerGridPosition.x, tileCount.x) +
            KernTileAddressEpsilon);
    float tileY = floor(
        tileCount.y -
        KernTileAddressEpsilon -
        KernPositiveModulo(integerGridPosition.y, tileCount.y));
    return clamp(float2(tileX, tileY), 0.0, tileCount - 1.0);
}

float2 KernResolveTerrainSheetUv(
    float2 gridPosition,
    float2 localUv,
    float2 tileCount,
    float tileGroupColumn,
    float useTileGroupColumn)
{
    float2 tileIndex = KernResolveTerrainTileIndex(
        gridPosition,
        tileCount,
        tileGroupColumn,
        useTileGroupColumn);
    float2 safeLocalUv = clamp(
        localUv,
        KernTileAddressEpsilon,
        1.0 - KernTileAddressEpsilon);
    return (tileIndex + safeLocalUv) / tileCount;
}

#endif

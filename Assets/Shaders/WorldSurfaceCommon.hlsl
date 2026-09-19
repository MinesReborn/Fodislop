#ifndef KERN_WORLD_SURFACE_COMMON_INCLUDED
#define KERN_WORLD_SURFACE_COMMON_INCLUDED

#include "TerrainTileAddressing.hlsl"

float2 KernResolveRedRockUv(
    float2 worldPosition,
    float2 tileCount,
    float worldHeight)
{
    float2 cell = floor(worldPosition);
    float2 localUv = frac(worldPosition);
    float serverCellY = (worldHeight - 1.0) - cell.y;
    return KernResolveTerrainSheetUv(
        float2(cell.x, serverCellY),
        localUv,
        tileCount,
        0.0,
        0.0);
}

float2 KernResolveSurfaceUv(
    float2 uv,
    float2 worldPosition,
    float2 baseMapTileCount,
    float worldHeight)
{
#if defined(KERN_SURFACE_REDROCK)
    return KernResolveRedRockUv(
        worldPosition,
        baseMapTileCount,
        worldHeight);
#elif defined(KERN_SURFACE_TRANSIT)
    return float2(frac(uv.x), saturate(uv.y));
#elif defined(KERN_SURFACE_PERSPECTIVE)
    // Perspective is a finite authored band: repeat along the world horizon,
    // but sample its vertical profile exactly once.
    return float2(frac(uv.x), saturate(uv.y));
#else
    return uv;
#endif
}

#endif

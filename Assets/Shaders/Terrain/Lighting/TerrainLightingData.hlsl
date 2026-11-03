#ifndef KERN_TERRAIN_LIGHTING_DATA_INCLUDED
#define KERN_TERRAIN_LIGHTING_DATA_INCLUDED

// Декодеры флагов света и контура вершины; раскладка — TerrainCellFormat.hlsl.
#include "Assets/Shaders/Terrain/Cells/Format/TerrainCellFormat.hlsl"

uint KernTerrainLightingFlags(float packedFlags)
{
    return (uint)floor(packedFlags + 0.0001);
}

int KernTerrainForegroundSides(uint lightingFlags)
{
    return int(lightingFlags & KERN_TERRAIN_FOREGROUND_SIDES_MASK);
}

// Код каймы: 0 — клетки без каймы (каймы нет), иначе маска + 1.
int KernTerrainRimCode(float packedContour)
{
    return (int(round(packedContour)) >> KERN_TERRAIN_RIM_SHIFT) & KERN_TERRAIN_RIM_MASK;
}

int KernTerrainRimCornerMask(float packedContour)
{
    return (int(round(packedContour)) >> KERN_TERRAIN_RIM_CORNERS_SHIFT) & KERN_TERRAIN_RIM_CORNERS_MASK;
}

bool KernTerrainGlows(uint lightingFlags)
{
    return (lightingFlags & KERN_TERRAIN_GLOW_FLAG) != 0u;
}

bool KernTerrainIsPhysicalMass(uint lightingFlags)
{
    return (lightingFlags & KERN_TERRAIN_PHYSICAL_MASS_FLAG) != 0u;
}

bool KernTerrainReceivesAmbientOcclusion(uint lightingFlags)
{
    return !KernTerrainIsPhysicalMass(lightingFlags);
}

bool KernTerrainIsRoundable(float packedContour)
{
    return ((uint)round(packedContour) & KERN_TERRAIN_ROUNDABLE_CONTOUR_FLAG) != 0u;
}

float KernTerrainGlow(float packedFlags, uint lightingFlags)
{
    return KernTerrainGlows(lightingFlags)
        ? saturate(frac(packedFlags) / KERN_TERRAIN_GLOW_FRACTION_SCALE)
        : 0.0;
}

#endif

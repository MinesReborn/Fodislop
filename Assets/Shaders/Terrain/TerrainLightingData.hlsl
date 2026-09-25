#ifndef KERN_TERRAIN_LIGHTING_DATA_INCLUDED
#define KERN_TERRAIN_LIGHTING_DATA_INCLUDED

static const uint KERN_TERRAIN_SOLID_BOUNDARY_MASK = 0x0Fu;
static const uint KERN_TERRAIN_EMISSIVE_FLAG = 0x10u;
static const uint KERN_TERRAIN_PHYSICAL_MASS_FLAG = 0x20u;
static const uint KERN_TERRAIN_ROUNDABLE_CONTOUR_FLAG = 0x01u;

uint KernTerrainLightingFlags(float packedFlags)
{
    return (uint)floor(packedFlags + 0.0001);
}

int KernTerrainSolidBoundary(uint lightingFlags)
{
    return int(lightingFlags & KERN_TERRAIN_SOLID_BOUNDARY_MASK);
}

// Код рельефа: 0 — клетки без рельефа (каймы нет), иначе маска + 1.
// Раскладка описана в TerrainLightingData.Pack.
int KernTerrainReliefCode(float packedContour)
{
    return (int(round(packedContour)) >> 5) & 31;
}

int KernTerrainSolidDiagonal(float packedContour)
{
    return (int(round(packedContour)) >> 1) & int(KERN_TERRAIN_SOLID_BOUNDARY_MASK);
}

bool KernTerrainIsEmissive(uint lightingFlags)
{
    return (lightingFlags & KERN_TERRAIN_EMISSIVE_FLAG) != 0u;
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

float KernTerrainEmissionStrength(float packedFlags, uint lightingFlags)
{
    return KernTerrainIsEmissive(lightingFlags)
        ? saturate(frac(packedFlags) * 4.0)
        : 0.0;
}

#endif

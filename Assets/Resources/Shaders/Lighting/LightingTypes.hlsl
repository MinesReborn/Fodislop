#ifndef KERN_LIGHTING_TYPES_HLSL
#define KERN_LIGHTING_TYPES_HLSL

// Общие типы, константы и packing/unpacking функции для всей lighting-системы.
//
// ЭТОТ ФАЙЛ НЕ ДОЛЖЕН ЗНАТЬ ни о каскадах, ни об источниках, ни о bounce.
// Он содержит только базовые типы и константы, которые используются всеми стадиями.

#define PI 3.14159265359
#define PI2 6.28318530718

// Dynamic light below this absolute radiance cannot move any display level
// (see TraceLightSegment). Not a tuning value: derived from output precision.
static const float InvisibleDynamicRadiance = 1e-6;

static const float DynamicNearCells = 6.0;
static const int DynamicEmitterPointsPerAxis = 3;

uint2 PackRadiance(float3 radiance)
{
    return uint2(
        f32tof16(radiance.r) | (f32tof16(radiance.g) << 16),
        f32tof16(radiance.b));
}

float3 UnpackRadiance(uint2 packedRadiance)
{
    return float3(
        f16tof32(packedRadiance.x & 0xffff),
        f16tof32(packedRadiance.x >> 16),
        f16tof32(packedRadiance.y & 0xffff));
}

uint3 PackInterval(float3 radiance, float3 transmittance)
{
    return uint3(
        f32tof16(radiance.r) | (f32tof16(radiance.g) << 16),
        f32tof16(radiance.b) | (f32tof16(transmittance.r) << 16),
        f32tof16(transmittance.g) | (f32tof16(transmittance.b) << 16));
}

float3 UnpackTransmittance(uint3 packedInterval)
{
    return float3(
        f16tof32(packedInterval.y >> 16),
        f16tof32(packedInterval.z & 0xffff),
        f16tof32(packedInterval.z >> 16));
}

struct DynamicLight
{
    float4 positionRadius;
    float4 colorIntensity;
};

struct DynamicTileInfo
{
    int2 fieldOrigin;
    int2 size;
    int2 tileOffset;
};

#endif // KERN_LIGHTING_TYPES_HLSL

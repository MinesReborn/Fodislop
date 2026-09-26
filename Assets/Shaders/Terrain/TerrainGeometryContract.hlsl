#ifndef KERN_TERRAIN_GEOMETRY_CONTRACT_INCLUDED
#define KERN_TERRAIN_GEOMETRY_CONTRACT_INCLUDED

// Canonical shader-side representation of a terrain cell silhouette.
// Coordinates are cell-local; corners and derived bend vertices snap to the
// same 1/32 geometry grid. Surface-map lookups may address its pixel centers;
// silhouette coverage always tests the continuous raster sample.
static const float KERN_TERRAIN_FACE_GRID_SIZE = 32.0;
static const int KERN_TERRAIN_ORGANIC_EDGE_BASE = 5;
static const int KERN_TERRAIN_ORGANIC_EDGE_CENTER = 2;
static const int KERN_TERRAIN_ORGANIC_EDGE_COUNT = 4;
static const int KERN_TERRAIN_ORGANIC_EDGE_CODE_OFFSET = 1;
static const int KERN_TERRAIN_ORGANIC_EDGE_META_OFFSET = 128;

float2 QuantizeTerrainGeometryPoint(float2 geometryPosition)
{
    return round(geometryPosition * KERN_TERRAIN_FACE_GRID_SIZE) /
        KERN_TERRAIN_FACE_GRID_SIZE;
}

float2 QuantizeTerrainPixelCenter(float2 position)
{
    return (floor(position * KERN_TERRAIN_FACE_GRID_SIZE) + 0.5) /
        KERN_TERRAIN_FACE_GRID_SIZE;
}

// Meta.b contains the low code byte. Meta.a distinguishes regular (0),
// classic geometry (255), and organic geometry (128 + high code bits).
float2 DecodeTerrainGeometryMetadata(float4 meta, bool geometryLayer)
{
    bool anchored = geometryLayer && meta.a > 0.5;
    bool organic = anchored && meta.a < 0.75;
    float edgeCode = organic
        ? round(meta.b * 255.0) + 256.0 *
            (round(meta.a * 255.0) - KERN_TERRAIN_ORGANIC_EDGE_META_OFFSET)
        : 0.0;
    return float2(anchored ? 1.0 : 0.0, edgeCode);
}

#endif

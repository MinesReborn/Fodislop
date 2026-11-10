// Standalone HLSL grammar/type check. This minimal shell deliberately supplies
// only the surface signature needed by the include; it is not a render oracle.
#define KERN_TERRAIN_FACE_GRID_SIZE 32.0
struct TerrainSurfaceInputs { float anchored; };
float EvaluateTerrainCellCoverage(TerrainSurfaceInputs surface, float antialiasScale, float applyGeometry)
{
    return surface.anchored;
}
#include "Assets/Shaders/Terrain/Geometry/TerrainDepth.hlsl"
float4 Main(float4 position : POSITION) : SV_POSITION
{
    return float4(TerrainVolumeProjectPoint(position.xy, float2(0, 0), 1, 16), 0, 1);
}

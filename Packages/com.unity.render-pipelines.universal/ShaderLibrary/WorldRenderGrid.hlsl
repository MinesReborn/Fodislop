#ifndef KERN_WORLD_RENDER_GRID_INCLUDED
#define KERN_WORLD_RENDER_GRID_INCLUDED

// Renderer-owned world-pass contract. Density is zero for UI, field transport,
// scene/preview cameras; the game world's visible passes use 32 pixels/cell.
// Vertex positions use lattice boundaries; shading positions use pixel centers.
float _KernWorldGridDensity;

float3 KernWorldGridVertex(float3 worldPosition)
{
    if (_KernWorldGridDensity > 0.0)
    {
        worldPosition.xy = floor(worldPosition.xy * _KernWorldGridDensity + 0.5) / _KernWorldGridDensity;
    }
    return worldPosition;
}

float2 KernWorldGridPixelCenter(float2 worldPosition)
{
    if (_KernWorldGridDensity > 0.0)
    {
        worldPosition = (floor(worldPosition * _KernWorldGridDensity) + 0.5) / _KernWorldGridDensity;
    }
    return worldPosition;
}

// Subtract the camera translation before projection. A precombined VP matrix
// loses fractional-cell precision at large world Y (the game world is ~40000
// cells tall); changing zoom then changes atlas/lighting sample positions.
float4 KernWorldGridClipPosition(float3 worldPosition)
{
    return mul(UNITY_MATRIX_P, mul(UNITY_MATRIX_V, float4(worldPosition, 1.0)));
}


#endif

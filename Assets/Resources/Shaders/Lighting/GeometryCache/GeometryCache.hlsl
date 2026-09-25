#ifndef KERN_GEOMETRY_CACHE_HLSL
#define KERN_GEOMETRY_CACHE_HLSL

// BuildCellSolidMask: кэш геометрии для closed-diagonal rule.
//
// READS: _MaterialField
// WRITES: _CellSolidMaskOutput, _SurfaceAirCacheOutput
// MUST NOT: знать о каскадах и источниках


[numthreads(8, 8, 1)]
void BuildCellSolidMask(uint3 dispatchId : SV_DispatchThreadID)
{
    if (any(dispatchId.xy >= (uint2)_CellGridSize))
    {
        return;
    }

    int2 cell = int2(dispatchId.xy);
    float2 cellsPerPixel = (_WorldRect.zw / _CellSize) / float2(_FieldSize);
    bool isSolid = false;
    SampleCellSolid(cell, cellsPerPixel, isSolid);
    _CellSolidMaskOutput[cell] = float4(isSolid ? 1.0 : 0.0, 0.0, 0.0, 0.0);
}

// First air texel in each cardinal direction, cached for the dynamic composite.
// The material field is static between geometry rebuilds; repeating this scan
// for every moving light used up to 16 material loads per solid output pixel.
[numthreads(8, 8, 1)]
void BuildSurfaceAirCache(uint3 dispatchId : SV_DispatchThreadID)
{
    int2 pixel = int2(dispatchId.xy);
    if (any(pixel >= _FieldSize))
    {
        return;
    }

    int2 materialPixel = MaterialPixel(pixel);
    if (saturate(_MaterialField.Load(int3(materialPixel, 0)).a) <= 0.0)
    {
        _SurfaceAirCacheOutput[pixel] = 0.0;
        return;
    }

    float2 pixelsPerCell = float2(_FieldSize) * _CellSize / _WorldRect.zw;
    int2 offsets[4] =
    {
        int2(-1, 0), int2(1, 0), int2(0, -1), int2(0, 1)
    };
    float4 firstAir = 0.0;
    [unroll]
    for (int direction = 0; direction < 4; direction++)
    {
        int reach = max(1, (int)ceil(abs(dot(float2(offsets[direction]), pixelsPerCell))));
        [loop]
        for (int stepIndex = 1; stepIndex <= reach; stepIndex++)
        {
            int2 neighbor = pixel + offsets[direction] * stepIndex;
            if (any(neighbor < 0) || any(neighbor >= _FieldSize))
            {
                break;
            }

            int2 materialNeighbor = MaterialPixel(neighbor);
            if (IsSolidOccupancy(_MaterialField.Load(int3(materialNeighbor, 0)).a))
            {
                continue;
            }

            firstAir[direction] = float(stepIndex);
            break;
        }
    }

    _SurfaceAirCacheOutput[pixel] = firstAir;
}

#endif // KERN_GEOMETRY_CACHE_HLSL

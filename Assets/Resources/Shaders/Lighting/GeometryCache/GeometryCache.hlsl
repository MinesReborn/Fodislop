#ifndef KERN_GEOMETRY_CACHE_HLSL
#define KERN_GEOMETRY_CACHE_HLSL

// BuildCellSolidMask: доказательства однородности клеток для пропуска клетки в DDA.
//
// READS: _MaterialField
// WRITES: _CellSolidMaskOutput, _SurfaceAirCacheOutput
// MUST NOT: знать о каскадах и источниках


// First field texel of a cell (cell texel ranges are [first(c), first(c + 1))).
int2 CellFirstTexel(int2 cell)
{
    float2 cellsPerPixel = (_WorldRect.zw / _CellSize) / float2(_FieldSize);
    return int2(round(float2(cell) / cellsPerPixel));
}

[numthreads(8, 8, 1)]
void BuildCellSolidMask(uint3 dispatchId : SV_DispatchThreadID)
{
    if (any(dispatchId.xy >= (uint2)_CellGridSize))
    {
        return;
    }

    int2 cell = int2(dispatchId.xy);
    // A transport shortcut needs a proof over EVERY base texel, including
    // partial silhouettes and emissive non-solid materials. Corner sealing
    // reads the texels themselves (CornerSealed), never a cell-centre sample.
    int2 first = CellFirstTexel(cell);
    int2 last = min(CellFirstTexel(cell + 1), _FieldSize);
    float firstOccupancy = _MaterialField.Load(int3(MaterialPixel(first), 0)).a;
    bool uniformOccupancy = true;
    bool emissionFree = true;
    bool uniformSolid = true;
    [loop]
    for (int y = first.y; y < last.y && (uniformOccupancy || uniformSolid); y++)
    {
        [loop]
        for (int x = first.x; x < last.x && (uniformOccupancy || uniformSolid); x++)
        {
            int2 materialPixel = MaterialPixel(int2(x, y));
            float occupancy = _MaterialField.Load(int3(materialPixel, 0)).a;
            uniformOccupancy = uniformOccupancy && occupancy == firstOccupancy;
            if (emissionFree && uniformOccupancy)
            {
                emissionFree = all(_EmissionField.Load(int3(materialPixel, 0)).rgb == 0.0);
            }
            uniformSolid = uniformSolid && IsSolidOccupancy(occupancy);
        }
    }
    // R proves clean air: every texel has zero occupancy and zero emission,
    // so any path through the cell is pure air transport (CleanCellCount).
    bool cleanAir = uniformOccupancy && emissionFree && firstOccupancy == 0.0;
    _CellSolidMaskOutput[cell] = float4(cleanAir ? 1.0 : 0.0,
        uniformOccupancy && emissionFree ? 1.0 : 0.0,
        uniformSolid ? 1.0 : 0.0, uniformOccupancy ? 1.0 : 0.0);
    // G proves constant extinction AND zero static emission; A proves constant
    // extinction alone (dynamic transport does not collect static emission).
    // Store proof flags, not half-precision occupancy. The traverser loads the
    // original UNorm material alpha once per proven cell, preserving its value.
}

// Summed-area tables of cells that are NOT clean air (x: mask R == 0) and
// NOT clean stone (y), so a ray neighbourhood can be proven one uniform
// medium with four loads. Clean stone: uniform, emission-free (mask G) and
// fully occupied — any path through it is solid transport at occupancy 1.
// Rows first, then columns; rebuilt with the mask.
[numthreads(64, 1, 1)]
void BuildCleanCellRows(uint3 id : SV_DispatchThreadID)
{
    int y = (int)id.x;
    if (y >= _CellGridSize.y)
    {
        return;
    }
    uint2 sum = uint2(0u, 0u);
    for (int x = 0; x < _CellGridSize.x; x++)
    {
        float4 proof = _CellSolidMask.Load(int3(x, y, 0));
        bool cleanStone = proof.g == 1.0 &&
            _MaterialField.Load(int3(MaterialPixel(CellFirstTexel(int2(x, y))), 0)).a == 1.0;
        sum += uint2(proof.r == 1.0 ? 0u : 1u, cleanStone ? 0u : 1u);
        _CleanCellRowsOutput[y * _CellGridSize.x + x] = sum;
    }
}

[numthreads(64, 1, 1)]
void BuildCleanCellColumns(uint3 id : SV_DispatchThreadID)
{
    int x = (int)id.x;
    if (x >= _CellGridSize.x)
    {
        return;
    }
    uint2 sum = uint2(0u, 0u);
    for (int y = 0; y < _CellGridSize.y; y++)
    {
        int index = y * _CellGridSize.x + x;
        sum += _CleanCellRows[index];
        _CleanCellPrefixOutput[index] = sum;
    }
}

// First air texel in each cardinal direction, cached for the dynamic composite.
// The material field is static between geometry rebuilds; repeating this scan
// for every moving light used up to 16 material loads per solid output pixel.
[numthreads(8, 8, 1)]
void BuildSurfaceAirCache(uint3 dispatchId : SV_DispatchThreadID)
{
    // Built on the light lattice read by CompositeLighting. Solidity of a
    // receiver texel is the transport material at its centre.
    int2 pixel = int2(dispatchId.xy);
    if (any(pixel >= _LightSize))
    {
        return;
    }

    int2 materialPixel = MaterialPixel(LightPxToFieldTexel(pixel));
    if (saturate(_MaterialField.Load(int3(materialPixel, 0)).a) <= 0.0)
    {
        _SurfaceAirCacheOutput[pixel] = 0.0;
        return;
    }

    float2 pixelsPerCell = float2(_LightSize) * _CellSize / _WorldRect.zw;
    int2 cell = int2(floor((float2(pixel) + 0.5) / pixelsPerCell));
    bool uniformSolid = _UniformCellTraversalEnabled != 0 &&
        _CellSolidMask.Load(int3(cell, 0)).b == 1.0;
    int2 cellFirst = int2(round(float2(cell) * pixelsPerCell));
    int2 cellLast = int2(round(float2(cell + 1) * pixelsPerCell));
    int2 offsets[4] =
    {
        int2(-1, 0), int2(1, 0), int2(0, -1), int2(0, 1)
    };
    float4 firstAir = 0.0;
    [unroll]
    for (int direction = 0; direction < 4; direction++)
    {
        // Composite weights a texel by its centre depth, (step - 0.5) texels
        // from the face; search exactly the steps that can be inside reach.
        int reach = max(0, (int)ceil(
            _SurfaceReflectionReachCells * abs(dot(float2(offsets[direction]), pixelsPerCell)) + 0.5) - 1);
        [loop]
        // In an exhaustively solid cell, the first possible air lies beyond
        // its boundary. Interior reads would all return the same answer.
        int firstStep = 1;
        if (uniformSolid)
        {
            if (direction == 0) { firstStep = pixel.x - cellFirst.x + 1; }
            if (direction == 1) { firstStep = cellLast.x - pixel.x; }
            if (direction == 2) { firstStep = pixel.y - cellFirst.y + 1; }
            if (direction == 3) { firstStep = cellLast.y - pixel.y; }
        }
        for (int stepIndex = firstStep; stepIndex <= reach; stepIndex++)
        {
            int2 neighbor = pixel + offsets[direction] * stepIndex;
            if (any(neighbor < 0) || any(neighbor >= _LightSize))
            {
                break;
            }

            int2 materialNeighbor = MaterialPixel(LightPxToFieldTexel(neighbor));
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

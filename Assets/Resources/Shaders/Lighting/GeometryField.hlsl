#ifndef KERN_GEOMETRY_FIELD_HLSL
#define KERN_GEOMETRY_FIELD_HLSL

// Утилиты для работы с material/emission полями.
//
// READS: _MaterialField, _EmissionField
// WRITES: ничего
// MUST NOT: знать о каскадах, источниках, bounce

float Max3(float3 value)
{
    return max(value.x, max(value.y, value.z));
}

float2 OutputUv(float2 pixelPosition)
{
    return saturate(pixelPosition / float2(_FieldSize));
}

float InterleavedGradientNoise(float2 pixelCoord)
{
    float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
    return frac(magic.z * frac(dot(pixelCoord, magic.xy)));
}

// Nearest field pixel to the center of the world cell containing `pixel`.
// Used by the PerBlock tier so every pixel inside one cell reads the same
// atlas probe, regardless of how many field pixels the cell actually spans.
void GetBlockSnappedPixel(int2 pixel, out int2 result)
{
    result = pixel;
    if (_BlockAveraged != 0)
    {
        float safeCellSize = max(_CellSize, 0.0001);
        float2 safeFieldSize = max(float2(_FieldSize), float2(1.0, 1.0));
        float2 regionCellCount = max(_WorldRect.zw / safeCellSize, float2(0.0001, 0.0001));
        float2 cell = floor((float2(pixel) + 0.5) * regionCellCount / safeFieldSize);
        float2 cellCenterFieldPos = (cell + 0.5) * safeFieldSize / regionCellCount;
        result = clamp(int2(floor(cellCenterFieldPos)), int2(0, 0), _FieldSize - 1);
    }
}

float2 MaterialUv(float2 pixelPosition)
{
    float2 uv = OutputUv(pixelPosition);
    if (_MaterialYFlip != 0)
    {
        uv.y = 1.0 - uv.y;
    }

    return uv;
}

float SampleOccupancy(float2 pixelPosition, float mipLevel)
{
    float4 material = 0.0;
    material = _MaterialField.SampleLevel(
        sampler_LinearClamp,
        MaterialUv(pixelPosition),
        mipLevel);
    return material.a;
}

float PathLengthInCells(float2 rayDirection, float pathLengthInPixels)
{
    float2 cellsPerPixel = (_WorldRect.zw / _CellSize) / float2(_FieldSize);
    return length(rayDirection * cellsPerPixel * pathLengthInPixels);
}

#endif // KERN_GEOMETRY_FIELD_HLSL

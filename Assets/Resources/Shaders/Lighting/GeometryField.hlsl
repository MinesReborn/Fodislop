#ifndef KERN_GEOMETRY_FIELD_HLSL
#define KERN_GEOMETRY_FIELD_HLSL

// Утилиты для работы с material/emission полями.
//
// READS: _MaterialField, _EmissionField
// WRITES: ничего
// MUST NOT: знать о каскадах и источниках

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

// Пороги solidity приходят юниформами _SolidOccupancyThreshold и
// _TransportSolidThreshold: авторские значения живут в VisualTuning.cs.
// TransportSolidThreshold ниже, чем SolidOccupancyThreshold: транспорт
// считает частично покрытый тексель блокирующим раньше, чтобы свет не тёк
// сквозь полупрозрачные кромки. Значения не унифицировать без перепроверки
// транспорта.

bool IsSolidOccupancy(float occupancy)
{
    return occupancy >= _SolidOccupancyThreshold;
}

// Compute-пиксель в тексель текстуры материала (Y-флип).
int2 MaterialPixel(int2 pixel)
{
    int2 materialPixel = pixel;
    if (_MaterialYFlip != 0)
    {
        materialPixel.y = _FieldSize.y - 1 - materialPixel.y;
    }

    return materialPixel;
}

float PathLengthInCells(float2 rayDirection, float pathLengthInPixels)
{
    float2 cellsPerPixel = (_WorldRect.zw / _CellSize) / float2(_FieldSize);
    return length(rayDirection * cellsPerPixel * pathLengthInPixels);
}

#endif // KERN_GEOMETRY_FIELD_HLSL

#ifndef KERN_BOUNCE_CACHE_HLSL
#define KERN_BOUNCE_CACHE_HLSL

// BuildBounceTaps и BuildBounceFilter: кэширование geometry для diffuse bounce.
//
// READS: _MaterialField
// WRITES: _BounceTaps, _BounceFilterWeights
// MAY: вызывать DDA (TraceRadianceSegment)
// MUST NOT: трогать каскады, источники

// The gather geometry of SolveDiffuseBounce: where each of the 8 rotated rays
// first strikes a surface and how much of that surface's reflection reaches
// the receiver. Depends only on the material field, never on light.
[numthreads(8, 8, 1)]
void BuildBounceTaps(uint3 dispatchId : SV_DispatchThreadID)
{
    if (any(dispatchId.xy >= (uint2)_BounceSize))
    {
        return;
    }

    int2 pixel = int2(dispatchId.xy);
    float2 fieldPosition = (float2(pixel) + 0.5) * float2(_FieldSize) / float2(_BounceSize);

    float2 cellsPerPixel = (_WorldRect.zw / _CellSize) / float2(_FieldSize);
    float pixelsPerCell = 1.0 / max(cellsPerPixel.x, 0.0001);

    // Rotate the eight directions per receiver; reconstruction checks visibility.
    float jitter = InterleavedGradientNoise(float2(pixel));
    float baseAngle = jitter * (PI2 / 8.0);
    uint baseIndex = ((uint)pixel.y * (uint)_BounceSize.x + (uint)pixel.x) * 16u;

    // The hit branch traces a variable-length DDA path. Keep both loops
    // dynamic so the Metal cross-compiler does not expand that path 8 x 16 times.
    [loop]
    for (int d = 0; d < 8; d++)
    {
        float4 bounceAlbedo = 0.0;
        float4 bounceTransmission = 0.0;
        float angle = baseAngle + float(d) * (PI2 / 8.0);
        float raySin;
        float rayCos;
        sincos(angle, raySin, rayCos);
        float2 rayDir = float2(rayCos, raySin);

        float2 prevPos = fieldPosition;
        int2 prevCell = int2(floor(fieldPosition * cellsPerPixel));

        [loop]
        for (int stepIdx = 1; stepIdx <= 16; stepIdx++)
        {
            float distCells = float(stepIdx) * 0.25;
            float2 samplePos = fieldPosition + rayDir * (distCells * pixelsPerCell);
            if (any(samplePos < 0.0) || any(samplePos >= float2(_FieldSize)))
            {
                break;
            }

            int2 currentCell = int2(floor(samplePos * cellsPerPixel));
            bool diagonalOccluded = false;
            CheckDiagonalStepOccluded(prevCell, currentCell, diagonalOccluded);
            if (diagonalOccluded)
            {
                break;
            }

            prevCell = currentCell;

            float4 mat = _MaterialField.SampleLevel(sampler_LinearClamp, MaterialUv(samplePos), 0);
            float solid = saturate(mat.a);

            if (solid > 0.2) // Struck an illuminated solid surface
            {
                float3 unusedRadiance;
                float3 transmission;
                TraceRadianceSegment(fieldPosition, prevPos, false, unusedRadiance, transmission);

                float mCh = max(mat.r, max(mat.g, mat.b));
                float3 albedo = mCh > 0.04
                    ? (mat.rgb / mCh) * max(mCh, 0.4)
                    : float3(0.4, 0.38, 0.36);
                albedo = min(albedo, 0.75);

                // Factors are stored separately so the runtime multiplies them
                // in the original order: bit-identical, not merely close.
                bounceAlbedo = float4(albedo, float(stepIdx));
                bounceTransmission = float4(transmission, 0.0);
                break; // Opaque surface occludes further ray travel
            }

            prevPos = samplePos;
        }

        _BounceTaps[baseIndex + (uint)d * 2u] = bounceAlbedo;
        _BounceTaps[baseIndex + (uint)d * 2u + 1u] = bounceTransmission;
    }
}

[numthreads(8, 8, 1)]
void BuildBounceFilter(uint3 dispatchId : SV_DispatchThreadID)
{
    if (any(dispatchId.xy >= (uint2)_FieldSize))
    {
        return;
    }

    int2 pixel = int2(dispatchId.xy);
    float2 uv = (float2(pixel) + 0.5) / float2(_FieldSize);
    float2 position = uv * float2(_BounceSize) - 0.5;
    int2 basePixel = int2(floor(position));
    float2 blend = frac(position);
    uint baseIndex = ((uint)pixel.y * (uint)_FieldSize.x + (uint)pixel.x) * 4u;
    [unroll]
    for (int y = 0; y < 2; y++)
    {
        [unroll]
        for (int x = 0; x < 2; x++)
        {
            int2 tap = clamp(basePixel + int2(x, y), int2(0, 0), _BounceSize - 1);
            float weight = (x == 0 ? 1.0 - blend.x : blend.x) * (y == 0 ? 1.0 - blend.y : blend.y);
            float2 tapPosition = (float2(tap) + 0.5) * float2(_FieldSize) / float2(_BounceSize);
            float3 unusedRadiance;
            float3 transmission;
            TraceRadianceSegment(uv * float2(_FieldSize), tapPosition, false, unusedRadiance, transmission);
            _BounceFilterWeights[baseIndex + (uint)(y * 2 + x)] = float4(transmission, weight);
        }
    }
}

#endif // KERN_BOUNCE_CACHE_HLSL

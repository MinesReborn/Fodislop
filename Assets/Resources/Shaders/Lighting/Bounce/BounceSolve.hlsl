#ifndef KERN_BOUNCE_SOLVE_HLSL
#define KERN_BOUNCE_SOLVE_HLSL

// SolveDiffuseBounce и вспомогательные функции: diffuse bounce solution.
//
// READS: _MaterialField, _BounceTaps, _DirectInput, _StaticDirectInput, _BounceInput, _BounceFilterWeights
// WRITES: _BounceTexture
// MUST NOT: вызывать DDA, трогать каскады, источники

// Wall-aware bilinear read of the half-resolution bounce. The taps are the
// same every frame; their transmission-weighted coefficients come from
// BuildBounceFilter.
float3 SampleBounceFiltered(int2 pixel, float2 uv)
{
    float2 position = uv * float2(_BounceSize) - 0.5;
    int2 basePixel = int2(floor(position));
    uint baseIndex = ((uint)pixel.y * (uint)_FieldSize.x + (uint)pixel.x) * 4u;
    float3 result = 0.0;
    [unroll]
    for (int y = 0; y < 2; y++)
    {
        [unroll]
        for (int x = 0; x < 2; x++)
        {
            int2 tap = clamp(basePixel + int2(x, y), int2(0, 0), _BounceSize - 1);
            float4 tapFilter = _BounceFilterWeights[baseIndex + (uint)(y * 2 + x)];
            result += _BounceInput.Load(int3(tap, 0)).rgb * tapFilter.rgb * tapFilter.a;
        }
    }

    return result;
}

[numthreads(8, 8, 1)]
void SolveDiffuseBounce(uint3 dispatchId : SV_DispatchThreadID)
{
    // Partial dispatch for dynamic-only frames: the host sets a bounce-space
    // origin/size covering the dynamic rect union plus gather margin. A
    // non-positive size keeps the legacy full-field behavior (native test
    // harness and any path that did not set the uniforms).
    int2 dispatchOrigin = _BounceDispatchOrigin;
    int2 dispatchSize = _BounceDispatchSize;
    if (dispatchSize.x <= 0 || dispatchSize.y <= 0)
    {
        dispatchOrigin = int2(0, 0);
        dispatchSize = _BounceSize;
    }

    if (any(int2(dispatchId.xy) >= dispatchSize))
    {
        return;
    }

    int2 pixel = dispatchOrigin + int2(dispatchId.xy);
    if (any(pixel < 0) || any(pixel >= _BounceSize))
    {
        return;
    }
    float bounceStrength = (_DebugView == 7) ? max(_BounceStrength, 1.0) : _BounceStrength;
    if (_EnableDiffuseBounce == 0 && _DebugView != 7)
    {
        _BounceTexture[pixel] = 0.0;
        return;
    }

    if (bounceStrength <= 0.0001)
    {
        _BounceTexture[pixel] = 0.0;
        return;
    }

    float2 fieldPosition = (float2(pixel) + 0.5) * float2(_FieldSize) / float2(_BounceSize);

    float4 centerMat = _MaterialField.SampleLevel(sampler_LinearClamp, MaterialUv(fieldPosition), 0);
    float centerSolid = saturate(centerMat.a);

    float2 cellsPerPixel = (_WorldRect.zw / _CellSize) / float2(_FieldSize);
    float pixelsPerCell = 1.0 / max(cellsPerPixel.x, 0.0001);
    float jitter = InterleavedGradientNoise(float2(pixel));
    float baseAngle = jitter * (PI2 / 8.0);
    uint baseIndex = ((uint)pixel.y * (uint)_BounceSize.x + (uint)pixel.x) * 16u;

    float3 gatheredBounce = 0.0;

    [loop]
    for (int d = 0; d < 8; d++)
    {
        float4 bounceAlbedo = _BounceTaps[baseIndex + (uint)d * 2u];
        if (bounceAlbedo.a <= 0.0)
        {
            continue;
        }

        float angle = baseAngle + float(d) * (PI2 / 8.0);
        float raySin;
        float rayCos;
        sincos(angle, raySin, rayCos);
        float2 rayDir = float2(rayCos, raySin);

        // The same position the march stood on one step before the hit.
        int hitStep = int(bounceAlbedo.a);
        float2 prevPos = fieldPosition;
        if (hitStep > 1)
        {
            float distCells = float(hitStep - 1) * 0.25;
            prevPos = fieldPosition + rayDir * (distCells * pixelsPerCell);
        }

        // The air immediately in front of the hit is the incident side.
        // Reading the solid's averaged lighting imports its opposite face.
        float2 uvPrev = OutputUv(prevPos);
        float3 incident =
            _DirectInput.SampleLevel(sampler_LinearClamp, uvPrev, 0).rgb +
            _StaticDirectInput.SampleLevel(sampler_LinearClamp, uvPrev, 0).rgb;
        float3 transmission = _BounceTaps[baseIndex + (uint)d * 2u + 1u].rgb;
        gatheredBounce += incident * bounceAlbedo.rgb * transmission;
    }

    // Angular integration over 8 directions (delta theta = 2*pi / 8).
    float3 scatteredInAir = gatheredBounce * (PI2 / 8.0);
    _BounceTexture[pixel] = float4(scatteredInAir * (1.0 - centerSolid) * bounceStrength, 1.0);
}

#endif // KERN_BOUNCE_SOLVE_HLSL

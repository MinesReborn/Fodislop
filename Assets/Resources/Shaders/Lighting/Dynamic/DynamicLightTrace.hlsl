#ifndef KERN_DYNAMIC_LIGHT_TRACE_HLSL
#define KERN_DYNAMIC_LIGHT_TRACE_HLSL

// SolveDynamicLighting и ComposeDynamicLighting: per-light tile tracing и композиция.
// При одном источнике SolveDynamicLighting может сразу писать DirectTexture.
//
// READS: _DynamicPolar, _DynamicLights, _DynamicTileInfos
// WRITES: _DynamicTiles, _DirectTexture
// MUST NOT: трогать каскады

// Receivers are bounded only by the analytic air-reach rectangle on the CPU
// (weakest extinction, so it is conservative). A per-receiver GPU reach
// radius used to force radiance to zero: whenever the traced reach came out
// short, the light ended in a hard circle of the near-zone floor radius
// (1.5 · (near + 1) cells) around every source.

[numthreads(8, 8, 1)]
void SolveDynamicLighting(uint3 dispatchId : SV_DispatchThreadID)
{
    if (any(int2(dispatchId.xy) >= _DynamicDispatchSize))
    {
        return;
    }

    // Receivers are light-lattice texels; transport runs on the field lattice.
    int2 pixel = _DynamicDispatchOrigin + int2(dispatchId.xy);
    if (any(pixel < 0) || any(pixel >= _LightSize))
    {
        return;
    }

    float2 origin = LightPxCenterToFieldPx(pixel);
    DynamicLight light = _DynamicLights[_DynamicLightIndex];

    if (_WriteDynamicDirect == 0 && _DynamicTilesScalarRadiance != 0)
    {
        // Neutral extinction makes each source's RGB radiance rank one.
        // Trace its brightest channel; keep that coefficient in float32.
        // Source RGB and the absolute visibility bound remain unchanged.
        float peak = Max3(max(light.colorIntensity.rgb, 0.0));
        light.colorIntensity.rgb = float3(peak, peak, peak);
    }
    float3 radiance = DynamicRadianceFromPolar(origin, light, _DynamicAngularSampleCount);

    if (_WriteDynamicDirect != 0)
    {
        _DirectTexture[pixel] = float4(radiance, 1.0);
    }
    else
    {
        _DynamicTiles[int3(_DynamicTileOffset + int2(dispatchId.xy), _DynamicReachIndex)] = float4(radiance, 1.0);
    }
}

[numthreads(8, 8, 1)]
void ClearDynamicDirect(uint3 dispatchId : SV_DispatchThreadID)
{
    if (any(int2(dispatchId.xy) >= _DynamicDispatchSize))
    {
        return;
    }

    int2 pixel = _DynamicDispatchOrigin + int2(dispatchId.xy);
    if (any(pixel < 0) || any(pixel >= _LightSize))
    {
        return;
    }

    _DirectTexture[pixel] = 0.0;
}

[numthreads(8, 8, 1)]
void ComposeDynamicLighting(uint3 dispatchId : SV_DispatchThreadID)
{
    if (any(int2(dispatchId.xy) >= _ComposeSize))
    {
        return;
    }

    int2 pixel = _ComposeOrigin + int2(dispatchId.xy);
    if (any(pixel < 0) || any(pixel >= _LightSize))
    {
        return;
    }

    float3 radiance = 0.0;
    [loop]
    for (int tileIndex = 0; tileIndex < _DynamicTileCount; tileIndex++)
    {
        DynamicTileInfo tile = _DynamicTileInfos[tileIndex];
        int2 local = pixel - tile.fieldOrigin;
        if (all(local >= 0) && all(local < tile.size))
        {
            DynamicLight light = _DynamicLights[tileIndex];
            float3 contribution = _DynamicTilesInput.Load(int4(tile.tileOffset + local, tile.reachIndex, 0)).rgb;
            if (_DynamicTilesScalarRadiance != 0)
            {
                float3 sourceColor = max(light.colorIntensity.rgb, 0.0);
                contribution = contribution.r * sourceColor / max(Max3(sourceColor), 1e-30);
            }
            radiance += contribution;
        }
    }

    _DirectTexture[pixel] = float4(radiance, 1.0);
}

#endif // KERN_DYNAMIC_LIGHT_TRACE_HLSL

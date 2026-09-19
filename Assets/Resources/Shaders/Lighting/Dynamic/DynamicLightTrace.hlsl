#ifndef KERN_DYNAMIC_LIGHT_TRACE_HLSL
#define KERN_DYNAMIC_LIGHT_TRACE_HLSL

// SolveDynamicLighting и ComposeDynamicLighting: per-light tile tracing и композиция.
// При одном источнике SolveDynamicLighting может сразу писать DirectTexture.
//
// READS: _DynamicPolar, _DynamicLights
// WRITES: _DynamicTiles, _DirectTexture
// MUST NOT: трогать каскады, bounce

[numthreads(8, 8, 1)]
void SolveDynamicLighting(uint3 dispatchId : SV_DispatchThreadID)
{
    if (any(int2(dispatchId.xy) >= _DynamicDispatchSize))
    {
        return;
    }

    int2 pixel = _DynamicDispatchOrigin + int2(dispatchId.xy);
    if (any(pixel < 0) || any(pixel >= _FieldSize))
    {
        return;
    }

    float2 origin = float2(pixel) + 0.5;
    float3 radiance = DynamicRadianceFromPolar(
        origin,
        _DynamicLights[_DynamicLightIndex],
        8);
    _DynamicTiles[_DynamicTileOffset + int2(dispatchId.xy)] = float4(radiance, 1.0);
    if (_WriteDynamicDirect != 0)
    {
        _DirectTexture[pixel] = float4(radiance, 1.0);
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
    if (any(pixel < 0) || any(pixel >= _FieldSize))
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
    if (any(pixel < 0) || any(pixel >= _FieldSize))
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
            radiance += _DynamicTilesInput.Load(int3(tile.tileOffset + local, 0)).rgb;
        }
    }

    _DirectTexture[pixel] = float4(radiance, 1.0);
}

#endif // KERN_DYNAMIC_LIGHT_TRACE_HLSL

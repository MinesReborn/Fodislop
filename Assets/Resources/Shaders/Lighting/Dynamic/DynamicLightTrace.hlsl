#ifndef KERN_DYNAMIC_LIGHT_TRACE_HLSL
#define KERN_DYNAMIC_LIGHT_TRACE_HLSL

// SolveDynamicLighting и ComposeDynamicLighting: per-light tile tracing и композиция.
// При одном источнике SolveDynamicLighting может сразу писать DirectTexture.
//
// READS: _DynamicPolar, _DynamicLights, _DynamicHorizonInput, _DynamicTileInfos
// WRITES: _DynamicTiles, _DirectTexture
// MUST NOT: трогать каскады

// Receivers come from the CPU air-reach rectangle (weakest extinction). Inside
// it, a receiver is skipped only when it lies beyond the angular horizon of
// every polar ray its far gather can blend. A single per-source reach radius
// used to cut light in a hard circle; the horizon is per direction, so open
// corridors keep their full reach while receivers behind walls are skipped.
//
// `origin` is a receiver centre in transport texels.
bool DynamicHorizonContains(float2 origin, DynamicLight light)
{
    float2 cellsPerPixel = (_WorldRect.zw / _CellSize) / float2(_FieldSize);
    float2 texelsPerCell = 1.0 / cellsPerPixel;
    float2 center = (light.positionRadius.xy - _WorldRect.xy) / _WorldRect.zw * float2(_FieldSize);
    float2 sourceMin = center - 0.5 * texelsPerCell;
    float2 sourceMax = center + 0.5 * texelsPerCell;
    float2 gapCells = abs(origin - min(max(origin, sourceMin), sourceMax)) * cellsPerPixel;
    bool contains = true;
    // The near zone is gathered by direct DDA, not by the fan: never skipped.
    if (max(gapCells.x, gapCells.y) >= _DynamicNearCells)
    {
        float2 offset = origin - center;
        float radius = length(offset);
        // Emitter points lie anywhere in the source cell: their rays to this
        // receiver differ from the centre's by this distance and angle.
        float spread = 0.75 * max(texelsPerCell.x, texelsPerCell.y);
        float halfWidth = asin(saturate(spread / max(radius, 1e-3)));
        float angle = atan2(offset.y, offset.x);
        int directions = _DynamicPolarSize.x;
        float rayStep = PI2 / float(directions);
        // PolarTransmission blends ray floor(a/step - 0.5) and the next one.
        int first = (int)floor((angle - halfWidth) / rayStep - 0.5);
        int last = (int)floor((angle + halfWidth) / rayStep - 0.5) + 1;
        uint horizon = 0u;
        int points = _DynamicEmitterPointsPerAxis * _DynamicEmitterPointsPerAxis;
        [loop]
        for (int ray = first; ray <= last; ray++)
        {
            // first > -directions: |angle| <= PI and halfWidth <= PI / 2.
            uint index = (uint)(ray + 2 * directions) % (uint)directions;
            [loop]
            for (int emitter = 0; emitter < points; emitter++)
            {
                horizon = max(horizon,
                    _DynamicHorizonInput[_DynamicHorizonBase + emitter * _DynamicHorizonStride + (int)index]);
            }
        }
        // One stored row of radial interpolation beyond the horizon.
        contains = radius - spread <= float(horizon) + float(_FieldTexelsPerLightTexel);
    }
    return contains;
}

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
    float3 radiance = 0.0;
    if (DynamicHorizonContains(origin, light))
    {
        radiance = DynamicRadianceFromPolar(origin, light, _DynamicAngularSampleCount);
    }

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

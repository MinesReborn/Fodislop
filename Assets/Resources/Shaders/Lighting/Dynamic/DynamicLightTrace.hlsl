#ifndef KERN_DYNAMIC_LIGHT_TRACE_HLSL
#define KERN_DYNAMIC_LIGHT_TRACE_HLSL

// SolveDynamicLighting и ComposeDynamicLighting: per-light tile tracing и композиция.
// При одном источнике SolveDynamicLighting может сразу писать DirectTexture.
//
// READS: _DynamicPolar, _DynamicLights
// WRITES: _DynamicTiles, _DirectTexture
// MUST NOT: трогать каскады

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
    DynamicLight light = _DynamicLights[_DynamicLightIndex];

    // Дальше дальности фонаря ни один его луч не несёт видимого света
    // (TraceDynamicPolar). Запас: излучатели разнесены по клетке фонаря
    // (до 0.71 клетки от центра) — _DynamicReachSlackCells клетки; смешивание
    // соседних радиусов — _DynamicReachSlackTexels текселя. Ближнее поле
    // (_DynamicNearCells по большей оси от клетки, то есть до
    // _DynamicReachSlackCells · (_DynamicNearCells + 1) клетки по прямой)
    // собирается прямым DDA без лучей и не отсекается никогда.
    float2 texelsPerCell = float2(_FieldSize) / (_WorldRect.zw / _CellSize);
    float cellTexels = max(texelsPerCell.x, texelsPerCell.y);
    float2 lightTexel = (light.positionRadius.xy - _WorldRect.xy) / _WorldRect.zw * float2(_FieldSize);
    float reachTexels = max(
        float(_DynamicReach[_DynamicLightIndex]) + cellTexels + _DynamicReachSlackTexels,
        _DynamicReachSlackCells * (_DynamicNearCells + 1.0) * cellTexels);
    float2 fromLight = origin - lightTexel;
    float3 radiance = 0.0;
    if (dot(fromLight, fromLight) <= reachTexels * reachTexels)
    {
        radiance = DynamicRadianceFromPolar(origin, light, _DynamicAngularSampleCount);
    }

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

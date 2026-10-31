#ifndef KERN_DYNAMIC_POLAR_CONTEXT_INCLUDED
#define KERN_DYNAMIC_POLAR_CONTEXT_INCLUDED
DynamicTraceContext SerialDynamicTraceContext()
{
    DynamicTraceContext context;
    context.receiverOrigin = _DynamicDispatchOrigin;
    context.receiverSize = _DynamicDispatchSize;
    context.tileOffset = _DynamicTileOffset;
    context.polarSize = _DynamicPolarSize;
    context.lightIndex = _DynamicLightIndex;
    context.slot = _DynamicReachIndex;
    context.polarLayerOffset = _DynamicPolarLayerOffset;
    context.horizonBase = _DynamicHorizonBase;
    return context;
}

DynamicTraceContext BatchedDynamicTraceContext(uint sourceIndex)
{
    DynamicLightWorkItem work = _DynamicLightWorkItems[_DynamicBatchOffset + sourceIndex];
    DynamicTraceContext context;
    context.receiverOrigin = work.receiverOrigin;
    context.receiverSize = work.receiverSize;
    context.tileOffset = int2(0, 0);
    context.polarSize = work.polarSize;
    context.lightIndex = work.lightIndex;
    context.slot = work.slot;
    context.polarLayerOffset = work.slot * _DynamicEmitterPointsPerAxis * _DynamicEmitterPointsPerAxis;
    context.horizonBase = context.polarLayerOffset * _DynamicHorizonStride;
    return context;
}

// Point `pointIndex` of a continuous glow grid over the dynamic light's
// complete one-cell source square, including outside the field. Keeping
// these sample positions in sub-texel coordinates lets the source move smoothly
// instead of snapping its emitter grid to field-texel centres.
void DynamicEmitterPoint(
    DynamicLight light,
    int pointIndex,
    out float2 position,
    out float areaCells,
    out bool emits)
{
    float2 worldCellMin = light.positionRadius.xy - 0.5 * _CellSize;
    float2 sourceMin = (worldCellMin - _WorldRect.xy) / _WorldRect.zw * float2(_FieldSize);
    float2 sourceSize = _CellSize / _WorldRect.zw * float2(_FieldSize);
    float2 emitterMin = sourceMin;
    float2 emitterSize = sourceSize;
    float2 cellsPerPixel = (_WorldRect.zw / _CellSize) / float2(_FieldSize);
    areaCells = emitterSize.x * cellsPerPixel.x * emitterSize.y * cellsPerPixel.y;
    uint index = uint(pointIndex);
    uint pointsPerAxis = uint(_DynamicEmitterPointsPerAxis);
    float2 grid = float2(index % pointsPerAxis, index / pointsPerAxis);
    position = emitterMin + emitterSize * (grid + 0.5) / float(_DynamicEmitterPointsPerAxis);
    emits = areaCells > 0.0;
}

// One extra column on each side lets hardware filtering cross the angular
// seam without blending into a different emitter point's rows.
void WriteDynamicPolar(int angleIndex, int radius, int pointIndex, float4 depth, DynamicTraceContext context)
{
    _DynamicPolar[int3(angleIndex + 1, radius, context.polarLayerOffset + pointIndex)] = depth;
    if (angleIndex == 0)
    {
        _DynamicPolar[int3(context.polarSize.x + 1, radius, context.polarLayerOffset + pointIndex)] = depth;
    }

    if (angleIndex == context.polarSize.x - 1)
    {
        _DynamicPolar[int3(0, radius, context.polarLayerOffset + pointIndex)] = depth;
    }
}


#endif // KERN_DYNAMIC_POLAR_CONTEXT_INCLUDED

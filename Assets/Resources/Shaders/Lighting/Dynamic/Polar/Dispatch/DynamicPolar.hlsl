#ifndef KERN_DYNAMIC_POLAR_HLSL
#define KERN_DYNAMIC_POLAR_HLSL

// Динамический свет источников: полярные лучи, optical depth lookup, per-pixel radiance.
//
// READS: _MaterialField, _DynamicLights, _DynamicPolarInput,
//        _CleanCellPrefix (opt-in uniform-source gather only)
// WRITES: _DynamicPolar
// MAY: вызывать DDA (TraceDynamicPolar, GatherDynamicSource)
// MUST NOT: трогать каскады

#include "Assets/Resources/Shaders/Lighting/Dynamic/Polar/Context/DynamicPolarContext.hlsl"
#include "Assets/Resources/Shaders/Lighting/Dynamic/Polar/Rays/DynamicPolarTrace.hlsl"
#include "Assets/Resources/Shaders/Lighting/Dynamic/Polar/DepthSampling/DynamicPolarDepthSampling.hlsl"
[numthreads(64, 1, 1)]
void TraceDynamicPolar(uint3 dispatchId : SV_DispatchThreadID)
{
    TraceDynamicPolarRay(dispatchId, SerialDynamicTraceContext());
}

[numthreads(64, 1, 1)]
void TraceDynamicPolarBatch(uint3 dispatchId : SV_DispatchThreadID)
{
    TraceDynamicPolarRay(dispatchId, BatchedDynamicTraceContext(dispatchId.z));
}

// Exact continuous emitter integral in a proven uniform medium. The caller
// proves the complete receiver/emitter box, including the corner-seal guard.
// COST: no field loads; one slab intersection and the existing medium integral.
#include "Assets/Resources/Shaders/Lighting/Dynamic/Polar/Emission/DynamicPolarRadiance.hlsl"
#endif // KERN_DYNAMIC_POLAR_HLSL

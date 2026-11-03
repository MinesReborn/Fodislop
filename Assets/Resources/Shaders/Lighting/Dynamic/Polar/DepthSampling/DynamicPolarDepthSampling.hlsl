#ifndef KERN_DYNAMIC_POLAR_DEPTH_SAMPLING_INCLUDED
#define KERN_DYNAMIC_POLAR_DEPTH_SAMPLING_INCLUDED
float3 PolarColumnDepth(int pointIndex, float column, float radius, DynamicTraceContext context)
{
    float radiusIndex = min(max(radius / float(_FieldTexelsPerLightTexel), 0.0),
        float(context.polarSize.y - 1));
    float2 polarUv = float2(
        (column + 0.5) / float(_DynamicPolarTextureSize.x),
        (radiusIndex + 0.5) / float(_DynamicPolarTextureSize.y));
    float3 depth = _DynamicPolarInput.SampleLevel(sampler_LinearClamp,
        float3(polarUv, float(context.polarLayerOffset + pointIndex)), 0).rgb;
    return _DynamicPolarScalarExtinction != 0 ? depth.rrr : depth;
}

// Transmission between `nearRadius` and `farRadius` along `angle` from
// emitter point `pointIndex`. Neighbouring rays are blended by their
// transmission, not by optical depth: a sealed or wall-blocked ray (huge
// depth) next to an open one gives a penumbra between them, where blending
// depths blackened the whole angular gap and drew dark wedges.
float3 PolarTransmission(int pointIndex, float angle, float farRadius, float nearRadius, DynamicTraceContext context)
{
    // Ray i is stored in column i + 1 at angle (i + 0.5) / N; columns 0 and
    // N + 1 repeat the last and first rays across the seam.
    float rayPosition = frac(angle / PI2) * float(context.polarSize.x) - 0.5;
    float lowerRay = floor(rayPosition);
    float blend = rayPosition - lowerRay;
    float lowerColumn = lowerRay + 1.0;
    float3 lowerDepth = max(PolarColumnDepth(pointIndex, lowerColumn, farRadius, context) -
        PolarColumnDepth(pointIndex, lowerColumn, nearRadius, context), 0.0);
    float3 upperDepth = max(PolarColumnDepth(pointIndex, lowerColumn + 1.0, farRadius, context) -
        PolarColumnDepth(pointIndex, lowerColumn + 1.0, nearRadius, context), 0.0);
    return lerp(OpticalDepthTransmission(lowerDepth), OpticalDepthTransmission(upperDepth), blend);
}


#endif // KERN_DYNAMIC_POLAR_DEPTH_SAMPLING_INCLUDED

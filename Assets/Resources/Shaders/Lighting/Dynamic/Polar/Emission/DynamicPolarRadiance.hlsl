#ifndef KERN_DYNAMIC_POLAR_RADIANCE_INCLUDED
#define KERN_DYNAMIC_POLAR_RADIANCE_INCLUDED
float3 UniformSourceRadiance(float2 origin, float2 direction, float2 sourceMin,
    float2 sourceMax, float3 sourceRadiance, float occupancy)
{
    float2 inverseDirection = float2(
        abs(direction.x) > 1e-20 ? 1.0 / direction.x : 1e20,
        abs(direction.y) > 1e-20 ? 1.0 / direction.y : 1e20);
    float2 slabA = (sourceMin - origin) * inverseDirection;
    float2 slabB = (sourceMax - origin) * inverseDirection;
    float entry = max(0.0, max(min(slabA.x, slabB.x), min(slabA.y, slabB.y)));
    float exitDistance = min(max(slabA.x, slabB.x), max(slabA.y, slabB.y));
    float3 result = 0.0;
    if (exitDistance > entry)
    {
        float2 cellsPerPixel = (_WorldRect.zw / _CellSize) / float2(_FieldSize);
        float cellsPerDistance = length(direction * cellsPerPixel);
        result = SegmentTransmission(occupancy, entry * cellsPerDistance) * sourceRadiance *
            MediumGlowWeight(SegmentExtinction(occupancy), (exitDistance - entry) * cellsPerDistance);
    }
    return result;
}

// Integrate only the angular interval subtended by this emitting cell. All
// paths use the same glow integral as the terrain cascades. A proven
// uniform source box integrates analytically; all other paths use exact DDA.
float3 GatherDynamicSource(float2 origin, DynamicLight light, int sampleCount)
{
    // One-cell square centred on the robot, moving continuously with it.
    float2 worldCellMin = light.positionRadius.xy - 0.5 * _CellSize;
    float2 sourceMin = (worldCellMin - _WorldRect.xy) / _WorldRect.zw * float2(_FieldSize);
    float2 sourceSize = _CellSize / _WorldRect.zw * float2(_FieldSize);
    float2 sourceMax = sourceMin + sourceSize;
    // Single return path. An early return here made the Metal cross-compiler
    // report the inlined result as potentially uninitialized.
    float3 result = 0.0;
    if (all(sourceSize > 0.0))
    {
        float2 toCenter = (sourceMin + sourceMax) * 0.5 - origin;
        float minAngle = -PI;
        float maxAngle = PI;
        bool inside = all(origin >= sourceMin) && all(origin < sourceMax);
        // Inside an emitter the angular domain is the whole circle; it has
        // no center direction. atan2(0, 0) is undefined on GPU backends and
        // can make the receiver at the exact source position gather zero light.
        float centerAngle = 0.0;
        if (!inside)
        {
            centerAngle = atan2(toCenter.y, toCenter.x);
            minAngle = PI;
            maxAngle = -PI;
            [unroll]
            for (int corner = 0; corner < 4; corner++)
            {
                float2 cornerPosition = float2(
                    (corner & 1) != 0 ? sourceMax.x : sourceMin.x,
                    (corner & 2) != 0 ? sourceMax.y : sourceMin.y);
                float2 toCorner = cornerPosition - origin;
                float angle = atan2(
                    toCenter.x * toCorner.y - toCenter.y * toCorner.x,
                    dot(toCenter, toCorner));
                minAngle = min(minAngle, angle);
                maxAngle = max(maxAngle, angle);
            }
        }

        float angularWidth = maxAngle - minAngle;
        float rayLength = length(toCenter) + length(sourceSize);
        float3 sourceRadiance = max(light.colorIntensity.rgb * light.colorIntensity.a, 0.0) * _GlowScale;
        float3 radiance = 0.0;
        float angleStep = angularWidth / float(sampleCount);
        float angleStart = centerAngle + minAngle + 0.5 * angleStep;
        float raySin = 0.0;
        float rayCos = 1.0;
        float stepSin = 0.0;
        float stepCos = 1.0;
        sincos(angleStart, raySin, rayCos);
        sincos(angleStep, stepSin, stepCos);
        // Amortize four existing prefix-buffer reads over the whole angular
        // gather, not over individual rays. A cell-plus-texel guard includes
        // both sides of every corner-seal query. Outside the field is air;
        // NonCleanCellCount explicitly excludes it from a stone proof.
        // Static glow conservatively rejects a proof even though this
        // gather integrates only its isolated continuous source.
        bool uniformSourceBox = false;
        float uniformOccupancy = 0.0;
        if (_UniformSourceTraversalEnabled != 0)
        {
            float2 texelsPerCell = float2(_FieldSize) / float2(_CellGridSize);
            float guardTexels = max(texelsPerCell.x, texelsPerCell.y) + 1.0;
            uint2 nonClean = NonCleanCellCount(min(origin, sourceMin) - guardTexels,
                max(origin, sourceMax) + guardTexels);
            uniformSourceBox = nonClean.x == 0u || nonClean.y == 0u;
            uniformOccupancy = nonClean.x == 0u ? 0.0 : 1.0;
        }
        [loop]
        for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            float3 sampleRadiance = 0.0;
            float3 transmission = 1.0;
            if (uniformSourceBox)
            {
                sampleRadiance = UniformSourceRadiance(origin, float2(rayCos, raySin),
                    sourceMin, sourceMax, sourceRadiance, uniformOccupancy);
            }
            else
            {
                TraceLightSegment(
                    origin,
                    origin + float2(rayCos, raySin) * rayLength,
                    true,
                    true,
                    float4(sourceMin, sourceMax),
                    sourceRadiance,
                    sampleRadiance,
                    transmission);
            }
            radiance += sampleRadiance;
            float nextCos = rayCos * stepCos - raySin * stepSin;
            raySin = raySin * stepCos + rayCos * stepSin;
            rayCos = nextCos;
        }

        result = radiance * (angularWidth / (float(sampleCount) * PI2));
    }

    return result;
}

// Dynamic light at a receiver from the emitter points' fans (see TraceDynamicPolar).
float3 DynamicRadianceFromPolar(float2 origin, DynamicLight light, int sampleCount, DynamicTraceContext context)
{
    float2 worldCellMin = light.positionRadius.xy - 0.5 * _CellSize;
    float2 sourceMin = (worldCellMin - _WorldRect.xy) / _WorldRect.zw * float2(_FieldSize);
    float2 sourceSize = _CellSize / _WorldRect.zw * float2(_FieldSize);
    float2 sourceMax = sourceMin + sourceSize;
    float2 cellsPerPixel = (_WorldRect.zw / _CellSize) / float2(_FieldSize);
    float2 nearestOnSource = min(max(origin, sourceMin), sourceMax);
    float2 gapCells = abs(origin - nearestOnSource) * cellsPerPixel;
    float3 result = 0.0;
    if (max(gapCells.x, gapCells.y) < _DynamicNearCells)
    {
        result = GatherDynamicSource(origin, light, sampleCount);
    }
    else
    {
        // Same angular samples and glow integral as GatherDynamicSource.
        // Each sample's transmittance is read from the fan of the emitter point
        // nearest to where the sample crosses the dynamic light: that ray ends exactly
        // at the receiver and starts within a sixth of a cell of the sample
        // ray, so walls shadow along the rays light actually takes.
        float2 toCenter = (sourceMin + sourceMax) * 0.5 - origin;
        float centerAngle = atan2(toCenter.y, toCenter.x);
        float minAngle = PI;
        float maxAngle = -PI;
        [unroll]
        for (int corner = 0; corner < 4; corner++)
        {
            float2 cornerPosition = float2(
                (corner & 1) != 0 ? sourceMax.x : sourceMin.x,
                (corner & 2) != 0 ? sourceMax.y : sourceMin.y);
            float2 toCorner = cornerPosition - origin;
            float cornerAngle = atan2(
                toCenter.x * toCorner.y - toCenter.y * toCorner.x,
                dot(toCenter, toCorner));
            minAngle = min(minAngle, cornerAngle);
            maxAngle = max(maxAngle, cornerAngle);
        }

        float angularWidth = maxAngle - minAngle;
        float3 sourceRadiance = max(light.colorIntensity.rgb * light.colorIntensity.a, 0.0) * _GlowScale;
        int2 centerPixel = int2(floor((sourceMin + sourceMax) * 0.5));
        float sourceOccupancy = 0.0;
        if (all(centerPixel >= 0) && all(centerPixel < _FieldSize))
        {
            sourceOccupancy = saturate(_MaterialField.Load(int3(MaterialPixel(centerPixel), 0)).a);
        }

        float3 sourceExtinction = SegmentExtinction(sourceOccupancy);
        // Match the complete moving emitter grid used to trace the polar rays.
        // Outside-field transport is explicitly air, never a clamped edge texel.
        float2 emitterMin = sourceMin;
        float2 emitterMax = sourceMax;
        float2 emitterSize = sourceSize;
        float3 radiance = 0.0;
        if (emitterSize.x > 0.0 && emitterSize.y > 0.0)
        {
            float angleStep = angularWidth / float(sampleCount);
            float angleStart = centerAngle + minAngle + 0.5 * angleStep;
            float raySine = 0.0;
            float rayCosine = 1.0;
            float stepSine = 0.0;
            float stepCosine = 1.0;
            sincos(angleStart, raySine, rayCosine);
            sincos(angleStep, stepSine, stepCosine);
            [loop]
            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                float2 direction = float2(rayCosine, raySine);
                float2 inverseDirection = float2(
                    abs(direction.x) > 1e-20 ? 1.0 / direction.x : 1e20,
                    abs(direction.y) > 1e-20 ? 1.0 / direction.y : 1e20);
                float2 slabA = (emitterMin - origin) * inverseDirection;
                float2 slabB = (emitterMax - origin) * inverseDirection;
                float entryDistance = max(0.0, max(min(slabA.x, slabB.x), min(slabA.y, slabB.y)));
                float exitDistance = min(max(slabA.x, slabB.x), max(slabA.y, slabB.y));
                if (exitDistance > entryDistance)
                {
                    float cellsPerDistance = length(direction * cellsPerPixel);
                    float chordCells = (exitDistance - entryDistance) * cellsPerDistance;
                    float3 glowWeight = MediumGlowWeight(sourceExtinction, chordCells);

                    float2 crossing = origin + direction * (0.5 * (entryDistance + exitDistance));
                    int2 nearestPoint = clamp(
                        int2(floor((crossing - emitterMin) / emitterSize * float(_DynamicEmitterPointsPerAxis))),
                        int2(0, 0),
                        int2(_DynamicEmitterPointsPerAxis - 1, _DynamicEmitterPointsPerAxis - 1));
                    int pointIndex = nearestPoint.y * _DynamicEmitterPointsPerAxis + nearestPoint.x;
                    float2 emitterPoint = 0.0;
                    float emitterArea = 0.0;
                    bool emits = false;
                    DynamicEmitterPoint(light, pointIndex, emitterPoint, emitterArea, emits);

                    float2 toReceiver = origin - emitterPoint;
                    float receiverRadius = length(toReceiver);
                    float2 rayDirection = toReceiver / max(receiverRadius, 1e-6);
                    float2 entryPoint = origin + direction * entryDistance;
                    float entryRadius = min(max(dot(entryPoint - emitterPoint, rayDirection), 0.0), receiverRadius);
                    float rayAngle = atan2(toReceiver.y, toReceiver.x);
                    radiance += PolarTransmission(pointIndex, rayAngle, receiverRadius, entryRadius, context) *
                        sourceRadiance * glowWeight;
                }

                float nextCosine = rayCosine * stepCosine - raySine * stepSine;
                raySine = raySine * stepCosine + rayCosine * stepSine;
                rayCosine = nextCosine;
            }
        }

        result = radiance * (angularWidth / (float(sampleCount) * PI2));
    }

    return result;
}


#endif // KERN_DYNAMIC_POLAR_RADIANCE_INCLUDED

#ifndef KERN_DYNAMIC_POLAR_HLSL
#define KERN_DYNAMIC_POLAR_HLSL

// Динамический свет источников: полярные лучи, optical depth lookup, per-pixel radiance.
//
// READS: _MaterialField, _DynamicLights, _DynamicPolarInput
// WRITES: _DynamicPolar
// MAY: вызывать DDA (TraceDynamicPolar, GatherDynamicSource)
// MUST NOT: трогать каскады

// Point `pointIndex` of a continuous emission grid over the dynamic light's
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
void WriteDynamicPolar(int angleIndex, int radius, int pointIndex, float4 depth)
{
    _DynamicPolar[int3(angleIndex + 1, radius, _DynamicPolarLayerOffset + pointIndex)] = depth;
    if (angleIndex == 0)
    {
        _DynamicPolar[int3(_DynamicPolarSize.x + 1, radius, _DynamicPolarLayerOffset + pointIndex)] = depth;
    }

    if (angleIndex == _DynamicPolarSize.x - 1)
    {
        _DynamicPolar[int3(0, radius, _DynamicPolarLayerOffset + pointIndex)] = depth;
    }
}

[numthreads(64, 1, 1)]
void TraceDynamicPolar(uint3 dispatchId : SV_DispatchThreadID)
{
    int angleIndex = int(dispatchId.x);
    int pointIndex = int(dispatchId.y);
    if (angleIndex >= _DynamicPolarSize.x ||
        pointIndex >= _DynamicEmitterPointsPerAxis * _DynamicEmitterPointsPerAxis)
    {
        return;
    }

    int radii = _DynamicPolarSize.y;
    // One polar row per receiver (light-lattice) texel. The march below still
    // visits every transport texel; only the stored radial samples are as
    // dense as the receivers that read them.
    float rowStep = float(_FieldTexelsPerLightTexel);
    if (_LightingCountersEnabled != 0)
    {
        InterlockedAdd(_LightingCounters[0], 1u);
    }
    float angle = (float(angleIndex) + 0.5) * PI2 / float(_DynamicPolarSize.x);
    float raySine = 0.0;
    float rayCosine = 1.0;
    sincos(angle, raySine, rayCosine);
    float2 direction = float2(rayCosine, raySine);
    float2 segmentStart = 0.0;
    float emitterArea = 0.0;
    bool emits = false;
    DynamicEmitterPoint(_DynamicLights[_DynamicLightIndex], pointIndex, segmentStart, emitterArea, emits);
    float2 cellsPerPixel = (_WorldRect.zw / _CellSize) / float2(_FieldSize);
    float cellsPerDistance = length(direction * cellsPerPixel);
    float intervalLength = float(radii - 1) * rowStep;
    float3 airExtinction = SegmentExtinction(0.0);
    float3 opticalDepth = 0.0;
    float distance = 0.0;
    WriteDynamicPolar(angleIndex, 0, pointIndex, float4(0.0, 0.0, 0.0, 0.0));

    int nextRadius = 1;

    // Angular horizon: the radius along this ray beyond which nothing it
    // carries can reach any display level. The stored depths are untouched;
    // receivers beyond the horizon of every ray they blend are skipped
    // (DynamicHorizonContains), so light is never cut while still visible.
    DynamicLight horizonLight = _DynamicLights[_DynamicLightIndex];
    float3 horizonSource = max(horizonLight.colorIntensity.rgb * horizonLight.colorIntensity.a, 0.0) *
        _EmissionScale * DynamicHorizonWeightMargin;
    float3 horizonEntryDepth = SegmentExtinction(1.0) * DynamicHorizonSourceDepthCells;
    float horizon = intervalLength;
    bool horizonFound = false;

    float2 inverseDirection = float2(
        abs(direction.x) > 1e-20 ? 1.0 / direction.x : 1e20,
        abs(direction.y) > 1e-20 ? 1.0 / direction.y : 1e20);
    float2 slabA = -segmentStart * inverseDirection;
    float2 slabB = (float2(_FieldSize) - segmentStart) * inverseDirection;
    float2 slabNear = min(slabA, slabB);
    float2 slabFar = max(slabA, slabB);
    float entry = max(0.0, max(slabNear.x, slabNear.y));
    float exitDistance = min(intervalLength, min(slabFar.x, slabFar.y));
    if (exitDistance > entry)
    {
        // Space outside the field is empty air.
        [loop]
        while (nextRadius < radii && float(nextRadius) * rowStep <= entry)
        {
            WriteDynamicPolar(angleIndex, nextRadius, pointIndex,
                float4(airExtinction * float(nextRadius) * rowStep * cellsPerDistance, 0.0));
            nextRadius++;
        }

        opticalDepth = airExtinction * entry * cellsPerDistance;
        distance = entry;
        float2 start = segmentStart + direction * entry;
        int2 texel = int2(floor(start));
        int2 step = int2(sign(direction));
        texel.x -= direction.x < 0.0 && start.x == floor(start.x) ? 1 : 0;
        texel.y -= direction.y < 0.0 && start.y == floor(start.y) ? 1 : 0;
        texel = clamp(texel, int2(0, 0), _FieldSize - 1);
        float2 boundary = float2(texel) + float2(direction.x > 0.0 ? 1.0 : 0.0, direction.y > 0.0 ? 1.0 : 0.0);
        float2 next = float2(
            step.x != 0 ? (boundary.x - segmentStart.x) / direction.x : 1e20,
            step.y != 0 ? (boundary.y - segmentStart.y) / direction.y : 1e20);
        float2 stride = abs(inverseDirection);
        // Corner-seal history, as in TraceLightSegmentLocal (DDA.hlsl).
        int enteredMask = 0;
        int2 enteredLines = 0;
        int previousMask = 0;
        int2 previousLines = 0;
        bool previousSolid = false;
        bool beforePreviousSolid = false;
        int2 cachedUniformCell = int2(-1, -1);
        bool uniformCell = false;
        float uniformOccupancy = 0.0;
        [loop]
        while (distance < exitDistance)
        {
            if (any(texel < 0) || any(texel >= _FieldSize))
            {
                break;
            }

            int2 cell = int2(floor((float2(texel) + 0.5) * cellsPerPixel));
            if (any(cell != cachedUniformCell))
            {
                uniformCell = _UniformCellTraversalEnabled != 0 &&
                    _CellSolidMask.Load(int3(cell, 0)).a == 1.0;
                if (uniformCell)
                {
                    uniformOccupancy = _MaterialField.Load(int3(MaterialPixel(texel), 0)).a;
                }
                cachedUniformCell = cell;
            }
            float2 cellMin = 0.0;
            float2 cellMax = 0.0;
            float2 intervalNext = next;
            if (uniformCell)
            {
                cellMin = float2(cell) / cellsPerPixel;
                cellMax = float2(cell + 1) / cellsPerPixel;
                float2 cellBoundary = float2(direction.x > 0.0 ? cellMax.x : cellMin.x,
                    direction.y > 0.0 ? cellMax.y : cellMin.y);
                intervalNext = float2(
                    step.x != 0 ? (cellBoundary.x - segmentStart.x) * inverseDirection.x : 1e20,
                    step.y != 0 ? (cellBoundary.y - segmentStart.y) * inverseDirection.y : 1e20);
            }
            if (_LightingCountersEnabled != 0)
            {
                InterlockedAdd(_LightingCounters[1], 1u);
            }
            float end = min(exitDistance, min(intervalNext.x, intervalNext.y));
            float distanceCells = max(0.0, end - distance) * cellsPerDistance;
            int2 materialPixel = MaterialPixel(texel);

            float solid = uniformOccupancy;
            if (!uniformCell)
            {
                solid = saturate(_MaterialField.Load(int3(materialPixel, 0)).a);
            }
            bool currentSolid = TransportSolidOccupancy(solid);
            if (CornerSealed(enteredMask, enteredLines, previousMask, previousLines,
                beforePreviousSolid, previousSolid, currentSolid, step))
            {
                [loop]
                while (nextRadius < radii)
                {
                    WriteDynamicPolar(angleIndex, nextRadius, pointIndex,
                        float4(1e6, 1e6, 1e6, 0.0));
                    nextRadius++;
                }

                if (!horizonFound)
                {
                    horizon = distance;
                    horizonFound = true;
                }

                break;
            }

            float3 extinction = SegmentExtinction(solid);
            [loop]
            while (nextRadius < radii && float(nextRadius) * rowStep <= end)
            {
                WriteDynamicPolar(angleIndex, nextRadius, pointIndex, float4(
                    opticalDepth + extinction * (float(nextRadius) * rowStep - distance) * cellsPerDistance,
                    0.0));
                nextRadius++;
            }

            opticalDepth += extinction * distanceCells;
            distance = end;
            if (!horizonFound &&
                Max3(OpticalDepthTransmission(max(opticalDepth - horizonEntryDepth, 0.0)) * horizonSource) <
                    _InvisibleDynamicRadiance)
            {
                horizon = distance;
                horizonFound = true;
            }

            if (distance >= exitDistance)
            {
                break;
            }

            bool crossX = intervalNext.x <= intervalNext.y;
            bool crossY = intervalNext.y <= intervalNext.x;
            int2 crossedLines = texel + int2(step.x > 0 ? 1 : 0, step.y > 0 ? 1 : 0);
            if (uniformCell)
            {
                int2 minimum = int2(round(cellMin));
                int2 maximum = int2(round(cellMax));
                crossedLines = int2(step.x > 0 ? maximum.x : minimum.x, step.y > 0 ? maximum.y : minimum.y);
            }
            beforePreviousSolid = previousSolid;
            previousSolid = currentSolid;
            previousMask = enteredMask;
            previousLines = enteredLines;
            enteredMask = (crossX ? 1 : 0) | (crossY ? 2 : 0);
            enteredLines = crossedLines;
            if (uniformCell)
            {
                int2 pointTexel = int2(floor(segmentStart + direction * distance));
                int2 minimum = int2(round(cellMin));
                int2 maximum = int2(round(cellMax));
                texel.x = crossX ? (step.x > 0 ? maximum.x : minimum.x - 1)
                    : clamp(pointTexel.x, minimum.x, maximum.x - 1);
                texel.y = crossY ? (step.y > 0 ? maximum.y : minimum.y - 1)
                    : clamp(pointTexel.y, minimum.y, maximum.y - 1);
                float2 nextBoundary = float2(texel) +
                    float2(direction.x > 0.0 ? 1.0 : 0.0, direction.y > 0.0 ? 1.0 : 0.0);
                next = float2(step.x != 0 ? (nextBoundary.x - segmentStart.x) * inverseDirection.x : 1e20,
                    step.y != 0 ? (nextBoundary.y - segmentStart.y) * inverseDirection.y : 1e20);
            }
            else if (crossX)
            {
                texel.x += step.x;
                next.x += stride.x;
            }

            if (!uniformCell && crossY)
            {
                texel.y += step.y;
                next.y += stride.y;
            }
        }
    }

    [loop]
    while (nextRadius < radii)
    {
        WriteDynamicPolar(angleIndex, nextRadius, pointIndex, float4(
            opticalDepth + airExtinction * (float(nextRadius) * rowStep - distance) * cellsPerDistance,
            0.0));
        nextRadius++;
    }

    // Past the march the ray is air and the stored depth grows linearly: the
    // invisibility point on that line is closed-form, from the same values.
    if (!horizonFound)
    {
        float3 depthRate = airExtinction * cellsPerDistance;
        float3 depthNeeded = log(max(horizonSource / _InvisibleDynamicRadiance, 1.0)) +
            horizonEntryDepth - opticalDepth;
        bool bounded =
            (depthNeeded.x <= 0.0 || depthRate.x > 0.0) &&
            (depthNeeded.y <= 0.0 || depthRate.y > 0.0) &&
            (depthNeeded.z <= 0.0 || depthRate.z > 0.0);
        if (bounded)
        {
            horizon = min(horizon, distance + max(Max3(depthNeeded / max(depthRate, 1e-30)), 0.0));
        }
    }

    _DynamicHorizon[_DynamicHorizonBase + pointIndex * _DynamicHorizonStride + angleIndex] = uint(ceil(horizon));
}

// Optical depth along one stored ray (texture column `column`, wrap columns
// included) from emitter point `pointIndex` to `radius` transport texels,
// linear between rows. Rows are one receiver texel apart.
float3 PolarColumnDepth(int pointIndex, float column, float radius)
{
    float radiusIndex = min(max(radius / float(_FieldTexelsPerLightTexel), 0.0),
        float(_DynamicPolarSize.y - 1));
    float2 polarUv = float2(
        (column + 0.5) / float(_DynamicPolarTextureSize.x),
        (radiusIndex + 0.5) / float(_DynamicPolarTextureSize.y));
    float3 depth = _DynamicPolarInput.SampleLevel(sampler_LinearClamp,
        float3(polarUv, float(_DynamicPolarLayerOffset + pointIndex)), 0).rgb;
    return _DynamicPolarScalarExtinction != 0 ? depth.rrr : depth;
}

// Transmission between `nearRadius` and `farRadius` along `angle` from
// emitter point `pointIndex`. Neighbouring rays are blended by their
// transmission, not by optical depth: a sealed or wall-blocked ray (huge
// depth) next to an open one gives a penumbra between them, where blending
// depths blackened the whole angular gap and drew dark wedges.
float3 PolarTransmission(int pointIndex, float angle, float farRadius, float nearRadius)
{
    // Ray i is stored in column i + 1 at angle (i + 0.5) / N; columns 0 and
    // N + 1 repeat the last and first rays across the seam.
    float rayPosition = frac(angle / PI2) * float(_DynamicPolarSize.x) - 0.5;
    float lowerRay = floor(rayPosition);
    float blend = rayPosition - lowerRay;
    float lowerColumn = lowerRay + 1.0;
    float3 lowerDepth = max(PolarColumnDepth(pointIndex, lowerColumn, farRadius) -
        PolarColumnDepth(pointIndex, lowerColumn, nearRadius), 0.0);
    float3 upperDepth = max(PolarColumnDepth(pointIndex, lowerColumn + 1.0, farRadius) -
        PolarColumnDepth(pointIndex, lowerColumn + 1.0, nearRadius), 0.0);
    return lerp(OpticalDepthTransmission(lowerDepth), OpticalDepthTransmission(upperDepth), blend);
}

// Integrate only the angular interval subtended by this emitting cell. All
// paths use the same DDA and emission integral as the terrain cascades.
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
        float3 sourceRadiance = max(light.colorIntensity.rgb * light.colorIntensity.a, 0.0) * _EmissionScale;
        float3 radiance = 0.0;
        float angleStep = angularWidth / float(sampleCount);
        float angleStart = centerAngle + minAngle + 0.5 * angleStep;
        float raySin = 0.0;
        float rayCos = 1.0;
        float stepSin = 0.0;
        float stepCos = 1.0;
        sincos(angleStart, raySin, rayCos);
        sincos(angleStep, stepSin, stepCos);
        [loop]
        for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            float3 sampleRadiance = 0.0;
            float3 transmission = 1.0;
            TraceLightSegment(
                origin,
                origin + float2(rayCos, raySin) * rayLength,
                true,
                true,
                float4(sourceMin, sourceMax),
                sourceRadiance,
                sampleRadiance,
                transmission);
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
float3 DynamicRadianceFromPolar(float2 origin, DynamicLight light, int sampleCount)
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
        // Same angular samples and emission integral as GatherDynamicSource.
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
        float3 sourceRadiance = max(light.colorIntensity.rgb * light.colorIntensity.a, 0.0) * _EmissionScale;
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
                    float3 emissionWeight = MediumEmissionWeight(sourceExtinction, chordCells);

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
                    radiance += PolarTransmission(pointIndex, rayAngle, receiverRadius, entryRadius) *
                        sourceRadiance * emissionWeight;
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

#endif // KERN_DYNAMIC_POLAR_HLSL

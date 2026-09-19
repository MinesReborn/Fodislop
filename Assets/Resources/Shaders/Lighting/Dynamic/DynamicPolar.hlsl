#ifndef KERN_DYNAMIC_POLAR_HLSL
#define KERN_DYNAMIC_POLAR_HLSL

// Динамический свет источников: полярные лучи, optical depth lookup, per-pixel radiance.
//
// READS: _MaterialField, _DynamicLights, _DynamicPolarInput
// WRITES: _DynamicPolar
// MAY: вызывать DDA (TraceDynamicPolar, GatherDynamicSource)
// MUST NOT: трогать каскады, bounce

// Point `pointIndex` of the dynamic light's emission grid, over the texels whose centres
// lie inside its square and inside the field — the texels TraceLightSegment
// lets emit. `emits` is false when no such texel exists.
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
    float2 emitterMin = max(ceil(sourceMin - 0.5), float2(0.0, 0.0));
    float2 emitterMax = min(ceil(sourceMin + sourceSize - 0.5), float2(_FieldSize));
    float2 emitterSize = max(emitterMax - emitterMin, float2(0.0, 0.0));
    float2 cellsPerPixel = (_WorldRect.zw / _CellSize) / float2(_FieldSize);
    areaCells = emitterSize.x * cellsPerPixel.x * emitterSize.y * cellsPerPixel.y;
    uint index = uint(pointIndex);
    uint pointsPerAxis = uint(DynamicEmitterPointsPerAxis);
    float2 grid = float2(index % pointsPerAxis, index / pointsPerAxis);
    position = emitterMin + emitterSize * (grid + 0.5) / float(DynamicEmitterPointsPerAxis);
    emits = areaCells > 0.0;
}

[numthreads(64, 1, 1)]
void TraceDynamicPolar(uint3 dispatchId : SV_DispatchThreadID)
{
    int angleIndex = int(dispatchId.x);
    if (angleIndex >= _DynamicPolarSize.x)
    {
        return;
    }

    int radii = _DynamicPolarSize.y;
    float angle = (float(angleIndex) + 0.5) * PI2 / float(_DynamicPolarSize.x);
    float raySine = 0.0;
    float rayCosine = 1.0;
    sincos(angle, raySine, rayCosine);
    float2 direction = float2(rayCosine, raySine);
    float2 segmentStart = 0.0;
    float emitterArea = 0.0;
    bool emits = false;
    DynamicEmitterPoint(_DynamicLights[_DynamicLightIndex], _DynamicPolarPoint, segmentStart, emitterArea, emits);
    int rowOffset = _DynamicPolarPoint * radii;
    float2 cellsPerPixel = (_WorldRect.zw / _CellSize) / float2(_FieldSize);
    float cellsPerDistance = length(direction * cellsPerPixel);
    float intervalLength = float(radii - 1);
    float3 airExtinction = SegmentExtinction(0.0);
    float3 opticalDepth = 0.0;
    float distance = 0.0;
    _DynamicPolar[int2(angleIndex, rowOffset)] = float4(0.0, 0.0, 0.0, 0.0);
    int nextRadius = 1;

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
        while (nextRadius < radii && float(nextRadius) <= entry)
        {
            _DynamicPolar[int2(angleIndex, rowOffset + nextRadius)] =
                float4(airExtinction * float(nextRadius) * cellsPerDistance, 0.0);
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
        int2 previousCell = int2(floor((float2(texel) + 0.5) * cellsPerPixel));
        [loop]
        while (distance < exitDistance)
        {
            if (any(texel < 0) || any(texel >= _FieldSize))
            {
                break;
            }

            float end = min(exitDistance, min(next.x, next.y));
            float distanceCells = max(0.0, end - distance) * cellsPerDistance;
            int2 materialPixel = texel;
            if (_MaterialYFlip != 0)
            {
                materialPixel.y = _FieldSize.y - 1 - materialPixel.y;
            }

            float solid = saturate(_MaterialField.Load(int3(materialPixel, 0)).a);
            int2 cell = int2(floor((float2(texel) + 0.5) * cellsPerPixel));
            bool diagonalOccluded = false;
            CheckDiagonalStepOccluded(previousCell, cell, diagonalOccluded);
            if (diagonalOccluded)
            {
                [loop]
                while (nextRadius < radii)
                {
                    _DynamicPolar[int2(angleIndex, rowOffset + nextRadius)] = float4(1e6, 1e6, 1e6, 0.0);
                    nextRadius++;
                }
                break;
            }

            previousCell = cell;
            float3 extinction = SegmentExtinction(solid);
            [loop]
            while (nextRadius < radii && float(nextRadius) <= end)
            {
                _DynamicPolar[int2(angleIndex, rowOffset + nextRadius)] = float4(
                    opticalDepth + extinction * (float(nextRadius) - distance) * cellsPerDistance,
                    0.0);
                nextRadius++;
            }

            opticalDepth += extinction * distanceCells;
            distance = end;
            if (distance >= exitDistance)
            {
                break;
            }

            bool crossX = next.x <= next.y;
            bool crossY = next.y <= next.x;
            if (crossX)
            {
                texel.x += step.x;
                next.x += stride.x;
            }

            if (crossY)
            {
                texel.y += step.y;
                next.y += stride.y;
            }
        }
    }

    [loop]
    while (nextRadius < radii)
    {
        _DynamicPolar[int2(angleIndex, rowOffset + nextRadius)] = float4(
            opticalDepth + airExtinction * (float(nextRadius) - distance) * cellsPerDistance,
            0.0);
        nextRadius++;
    }
}

// Optical depth from emitter point `pointIndex` to `radius` texels along its
// ray at `angle`, interpolated between neighbouring rays and whole texels.
float3 PolarOpticalDepth(int pointIndex, float angle, float radius)
{
    int rowOffset = pointIndex * _DynamicPolarSize.y;
    uint angles = uint(_DynamicPolarSize.x);
    // Shifted by a full turn so the index is never negative: unsigned modulus.
    float angleIndex = frac(angle / PI2) * float(angles) - 0.5 + float(angles);
    float angleFloor = floor(angleIndex);
    float angleBlend = angleIndex - angleFloor;
    uint angle0 = uint(angleFloor) % angles;
    uint angle1 = (angle0 + 1u) % angles;
    float radiusIndex = min(max(radius, 0.0), float(_DynamicPolarSize.y - 1));
    int radius0 = int(floor(radiusIndex));
    int radius1 = min(radius0 + 1, _DynamicPolarSize.y - 1);
    float radiusBlend = radiusIndex - float(radius0);
    float3 inner = lerp(
        _DynamicPolarInput.Load(int3(angle0, rowOffset + radius0, 0)).rgb,
        _DynamicPolarInput.Load(int3(angle1, rowOffset + radius0, 0)).rgb,
        angleBlend);
    float3 outer = lerp(
        _DynamicPolarInput.Load(int3(angle0, rowOffset + radius1, 0)).rgb,
        _DynamicPolarInput.Load(int3(angle1, rowOffset + radius1, 0)).rgb,
        angleBlend);
    return lerp(inner, outer, radiusBlend);
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
    if (all(sourceMax > 0.0) && all(sourceMin < float2(_FieldSize)))
    {
        float2 toCenter = (sourceMin + sourceMax) * 0.5 - origin;
        float centerAngle = atan2(toCenter.y, toCenter.x);
        float minAngle = -PI;
        float maxAngle = PI;
        bool inside = all(origin >= sourceMin) && all(origin < sourceMax);
        if (!inside)
        {
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
        [loop]
        for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            float angle = centerAngle + minAngle + (float(sampleIndex) + 0.5) * angularWidth / float(sampleCount);
            float raySin = 0.0;
            float rayCos = 1.0;
            sincos(angle, raySin, rayCos);
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
    if (max(gapCells.x, gapCells.y) < DynamicNearCells)
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
        int2 centerPixel = clamp(int2(floor((sourceMin + sourceMax) * 0.5)), int2(0, 0), _FieldSize - 1);
        if (_MaterialYFlip != 0)
        {
            centerPixel.y = _FieldSize.y - 1 - centerPixel.y;
        }

        float3 sourceExtinction = SegmentExtinction(saturate(_MaterialField.Load(int3(centerPixel, 0)).a));
        // The texels TraceLightSegment lets emit: centres inside the square,
        // inside the field.
        float2 emitterMin = max(ceil(sourceMin - 0.5), float2(0.0, 0.0));
        float2 emitterMax = min(ceil(sourceMax - 0.5), float2(_FieldSize));
        float2 emitterSize = max(emitterMax - emitterMin, float2(0.0, 0.0));
        float3 radiance = 0.0;
        if (emitterSize.x > 0.0 && emitterSize.y > 0.0)
        {
            [loop]
            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                float angle = centerAngle + minAngle + (float(sampleIndex) + 0.5) * angularWidth / float(sampleCount);
                float raySine = 0.0;
                float rayCosine = 1.0;
                sincos(angle, raySine, rayCosine);
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
                    float3 emissionWeight = float3(
                        CellEmissionWeight(sourceExtinction.r, chordCells),
                        CellEmissionWeight(sourceExtinction.g, chordCells),
                        CellEmissionWeight(sourceExtinction.b, chordCells));

                    float2 crossing = origin + direction * (0.5 * (entryDistance + exitDistance));
                    int2 nearestPoint = clamp(
                        int2(floor((crossing - emitterMin) / emitterSize * float(DynamicEmitterPointsPerAxis))),
                        int2(0, 0),
                        int2(DynamicEmitterPointsPerAxis - 1, DynamicEmitterPointsPerAxis - 1));
                    int pointIndex = nearestPoint.y * DynamicEmitterPointsPerAxis + nearestPoint.x;
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
                    float3 opticalDepth = max(
                        PolarOpticalDepth(pointIndex, rayAngle, receiverRadius) -
                            PolarOpticalDepth(pointIndex, rayAngle, entryRadius),
                        0.0);
                    radiance += exp(-opticalDepth) * sourceRadiance * emissionWeight;
                }
            }
        }

        result = radiance * (angularWidth / (float(sampleCount) * PI2));
    }

    return result;
}

#endif // KERN_DYNAMIC_POLAR_HLSL

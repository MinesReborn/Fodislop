#ifndef KERN_DYNAMIC_POLAR_HLSL
#define KERN_DYNAMIC_POLAR_HLSL

// Динамический свет источников: полярные лучи, optical depth lookup, per-pixel radiance.
//
// READS: _MaterialField, _DynamicLights, _DynamicPolarInput
// WRITES: _DynamicPolar
// MAY: вызывать DDA (TraceDynamicPolar, GatherDynamicSource)
// MUST NOT: трогать каскады

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
    uint pointsPerAxis = uint(_DynamicEmitterPointsPerAxis);
    float2 grid = float2(index % pointsPerAxis, index / pointsPerAxis);
    position = emitterMin + emitterSize * (grid + 0.5) / float(_DynamicEmitterPointsPerAxis);
    emits = areaCells > 0.0;
}

// One extra column on each side lets hardware filtering cross the angular
// seam without blending into a different emitter point's rows.
void WriteDynamicPolar(int angleIndex, int row, float4 depth)
{
    _DynamicPolar[int2(angleIndex + 1, row)] = depth;
    if (angleIndex == 0)
    {
        _DynamicPolar[int2(_DynamicPolarSize.x + 1, row)] = depth;
    }

    if (angleIndex == _DynamicPolarSize.x - 1)
    {
        _DynamicPolar[int2(0, row)] = depth;
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
    float angle = (float(angleIndex) + 0.5) * PI2 / float(_DynamicPolarSize.x);
    float raySine = 0.0;
    float rayCosine = 1.0;
    sincos(angle, raySine, rayCosine);
    float2 direction = float2(rayCosine, raySine);
    float2 segmentStart = 0.0;
    float emitterArea = 0.0;
    bool emits = false;
    DynamicEmitterPoint(_DynamicLights[_DynamicLightIndex], pointIndex, segmentStart, emitterArea, emits);
    int rowOffset = pointIndex * radii;
    float2 cellsPerPixel = (_WorldRect.zw / _CellSize) / float2(_FieldSize);
    float cellsPerDistance = length(direction * cellsPerPixel);
    float intervalLength = float(radii - 1);
    float3 airExtinction = SegmentExtinction(0.0);
    float3 opticalDepth = 0.0;
    float distance = 0.0;
    WriteDynamicPolar(angleIndex, rowOffset, float4(0.0, 0.0, 0.0, 0.0));

    // Где свет по лучу перестаёт быть видимым. Значения луча не меняются:
    // сбор смешивает соседние лучи, и любая подмена глубины за этой точкой
    // гасила бы видимый свет на краях теней. Глубина вдоль луча только
    // растёт, смешивание не опускает её ниже меньшей из двух, поэтому дальше
    // самого дальнего такого радиуса по всем лучам фонаря пикселю не достаётся
    // ничего видимого. Запас на глубину внутри клетки-источника (до
    // _DynamicPolarMargin клеток сплошного) — сбор вычитает её как глубину
    // входа. Правило то же, что у каскадов в DDA.hlsl.
    DynamicLight reachLight = _DynamicLights[_DynamicLightIndex];
    float3 reachSource = max(reachLight.colorIntensity.rgb * reachLight.colorIntensity.a, 0.0) * _EmissionScale;
    float3 reachEntryDepth = SegmentExtinction(1.0) * _DynamicPolarMargin;
    float reach = float(radii);
    bool reachFound = false;
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
            WriteDynamicPolar(angleIndex, rowOffset + nextRadius,
                float4(airExtinction * float(nextRadius) * cellsPerDistance, 0.0));
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
            int2 materialPixel = MaterialPixel(texel);

            float solid = saturate(_MaterialField.Load(int3(materialPixel, 0)).a);
            int2 cell = int2(floor((float2(texel) + 0.5) * cellsPerPixel));
            bool diagonalOccluded = false;
            CheckDiagonalStepOccluded(previousCell, cell, diagonalOccluded);
            if (diagonalOccluded)
            {
                [loop]
                while (nextRadius < radii)
                {
                    WriteDynamicPolar(angleIndex, rowOffset + nextRadius,
                        float4(1e6, 1e6, 1e6, 0.0));
                    nextRadius++;
                }

                if (!reachFound)
                {
                    reach = distance;
                    reachFound = true;
                }

                break;
            }

            previousCell = cell;
            float3 extinction = SegmentExtinction(solid);
            [loop]
            while (nextRadius < radii && float(nextRadius) <= end)
            {
                WriteDynamicPolar(angleIndex, rowOffset + nextRadius, float4(
                    opticalDepth + extinction * (float(nextRadius) - distance) * cellsPerDistance,
                    0.0));
                nextRadius++;
            }

            opticalDepth += extinction * distanceCells;
            distance = end;
            if (!reachFound &&
                Max3(exp(-max(opticalDepth - reachEntryDepth, 0.0)) * reachSource) * _DynamicPolarMargin < InvisibleDynamicRadiance)
            {
                reach = distance;
                reachFound = true;
            }

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
        WriteDynamicPolar(angleIndex, rowOffset + nextRadius, float4(
            opticalDepth + airExtinction * (float(nextRadius) - distance) * cellsPerDistance,
            0.0));
        nextRadius++;
    }

    // Свет не погас внутри прохода: дальше луч идёт воздухом, и записанная
    // выше глубина растёт линейно. Точка невидимости на этой прямой
    // считается формулой — те же значения, что прочтёт сбор.
    if (!reachFound)
    {
        float3 depthRate = airExtinction * cellsPerDistance;
        float3 depthNeeded = log(max(reachSource * _DynamicPolarMargin / InvisibleDynamicRadiance, 1.0)) +
            reachEntryDepth - opticalDepth;
        // Канал без затухания в воздухе с недобранной глубиной не гаснет
        // никогда: тогда дальность остаётся полной.
        bool bounded =
            (depthNeeded.x <= 0.0 || depthRate.x > 0.0) &&
            (depthNeeded.y <= 0.0 || depthRate.y > 0.0) &&
            (depthNeeded.z <= 0.0 || depthRate.z > 0.0);
        float extra = max(Max3(depthNeeded / max(depthRate, 1e-30)), 0.0);

        if (bounded)
        {
            reach = min(reach, distance + extra);
        }
    }

    InterlockedMax(_DynamicReach[_DynamicLightIndex], uint(ceil(reach)));
}

// Optical depth from emitter point `pointIndex` to `radius` texels along its
// ray at `angle`, interpolated between neighbouring rays and whole texels.
float3 PolarOpticalDepth(int pointIndex, float angle, float radius)
{
    int rowOffset = pointIndex * _DynamicPolarSize.y;
    float radiusIndex = min(max(radius, 0.0), float(_DynamicPolarSize.y - 1));
    float2 polarUv = float2(
        (frac(angle / PI2) * float(_DynamicPolarSize.x) + 1.0) /
            float(_DynamicPolarTextureSize.x),
        (float(rowOffset) + radiusIndex + 0.5) /
            float(_DynamicPolarTextureSize.y));
    return _DynamicPolarInput.SampleLevel(sampler_LinearClamp, polarUv, 0).rgb;
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
        int2 centerPixel = clamp(int2(floor((sourceMin + sourceMax) * 0.5)), int2(0, 0), _FieldSize - 1);
        int2 materialPixel = MaterialPixel(centerPixel);

        float3 sourceExtinction = SegmentExtinction(saturate(_MaterialField.Load(int3(materialPixel, 0)).a));
        // The texels TraceLightSegment lets emit: centres inside the square,
        // inside the field.
        float2 emitterMin = max(ceil(sourceMin - 0.5), float2(0.0, 0.0));
        float2 emitterMax = min(ceil(sourceMax - 0.5), float2(_FieldSize));
        float2 emitterSize = max(emitterMax - emitterMin, float2(0.0, 0.0));
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
                    float3 emissionWeight = float3(
                        CellEmissionWeight(sourceExtinction.r, chordCells),
                        CellEmissionWeight(sourceExtinction.g, chordCells),
                        CellEmissionWeight(sourceExtinction.b, chordCells));

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
                    float3 opticalDepth = max(
                        PolarOpticalDepth(pointIndex, rayAngle, receiverRadius) -
                            PolarOpticalDepth(pointIndex, rayAngle, entryRadius),
                        0.0);
                    radiance += exp(-opticalDepth) * sourceRadiance * emissionWeight;
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

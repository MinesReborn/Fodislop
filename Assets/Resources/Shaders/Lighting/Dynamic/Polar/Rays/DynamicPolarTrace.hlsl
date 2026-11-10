#ifndef KERN_DYNAMIC_POLAR_TRACE_INCLUDED
#define KERN_DYNAMIC_POLAR_TRACE_INCLUDED
void TraceDynamicPolarRay(uint3 dispatchId, DynamicTraceContext context)
{
    int angleIndex = int(dispatchId.x);
    int pointIndex = int(dispatchId.y);
    if (angleIndex >= context.polarSize.x ||
        pointIndex >= _DynamicEmitterPointsPerAxis * _DynamicEmitterPointsPerAxis)
    {
        return;
    }

    int radii = context.polarSize.y;
    // One polar row per receiver (light-lattice) texel. The march below still
    // visits every transport texel; only the stored radial samples are as
    // dense as the receivers that read them.
    float rowStep = float(_FieldTexelsPerLightTexel);
    if (_LightingCountersEnabled != 0)
    {
        InterlockedAdd(_LightingCounters[0], 1u);
    }
    float angle = (float(angleIndex) + 0.5) * PI2 / float(context.polarSize.x);
    float raySine = 0.0;
    float rayCosine = 1.0;
    sincos(angle, raySine, rayCosine);
    float2 direction = float2(rayCosine, raySine);
    float2 segmentStart = 0.0;
    float emitterArea = 0.0;
    bool emits = false;
    DynamicEmitterPoint(_DynamicLights[context.lightIndex], pointIndex, segmentStart, emitterArea, emits);
    float2 cellsPerPixel = (_WorldRect.zw / _CellSize) / float2(_FieldSize);
    float cellsPerDistance = length(direction * cellsPerPixel);
    float intervalLength = float(radii - 1) * rowStep;
    float3 airExtinction = SegmentExtinction(0.0);
    float3 opticalDepth = 0.0;
    float distance = 0.0;
    uint ddaStepCount = 0u;
    uint sdfStepCount = 0u;
    WriteDynamicPolar(angleIndex, 0, pointIndex, float4(0.0, 0.0, 0.0, 0.0), context);

    int nextRadius = 1;

    // Angular horizon: the radius along this ray beyond which nothing it
    // carries can reach any display level. The stored depths are untouched;
    // receivers beyond the horizon of every ray they blend are skipped
    // (DynamicHorizonContains), so light is never cut while still visible.
    DynamicLight horizonLight = _DynamicLights[context.lightIndex];
    float3 horizonSource = max(horizonLight.colorIntensity.rgb * horizonLight.colorIntensity.a, 0.0) *
        _GlowScale * DynamicHorizonWeightMargin;
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
                float4(airExtinction * float(nextRadius) * rowStep * cellsPerDistance, 0.0), context);
            nextRadius++;
        }

        opticalDepth = airExtinction * entry * cellsPerDistance;
        distance = entry;
        if (_DynamicSdfTransportEnabled != 0)
        {
            // Treat JFA distance as an estimate, subtract a texel-sized margin,
            // and leave the boundary tail to the exact DDA path below. The
            // production DDA image comparison remains the accuracy gate for this
            // opt-in mode because JFA does not prove the globally nearest seed.
            [loop]
            while (distance < exitDistance)
            {
                float2 position = segmentStart + direction * distance;
                int2 sdfTexel = int2(floor(position));
                if (any(sdfTexel < 0) || any(sdfTexel >= _FieldSize))
                {
                    break;
                }

                float seedDistance = _DynamicSdfInput.Load(int3(sdfTexel, 0));
                if (_LightingCountersEnabled != 0)
                {
                    InterlockedAdd(_LightingCounters[3], 1u);
                    sdfStepCount++;
                }
                float sampleOffset = length(position - (float2(sdfTexel) + 0.5));
                float clearance = max(seedDistance - sampleOffset - 1.0, 0.0);
                if (clearance <= 0.5)
                {
                    break;
                }

                float advance = min(clearance * 0.5, exitDistance - distance);
                if (advance <= 0.0)
                {
                    break;
                }

                float end = distance + advance;
                [loop]
                while (nextRadius < radii && float(nextRadius) * rowStep <= end)
                {
                    WriteDynamicPolar(angleIndex, nextRadius, pointIndex, float4(
                        opticalDepth + airExtinction * (float(nextRadius) * rowStep - distance) * cellsPerDistance,
                        0.0), context);
                    nextRadius++;
                }
                opticalDepth += airExtinction * advance * cellsPerDistance;
                distance = end;
            }
        }
        float2 start = segmentStart + direction * distance;
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
        int solidEntryMask = 0;
        int2 solidEntryLines = 0;
        int2 solidEntryTexel = 0;
        float solidDistanceCells = 0.0;
        bool previousSolid = false;
        int2 maxCornerSpan = int2(ceil(1.0 / max(cellsPerPixel, 0.0001))) + 1;
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
                ddaStepCount++;
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
            if (CornerSealed(enteredMask, enteredLines, solidEntryMask, solidEntryLines,
                solidEntryTexel, texel, maxCornerSpan,
                previousSolid, currentSolid, step))
            {
                float extraCells = max(0.0, 1.0 - solidDistanceCells);
                opticalDepth += SegmentExtinction(1.0) * extraCells;
                solidEntryMask = 0;
            }

            if (!previousSolid && currentSolid)
            {
                solidEntryMask = enteredMask;
                solidEntryLines = enteredLines;
                solidEntryTexel = texel;
                solidDistanceCells = 0.0;
            }

            float3 extinction = SegmentExtinction(solid);
            [loop]
            while (nextRadius < radii && float(nextRadius) * rowStep <= end)
            {
                WriteDynamicPolar(angleIndex, nextRadius, pointIndex, float4(
                    opticalDepth + extinction * (float(nextRadius) * rowStep - distance) * cellsPerDistance,
                    0.0), context);
                nextRadius++;
            }

            opticalDepth += extinction * distanceCells;
            if (currentSolid)
            {
                solidDistanceCells += distanceCells;
            }
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
            previousSolid = currentSolid;
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
            0.0), context);
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

    if (_LightingCountersEnabled != 0)
    {
        uint ddaBin = min(ddaStepCount, DynamicTraversalHistogramBins - 1u);
        uint sdfBin = min(sdfStepCount, DynamicTraversalHistogramBins - 1u);
        uint totalBin = min(ddaStepCount + sdfStepCount, DynamicTraversalHistogramBins - 1u);
        InterlockedAdd(_LightingCounters[DynamicTraversalDdaHistogramOffset + ddaBin], 1u);
        InterlockedAdd(_LightingCounters[DynamicTraversalSdfHistogramOffset + sdfBin], 1u);
        InterlockedAdd(_LightingCounters[DynamicTraversalTotalHistogramOffset + totalBin], 1u);
    }

    _DynamicHorizon[context.horizonBase + pointIndex * _DynamicHorizonStride + angleIndex] = uint(ceil(horizon));
}

// Optical depth along one stored ray (texture column `column`, wrap columns
// included) from emitter point `pointIndex` to `radius` transport texels,
// linear between rows. Rows are one receiver texel apart.

#endif // KERN_DYNAMIC_POLAR_TRACE_INCLUDED

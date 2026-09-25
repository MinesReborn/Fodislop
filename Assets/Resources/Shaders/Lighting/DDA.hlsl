#ifndef KERN_DDA_HLSL
#define KERN_DDA_HLSL

// Примитивы геометрического traversal (DDA ray marching).
//
// READS: _MaterialField, _CellSolidMask, _EmissionField, _MaterialYFlip, _FieldSize, _WorldRect, _CellSize, _CellGridSize
// WRITES: ничего (out-параметры)
// MAY: маршировать геометрию
// MUST NOT: знать о каскадах, источниках, dynamic lights

// COST: 1 bilinear texture sample
// Reference rule, evaluated once per cell by BuildCellSolidMask.
void SampleCellSolid(int2 cellCoord, float2 cellsPerPixel, out bool isSolid)
{
    isSolid = false;
    float2 centerPixel = (float2(cellCoord) + 0.5) / cellsPerPixel;
    if (all(centerPixel >= 0.0) && all(centerPixel < float2(_FieldSize)))
    {
        isSolid = (SampleOccupancy(centerPixel, 0.0) >= _TransportSolidThreshold);
    }
}

// COST: 1 point texture load from mask (O(1)). The caller already has a base
// cell coordinate, so do not reconstruct a pixel position for every DDA step.
void CheckCellSolid(int2 cellCoord, out bool isSolid)
{
    isSolid = false;
    if (all(cellCoord >= 0) && all(cellCoord < _CellGridSize))
    {
        isSolid = IsSolidOccupancy(_CellSolidMask.Load(int3(cellCoord, 0)).r);
    }
}

// COST: 2 point loads (diagonal corner check)
// Blocks sharing a single common vertex (touching diagonally) must not transmit light between them.
void CheckDiagonalStepOccluded(int2 prevCell, int2 currentCell, out bool isOccluded)
{
    isOccluded = false;
    int dx = currentCell.x - prevCell.x;
    int dy = currentCell.y - prevCell.y;
    if (dx != 0 && dy != 0)
    {
        int stepX = clamp(dx, -1, 1);
        int stepY = clamp(dy, -1, 1);
        int2 cornerA = int2(prevCell.x + stepX, prevCell.y);
        int2 cornerB = int2(prevCell.x, prevCell.y + stepY);
        bool solidA = false;
        bool solidB = false;
        CheckCellSolid(cornerA, solidA);
        CheckCellSolid(cornerB, solidB);

        // Только пара углов у ИСХОДНОЙ клетки. Вторая пара, у клетки
        // назначения, стоила ещё двух выборок и срабатывала ровно тогда,
        // когда шаг перепрыгнул клетку — а перепрыгнувший луч прошёл не
        // через названный угол, а мимо. Две выборки за догадку.
        isOccluded = solidA && solidB;
    }
}

// COST: O(1). Пересечение отрезка с полем (slab-тест).
// Пространство вне поля пусто и не светится, поэтому марш идёт только по
// пересечению, а хвосты снаружи учитываются одним множителем пропускания.
void ClipSegmentToField(
    float2 segmentStart,
    float2 inverseDirection,
    float intervalLength,
    out float entry,
    out float exitDistance)
{
    float2 slabA = -segmentStart * inverseDirection;
    float2 slabB = (float2(_FieldSize) - segmentStart) * inverseDirection;
    float2 slabNear = min(slabA, slabB);
    float2 slabFar = max(slabA, slabB);
    entry = max(0.0, max(slabNear.x, slabNear.y));
    exitDistance = min(intervalLength, min(slabFar.x, slabFar.y));
}

// COST: O(1). Расстояние, за которым изолированный источник уже не светит.
//
// Изолированный источник излучает только текселями, чьи центры лежат внутри
// sourceRect, то есть целочисленной коробкой [ceil(min - 0.5), ceil(max - 0.5)).
// Выйдя из неё, луч вернуться в неё не может, и каждый следующий тексель
// добавляет ровно ноль. Остановка здесь не меняет radiance; частичным остаётся
// только пропускание, а вызывающие с isolateSource его не читают.
float EmissiveBoxExit(float2 segmentStart, float2 inverseDirection, float4 sourceRect)
{
    float2 boxA = (ceil(sourceRect.xy - 0.5) - segmentStart) * inverseDirection;
    float2 boxB = (ceil(sourceRect.zw - 0.5) - segmentStart) * inverseDirection;
    float2 boxFar = max(boxA, boxB);
    return min(boxFar.x, boxFar.y);
}

// COST: O(1). Клетка, которой принадлежит тексель поля.
int2 CellOfTexel(int2 texel, float2 cellsPerPixel)
{
    return int2(floor((float2(texel) + 0.5) * cellsPerPixel));
}

// COST: O(N) where N is crossed cells in segment (DDA marching traversal)
void TraceLightSegment(
    float2 segmentStart,
    float2 segmentEnd,
    bool collectEmission,
    bool isolateSource,
    float4 sourceRect,
    float3 sourceRadiance,
    out float3 radiance,
    out float3 transmittance)
{
    if (_LightingCountersEnabled != 0)
    {
        InterlockedAdd(_LightingCounters[0], 1u);
    }
    radiance = 0.0;
    transmittance = 1.0;
    float2 segment = segmentEnd - segmentStart;
    float intervalLength = length(segment);
    if (intervalLength <= 0.0)
    {
        return;
    }

    float2 direction = segment / intervalLength;
    float2 cellsPerPixel = (_WorldRect.zw / _CellSize) / float2(_FieldSize);
    float cellsPerDistance = length(direction * cellsPerPixel);

    float2 inverseDirection = float2(
        abs(direction.x) > 1e-20 ? 1.0 / direction.x : 1e20,
        abs(direction.y) > 1e-20 ? 1.0 / direction.y : 1e20);
    float entry = 0.0;
    float exitDistance = 0.0;
    ClipSegmentToField(segmentStart, inverseDirection, intervalLength, entry, exitDistance);
    if (exitDistance <= entry)
    {
        transmittance = SegmentTransmission(0.0, intervalLength * cellsPerDistance);
        return;
    }

    transmittance = SegmentTransmission(0.0, entry * cellsPerDistance);
    float2 start = segmentStart + direction * entry;
    int2 texel = int2(floor(start));
    int2 step = int2(sign(direction));
    // A ray starting on a boundary and travelling backwards enters the
    // preceding texel. No positional epsilon that could skip thin blockers.
    texel.x -= direction.x < 0.0 && start.x == floor(start.x) ? 1 : 0;
    texel.y -= direction.y < 0.0 && start.y == floor(start.y) ? 1 : 0;
    texel = clamp(texel, int2(0, 0), _FieldSize - 1);
    float2 boundary = float2(texel) + float2(direction.x > 0.0 ? 1.0 : 0.0, direction.y > 0.0 ? 1.0 : 0.0);
    float2 next = float2(
        step.x != 0 ? (boundary.x - segmentStart.x) / direction.x : 1e20,
        step.y != 0 ? (boundary.y - segmentStart.y) / direction.y : 1e20);
    float2 stride = abs(inverseDirection);
    float distance = entry;
    int2 previousCell = CellOfTexel(texel, cellsPerPixel);

    float emissionExit = collectEmission && isolateSource
        ? EmissiveBoxExit(segmentStart, inverseDirection, sourceRect)
        : 1e30;

    // DDA visits EVERY crossed base-level texel. A quality step budget must
    // never turn a wall into an averaged mip or jump over it.
    [loop]
    while (distance < exitDistance)
    {
        // Accumulated boundary distances can differ from slab clipping by an
        // ulp at the field edge. Never issue an out-of-range texture load.
        if (any(texel < 0) || any(texel >= _FieldSize))
        {
            transmittance *= SegmentTransmission(0.0, max(0.0, exitDistance - distance) * cellsPerDistance);
            break;
        }

        if (_LightingCountersEnabled != 0)
        {
            InterlockedAdd(_LightingCounters[1], 1u);
        }

        float end = min(exitDistance, min(next.x, next.y));
        float distanceCells = max(0.0, end - distance) * cellsPerDistance;
        int2 materialPixel = MaterialPixel(texel);

        float solid = saturate(_MaterialField.Load(int3(materialPixel, 0)).a);
        int2 cell = CellOfTexel(texel, cellsPerPixel);
        bool diagonalOccluded = false;
        CheckDiagonalStepOccluded(previousCell, cell, diagonalOccluded);
        if (diagonalOccluded)
        {
            // The closed corner is before this texel, including its emitter.
            transmittance = 0.0;
            break;
        }

        previousCell = cell;
        float3 extinction = SegmentExtinction(solid);
        float3 transmission = exp(-extinction * distanceCells);
        if (collectEmission)
        {
            float3 emission = 0.0;
            if (isolateSource)
            {
                float2 samplePosition = float2(texel) + 0.5;
                if (all(samplePosition >= sourceRect.xy) && all(samplePosition < sourceRect.zw))
                {
                    emission = sourceRadiance;
                }
            }
            else
            {
                emission = max(_EmissionField.Load(int3(materialPixel, 0)).rgb, 0.0) * _EmissionScale;
            }

            if (Max3(emission) > 0.0)
            {
                float3 emissionWeight = float3(
                    CellEmissionWeight(extinction.r, distanceCells),
                    CellEmissionWeight(extinction.g, distanceCells),
                    CellEmissionWeight(extinction.b, distanceCells));
                radiance += transmittance * emission * emissionWeight;
            }
        }

        transmittance *= transmission;
        distance = end;
        // A dynamic light ray also stops once everything it could still bring is below
        // what any display can show. The rest of the path is bounded by
        // transmittance * source radiance * the largest single-cell emission
        // weight (a cell crossed diagonally, ~1.42 < 1.5). The bound is
        // absolute radiance, where 1.0 is exposure-0 white: 1e-6 sits two
        // orders below half an 8-bit sRGB step at black (1.5e-4), so even the
        // tails of dozens of dynamic lights meeting in one pixel stay below one level.
        bool tailInvisible = collectEmission && isolateSource &&
            Max3(transmittance * sourceRadiance) * 1.5 < InvisibleDynamicRadiance;
        if (distance >= exitDistance || distance >= emissionExit || Max3(transmittance) == 0.0 ||
            tailInvisible)
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

    transmittance *= SegmentTransmission(0.0, (intervalLength - exitDistance) * cellsPerDistance);
}

void TraceRadianceSegment(
    float2 segmentStart,
    float2 segmentEnd,
    bool collectEmission,
    out float3 radiance,
    out float3 transmittance)
{
    TraceLightSegment(segmentStart, segmentEnd, collectEmission, false,
        float4(0.0, 0.0, 0.0, 0.0), float3(0.0, 0.0, 0.0), radiance, transmittance);
}

#endif // KERN_DDA_HLSL

#ifndef KERN_DDA_HLSL
#define KERN_DDA_HLSL

// Примитивы геометрического traversal (DDA ray marching).
//
// READS: _MaterialField, _CellSolidMask, _EmissionField, _MaterialYFlip, _FieldSize, _WorldRect, _CellSize, _CellGridSize
// WRITES: ничего (out-параметры)
// MAY: маршировать геометрию
// MUST NOT: знать о каскадах, источниках, dynamic lights

// Geometry-aware corner seal on the transport lattice.
//
// Two solids that touch at a single texel corner form a closed wall: light
// must not slip between them. Rasterized geometry decides, not the cell grid:
// cells that share a grid vertex but whose displaced/rounded silhouettes leave
// a gap at that corner have air texels there and stay open.
//
// A ray crosses such a corner either exactly through the lattice point (both
// axes at once) or by cutting the corner of one solid: air P -> solid M ->
// air D, entering M across one axis and leaving across the other. The texel O
// diagonally opposite M across that corner is the second solid of the pair.
// The chord through M is then arbitrarily short and the extinction model alone
// would let the light through almost unattenuated.

// COST: 1 point load. Field-global texel; outside the field is air.
bool TransportSolidTexel(int2 fieldTexel)
{
    bool solid = false;
    if (all(fieldTexel >= 0) && all(fieldTexel < _FieldSize))
    {
        solid = _MaterialField.Load(int3(MaterialPixel(fieldTexel), 0)).a >= _TransportSolidThreshold;
    }
    return solid;
}

bool TransportSolidOccupancy(float occupancy)
{
    return occupancy >= _TransportSolidThreshold;
}

// Texel coordinate on the side of lattice line `latticeLine` the ray comes
// from (near) or goes to (far), for a ray moving with sign `stepSign`.
// (`line` is a reserved HLSL word.)
int NearSideTexel(int latticeLine, int stepSign)
{
    return stepSign > 0 ? latticeLine - 1 : latticeLine;
}

int FarSideTexel(int latticeLine, int stepSign)
{
    return stepSign > 0 ? latticeLine : latticeLine - 1;
}

// Crossing mask bits: 1 = x lattice line crossed, 2 = y lattice line crossed.
// `entered*` describe the step into the current region D, `previous*` the
// step into the region M before it; lines are field-global texel coordinates.
// COST: 0 loads on ordinary steps; 1 at an L-turn out of a solid between air;
// 2 at an exact lattice-point crossing.
bool CornerSealed(
    int enteredMask,
    int2 enteredLines,
    int previousMask,
    int2 previousLines,
    bool beforePreviousSolid,
    bool previousSolid,
    bool currentSolid,
    int2 step)
{
    bool sealed = false;
    if (enteredMask == 3)
    {
        // Exactly through the lattice point: the two texels beside the
        // crossing, on opposite diagonals, close it when both are solid.
        int2 sideA = int2(FarSideTexel(enteredLines.x, step.x), NearSideTexel(enteredLines.y, step.y));
        int2 sideB = int2(NearSideTexel(enteredLines.x, step.x), FarSideTexel(enteredLines.y, step.y));
        sealed = TransportSolidTexel(sideA) && TransportSolidTexel(sideB);
    }
    else if ((enteredMask == 1 || enteredMask == 2) &&
        (previousMask == 1 || previousMask == 2) &&
        enteredMask != previousMask &&
        previousSolid && !beforePreviousSolid && !currentSolid)
    {
        // M was entered across one axis and left across the other: O lies on
        // the near side of M's entry line and the far side of its exit line.
        int lineX = previousMask == 1 ? previousLines.x : enteredLines.x;
        int lineY = previousMask == 2 ? previousLines.y : enteredLines.y;
        int2 opposite = int2(
            previousMask == 1 ? NearSideTexel(lineX, step.x) : FarSideTexel(lineX, step.x),
            previousMask == 2 ? NearSideTexel(lineY, step.y) : FarSideTexel(lineY, step.y));
        sealed = TransportSolidTexel(opposite);
    }
    return sealed;
}

// COST: 4 loads. Cells inside the field-texel box [minTexel, maxTexel] that
// are not clean air (x: zero occupancy and emission in every texel) and not
// clean stone (y: full occupancy, zero emission in every texel). Space
// outside the cell grid is air, exactly as DDA treats outside-field space:
// it never breaks the air proof and always breaks the stone proof.
uint2 CleanCellPrefixAt(int2 cell)
{
    return any(cell < 0) ? uint2(0u, 0u) : _CleanCellPrefix[cell.y * _CellGridSize.x + cell.x];
}

uint2 NonCleanCellCount(float2 minTexel, float2 maxTexel)
{
    float2 cellsPerPixel = (_WorldRect.zw / _CellSize) / float2(_FieldSize);
    int2 firstUnclamped = int2(floor(minTexel * cellsPerPixel));
    int2 lastUnclamped = int2(floor(maxTexel * cellsPerPixel));
    int2 first = max(firstUnclamped, int2(0, 0));
    int2 last = min(lastUnclamped, _CellGridSize - 1);
    uint2 count = uint2(0u, 0u);
    if (all(first <= last))
    {
        count = CleanCellPrefixAt(last) + CleanCellPrefixAt(first - 1) -
            CleanCellPrefixAt(int2(first.x - 1, last.y)) -
            CleanCellPrefixAt(int2(last.x, first.y - 1));
    }
    if (any(firstUnclamped != first) || any(lastUnclamped != last))
    {
        count.y += 1u;
    }
    return count;
}

// Transmittance of a straight path of `lengthTexels` along `direction`
// through one uniform medium (occupancy 0 = clean air, 1 = clean stone): what
// DDA multiplies segment by segment, in one step.
float3 CleanMediumTransmittance(float occupancy, float2 direction, float lengthTexels)
{
    return SegmentTransmission(occupancy, PathLengthInCells(direction, lengthTexels));
}

// COST: O(1). Пересечение отрезка с полем (slab-тест).
// Outside-field material is empty air. Static field emission is absent there;
// a supplied continuous emitter is integrated analytically through those tails.
void ClipSegmentToField(
    float2 segmentStart,
    float2 inverseDirection,
    float intervalLength,
    int2 fieldAnchor,
    out float entry,
    out float exitDistance)
{
    float2 slabA = (-float2(fieldAnchor) - segmentStart) * inverseDirection;
    float2 slabB = (float2(_FieldSize - fieldAnchor) - segmentStart) * inverseDirection;
    float2 slabNear = min(slabA, slabB);
    float2 slabFar = max(slabA, slabB);
    entry = max(0.0, max(slabNear.x, slabNear.y));
    exitDistance = min(intervalLength, min(slabFar.x, slabFar.y));
}

// COST: O(1). Расстояние, за которым изолированный источник уже не светит.
//
// The source has continuous bounds; material transport still visits every
// crossed field texel. Rounding the source to texel centers changes its area
// abruptly when the robot crosses the transport grid.
float EmissiveBoxExit(float2 segmentStart, float2 inverseDirection, float4 sourceRect)
{
    float2 boxA = (sourceRect.xy - segmentStart) * inverseDirection;
    float2 boxB = (sourceRect.zw - segmentStart) * inverseDirection;
    float2 boxFar = max(boxA, boxB);
    return min(boxFar.x, boxFar.y);
}

// COST: O(1). Клетка, которой принадлежит тексель поля.
int2 CellOfTexel(int2 texel, float2 cellsPerPixel)
{
    return int2(floor((float2(texel) + 0.5) * cellsPerPixel));
}

// COST: O(N) where N is crossed cells in segment (DDA marching traversal)
void TraceLightSegmentLocal(
    float2 segmentStart,
    float2 segmentEnd,
    bool collectEmission,
    bool isolateSource,
    float4 sourceRect,
    float3 sourceRadiance,
    int2 fieldAnchor,
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
    float emissionExit = collectEmission && isolateSource
        ? EmissiveBoxExit(segmentStart, inverseDirection, sourceRect)
        : 1e30;
    float2 sourceNear = min((sourceRect.xy - segmentStart) * inverseDirection,
        (sourceRect.zw - segmentStart) * inverseDirection);
    float emissionEntry = max(0.0, max(sourceNear.x, sourceNear.y));
    float entry = 0.0;
    float exitDistance = 0.0;
    ClipSegmentToField(segmentStart, inverseDirection, intervalLength, fieldAnchor, entry, exitDistance);
    if (exitDistance <= entry)
    {
        float airEmissionEnd = min(intervalLength, emissionExit);
        if (collectEmission && isolateSource && airEmissionEnd > emissionEntry)
        {
            radiance = SegmentTransmission(0.0, emissionEntry * cellsPerDistance) * sourceRadiance *
                MediumEmissionWeight(SegmentExtinction(0.0), (airEmissionEnd - emissionEntry) * cellsPerDistance);
        }
        transmittance = SegmentTransmission(0.0, intervalLength * cellsPerDistance);
        return;
    }

    float prefixEmissionEnd = min(entry, emissionExit);
    if (collectEmission && isolateSource && prefixEmissionEnd > emissionEntry)
    {
        radiance = SegmentTransmission(0.0, emissionEntry * cellsPerDistance) * sourceRadiance *
            MediumEmissionWeight(SegmentExtinction(0.0), (prefixEmissionEnd - emissionEntry) * cellsPerDistance);
    }
    transmittance = SegmentTransmission(0.0, entry * cellsPerDistance);
    float2 start = segmentStart + direction * entry;
    int2 texel = int2(floor(start));
    int2 step = int2(sign(direction));
    // A ray starting on a boundary and travelling backwards enters the
    // preceding texel. No positional epsilon that could skip thin blockers.
    texel.x -= direction.x < 0.0 && start.x == floor(start.x) ? 1 : 0;
    texel.y -= direction.y < 0.0 && start.y == floor(start.y) ? 1 : 0;
    texel = clamp(texel, -fieldAnchor, _FieldSize - fieldAnchor - 1);
    float2 boundary = float2(texel) + float2(direction.x > 0.0 ? 1.0 : 0.0, direction.y > 0.0 ? 1.0 : 0.0);
    float2 next = float2(
        step.x != 0 ? (boundary.x - segmentStart.x) / direction.x : 1e20,
        step.y != 0 ? (boundary.y - segmentStart.y) / direction.y : 1e20);
    float2 stride = abs(inverseDirection);
    float distance = entry;
    // Corner-seal history: how the current and previous regions (texel or
    // uniform cell) were entered, and whether the two regions before were solid.
    int enteredMask = 0;
    int2 enteredLines = 0;
    int previousMask = 0;
    int2 previousLines = 0;
    bool previousSolid = false;
    bool beforePreviousSolid = false;
    int2 cachedUniformCell = int2(-1, -1);
    bool uniformCell = false;
    float uniformOccupancy = 0.0;

    // Nonuniform cells visit EVERY crossed base texel. Uniform cells have
    // an exhaustive mip-zero proof, so their identical extinction integrates
    // analytically to the cell boundary. No silhouette or emitter is skipped.
    [loop]
    while (distance < exitDistance)
    {
        // Accumulated boundary distances can differ from slab clipping by an
        // ulp at the field edge. Never issue an out-of-range texture load.
        if (any(texel + fieldAnchor < 0) || any(texel + fieldAnchor >= _FieldSize))
        {
            transmittance *= SegmentTransmission(0.0, max(0.0, exitDistance - distance) * cellsPerDistance);
            break;
        }

        if (_LightingCountersEnabled != 0)
        {
            InterlockedAdd(_LightingCounters[1], 1u);
        }

        int2 cell = CellOfTexel(texel + fieldAnchor, cellsPerPixel);
        if (any(cell != cachedUniformCell))
        {
            float4 proof = _CellSolidMask.Load(int3(cell, 0));
            uniformCell = _UniformCellTraversalEnabled != 0 &&
                ((collectEmission && !isolateSource) ? proof.g : proof.a) == 1.0;
            if (uniformCell)
            {
                uniformOccupancy = _MaterialField.Load(int3(MaterialPixel(texel + fieldAnchor), 0)).a;
            }
            cachedUniformCell = cell;
        }
        float2 cellMin = 0.0;
        float2 cellMax = 0.0;
        float2 intervalNext = next;
        if (uniformCell)
        {
            cellMin = float2(cell) / cellsPerPixel - float2(fieldAnchor);
            cellMax = float2(cell + 1) / cellsPerPixel - float2(fieldAnchor);
            float2 cellBoundary = float2(direction.x > 0.0 ? cellMax.x : cellMin.x,
                direction.y > 0.0 ? cellMax.y : cellMin.y);
            intervalNext = float2(
                step.x != 0 ? (cellBoundary.x - segmentStart.x) * inverseDirection.x : 1e20,
                step.y != 0 ? (cellBoundary.y - segmentStart.y) * inverseDirection.y : 1e20);
        }
        float end = min(exitDistance, min(intervalNext.x, intervalNext.y));
        float distanceCells = max(0.0, end - distance) * cellsPerDistance;
        int2 materialPixel = MaterialPixel(texel + fieldAnchor);

        float solid = uniformOccupancy;
        if (!uniformCell)
        {
            solid = saturate(_MaterialField.Load(int3(materialPixel, 0)).a);
        }
        bool currentSolid = TransportSolidOccupancy(solid);
        if (CornerSealed(enteredMask, enteredLines, previousMask, previousLines,
            beforePreviousSolid, previousSolid, currentSolid, step))
        {
            // The closed corner is before this region, including its emitter.
            transmittance = 0.0;
            break;
        }

        float3 extinction = SegmentExtinction(solid);
        float3 transmission = OpticalDepthTransmission(extinction * distanceCells);
        if (collectEmission)
        {
            float3 emission = 0.0;
            float emissionDistanceCells = distanceCells;
            float emissionLeadCells = 0.0;
            if (isolateSource)
            {
                float emissionStart = max(distance, emissionEntry);
                float emissionEnd = min(end, emissionExit);
                emissionDistanceCells = max(0.0, emissionEnd - emissionStart) * cellsPerDistance;
                emissionLeadCells = max(0.0, emissionStart - distance) * cellsPerDistance;
                emission = sourceRadiance;
            }
            else if (!uniformCell)
            {
                emission = max(_EmissionField.Load(int3(materialPixel, 0)).rgb, 0.0) * _EmissionScale;
            }

            // Most isolated-source intervals are between receiver and emitter,
            // where the emission integral is exactly zero. Do not evaluate its
            // exponentials/divisions until the ray actually enters the source.
            if (Max3(emission) > 0.0 && emissionDistanceCells > 0.0)
            {
                float3 emissionWeight = MediumEmissionWeight(extinction, emissionDistanceCells);
                radiance += transmittance * OpticalDepthTransmission(extinction * emissionLeadCells) * emission * emissionWeight;
            }
        }

        transmittance *= transmission;
        distance = end;
        // A dynamic light ray also stops once everything it could still bring is below
        // what any display can show. The rest of the path is bounded by
        // transmittance * source radiance * the largest single-cell emission
        // weight (a cell crossed diagonally, ~1.42 < 1.5). The bound is
        // absolute radiance derived from the active output (SDR or HDR PQ),
        // exposure and source count, so even the tails of every dynamic light
        // meeting in one pixel stay below one display level.
        bool tailInvisible = collectEmission && isolateSource &&
            Max3(transmittance * sourceRadiance) * 1.5 < _InvisibleDynamicRadiance;
        if (distance >= exitDistance || distance >= emissionExit || Max3(transmittance) == 0.0 ||
            tailInvisible)
        {
            break;
        }

        bool crossX = intervalNext.x <= intervalNext.y;
        bool crossY = intervalNext.y <= intervalNext.x;
        // Lattice lines this step crosses, in field-global texel coordinates.
        int2 crossedLines = texel + fieldAnchor + int2(step.x > 0 ? 1 : 0, step.y > 0 ? 1 : 0);
        if (uniformCell)
        {
            int2 minimum = int2(round(cellMin));
            int2 maximum = int2(round(cellMax));
            crossedLines = int2(step.x > 0 ? maximum.x : minimum.x, step.y > 0 ? maximum.y : minimum.y) +
                fieldAnchor;
        }
        beforePreviousSolid = previousSolid;
        previousSolid = currentSolid;
        previousMask = enteredMask;
        previousLines = enteredLines;
        enteredMask = (crossX ? 1 : 0) | (crossY ? 2 : 0);
        enteredLines = crossedLines;
        if (uniformCell)
        {
            // Set the crossed axis from the exact integer cell boundary.
            // Reconstructing both axes from a float endpoint can re-enter the
            // preceding cell at large field coordinates and stall the loop.
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

    float tailEmissionStart = max(exitDistance, emissionEntry);
    float tailEmissionEnd = min(intervalLength, emissionExit);
    if (collectEmission && isolateSource && distance >= exitDistance && tailEmissionEnd > tailEmissionStart)
    {
        radiance += transmittance * SegmentTransmission(0.0, (tailEmissionStart - exitDistance) * cellsPerDistance) *
            sourceRadiance * MediumEmissionWeight(SegmentExtinction(0.0), (tailEmissionEnd - tailEmissionStart) * cellsPerDistance);
    }
    transmittance *= SegmentTransmission(0.0, (intervalLength - exitDistance) * cellsPerDistance);
}

// Existing dynamic/bounce callers retain field-local coordinates. Static
// cascade callers use an integer probe anchor so translating the field cannot
// change ray lengths by rounding large absolute endpoints before subtraction.
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
    TraceLightSegmentLocal(segmentStart, segmentEnd, collectEmission, isolateSource,
        sourceRect, sourceRadiance, int2(0, 0), radiance, transmittance);
}

void TraceRadianceProbeSegment(
    float2 probeOrigin,
    float2 startOffset,
    float2 endOffset,
    out float3 radiance,
    out float3 transmittance)
{
    int2 anchor = int2(floor(probeOrigin));
    float2 localOrigin = frac(probeOrigin);
    TraceLightSegmentLocal(localOrigin + startOffset, localOrigin + endOffset,
        true, false, float4(0.0, 0.0, 0.0, 0.0), float3(0.0, 0.0, 0.0),
        anchor, radiance, transmittance);
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

#ifndef KERN_TERRAIN_GEOMETRY_INCLUDED
#define KERN_TERRAIN_GEOMETRY_INCLUDED

#include "TerrainGeometryContract.hlsl"

// Requires TerrainMaterialCBuffer.hlsl before inclusion because the canonical
// cell shape is parameterized by the material's organic bend strength and pivot.
static const float KERN_TERRAIN_GEOMETRY_EPSILON = 0.0001;
static const float KERN_TERRAIN_BEND_ROUNDING_ERROR_BOUND =
    0.5 / KERN_TERRAIN_FACE_GRID_SIZE;

float TerrainGeometryEdgeCross(
    float2 edgeStart,
    float2 edgeEnd,
    float2 samplePosition)
{
    float2 edge = edgeEnd - edgeStart;
    float2 toSample = samplePosition - edgeStart;
    return (edge.x * toSample.y) - (edge.y * toSample.x);
}

float2 TerrainGeometryRawCorner(float4 cornersX, float4 cornersY, int index)
{
    if (index == 0)
    {
        return float2(cornersX.x, cornersY.x);
    }

    if (index == 1)
    {
        return float2(cornersX.y, cornersY.y);
    }

    if (index == 2)
    {
        return float2(cornersX.z, cornersY.z);
    }

    return float2(cornersX.w, cornersY.w);
}

// The pixel grid is applied only after all continuous corner and edge
// displacements have been combined into the final polygon vertices.
float2 TerrainGeometryCorner(float4 cornersX, float4 cornersY, int index)
{
    return QuantizeTerrainGeometryPoint(
        TerrainGeometryRawCorner(cornersX, cornersY, index));
}

float2 TerrainGeometrySegmentClosestPoint(
    float2 samplePosition,
    float2 edgeStart,
    float2 edgeEnd)
{
    float2 edge = edgeEnd - edgeStart;
    float edgeLengthSquared = max(dot(edge, edge), KERN_TERRAIN_GEOMETRY_EPSILON);
    float projection = saturate(dot(samplePosition - edgeStart, edge) / edgeLengthSquared);
    return edgeStart + edge * projection;
}

float TerrainGeometrySegmentDistanceSquared(
    float2 samplePosition,
    float2 edgeStart,
    float2 edgeEnd)
{
    float2 delta = samplePosition - TerrainGeometrySegmentClosestPoint(
        samplePosition, edgeStart, edgeEnd);
    return dot(delta, delta);
}

float2 TerrainOrganicGeometryPoint(
    float4 cornersX,
    float4 cornersY,
    float4 bends,
    int index)
{
    if ((index & 1) == 0)
    {
        return TerrainGeometryCorner(cornersX, cornersY, index >> 1);
    }

    int side = index >> 1;
    float2 start = TerrainGeometryRawCorner(cornersX, cornersY, side);
    float2 end = TerrainGeometryRawCorner(
        cornersX, cornersY, (side + 1) % KERN_TERRAIN_ORGANIC_EDGE_COUNT);
    float2 bend = side == 0 ? float2(0.0, bends.x) :
        side == 1 ? float2(bends.y, 0.0) :
        side == 2 ? float2(0.0, bends.z) : float2(bends.w, 0.0);
    float signedBend = side == 0 ? bends.x :
        side == 1 ? bends.y : side == 2 ? bends.z : bends.w;
    // A centered outward bend gives a barrel silhouette. Offset the extra
    // point along the edge; reverse t for the top and left shared edges. Snap
    // the generated bend vertex once to the cell geometry grid.
    float edgeT = signedBend > 0.0 ? _OrganicBendPivot : 1.0 - _OrganicBendPivot;
    if (side >= 2)
    {
        edgeT = 1.0 - edgeT;
    }

    float2 bendPosition = lerp(start, end, edgeT) + bend;
    return QuantizeTerrainGeometryPoint(bendPosition);
}

float2 TerrainPolygonVertex(
    float4 cornersX,
    float4 cornersY,
    float4 bends,
    int index,
    bool isOrganic)
{
    return isOrganic
        ? TerrainOrganicGeometryPoint(cornersX, cornersY, bends, index)
        : TerrainGeometryCorner(
            cornersX, cornersY, index % KERN_TERRAIN_ORGANIC_EDGE_COUNT);
}

float4 TerrainOrganicGeometryBends(float packedEdges)
{
    int code = (int)round(packedEdges) - KERN_TERRAIN_ORGANIC_EDGE_CODE_OFFSET;
    float4 bends;
    bends.x = (code % KERN_TERRAIN_ORGANIC_EDGE_BASE) - KERN_TERRAIN_ORGANIC_EDGE_CENTER;
    code /= KERN_TERRAIN_ORGANIC_EDGE_BASE;
    bends.y = (code % KERN_TERRAIN_ORGANIC_EDGE_BASE) - KERN_TERRAIN_ORGANIC_EDGE_CENTER;
    code /= KERN_TERRAIN_ORGANIC_EDGE_BASE;
    bends.z = (code % KERN_TERRAIN_ORGANIC_EDGE_BASE) - KERN_TERRAIN_ORGANIC_EDGE_CENTER;
    code /= KERN_TERRAIN_ORGANIC_EDGE_BASE;
    bends.w = (code % KERN_TERRAIN_ORGANIC_EDGE_BASE) - KERN_TERRAIN_ORGANIC_EDGE_CENTER;
    return bends * (_OrganicBendStrength / KERN_TERRAIN_FACE_GRID_SIZE);
}

float TerrainOrganicInteriorMargin()
{
    return KERN_TERRAIN_ORGANIC_EDGE_CENTER * abs(_OrganicBendStrength) /
        KERN_TERRAIN_FACE_GRID_SIZE + KERN_TERRAIN_BEND_ROUNDING_ERROR_BOUND;
}

bool TerrainPolygonContains(
    float2 samplePosition,
    float4 cornersX,
    float4 cornersY,
    float packedEdges,
    bool isOrganic)
{
    float4 bends = isOrganic
        ? TerrainOrganicGeometryBends(packedEdges)
        : float4(0.0, 0.0, 0.0, 0.0);
    int vertexCount = isOrganic ? 2 * KERN_TERRAIN_ORGANIC_EDGE_COUNT :
        KERN_TERRAIN_ORGANIC_EDGE_COUNT;
    bool inside = false;

    for (int index = 0; index < vertexCount; index++)
    {
        float2 edgeStart = TerrainPolygonVertex(
            cornersX, cornersY, bends, index, isOrganic);
        float2 edgeEnd = TerrainPolygonVertex(
            cornersX, cornersY, bends, (index + 1) % vertexCount, isOrganic);
        float2 edge = edgeEnd - edgeStart;
        float edgeCross = TerrainGeometryEdgeCross(
            edgeStart, edgeEnd, samplePosition);
        bool onEdge = abs(edgeCross) <= KERN_TERRAIN_GEOMETRY_EPSILON &&
            samplePosition.x >= min(edgeStart.x, edgeEnd.x) - KERN_TERRAIN_GEOMETRY_EPSILON &&
            samplePosition.x <= max(edgeStart.x, edgeEnd.x) + KERN_TERRAIN_GEOMETRY_EPSILON &&
            samplePosition.y >= min(edgeStart.y, edgeEnd.y) - KERN_TERRAIN_GEOMETRY_EPSILON &&
            samplePosition.y <= max(edgeStart.y, edgeEnd.y) + KERN_TERRAIN_GEOMETRY_EPSILON;
        if (onEdge)
        {
            return true;
        }

        bool crossesScanline = (edgeStart.y > samplePosition.y) !=
            (edgeEnd.y > samplePosition.y);
        if (crossesScanline)
        {
            float xAtScanline = edgeStart.x +
                ((samplePosition.y - edgeStart.y) * edge.x / edge.y);
            if (samplePosition.x < xAtScanline)
            {
                inside = !inside;
            }
        }
    }

    return inside;
}

float TerrainGeometryCoverage(
    float2 samplePosition,
    float4 cornersX,
    float4 cornersY,
    float anchored)
{
    if (anchored < 0.5)
    {
        return 1.0;
    }

    // Classic distortion moves each corner by less than a quarter cell, so its
    // four-corner contour remains convex. The signed edge tests avoid the
    // divisions and scanline branches of the general polygon test.
    bool insidePositive = true;
    bool insideNegative = true;
    [unroll]
    for (int side = 0; side < KERN_TERRAIN_ORGANIC_EDGE_COUNT; side++)
    {
        float2 edgeStart = TerrainGeometryCorner(cornersX, cornersY, side);
        float2 edgeEnd = TerrainGeometryCorner(
            cornersX, cornersY, (side + 1) % KERN_TERRAIN_ORGANIC_EDGE_COUNT);
        float edgeCross = TerrainGeometryEdgeCross(edgeStart, edgeEnd, samplePosition);
        insidePositive = insidePositive && edgeCross >= -KERN_TERRAIN_GEOMETRY_EPSILON;
        insideNegative = insideNegative && edgeCross <= KERN_TERRAIN_GEOMETRY_EPSILON;
    }

    return insidePositive || insideNegative ? 1.0 : 0.0;
}

// Distortion changes each side's direction and distance from the carrier
// coordinate. Measure contact from the actual side path, including its organic
// bend point where present.
float TerrainGeometrySideDistance(
    float2 samplePosition,
    float4 cornersX,
    float4 cornersY,
    float4 organicBends,
    bool isOrganic,
    int side)
{
    if (isOrganic)
    {
        int firstPoint = side * 2;
        float2 edgeStart = TerrainOrganicGeometryPoint(
            cornersX, cornersY, organicBends, firstPoint);
        float2 bendPoint = TerrainOrganicGeometryPoint(
            cornersX, cornersY, organicBends, firstPoint + 1);
        float2 edgeEnd = TerrainOrganicGeometryPoint(
            cornersX, cornersY, organicBends, (firstPoint + 2) & 7);
        float firstDistance = TerrainGeometrySegmentDistanceSquared(
            samplePosition, edgeStart, bendPoint);
        float secondDistance = TerrainGeometrySegmentDistanceSquared(
            samplePosition, bendPoint, edgeEnd);
        return sqrt(min(firstDistance, secondDistance));
    }

    float2 edgeStart = TerrainGeometryCorner(cornersX, cornersY, side);
        float2 edgeEnd = TerrainGeometryCorner(
            cornersX, cornersY, (side + 1) % KERN_TERRAIN_ORGANIC_EDGE_COUNT);
    return sqrt(TerrainGeometrySegmentDistanceSquared(
        samplePosition, edgeStart, edgeEnd));
}

float TerrainOrganicGeometryCoverage(
    float2 samplePosition,
    float4 cornersX,
    float4 cornersY,
    float packedEdges)
{
    // Вершины восьмиугольника — углы и точки рёбер, сдвинутые изгибом вдоль
    // оси не дальше чем на размах кода, умноженный на силу изгиба. Вся его граница
    // лежит в этой полосе вокруг прямого четырёхугольника углов, и точка
    // глубже полосы от всех четырёх прямых рёбер внутри при любых изгибах.
    // Это большая часть клетки: цикл по восьми рёбрам нужен только у края.
    float interiorMargin = TerrainOrganicInteriorMargin();
    float interiorMarginSquared = interiorMargin * interiorMargin;
    bool insidePositive = true;
    bool insideNegative = true;
    for (int side = 0; side < KERN_TERRAIN_ORGANIC_EDGE_COUNT; side++)
    {
        float2 sideStart = TerrainGeometryCorner(cornersX, cornersY, side);
        float2 sideEnd = TerrainGeometryCorner(
            cornersX, cornersY, (side + 1) % KERN_TERRAIN_ORGANIC_EDGE_COUNT);
        float2 sideVector = sideEnd - sideStart;
        float edgeCross = TerrainGeometryEdgeCross(sideStart, sideEnd, samplePosition);
        bool outsideMargin = edgeCross * edgeCross >
            interiorMarginSquared * max(dot(sideVector, sideVector), KERN_TERRAIN_GEOMETRY_EPSILON);
        insidePositive = insidePositive && edgeCross > 0.0 && outsideMargin;
        insideNegative = insideNegative && edgeCross < 0.0 && outsideMargin;
    }

    // Обход углов в любую сторону: внутри — все расстояния одного знака.
    if (insidePositive || insideNegative)
    {
        return 1.0;
    }

    return TerrainPolygonContains(
        samplePosition,
        cornersX,
        cornersY,
        packedEdges,
        true) ? 1.0 : 0.0;
}

// AO needs the sign for pixels safely inside the silhouette, not their exact
// distance to the nearest edge. Return true only for a conservative interior
// core; boundary and exterior pixels continue through the exact distance path.
bool TerrainGeometryIsDeepInterior(
    float2 samplePosition,
    float4 cornersX,
    float4 cornersY,
    bool isOrganic)
{
    float margin = isOrganic
        ? TerrainOrganicInteriorMargin()
        : 0.0;
    float marginSquared = margin * margin;
    bool insidePositive = true;
    bool insideNegative = true;
    [unroll]
    for (int side = 0; side < KERN_TERRAIN_ORGANIC_EDGE_COUNT; side++)
    {
        float2 edgeStart = TerrainGeometryCorner(cornersX, cornersY, side);
        float2 edgeEnd = TerrainGeometryCorner(
            cornersX, cornersY, (side + 1) % KERN_TERRAIN_ORGANIC_EDGE_COUNT);
        float2 edge = edgeEnd - edgeStart;
        float edgeCross = TerrainGeometryEdgeCross(edgeStart, edgeEnd, samplePosition);
        bool beyondBendReach = edgeCross * edgeCross >
            marginSquared * max(dot(edge, edge), KERN_TERRAIN_GEOMETRY_EPSILON);
        insidePositive = insidePositive && edgeCross > 0.0 && beyondBendReach;
        insideNegative = insideNegative && edgeCross < 0.0 && beyondBendReach;
    }

    return insidePositive || insideNegative;
}

float TerrainGeometrySignedDistance(
    float2 samplePosition,
    float4 cornersX,
    float4 cornersY,
    float packedEdges,
    bool isOrganic,
    out float2 nearestPosition)
{
    float4 bends = isOrganic
        ? TerrainOrganicGeometryBends(packedEdges)
        : float4(0.0, 0.0, 0.0, 0.0);
    float nearestDistanceSquared = 1.0e20;
    nearestPosition = samplePosition;
    int edgeCount = isOrganic ? 2 * KERN_TERRAIN_ORGANIC_EDGE_COUNT :
        KERN_TERRAIN_ORGANIC_EDGE_COUNT;
    bool inside = false;
    bool onBoundary = false;

    for (int index = 0; index < edgeCount; index++)
    {
        float2 edgeStart = TerrainPolygonVertex(
            cornersX, cornersY, bends, index, isOrganic);
        float2 edgeEnd = TerrainPolygonVertex(
            cornersX,
            cornersY,
            bends,
            (index + 1) % edgeCount,
            isOrganic);
        float2 edge = edgeEnd - edgeStart;
        float edgeCross = TerrainGeometryEdgeCross(
            edgeStart, edgeEnd, samplePosition);
        onBoundary = onBoundary ||
            (abs(edgeCross) <= KERN_TERRAIN_GEOMETRY_EPSILON &&
             samplePosition.x >= min(edgeStart.x, edgeEnd.x) - KERN_TERRAIN_GEOMETRY_EPSILON &&
             samplePosition.x <= max(edgeStart.x, edgeEnd.x) + KERN_TERRAIN_GEOMETRY_EPSILON &&
             samplePosition.y >= min(edgeStart.y, edgeEnd.y) - KERN_TERRAIN_GEOMETRY_EPSILON &&
             samplePosition.y <= max(edgeStart.y, edgeEnd.y) + KERN_TERRAIN_GEOMETRY_EPSILON);
        if ((edgeStart.y > samplePosition.y) != (edgeEnd.y > samplePosition.y))
        {
            float xAtScanline = edgeStart.x +
                ((samplePosition.y - edgeStart.y) * edge.x / edge.y);
            if (samplePosition.x < xAtScanline)
            {
                inside = !inside;
            }
        }
        float2 closestPosition = TerrainGeometrySegmentClosestPoint(
            samplePosition, edgeStart, edgeEnd);
        float2 delta = samplePosition - closestPosition;
        float distanceSquared = dot(delta, delta);
        if (distanceSquared < nearestDistanceSquared)
        {
            nearestDistanceSquared = distanceSquared;
            nearestPosition = closestPosition;
        }
    }

    float distanceToEdge = sqrt(nearestDistanceSquared);
    return inside || onBoundary ? distanceToEdge : -distanceToEdge;
}

float TerrainGeometrySignedDistance(
    float2 samplePosition,
    float4 cornersX,
    float4 cornersY,
    float packedEdges,
    bool isOrganic)
{
    float2 nearestPosition;
    return TerrainGeometrySignedDistance(
        samplePosition,
        cornersX,
        cornersY,
        packedEdges,
        isOrganic,
        nearestPosition);
}

float TerrainSignedDistanceToBox(float2 samplePosition, float2 boxCenter, float2 halfSize)
{
    float2 delta = abs(samplePosition - boxCenter) - halfSize;
    return length(max(delta, float2(0.0, 0.0))) + min(max(delta.x, delta.y), 0.0);
}


#endif

#ifndef KERN_TERRAIN_CONTOUR_INCLUDED
#define KERN_TERRAIN_CONTOUR_INCLUDED

#include "TerrainLightingData.hlsl"

// Форма органического искажения приходит свойствами материала, а вершинный
// проход Universal2D cbuffer сам не подключает — иначе _OrganicBendStrength и
// _OrganicBendPivot оказываются необъявленными. Объявлять их вне
// UnityPerMaterial нельзя, поэтому подключаем блок целиком.
//
// Порядок обязателен: cbuffer-файл содержит ещё и TerrainMaterialAtlasTexelSize,
// который зовёт TerrainAtlasTexelSize, — сначала идёт выборка атласа. Guard'ы
// внутри обоих файлов делают повторное включение безопасным.
#include "TerrainAtlasSampling.hlsl"
#include "TerrainMaterialCBuffer.hlsl"

static const float KERN_TERRAIN_FACE_GRID_SIZE = 32.0;
static const float KERN_TERRAIN_GEOMETRY_EPSILON = 0.0001;
// Maximum coordinate error introduced when a derived bend vertex is rounded
// to the geometry grid. Used only to keep the interior fast path conservative;
// it never expands the polygon or its rasterized/AA coverage.
static const float KERN_TERRAIN_BEND_ROUNDING_ERROR_BOUND =
    0.5 / KERN_TERRAIN_FACE_GRID_SIZE;

// Размах кода изгиба: код вершины лежит в (code % 5) - 2, то есть не дальше
// двух шагов. Это часть упаковки вершинного формата, а не настройка, — менять
// вместе с распаковкой ниже. Силу искажения задаёт _OrganicBendStrength.
static const float KERN_TERRAIN_BEND_CODE_RANGE = 2.0;

float TerrainGeometryEdgeCross(
    float2 edgeStart,
    float2 edgeEnd,
    float2 samplePosition)
{
    float2 edge = edgeEnd - edgeStart;
    float2 toSample = samplePosition - edgeStart;
    return (edge.x * toSample.y) - (edge.y * toSample.x);
}

float2 TerrainGeometryCorner(float4 cornersX, float4 cornersY, int index)
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
    float2 start = TerrainGeometryCorner(cornersX, cornersY, side);
    float2 end = TerrainGeometryCorner(cornersX, cornersY, (side + 1) & 3);
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
    return round(bendPosition * KERN_TERRAIN_FACE_GRID_SIZE) /
        KERN_TERRAIN_FACE_GRID_SIZE;
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
        : TerrainGeometryCorner(cornersX, cornersY, index & 3);
}

float4 TerrainOrganicGeometryBends(float packedEdges)
{
    int code = (int)round(packedEdges) - 1;
    float4 bends;
    bends.x = (code % 5) - 2;
    code /= 5;
    bends.y = (code % 5) - 2;
    code /= 5;
    bends.z = (code % 5) - 2;
    code /= 5;
    bends.w = (code % 5) - 2;
    return bends * (_OrganicBendStrength / KERN_TERRAIN_FACE_GRID_SIZE);
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
    int vertexCount = isOrganic ? 8 : 4;
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

    // Geometry vertices are quantized once before rasterization. Fragment
    // positions stay continuous and are tested against that final polygon.
    return TerrainPolygonContains(
        samplePosition,
        cornersX,
        cornersY,
        0.0,
        false) ? 1.0 : 0.0;
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
    float2 edgeEnd = TerrainGeometryCorner(cornersX, cornersY, (side + 1) & 3);
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
    const float interiorMargin =
        KERN_TERRAIN_BEND_CODE_RANGE * _OrganicBendStrength /
        KERN_TERRAIN_FACE_GRID_SIZE + KERN_TERRAIN_BEND_ROUNDING_ERROR_BOUND;
    const float interiorMarginSquared = interiorMargin * interiorMargin;
    bool insidePositive = true;
    bool insideNegative = true;
    for (int side = 0; side < 4; side++)
    {
        float2 sideStart = TerrainGeometryCorner(cornersX, cornersY, side);
        float2 sideEnd = TerrainGeometryCorner(cornersX, cornersY, (side + 1) & 3);
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
    int edgeCount = isOrganic ? 8 : 4;
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

// The roundable contour is a disk with the quadrants connected to like
// neighbors filled back to the cell boundary. Keep AO's distance field tied
// to that shape instead of casting a square shadow from a rounded tile.
float TerrainRoundableSignedDistance(float2 samplePosition, float packedLightingFlags)
{
    float2 cellPosition = samplePosition - 0.5;
    float distance = length(cellPosition) - _RoundableCornerRadius;
    int sameMask = KernTerrainSolidBoundary(
        KernTerrainLightingFlags(packedLightingFlags));
    if ((sameMask & 1) != 0 || (sameMask & 2) != 0)
    {
        distance = min(distance, TerrainSignedDistanceToBox(
            cellPosition, float2(-0.25, 0.25), float2(0.25, 0.25)));
    }
    if ((sameMask & 1) != 0 || (sameMask & 8) != 0)
    {
        distance = min(distance, TerrainSignedDistanceToBox(
            cellPosition, float2(0.25, 0.25), float2(0.25, 0.25)));
    }
    if ((sameMask & 4) != 0 || (sameMask & 2) != 0)
    {
        distance = min(distance, TerrainSignedDistanceToBox(
            cellPosition, float2(-0.25, -0.25), float2(0.25, 0.25)));
    }
    if ((sameMask & 4) != 0 || (sameMask & 8) != 0)
    {
        distance = min(distance, TerrainSignedDistanceToBox(
            cellPosition, float2(0.25, -0.25), float2(0.25, 0.25)));
    }
    return -distance;
}

float TerrainGeometryCoverageForField(
    float2 samplePosition,
    float4 cornersX,
    float4 cornersY,
    float packedEdges)
{
    bool isOrganic = packedEdges > 0.5;
    float signedDistance = TerrainGeometrySignedDistance(
        samplePosition, cornersX, cornersY, packedEdges, isOrganic);
    float halfFootprint = max(
        fwidth(signedDistance) * 0.5,
        KERN_TERRAIN_GEOMETRY_EPSILON);
    return smoothstep(-halfFootprint, halfFootprint, signedDistance);
}

float2 QuantizeTerrainFaceUV(float2 uv)
{
    // The input can be the displaced corner coordinate. Keep it outside the
    // canonical range: clamping it would collapse a moved corner onto the
    // edge and turn a one-pixel displacement into a large flat step.
    float2 pixel = floor(uv * KERN_TERRAIN_FACE_GRID_SIZE);
    return (pixel + 0.5) / KERN_TERRAIN_FACE_GRID_SIZE;
}

float EvaluateRoundableBlockAlpha(
    float2 uv,
    float packedContour,
    float packedLightingFlags,
    float antialiasScale)
{
    if (!KernTerrainIsRoundable(packedContour))
    {
        return 1.0;
    }

    uint lightingFlags = KernTerrainLightingFlags(packedLightingFlags);
    int sameMask = KernTerrainSolidBoundary(lightingFlags);
    float4 bits = frac(sameMask * float4(0.5, 0.25, 0.125, 0.0625));
    bool4 hasSame = bits >= 0.5;
    float2 p = QuantizeTerrainFaceUV(uv) - 0.5;
    float rTL = (hasSame.x || hasSame.y) ? 0.0 : 0.5;
    float rTR = (hasSame.x || hasSame.w) ? 0.0 : 0.5;
    float rBL = (hasSame.z || hasSame.y) ? 0.0 : 0.5;
    float rBR = (hasSame.z || hasSame.w) ? 0.0 : 0.5;
    float dist = length(p);
    float alpha = step(dist, _RoundableCornerRadius);
    if (rTL < 0.25)
    {
        float fill = step(p.x, 0.0) * step(0.0, p.y);
        alpha = max(alpha, fill);
    }
    if (rTR < 0.25)
    {
        float fill = step(0.0, p.x) * step(0.0, p.y);
        alpha = max(alpha, fill);
    }
    if (rBL < 0.25)
    {
        float fill = step(p.x, 0.0) * step(p.y, 0.0);
        alpha = max(alpha, fill);
    }
    if (rBR < 0.25)
    {
        float fill = step(0.0, p.x) * step(p.y, 0.0);
        alpha = max(alpha, fill);
    }
    float cornerDist = abs(abs(p.x) - abs(p.y));
    float cornerExclude = step(0.4, cornerDist);
    return lerp(alpha, 1.0, cornerExclude);
}

// Выключатель каймы. Настройка игрока, публикуется TerrainRenderer.
float _TerrainReliefRimEnabled;

// Затухание от одной чужой грани. d — расстояние до неё в долях клетки:
// 0 на самой грани, 0.5 в середине клетки.
//
// Огибающая та же, что в оригинале: на грани множитель 0.125, к середине
// выходит в единицу, куб прижимает затемнение к краю. Разница в том, что
// считается расстояние до грани, а не сектор клетки. Масштаб дистанции и
// коэффициент спада — свойства _ReliefRimDistanceScale и _ReliefRimFalloff.
float TerrainReliefRimSide(float distanceToEdge)
{
    float s = saturate(1.0 - (distanceToEdge * _ReliefRimDistanceScale));
    float fall = 1.0 - (_ReliefRimFalloff * s * s);
    return fall * fall * fall;
}

// Кайма рельефа: затемнение к тем сторонам клетки, за которыми лежит чужая
// рельефная семья. Ради неё маска и считается — без каймы кристалл и скала
// смыкаются тайлами вплотную и читаются одним пятном.
//
// Грани, а не секторы. В оригинале клетка делится диагоналями на секторы, и
// каждый сектор гасится целиком. Тогда полоса вдоль длинной границы массива
// обрывается на каждом стыке клеток: у соседней граничной клетки её сектор
// срезан той же диагональю, и в месте стыка обе полосы сходят на нет —
// кайма не тайлится. Здесь каждая чужая грань даёт своё затухание по
// расстоянию до неё, а затухания перемножаются. Вдоль грани значение
// постоянно, поэтому полоса переходит в соседнюю клетку без разрыва, а на
// углу двух чужих граней множители складываются в более тёмный угол.
//
// Перемножение — тоже из оригинала: там ветки секторов домножают уже
// затемнённый цвет, а не выбирают один из.
// Raw form consumes the common cell-data decode and measures each rim against
// the actual displaced polygon sides.
float TerrainReliefRimRaw(
    float4 packedData,
    float4 cornersX,
    float4 cornersY,
    float packedContour)
{
    if (_TerrainReliefRimEnabled < 0.5)
    {
        return 1.0;
    }

    int reliefCode = KernTerrainReliefCode(packedContour);
    if (reliefCode == 0)
    {
        return 1.0;
    }

    // Код хранит маску своих соседей со сдвигом на единицу; кайме нужны
    // чужие, то есть дополнение до четырёх сторон.
    int foreignSides = (~(reliefCode - 1)) & 0x0F;
    if (foreignSides == 0)
    {
        return 1.0;
    }

    // Stored polygon vertices are already snapped to the geometry grid.
    // Quantizing the fragment again shifts the rim relative to the visible
    // edge and turns a smooth side distance into 1/32-cell steps.
    float2 samplePosition = packedData.yz;
    bool isOrganic = packedData.w > 0.5;
    float4 organicBends = isOrganic
        ? TerrainOrganicGeometryBends(packedData.w)
        : float4(0.0, 0.0, 0.0, 0.0);

    float rim = 1.0;
    if ((foreignSides & 1) != 0)
    {
        rim *= TerrainReliefRimSide(TerrainGeometrySideDistance(
            samplePosition, cornersX, cornersY, organicBends, isOrganic, 2));
    }

    if ((foreignSides & 2) != 0)
    {
        rim *= TerrainReliefRimSide(TerrainGeometrySideDistance(
            samplePosition, cornersX, cornersY, organicBends, isOrganic, 3));
    }

    if ((foreignSides & 4) != 0)
    {
        rim *= TerrainReliefRimSide(TerrainGeometrySideDistance(
            samplePosition, cornersX, cornersY, organicBends, isOrganic, 0));
    }

    if ((foreignSides & 8) != 0)
    {
        rim *= TerrainReliefRimSide(TerrainGeometrySideDistance(
            samplePosition, cornersX, cornersY, organicBends, isOrganic, 1));
    }

    return rim;
}

// Разбор вершины террейна: всё, что оба прохода и отладочный вид раньше
// выводили каждый у себя.
//
// ЗАЧЕМ. Проходов два (экранный Universal2D и поле материалов), путей вершин
// тоже два (CPU-квады и GPU-клетки), и решение «что такое координата клетки»
// принималось в каждом сочетании заново. Каждый дефект каймы и силуэта в этом
// шве и сидел: ветки расходились между собой и с отладочным видом, который
// показывал четвёртое мнение. Здесь разбор один, дальше по коду ходит
// структура, и подать в кайму «не ту» координату больше нечем.
struct TerrainSurfaceInputs
{
    // Клеточная координата угла: 0..1 по клетке, у смещённой — координата
    // несущего прямоугольника. Мировая ориентация, вариантом тайла не тронута.
    float2 cellSample;

    // Координата для контура. У смещённой клетки это та же клеточная, у
    // ровной — UV тайла: скругление блока живёт в тайле и обязано ехать
    // вместе с его отражениями.
    float2 contourUV;

    float4 cornersX;
    float4 cornersY;
    float anchored;
    float packedOrganicEdges;
    float packedContour;
    float packedLightingFlags;
    int animationProfile;
};

TerrainSurfaceInputs BuildTerrainSurfaceInputs(
    float4 packedData,
    float2 tileUV,
    float4 cornersX,
    float4 cornersY,
    float4 glowData,
    float packedAnimationProfile)
{
    TerrainSurfaceInputs surface;
    surface.cellSample = packedData.yz;
    surface.contourUV = packedData.x > 0.5 ? packedData.yz : tileUV;
    surface.cornersX = cornersX;
    surface.cornersY = cornersY;
    surface.anchored = packedData.x;
    surface.packedOrganicEdges = packedData.w;
    surface.packedContour = glowData.z;
    surface.packedLightingFlags = glowData.y;
    surface.animationProfile = (int)(packedAnimationProfile + 0.5);
    return surface;
}

float TerrainReliefRim(TerrainSurfaceInputs surface)
{
    return TerrainReliefRimRaw(
        float4(
            surface.anchored,
            surface.cellSample,
            surface.packedOrganicEdges),
        surface.cornersX,
        surface.cornersY,
        surface.packedContour);
}

// The visible terrain and the material field must use the same cell shape.
// Geometry is evaluated only for the GPU cell path; CPU overlays already carry
// the displaced polygon in POSITION and therefore do not need a second mask.
float EvaluateTerrainCellContourCoverage(
    TerrainSurfaceInputs surface,
    float antialiasScale)
{
    return KernTerrainIsRoundable(surface.packedContour)
        ? EvaluateRoundableBlockAlpha(
            surface.contourUV,
            surface.packedContour,
            surface.packedLightingFlags,
            antialiasScale)
        : 1.0;
}

float EvaluateTerrainCellCoverage(
    TerrainSurfaceInputs surface,
    float antialiasScale,
    float applyGeometry)
{
    float geometryCoverage = 1.0;
    if (applyGeometry > 0.5)
    {
        geometryCoverage = surface.packedOrganicEdges > 0.5
            ? TerrainOrganicGeometryCoverage(
                surface.cellSample,
                surface.cornersX,
                surface.cornersY,
                surface.packedOrganicEdges)
            : TerrainGeometryCoverage(
                surface.cellSample,
                surface.cornersX,
                surface.cornersY,
                surface.anchored);
    }
    float contourCoverage = EvaluateTerrainCellContourCoverage(surface, antialiasScale);
    return geometryCoverage * contourCoverage;
}

float TerrainCellOccupancy(float coverage)
{
    return step(0.5, coverage);
}

#endif

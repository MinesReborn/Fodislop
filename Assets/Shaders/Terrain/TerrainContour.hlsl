#ifndef KERN_TERRAIN_CONTOUR_INCLUDED
#define KERN_TERRAIN_CONTOUR_INCLUDED

#include "TerrainLightingData.hlsl"
#include "TerrainGeometryContract.hlsl"

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
#include "TerrainGeometry.hlsl"

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

float2 QuantizeTerrainFaceUV(float2 uv)
{
    // The input can be the displaced corner coordinate. Keep it outside the
    // canonical range: clamping it would collapse a moved corner onto the
    // edge and turn a one-pixel displacement into a large flat step.
    return QuantizeTerrainPixelCenter(uv);
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

float TerrainQuantizeReliefBevel(float bevel)
{
    if (_ReliefRimQuantizationEnabled > 0.5)
    {
        return round(bevel * KERN_TERRAIN_FACE_GRID_SIZE) /
            KERN_TERRAIN_FACE_GRID_SIZE;
    }

    return bevel;
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

// Shade each exposed side independently. Distances use the rendered polygon,
// including the quantized organic bend points, so the bevel follows distortion.
float TerrainReliefBevel(TerrainSurfaceInputs surface)
{
    if (_TerrainReliefRimEnabled < 0.5)
    {
        return 1.0;
    }

    int reliefCode = KernTerrainReliefCode(surface.packedContour);
    if (reliefCode == 0)
    {
        return 1.0;
    }

    // Code stores the foreign-side mask inverted, plus one; zero means no bevel.
    int foreignSides = (~(reliefCode - 1)) & 0x0F;
    int concaveCorners = KernTerrainReliefCornerMask(surface.packedContour);
    if (foreignSides == 0 && concaveCorners == 0)
    {
        return 1.0;
    }

    bool isOrganic = surface.packedOrganicEdges > 0.5;
    float4 organicBends = isOrganic
        ? TerrainOrganicGeometryBends(surface.packedOrganicEdges)
        : float4(0.0, 0.0, 0.0, 0.0);
    float bevel = 1.0;
    [unroll]
    for (int side = 0; side < KERN_TERRAIN_ORGANIC_EDGE_COUNT; side++)
    {
        int sideBit = side == 0 ? 4 : side == 1 ? 8 : side == 2 ? 1 : 2;
        if ((foreignSides & sideBit) == 0)
        {
            continue;
        }

        float distanceToSide = TerrainGeometrySideDistance(
            surface.cellSample,
            surface.cornersX,
            surface.cornersY,
            organicBends,
            isOrganic,
            side);
        float influence = saturate(1.0 - distanceToSide * _ReliefRimDistanceScale);
        float sideShade = 1.0 - _ReliefRimFalloff * influence * influence;
        bevel *= sideShade * sideShade * sideShade;
    }

    [branch]
    if (concaveCorners != 0)
    {
        [unroll]
        for (int corner = 0; corner < KERN_TERRAIN_ORGANIC_EDGE_COUNT; corner++)
        {
            if ((concaveCorners & (1 << corner)) == 0)
            {
                continue;
            }

            float distanceToCorner = distance(
                surface.cellSample,
                TerrainGeometryCorner(surface.cornersX, surface.cornersY, corner));
            float influence = saturate(1.0 - distanceToCorner * _ReliefRimDistanceScale);
            float cornerShade = 1.0 - _ReliefRimFalloff * influence * influence;
            bevel *= cornerShade * cornerShade * cornerShade;
        }
    }

    return TerrainQuantizeReliefBevel(bevel);
}


float TerrainReliefRim(TerrainSurfaceInputs surface)
{
    return TerrainReliefBevel(surface);
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
    // Corners and bends are quantized once in the shared polygon contract.
    // Classify the real raster sample: snapping each cell's fragment position
    // independently moves shared edges apart and opens subpixel gaps.
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

#ifndef KERN_TERRAIN_CONTOUR_INCLUDED
#define KERN_TERRAIN_CONTOUR_INCLUDED

#include "Assets/Shaders/Terrain/Lighting/TerrainLightingData.hlsl"
#include "Assets/Shaders/Terrain/Geometry/TerrainGeometryContract.hlsl"

// Порядок обязателен: cbuffer-файл содержит ещё и TerrainMaterialAtlasTexelSize,
// который зовёт TerrainAtlasTexelSize, — сначала идёт выборка атласа. Guard'ы
// внутри обоих файлов делают повторное включение безопасным.
#include "Assets/Shaders/Terrain/Textures/TerrainAtlasSampling.hlsl"
#include "Assets/Shaders/Terrain/Materials/TerrainMaterialCBuffer.hlsl"
#include "Assets/Shaders/Terrain/Geometry/TerrainGeometry.hlsl"

// The roundable contour is a disk with the quadrants connected to like
// neighbors filled back to the cell boundary. Keep AO's distance field tied
// to that shape instead of casting a square shadow from a rounded tile.
float TerrainRoundableSignedDistance(float2 samplePosition, float packedLightingFlags)
{
    float2 cellPosition = samplePosition - 0.5;
    float distance = length(cellPosition) - _RoundableCornerRadius;
    int sameMask = KernTerrainForegroundSides(
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
    int sameMask = KernTerrainForegroundSides(lightingFlags);
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

// The rim switch is bound by the terrain material in UnityPerMaterial.

float TerrainQuantizeRimBevel(float bevel)
{
    if (_RimQuantizationEnabled > 0.5)
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

    // The contour and neighbor masks share world-oriented cell coordinates.
    // Texture variants may rotate or reflect independently of the geometry.
    float2 contourUV;

    float4 cornersX;
    float4 cornersY;
    float anchored;
    float packedContour;
    float packedLightingFlags;
};

TerrainSurfaceInputs BuildTerrainSurfaceInputs(
    float4 packedData,
    float2 tileUV,
    float4 cornersX,
    float4 cornersY,
    float4 lightContourDecal)
{
    TerrainSurfaceInputs surface;
    surface.cellSample = packedData.yz;
    surface.contourUV = packedData.yz;
    surface.cornersX = cornersX;
    surface.cornersY = cornersY;
    surface.anchored = packedData.x;
    surface.packedContour = lightContourDecal.z;
    surface.packedLightingFlags = lightContourDecal.y;
    return surface;
}

// Shade each exposed side independently. Distances use the rendered polygon,
// so the bevel follows the displaced corners.
float TerrainRimSideBevel(TerrainSurfaceInputs surface, int foreignSides)
{
    float bevel = 1.0;
    [unroll]
    for (int side = 0; side < KERN_TERRAIN_SIDE_COUNT; side++)
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
            side);
        float influence = saturate(1.0 - distanceToSide * _RimDistanceScale);
        float sideShade = 1.0 - _RimFalloff * influence;
        bevel = min(bevel, sideShade);
    }

    return bevel;
}

// Кайма: стороны с чужой поверхностью и вогнутые углы.
float TerrainRimBevel(TerrainSurfaceInputs surface)
{
    if (_TerrainRimEnabled < 0.5)
    {
        return 1.0;
    }

    int rimCode = KernTerrainRimCode(surface.packedContour);
    if (rimCode == 0)
    {
        return 1.0;
    }

    // Code stores the foreign-side mask inverted, plus one; zero means no bevel.
    int foreignSides = (~(rimCode - 1)) & (int)KERN_TERRAIN_RIM_SIDES_MASK;
    int concaveCorners = KernTerrainRimCornerMask(surface.packedContour);
    if (foreignSides == 0 && concaveCorners == 0)
    {
        return 1.0;
    }

    float bevel = TerrainRimSideBevel(surface, foreignSides);
    if (KernTerrainIsRoundable(surface.packedContour))
    {
        // Filled quadrants continue into matching neighbors. They have no
        // circular boundary. A min-union of continuation boxes is not the
        // distance to the union boundary: shared box corners still yield
        // zero and create dark spots inside the connected surface.
        float2 uv = QuantizeTerrainFaceUV(surface.contourUV);
        float2 p = uv - 0.5;
        int verticalSide = p.y >= 0.0 ? 1 : 4;
        int horizontalSide = p.x >= 0.0 ? 8 : 2;
        // Contour continuation uses physical foreground neighbors, whereas
        // foreignSides distinguishes rim materials. A different material can
        // fill this quadrant too: it must not create a circular rim inside
        // that filled area, especially outside the disk where arc distance is 0.
        int contourSides = KernTerrainForegroundSides(
            KernTerrainLightingFlags(surface.packedLightingFlags));
        bool hasCircularBoundary = (contourSides & (verticalSide | horizontalSide)) == 0;
        bool exposedArc = hasCircularBoundary && (foreignSides & verticalSide) != 0 &&
            (foreignSides & horizontalSide) != 0;
        if (exposedArc)
        {
            float distanceToArc = max(_RoundableCornerRadius - length(p), 0.0);
            float influence = saturate(1.0 - distanceToArc * _RimDistanceScale);
            bevel = min(bevel, 1.0 - _RimFalloff * influence);
        }
    }

    [branch]
    if (concaveCorners != 0)
    {
        [unroll]
        for (int corner = 0; corner < KERN_TERRAIN_SIDE_COUNT; corner++)
        {
            if ((concaveCorners & (1 << corner)) == 0)
            {
                continue;
            }

            float distanceToCorner = distance(
                surface.cellSample,
                TerrainGeometryCorner(surface.cornersX, surface.cornersY, corner));
            float influence = saturate(1.0 - distanceToCorner * _RimDistanceScale);
            float cornerShade = 1.0 - _RimFalloff * influence;
            bevel = min(bevel, cornerShade);
        }
    }

    return TerrainQuantizeRimBevel(bevel);
}


float TerrainRim(TerrainSurfaceInputs surface)
{
    return TerrainRimBevel(surface);
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
    // Corners are quantized once in the shared polygon contract.
    // Classify the real raster sample: snapping each cell's fragment position
    // independently moves shared edges apart and opens subpixel gaps.
    float geometryCoverage = 1.0;
    if (applyGeometry > 0.5)
    {
        geometryCoverage = TerrainGeometryCoverage(
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

#ifndef KERN_TERRAIN_DEBUG_VIEW_INCLUDED
#define KERN_TERRAIN_DEBUG_VIEW_INCLUDED

#include "TerrainLightingData.hlsl"
#include "TerrainContour.hlsl"
#include "TerrainAnimationProfile.hlsl"

// Здесь разбираются геометрия клетки и поверхностные эффекты. Диагностика
// мирового света остаётся в световом отладочном виде.
int _TerrainDebugView;

static const int KERN_TERRAIN_DEBUG_OFF = 0;
static const int KERN_TERRAIN_DEBUG_RELIEF_RIM = 1;
static const int KERN_TERRAIN_DEBUG_FOREIGN_SIDES = 2;
static const int KERN_TERRAIN_DEBUG_COVERAGE = 3;
static const int KERN_TERRAIN_DEBUG_LAYER = 4;
static const int KERN_TERRAIN_DEBUG_ANCHORED = 5;
static const int KERN_TERRAIN_DEBUG_CELL_LOCAL = 6;
static const int KERN_TERRAIN_DEBUG_RELIEF_GROUP = 7;
static const int KERN_TERRAIN_DEBUG_CONTINUOUS_SHEET = 8;
static const int KERN_TERRAIN_DEBUG_AMBIENT_OCCLUSION = 9;
static const int KERN_TERRAIN_DEBUG_BACKGROUND_TILE_IDENTITY = 10;
static const int KERN_TERRAIN_DEBUG_FACETED_GLINT = 11;
static const int KERN_TERRAIN_DEBUG_SOURCE_ALBEDO = 12;
static const int KERN_TERRAIN_DEBUG_ANIMATED_COLOR = 13;
static const int KERN_TERRAIN_DEBUG_PRISMATIC_TINT = 14;
static const int KERN_TERRAIN_DEBUG_DECAL_CONTRIBUTION = 15;
static const int KERN_TERRAIN_DEBUG_SHIMMER_FLOW = 16;
static const int KERN_TERRAIN_DEBUG_PRISMATIC_FLOW = 17;
static const int KERN_TERRAIN_DEBUG_FOREGROUND_TILE_IDENTITY = 18;

// Диапазон, а не «не ноль». Глобаль живёт в нативной части и переживает
// доменную перезагрузку: номер удалённого вида остаётся в ней и после того,
// как C# сбросился в Off. Пока проверка была «не ноль», такой осиротевший
// номер проваливался в последнюю ветку и заливал мир её цветом.
bool KernTerrainDebugActive()
{
    return _TerrainDebugView > KERN_TERRAIN_DEBUG_OFF &&
        _TerrainDebugView <= KERN_TERRAIN_DEBUG_FOREGROUND_TILE_IDENTITY;
}

bool KernTerrainDebugNeedsSurfaceSample()
{
    return _TerrainDebugView >= KERN_TERRAIN_DEBUG_FACETED_GLINT &&
        _TerrainDebugView <= KERN_TERRAIN_DEBUG_PRISMATIC_FLOW;
}

float3 KernTerrainDebugEncodeSignedDelta(float3 delta, float contrast)
{
    // Diagnostic contrast only: amplify subtle per-stage changes while keeping
    // the signed mapping monotonic and asymptotically inside display range.
    float3 contrastedDelta = delta * max(contrast, 0.0);
    return float3(0.5, 0.5, 0.5) +
        (0.5 * contrastedDelta) / (float3(1.0, 1.0, 1.0) + abs(contrastedDelta));
}

float3 KernTerrainDebugSurfaceColor(
    float3 sourceAlbedo,
    float3 flowSample,
    float3 animatedColor,
    float3 decalColor,
    float facetedGlintSignal,
    int animationProfile,
    int animationType,
    float deltaContrast)
{
    if (_TerrainDebugView == KERN_TERRAIN_DEBUG_SOURCE_ALBEDO)
    {
        return sourceAlbedo;
    }

    if (_TerrainDebugView == KERN_TERRAIN_DEBUG_SHIMMER_FLOW)
    {
        bool usesShimmerFlow = animationType == 2 &&
            animationProfile != KERN_TERRAIN_ANIMATION_PROFILE_PRISMATIC_CRYSTAL &&
            animationProfile != KERN_TERRAIN_ANIMATION_PROFILE_FACETED_CRYSTAL;
        return usesShimmerFlow ? flowSample : float3(0.0, 0.0, 0.0);
    }

    if (_TerrainDebugView == KERN_TERRAIN_DEBUG_PRISMATIC_FLOW)
    {
        return animationProfile == KERN_TERRAIN_ANIMATION_PROFILE_PRISMATIC_CRYSTAL
            ? flowSample
            : float3(0.0, 0.0, 0.0);
    }

    if (_TerrainDebugView == KERN_TERRAIN_DEBUG_ANIMATED_COLOR)
    {
        return animatedColor;
    }

    if (_TerrainDebugView == KERN_TERRAIN_DEBUG_PRISMATIC_TINT)
    {
        float3 tintDelta = animationProfile == KERN_TERRAIN_ANIMATION_PROFILE_PRISMATIC_CRYSTAL
            ? animatedColor - sourceAlbedo
            : float3(0.0, 0.0, 0.0);
        return KernTerrainDebugEncodeSignedDelta(tintDelta, deltaContrast);
    }

    if (_TerrainDebugView == KERN_TERRAIN_DEBUG_FACETED_GLINT)
    {
        return lerp(
            float3(0.015, 0.01, 0.025),
            float3(1.0, 0.42, 0.05),
            saturate(facetedGlintSignal));
    }

    if (_TerrainDebugView == KERN_TERRAIN_DEBUG_DECAL_CONTRIBUTION)
    {
        return KernTerrainDebugEncodeSignedDelta(decalColor - animatedColor, deltaContrast);
    }

    return float3(1.0, 0.0, 0.8);
}

float3 KernTerrainUniqueTileColor(
    float atlasIndex,
    float4 subAtlasRect,
    float2 tileOffsetUV,
    float2 tileSizeUV,
    float4 atlasTexelSize,
    bool isValid)
{
    if (!isValid || subAtlasRect.z <= 0.0 || subAtlasRect.w <= 0.0)
    {
        return float3(1.0, 0.0, 0.8);
    }

    // Atlas regions contain 32-pixel terrain tiles. Their packed rectangles do
    // not overlap, so the atlas tile-grid coordinate is an injective address.
    // Seven bits per axis cover the configured 4096-pixel atlas limit; three
    // more bits select one of TerrainAtlasSampling's eight bound textures.
    float2 atlasDimensions = max(atlasTexelSize.zw, float2(1.0, 1.0));
    float2 tileSizePixels = round(tileSizeUV * atlasDimensions);
    float2 tileOriginPixels = round((subAtlasRect.xy + tileOffsetUV) * atlasDimensions);
    uint2 tileCoordinate = (uint2)round(tileOriginPixels / 32.0);
    uint atlasSlot = (uint)round(atlasIndex);
    if (atlasSlot > 7u || any(atlasDimensions > float2(4096.0, 4096.0)) ||
        any(tileSizePixels != float2(32.0, 32.0)) ||
        any(tileCoordinate > uint2(127u, 127u)))
    {
        return float3(1.0, 0.0, 0.8);
    }

    uint identity = (atlasSlot << 14u) |
        (tileCoordinate.y << 7u) |
        tileCoordinate.x;

    // Offset into an 18-bit 6/6/6 RGB code so valid address zero is visible
    // instead of sharing the clear/background black. The extra bit keeps all
    // 17-bit atlas addresses injective after the offset.
    uint visibleIdentity = identity + 1u;
    float3 key = float3(
        (float)(visibleIdentity & 63u),
        (float)((visibleIdentity >> 6u) & 63u),
        (float)((visibleIdentity >> 12u) & 63u)) / 63.0;
    return key;
}

// Стороны кодируются раздельными каналами, иначе «сверху и снизу» не
// отличить от «слева и справа» на глаз: верх — красный, низ — зелёный,
// левая — синий, правая — жёлтая примесь в красный и зелёный.
float3 KernTerrainForeignSideColor(float packedContour)
{
    int reliefCode = KernTerrainReliefCode(packedContour);
    if (reliefCode == 0)
    {
        return float3(0.15, 0.15, 0.15);
    }

    int foreignSides = (~(reliefCode - 1)) & 0x0F;
    float top = (foreignSides & 1) != 0 ? 1.0 : 0.0;
    float left = (foreignSides & 2) != 0 ? 1.0 : 0.0;
    float bottom = (foreignSides & 4) != 0 ? 1.0 : 0.0;
    float right = (foreignSides & 8) != 0 ? 1.0 : 0.0;
    return float3(
        max(top, right * 0.6),
        max(bottom, right * 0.6),
        left);
}

// Вид получает тот же разбор вершины, что и кадр. Раньше он собирал термы
// сам и показывал четвёртое мнение о том, что такое координата клетки: кайму
// он считал по UV тайла, тогда как кадр считал по клеточной координате, и
// подтвердить видом было нельзя ничего.
float3 KernTerrainDebugColor(
    TerrainSurfaceInputs surface,
    float coverage,
    float isForeground,
    float packedColumn,
    float ambientOcclusion)
{
    float2 cellLocal = surface.cellSample;
    float packedContour = surface.packedContour;
    float anchored = surface.anchored;
    if (_TerrainDebugView == KERN_TERRAIN_DEBUG_RELIEF_RIM)
    {
        // Выключенная кайма красится отдельно. Иначе вид заливает мир
        // зелёным, и «кайма выключена» неотличимо от «кайма посчитана и
        // никого не трогает» — ровно та неоднозначность, из-за которой
        // белый кадр однажды уже нельзя было прочитать.
        if (_TerrainReliefRimEnabled < 0.5)
        {
            return float3(0.35, 0.25, 0.55);
        }

        // Зелёное — кайма не трогает пиксель, красное — гасит до предела.
        // Та же структура, что у кадра: выбрать «не ту» координату здесь
        // больше нечем.
        float rim = TerrainReliefRim(surface);
        return float3(1.0 - rim, rim, 0.35);
    }

    if (_TerrainDebugView == KERN_TERRAIN_DEBUG_FOREIGN_SIDES)
    {
        return KernTerrainForeignSideColor(packedContour);
    }

    if (_TerrainDebugView == KERN_TERRAIN_DEBUG_COVERAGE)
    {
        // Подложка не участвует в силуэте переднего плана. Если красить её
        // тем же цветом, что заполненный foreground, видны лишь узкие
        // вырезанные клинья, а граница клетки теряется в фоне.
        if (isForeground < 0.5)
        {
            return float3(0.15, 0.15, 0.15);
        }

        // Бирюза — пиксель переднего плана, малиновый — вырезанная часть
        // его несущего прямоугольника.
        return lerp(
            float3(0.95, 0.15, 0.55),
            float3(0.10, 0.85, 0.95),
            saturate(coverage));
    }

    if (_TerrainDebugView == KERN_TERRAIN_DEBUG_LAYER)
    {
        return isForeground > 0.5
            ? float3(0.1, 0.9, 0.2)
            : float3(0.2, 0.3, 1.0);
    }

    if (_TerrainDebugView == KERN_TERRAIN_DEBUG_ANCHORED)
    {
        return anchored > 0.5
            ? float3(1.0, 0.85, 0.1)
            : float3(0.12, 0.12, 0.15);
    }

    if (_TerrainDebugView == KERN_TERRAIN_DEBUG_CELL_LOCAL)
    {
        // Координата внутри клетки. За пределами 0..1 канал уходит в
        // насыщение на видимой части смещённого контура.
        return float3(saturate(cellLocal.x), saturate(cellLocal.y), 0.0);
    }

    if (_TerrainDebugView == KERN_TERRAIN_DEBUG_RELIEF_GROUP)
    {
        int reliefCode = KernTerrainReliefCode(packedContour);
        float shade = reliefCode == 0 ? 0.0 : (float)reliefCode / 17.0;
        return float3(shade, shade * 0.5, 1.0 - shade);
    }

    if (_TerrainDebugView == KERN_TERRAIN_DEBUG_CONTINUOUS_SHEET)
    {
        bool sheet = (((int)(packedColumn + 0.5)) & 32) != 0;
        return sheet
            ? float3(0.2, 0.9, 0.9)
            : float3(0.2, 0.1, 0.1);
    }

    if (_TerrainDebugView == KERN_TERRAIN_DEBUG_AMBIENT_OCCLUSION)
    {
        // Рампа, а не серая шкала: отсутствие AO должно отличаться от белого
        // пересвета в кадре.
        float occlusion = 1.0 - ambientOcclusion;
        return float3(occlusion, 1.0 - occlusion, 0.35);
    }

    return float3(1.0, 0.0, 0.8);
}

#endif

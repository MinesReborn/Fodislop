#ifndef KERN_TERRAIN_COLOR_ANIMATION_INCLUDED
#define KERN_TERRAIN_COLOR_ANIMATION_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

// Числа анимации — свойства материала, а проходы cbuffer сам не подключает до
// этого места: без него _PrismaticTint* и _FacetedGlint* оказались
// бы необъявленными. Вне UnityPerMaterial их объявлять нельзя, поэтому
// подключаем блок целиком.
//
// Порядок обязателен: cbuffer-файл содержит ещё и TerrainMaterialAtlasTexelSize,
// который зовёт TerrainAtlasTexelSize, — сначала идёт выборка атласа. Guard'ы
// внутри обоих файлов делают повторное включение безопасным.
#include "TerrainAtlasSampling.hlsl"
#include "TerrainMaterialCBuffer.hlsl"
#include "TerrainAnimationProfile.hlsl"
#include "TerrainContour.hlsl"

// Анимация цвета клетки террейна — одна на оба пасса.
//
// ЗАЧЕМ ОТДЕЛЬНЫМ ФАЙЛОМ. Раньше анимация жила только в видимом пассе, а поле
// материалов писало упакованный цвет вершины как есть. Из-за этого shimmer
// не попадал в отскок, и
// освещение вообще не знало, что текстура анимирована: альбедо получалось
// статическим при динамической картинке. Копировать код во второй пасс нельзя —
// две копии разойдутся на первой же правке вида, и разойдутся молча.
//
// Выборки текстур здесь нет намеренно: карта потока читается снаружи и
// приезжает готовым цветом. Пассы объявляют разные наборы текстур, и
// включаемый файл не должен сэмплировать ни один из них.

float3 TerrainRGBToHSV(float3 c)
{
    float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
    float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));

    float d = q.x - min(q.w, q.y);
    float e = 1.0e-10;
    return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
}

float3 TerrainHSVToRGB(float3 c)
{
    float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * lerp(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
}

struct TerrainShimmerSignal
{
    float wave;
    float body;
    float surfaceMask;
};

TerrainShimmerSignal EvaluateTerrainShimmer(
    float3 luminanceSource,
    float3 flowSample,
    float animationSpeed,
    float shimmerSpeedScale)
{
    float3 flowHSV = TerrainRGBToHSV(flowSample);
    float hueAngle = flowHSV.x * 6.28318548;
    float chroma =
        max(flowSample.r, max(flowSample.g, flowSample.b)) -
        min(flowSample.r, min(flowSample.g, flowSample.b));

    float wave = sin(-(hueAngle + _Time.y * animationSpeed * shimmerSpeedScale));
    wave = wave * 0.5 + 0.5;

    float luminance = dot(luminanceSource, float3(0.299, 0.587, 0.114));
    float inverseLuminance = 1.0 - luminance;
    float luminanceMask =
        1.0 - inverseLuminance * inverseLuminance * inverseLuminance;

    TerrainShimmerSignal signal;
    signal.wave = wave;
    signal.body = wave * wave * wave;
    signal.surfaceMask = luminanceMask * lerp(_ShimmerChromaFloor, 1.0, chroma);
    return signal;
}

float3 TerrainUnpackRgb24(float packedColor)
{
    uint packed = (uint)round(packedColor);
    return float3(
        packed & 0xFFu,
        (packed >> 8u) & 0xFFu,
        (packed >> 16u) & 0xFFu) / 255.0;
}

// Фактическая интенсивность faceted-glint. Один расчёт используется видимым
// цветом и Terrain Debug View, чтобы диагностический слой не расходился с кадром.
float EvaluateFacetedGlintStrength(
    float2 localUV,
    float3 luminanceSource,
    int animationProfile,
    float animationSpeed,
    float animationOffset)
{
    if (animationProfile != KERN_TERRAIN_ANIMATION_PROFILE_FACETED_CRYSTAL)
    {
        return 0.0;
    }

    float phase = frac(_Time.y * animationSpeed + animationOffset);
    float eventEnvelope =
        smoothstep(0.0, _FacetedGlintRiseEnd, phase) *
        (1.0 - smoothstep(_FacetedGlintFallStart, _FacetedGlintFallEnd, phase));
    float sweepProgress = saturate(phase / _FacetedGlintSweepDuration);
    float sweepCoordinate = dot(localUV, _FacetedGlintDirection.xy);
    float sweepCenter = lerp(_FacetedGlintSweepStart, _FacetedGlintSweepEnd, sweepProgress);
    float bandDistance = abs(sweepCoordinate - sweepCenter);
    float band = 1.0 - smoothstep(_FacetedGlintBandStart, _FacetedGlintBandEnd, bandDistance);

    float luminance = dot(luminanceSource, float3(0.299, 0.587, 0.114));
    float facetMask = smoothstep(_FacetedGlintMaskStart, _FacetedGlintMaskEnd, luminance);
    return eventEnvelope * band * facetMask * _FacetedGlintStrength;
}

#include "TerrainPrismaticCrystal.hlsl"

// baseColor      — цвет, который анимируется.
// luminanceSource — по чему считается маска яркости для мерцания. В видимом
//                   пассе и в поле материалов это цвет текселя атласа.
// flowSample     — выборка карты потока в мировой точке, снаружи.
float3 AnimateTerrainColor(
    float3 baseColor,
    float3 luminanceSource,
    float2 localUV,
    int animationType,
    int animationProfile,
    float animationSpeed,
    float animationOffset,
    float3 flowSample,
    float packedCellColor,
    float3 shimmerColor,
    float shimmerSpeedScale,
    float pulseSpeedScale)
{
    float3 result = baseColor;

    if (animationProfile == KERN_TERRAIN_ANIMATION_PROFILE_PRISMATIC_CRYSTAL)
    {
#if defined(UNITY_COLORSPACE_GAMMA)
        result = EvaluatePrismaticCrystal(
            baseColor, flowSample, animationOffset, _Time.y * animationSpeed * _PrismaticPhaseSpeed);
#else
        result = SRGBToLinear(EvaluatePrismaticCrystal(
            LinearToSRGB(baseColor), flowSample, animationOffset, _Time.y * animationSpeed * _PrismaticPhaseSpeed));
#endif
    }
    else if (animationProfile == KERN_TERRAIN_ANIMATION_PROFILE_FACETED_CRYSTAL)
    {
        // Each cell receives a deterministic phase from TerrainQuadBuilder.
        // A diagonal sweep brings out facet glints without long dead pauses.
        float strength = EvaluateFacetedGlintStrength(
            localUV,
            luminanceSource,
            animationProfile,
            animationSpeed,
            animationOffset);
        float3 cellColor = TerrainUnpackRgb24(packedCellColor);
        float3 glintColor = lerp(cellColor, 1.0.xxx, _FacetedGlintMix);
        result = baseColor + glintColor * strength;
    }
    else if (animationType == 1) // Blinking
    {
        float pulse = 0.5 + 0.5 * sin(
            _Time.y * animationSpeed * pulseSpeedScale + animationOffset);
        result = baseColor * pulse;
    }
    else if (animationType == 2) // Shimmer
    {
        TerrainShimmerSignal signal = EvaluateTerrainShimmer(
            luminanceSource,
            flowSample,
            animationSpeed,
            shimmerSpeedScale);
        result = lerp(
            baseColor,
            shimmerColor,
            signal.body * signal.surfaceMask);
    }
    else if (animationType == 3) // Rainbow
    {
        float3 rainbowHSV = TerrainRGBToHSV(baseColor);
        rainbowHSV.x = frac(rainbowHSV.x + _Time.y * (animationSpeed / _RainbowHueDivisor));
        result = TerrainHSVToRGB(rainbowHSV);
    }

    return result;
}

#endif

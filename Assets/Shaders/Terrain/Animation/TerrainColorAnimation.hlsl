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
#include "Assets/Shaders/Terrain/Textures/TerrainAtlasSampling.hlsl"
#include "Assets/Shaders/Terrain/Materials/TerrainMaterialCBuffer.hlsl"
#include "Assets/Shaders/Terrain/Animation/TerrainAnimationProfile.hlsl"
#include "Assets/Shaders/Terrain/Geometry/TerrainContour.hlsl"

static const float KERN_TERRAIN_PRISMATIC_ANIMATION_SPEED = 50.0;
static const float KERN_TERRAIN_FACETED_ANIMATION_SPEED = 0.06;
static const float KERN_TERRAIN_MOLTEN_ANIMATION_SPEED = 10.0;
static const float KERN_TERRAIN_BLINKING_ANIMATION_SPEED = 10.0;
static const float KERN_TERRAIN_SHIMMER_ANIMATION_SPEED = 10.0;
static const float KERN_TERRAIN_RAINBOW_ANIMATION_SPEED = 10.0;

// Анимация цвета клетки террейна для видимого пасса.
//
// Освещение намеренно не включает этот файл: его material field хранит
// фиксированное базовое альбедо и не меняется от shimmer, переливания
// кристаллов или времени пересборки окна. Эти эффекты остаются презентацией,
// а не новым входом статического transport.
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

// Фактическая интенсивность faceted-glint. Один расчёт используется видимым
// цветом и Terrain Debug View, чтобы диагностический слой не расходился с кадром.
float EvaluateFacetedGlintStrength(
    float2 localUV,
    float3 luminanceSource,
    int cellAnimationType,
    float animationSpeed,
    float animationOffset)
{
    if (cellAnimationType != (int)KERN_TERRAIN_ANIMATION_TYPE_FACETED)
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

#include "Assets/Shaders/Terrain/Animation/Prismatic/TerrainPrismaticCrystal.hlsl"

// baseColor      — цвет, который анимируется.
// luminanceSource — по чему считается маска яркости для мерцания. В видимом
//                   пассе и в поле материалов это цвет текселя атласа.
// flowSample     — выборка карты потока в мировой точке, снаружи.
float3 AnimateTerrainColor(
    float3 baseColor,
    float3 luminanceSource,
    float2 localUV,
    float2 surfacePosition,
    int cellAnimationType,
    float animationOffset,
    float animationSpeed,
    float3 flowSample,
    float3 shimmerColor,
    float shimmerSpeedScale,
    float blinkingSpeedScale)
{
    float3 result = baseColor;
    TerrainAnimationProfile profile = ResolveTerrainAnimationProfile(cellAnimationType);

    if (profile.behavior == KERN_TERRAIN_PROFILE_PRISMATIC)
    {
#if defined(UNITY_COLORSPACE_GAMMA)
        result = EvaluatePrismaticCrystal(
            baseColor, flowSample, animationOffset, _Time.y * KERN_TERRAIN_PRISMATIC_ANIMATION_SPEED * _PrismaticPhaseSpeed);
#else
        result = SRGBToLinear(EvaluatePrismaticCrystal(
            LinearToSRGB(baseColor), flowSample, animationOffset, _Time.y * KERN_TERRAIN_PRISMATIC_ANIMATION_SPEED * _PrismaticPhaseSpeed));
#endif
    }
    else if (profile.behavior == KERN_TERRAIN_PROFILE_FACETED)
    {
        // Each cell receives a deterministic phase from TerrainQuadBuilder.
        // A diagonal sweep brings out facet glints without long dead pauses.
        float strength = EvaluateFacetedGlintStrength(
            localUV,
            luminanceSource,
            cellAnimationType,
            KERN_TERRAIN_FACETED_ANIMATION_SPEED,
            animationOffset);
        // Цвет блика — альбедо текселя, высветленное к белому.
        float3 glintColor = lerp(luminanceSource, 1.0.xxx, _FacetedGlintMix);
        result = baseColor + glintColor * strength;
    }
    else if (profile.behavior == KERN_TERRAIN_PROFILE_MOLTEN)
    {
        // Stable world-anchored heat flow: adjacent Lava cells share one
        // continuous pattern, while only the visual albedo changes over time.
        float2 pixelPosition = (floor(surfacePosition * 32.0) + 0.5) / 32.0;
        float phase = _Time.y * KERN_TERRAIN_MOLTEN_ANIMATION_SPEED * 0.12;
        float broadFlow = sin(dot(pixelPosition, float2(1.9, -1.3)) + phase);
        float crossFlow = sin(dot(pixelPosition, float2(-1.1, 2.1)) - phase * 0.7);
        float heat = saturate(0.5 + broadFlow * 0.3 + crossFlow * 0.2);
        float hot = heat * heat;
        float material = max(baseColor.r, max(baseColor.g, baseColor.b));
        result = baseColor * (0.35 + 0.8 * heat)
            + float3(0.6, 0.35, 0.035) * (hot * material);
    }
    else if (profile.animationType == KERN_TERRAIN_ANIMATION_TYPE_LEGACY2TEST ||
             profile.animationType == KERN_TERRAIN_ANIMATION_TYPE_LEGACY5TEST)
    {
        float3 flowHSV = TerrainRGBToHSV(flowSample);
        float wave = 0.5 + 0.5 * sin(-(flowHSV.x * 6.28318530718 + _Time.y * animationSpeed / 20.0));
        float blend = wave * wave * wave;
        float inverseLuminance = 1.0 - dot(luminanceSource, float3(0.3, 0.59, 0.11));
        float brightness = 1.0 - inverseLuminance * inverseLuminance * inverseLuminance;
        if (profile.animationType == KERN_TERRAIN_ANIMATION_TYPE_LEGACY5TEST)
        {
            brightness *= max(flowSample.r, max(flowSample.g, flowSample.b)) -
                min(flowSample.r, min(flowSample.g, flowSample.b));
        }
        result = baseColor * (1.0 - blend) + shimmerColor * (brightness * blend);
    }
    else if (profile.animationType == KERN_TERRAIN_ANIMATION_TYPE_LEGACY7TEST)
    {
        float3 hsv = TerrainRGBToHSV(baseColor);
        hsv.x = frac(hsv.x + _Time.y * animationSpeed / 20.0);
        result = TerrainHSVToRGB(hsv);
    }
    else if (profile.animationType == KERN_TERRAIN_ANIMATION_TYPE_BLINKING)
    {
        float blinking = 0.5 + 0.5 * sin(
            _Time.y * KERN_TERRAIN_BLINKING_ANIMATION_SPEED * blinkingSpeedScale + animationOffset);
        result = baseColor * blinking;
    }
    else if (profile.animationType == KERN_TERRAIN_ANIMATION_TYPE_SHIMMER)
    {
        TerrainShimmerSignal signal = EvaluateTerrainShimmer(
            luminanceSource,
            flowSample,
            KERN_TERRAIN_SHIMMER_ANIMATION_SPEED,
            shimmerSpeedScale);
        result = lerp(
            baseColor,
            shimmerColor,
            signal.body * signal.surfaceMask);
    }
    else if (profile.animationType == KERN_TERRAIN_ANIMATION_TYPE_RAINBOW)
    {
        float3 rainbowHSV = TerrainRGBToHSV(baseColor);
        rainbowHSV.x = frac(rainbowHSV.x + _Time.y * (KERN_TERRAIN_RAINBOW_ANIMATION_SPEED / _RainbowHueDivisor));
        result = TerrainHSVToRGB(rainbowHSV);
    }

    return result;
}

#endif

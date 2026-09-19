#ifndef KERN_TERRAIN_COLOR_ANIMATION_INCLUDED
#define KERN_TERRAIN_COLOR_ANIMATION_INCLUDED

// Анимация цвета клетки террейна — одна на оба пасса.
//
// ЗАЧЕМ ОТДЕЛЬНЫМ ФАЙЛОМ. Раньше анимация жила только в видимом пассе, а поле
// материалов писало упакованный цвет вершины как есть. Из-за этого мигающая
// лава светила ровно, радужный блок отдавал в отскок постоянный цвет, и
// освещение вообще не знало, что текстура анимирована: альбедо получалось
// статическим при динамической картинке. Копировать код во второй пасс нельзя —
// две копии разойдутся на первой же правке вида, и разойдутся молча.
//
// Текстур здесь нет намеренно: выборка потока делается снаружи и приезжает
// готовым цветом. Пассы объявляют разные наборы текстур, и включаемый файл не
// должен зависеть ни от одного из них.

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

// baseColor      — цвет, который анимируется.
// luminanceSource — по чему считается маска яркости для мерцания. В видимом
//                   пассе это цвет текселя, в поле материалов — само альбедо
//                   клетки: там текстуры нет, и средний цвет клетки — лучшее,
//                   что есть.
// flowSample     — выборка карты потока в мировой точке, снаружи.
float3 AnimateTerrainColor(
    float3 baseColor,
    float3 luminanceSource,
    int animationType,
    float animationSpeed,
    float animationOffset,
    float3 flowSample,
    float3 shimmerColor,
    float shimmerSpeedScale,
    float pulseSpeedScale)
{
    if (animationType == 1) // Blinking
    {
        float pulse = 0.5 + 0.5 * sin(
            _Time.y * animationSpeed * pulseSpeedScale + animationOffset);
        return baseColor * pulse;
    }

    if (animationType == 2) // Shimmer
    {
        float3 flowHSV = TerrainRGBToHSV(flowSample);
        float hueAngle = flowHSV.x * 6.28318548;
        float chroma =
            max(flowSample.r, max(flowSample.g, flowSample.b)) -
            min(flowSample.r, min(flowSample.g, flowSample.b));

        float wave = sin(-(hueAngle + _Time.y * animationSpeed * shimmerSpeedScale));
        wave = (wave + 1.0) * 0.5;
        float waveCubed = wave * wave * wave;

        float luminance = dot(luminanceSource, float3(0.299, 0.587, 0.114));
        float inverseLuminance = 1.0 - luminance;
        float luminanceMask =
            1.0 - inverseLuminance * inverseLuminance * inverseLuminance;

        return lerp(baseColor, shimmerColor, waveCubed * luminanceMask * chroma);
    }

    if (animationType == 3) // Rainbow
    {
        float3 rainbowHSV = TerrainRGBToHSV(baseColor);
        rainbowHSV.x = frac(rainbowHSV.x + _Time.y * (animationSpeed / 255.0));
        return TerrainHSVToRGB(rainbowHSV);
    }

    return baseColor;
}

#endif

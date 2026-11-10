#ifndef KERN_TERRAIN_MATERIAL_CBUFFER_INCLUDED
#define KERN_TERRAIN_MATERIAL_CBUFFER_INCLUDED

// ВСЁ, ЧТО ЗАВИСИТ ОТ МАТЕРИАЛА ТЕРРЕЙНА, ОБЪЯВЛЕНО ЗДЕСЬ, И ТОЛЬКО ЗДЕСЬ.
//
// SRP Batcher склеивает вызовы отрисовки только у шейдеров, где ни одно
// свойство материала не объявлено снаружи UnityPerMaterial, И где КАЖДЫЙ пасс
// объявляет этот блок одинаково. Пасс без блока или с другой раскладкой делает
// несовместимым весь шейдер целиком, а не только себя, — батчер молча
// выключается, и счётчик пакетов показывает ноль при трёх сотнях смен
// материала.
//
// Раньше блок стоял в обоих пассах скопированным слово в слово, и совпадение
// держалось на дисциплине. Теперь оно механическое: файл один. Ни одно из этих
// свойств не читается в пассе поля материалов — блок стоит там ради раскладки,
// и убирать его как «мёртвый» нельзя.
CBUFFER_START(UnityPerMaterial)
    float4 _ShimmerColor;
    float4 _FlowScale;
    float _TerrainDepthPixels;
    float _TerrainDepthDarkening;
    float _ShimmerSpeedScale;
    float _BlinkingSpeedScale;
    float _RoundableCornerRadius;
    float _RimDistanceScale;
    float _RimFalloff;
    float _TerrainRimEnabled;
    float _RimQuantizationEnabled;

    // Авторский вид поверхности: числа лежат в TerrainConfigHolder и приезжают
    // свойствами материала. Блок один на все проходы, поэтому раскладку держит
    // этот файл, а не дисциплина копий.
    float _EmissionBrightnessStart;
    float _EmissionBrightnessEnd;
    float _SurfaceEmissionStrength;
    float _TerrainDebugDeltaContrast;
    float4 _FacetedGlintDirection;
    float _FacetedGlintSweepStart;
    float _FacetedGlintSweepEnd;
    float _FacetedGlintBandStart;
    float _FacetedGlintBandEnd;
    float _FacetedGlintMaskStart;
    float _FacetedGlintMaskEnd;
    float _FacetedGlintStrength;
    float _FacetedGlintMix;
    float _FacetedGlintRiseEnd;
    float _FacetedGlintFallStart;
    float _FacetedGlintFallEnd;
    float _FacetedGlintSweepDuration;
    float _ShimmerChromaFloor;
    float _PrismaticPhaseSpeed;
    float _RainbowHueDivisor;
    float4 _PrismaticTintA;
    float4 _PrismaticTintB;
    float4 _PrismaticTintC;
    float4 _PrismaticTintD;
    float4 _PrismaticTintE;
    float _PremultiplyAlphaFloor;
    float _AlphaCutoff;
    float4 _FlowMap_TexelSize; // KERN-SHADER-DEAD-UNIFORM: layout — см. комментарий выше
    float4 _TerrainAtlas0_TexelSize;
    float4 _TerrainAtlas1_TexelSize;
    float4 _TerrainAtlas2_TexelSize;
    float4 _TerrainAtlas3_TexelSize;
    float4 _TerrainAtlas4_TexelSize;
    float4 _TerrainAtlas5_TexelSize;
    float4 _TerrainAtlas6_TexelSize;
    float4 _TerrainAtlas7_TexelSize;
CBUFFER_END

// Размер текселя атласа, из которого читается клетка.
//
// В режиме клеток атлас выбирает сама клетка, поэтому размер берётся по её
// слоту. Вне его рисуется накладка дверей — у неё материал на атлас, и слот
// один. Требует TerrainAtlasSampling.hlsl.
float4 TerrainMaterialAtlasTexelSize(int slot)
{
    return TerrainAtlasTexelSize(
        slot,
        _TerrainAtlas0_TexelSize,
        _TerrainAtlas1_TexelSize,
        _TerrainAtlas2_TexelSize,
        _TerrainAtlas3_TexelSize,
        _TerrainAtlas4_TexelSize,
        _TerrainAtlas5_TexelSize,
        _TerrainAtlas6_TexelSize,
        _TerrainAtlas7_TexelSize);
}

#endif

#nullable enable

using UnityEngine;

namespace Kern.World.Terrain;

// Авторский вид поверхности: числа этого списка лежат в TerrainConfigHolder и
// приезжают свойствами материала. Раньше список был скопирован слово в слово
// в двух местах TerrainMaterialManager — теперь он один и лежит рядом.
internal static class TerrainMaterialTuning
{
    private static readonly int s_rimEnabledPropertyId = Shader.PropertyToID("_TerrainRimEnabled");
    private static readonly int s_roundableCornerRadiusPropertyId =
        Shader.PropertyToID("_RoundableCornerRadius");
    private static readonly int s_rimDistanceScalePropertyId =
        Shader.PropertyToID("_RimDistanceScale");
    private static readonly int s_rimQuantizationEnabledPropertyId =
        Shader.PropertyToID("_RimQuantizationEnabled");
    private static readonly int s_rimFalloffPropertyId =
        Shader.PropertyToID("_RimFalloff");
    private static readonly int s_terrainDebugDeltaContrastPropertyId =
        Shader.PropertyToID("_TerrainDebugDeltaContrast");
    private static readonly int s_facetedGlintDirectionPropertyId =
        Shader.PropertyToID("_FacetedGlintDirection");
    private static readonly int s_facetedGlintSweepStartPropertyId =
        Shader.PropertyToID("_FacetedGlintSweepStart");
    private static readonly int s_facetedGlintSweepEndPropertyId =
        Shader.PropertyToID("_FacetedGlintSweepEnd");
    private static readonly int s_facetedGlintBandStartPropertyId =
        Shader.PropertyToID("_FacetedGlintBandStart");
    private static readonly int s_facetedGlintBandEndPropertyId =
        Shader.PropertyToID("_FacetedGlintBandEnd");
    private static readonly int s_facetedGlintMaskStartPropertyId =
        Shader.PropertyToID("_FacetedGlintMaskStart");
    private static readonly int s_facetedGlintMaskEndPropertyId =
        Shader.PropertyToID("_FacetedGlintMaskEnd");
    private static readonly int s_facetedGlintStrengthPropertyId =
        Shader.PropertyToID("_FacetedGlintStrength");
    private static readonly int s_facetedGlintMixPropertyId =
        Shader.PropertyToID("_FacetedGlintMix");
    private static readonly int s_facetedGlintRiseEndPropertyId =
        Shader.PropertyToID("_FacetedGlintRiseEnd");
    private static readonly int s_facetedGlintFallStartPropertyId =
        Shader.PropertyToID("_FacetedGlintFallStart");
    private static readonly int s_facetedGlintFallEndPropertyId =
        Shader.PropertyToID("_FacetedGlintFallEnd");
    private static readonly int s_facetedGlintSweepDurationPropertyId =
        Shader.PropertyToID("_FacetedGlintSweepDuration");
    private static readonly int s_shimmerChromaFloorPropertyId =
        Shader.PropertyToID("_ShimmerChromaFloor");
    private static readonly int s_prismaticPhaseSpeedPropertyId =
        Shader.PropertyToID("_PrismaticPhaseSpeed");
    private static readonly int s_rainbowHueDivisorPropertyId =
        Shader.PropertyToID("_RainbowHueDivisor");
    private static readonly int s_prismaticTintAPropertyId =
        Shader.PropertyToID("_PrismaticTintA");
    private static readonly int s_prismaticTintBPropertyId =
        Shader.PropertyToID("_PrismaticTintB");
    private static readonly int s_prismaticTintCPropertyId =
        Shader.PropertyToID("_PrismaticTintC");
    private static readonly int s_prismaticTintDPropertyId =
        Shader.PropertyToID("_PrismaticTintD");
    private static readonly int s_prismaticTintEPropertyId =
        Shader.PropertyToID("_PrismaticTintE");
    private static readonly int s_premultiplyAlphaFloorPropertyId =
        Shader.PropertyToID("_PremultiplyAlphaFloor");
    private static readonly int s_alphaCutoffPropertyId =
        Shader.PropertyToID("_AlphaCutoff");

    private static readonly int s_emissionBrightnessStartId = Shader.PropertyToID("_EmissionBrightnessStart");
    private static readonly int s_emissionBrightnessEndId = Shader.PropertyToID("_EmissionBrightnessEnd");
    private static readonly int s_surfaceEmissionStrengthId = Shader.PropertyToID("_SurfaceEmissionStrength");

    public static void Apply(Material material, bool enableRim)
    {
        material.SetFloat(s_rimEnabledPropertyId, enableRim ? 1f : 0f);
        material.SetFloat(
            s_roundableCornerRadiusPropertyId,
            TerrainConfigHolder.RoundableCornerRadiusCells);
        material.SetFloat(
            s_rimDistanceScalePropertyId,
            TerrainConfigHolder.RimDistanceScale);
        material.SetFloat(
            s_rimQuantizationEnabledPropertyId,
            TerrainConfigHolder.RimQuantizationEnabled ? 1f : 0f);
        material.SetFloat(
            s_rimFalloffPropertyId,
            TerrainConfigHolder.RimFalloff);
        material.SetFloat(s_emissionBrightnessStartId, TerrainConfigHolder.EmissionBrightnessStart);
        material.SetFloat(s_emissionBrightnessEndId, TerrainConfigHolder.EmissionBrightnessEnd);
        material.SetFloat(s_surfaceEmissionStrengthId, TerrainConfigHolder.SurfaceEmissionStrength);
        material.SetFloat(
            s_terrainDebugDeltaContrastPropertyId,
            TerrainConfigHolder.TerrainDebugDeltaContrast);
        material.SetVector(s_facetedGlintDirectionPropertyId, TerrainConfigHolder.FacetedGlintDirection);
        material.SetFloat(s_facetedGlintSweepStartPropertyId, TerrainConfigHolder.FacetedGlintSweepStart);
        material.SetFloat(s_facetedGlintSweepEndPropertyId, TerrainConfigHolder.FacetedGlintSweepEnd);
        material.SetFloat(s_facetedGlintBandStartPropertyId, TerrainConfigHolder.FacetedGlintBandStart);
        material.SetFloat(s_facetedGlintBandEndPropertyId, TerrainConfigHolder.FacetedGlintBandEnd);
        material.SetFloat(s_facetedGlintMaskStartPropertyId, TerrainConfigHolder.FacetedGlintMaskStart);
        material.SetFloat(s_facetedGlintMaskEndPropertyId, TerrainConfigHolder.FacetedGlintMaskEnd);
        material.SetFloat(s_facetedGlintStrengthPropertyId, TerrainConfigHolder.FacetedGlintStrength);
        material.SetFloat(s_facetedGlintMixPropertyId, TerrainConfigHolder.FacetedGlintMix);
        material.SetFloat(s_facetedGlintRiseEndPropertyId, TerrainConfigHolder.FacetedGlintRiseEnd);
        material.SetFloat(s_facetedGlintFallStartPropertyId, TerrainConfigHolder.FacetedGlintFallStart);
        material.SetFloat(s_facetedGlintFallEndPropertyId, TerrainConfigHolder.FacetedGlintFallEnd);
        material.SetFloat(s_facetedGlintSweepDurationPropertyId, TerrainConfigHolder.FacetedGlintSweepDuration);
        material.SetFloat(s_shimmerChromaFloorPropertyId, TerrainConfigHolder.ShimmerChromaFloor);
        material.SetFloat(s_prismaticPhaseSpeedPropertyId, TerrainConfigHolder.PrismaticPhaseSpeed);
        material.SetFloat(s_rainbowHueDivisorPropertyId, TerrainConfigHolder.RainbowHueDivisor);
        material.SetColor(s_prismaticTintAPropertyId, TerrainConfigHolder.PrismaticTintA);
        material.SetColor(s_prismaticTintBPropertyId, TerrainConfigHolder.PrismaticTintB);
        material.SetColor(s_prismaticTintCPropertyId, TerrainConfigHolder.PrismaticTintC);
        material.SetColor(s_prismaticTintDPropertyId, TerrainConfigHolder.PrismaticTintD);
        material.SetColor(s_prismaticTintEPropertyId, TerrainConfigHolder.PrismaticTintE);
        material.SetFloat(s_premultiplyAlphaFloorPropertyId, TerrainConfigHolder.PremultiplyAlphaFloor);
        material.SetFloat(s_alphaCutoffPropertyId, TerrainConfigHolder.AlphaCutoff);
    }
}

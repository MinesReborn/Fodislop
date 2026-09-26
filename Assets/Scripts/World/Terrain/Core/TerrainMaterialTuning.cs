#nullable enable

using UnityEngine;

namespace Kern.World.Terrain;

// Авторский вид поверхности: числа этого списка лежат в TerrainConfigHolder и
// приезжают свойствами материала. Раньше список был скопирован слово в слово
// в двух местах TerrainMaterialManager — теперь он один и лежит рядом.
internal static class TerrainMaterialTuning
{
    private static readonly int _OrganicBendStrengthPropertyID =
        Shader.PropertyToID("_OrganicBendStrength");
    private static readonly int _OrganicBendPivotPropertyID =
        Shader.PropertyToID("_OrganicBendPivot");
    private static readonly int _RoundableCornerRadiusPropertyID =
        Shader.PropertyToID("_RoundableCornerRadius");
    private static readonly int _ReliefRimDistanceScalePropertyID =
        Shader.PropertyToID("_ReliefRimDistanceScale");
    private static readonly int _ReliefRimQuantizationEnabledPropertyID =
        Shader.PropertyToID("_ReliefRimQuantizationEnabled");
    private static readonly int _ReliefRimFalloffPropertyID =
        Shader.PropertyToID("_ReliefRimFalloff");
    private static readonly int _GroundDecalStrengthPropertyID =
        Shader.PropertyToID("_GroundDecalStrength");
    private static readonly int _StoneDecalStrengthPropertyID =
        Shader.PropertyToID("_StoneDecalStrength");
    private static readonly int _DecalPlacementOffsetPropertyID =
        Shader.PropertyToID("_DecalPlacementOffset");
    private static readonly int _TerrainDebugDeltaContrastPropertyID =
        Shader.PropertyToID("_TerrainDebugDeltaContrast");
    private static readonly int _FacetedGlintDirectionPropertyID =
        Shader.PropertyToID("_FacetedGlintDirection");
    private static readonly int _FacetedGlintSweepStartPropertyID =
        Shader.PropertyToID("_FacetedGlintSweepStart");
    private static readonly int _FacetedGlintSweepEndPropertyID =
        Shader.PropertyToID("_FacetedGlintSweepEnd");
    private static readonly int _FacetedGlintBandStartPropertyID =
        Shader.PropertyToID("_FacetedGlintBandStart");
    private static readonly int _FacetedGlintBandEndPropertyID =
        Shader.PropertyToID("_FacetedGlintBandEnd");
    private static readonly int _FacetedGlintMaskStartPropertyID =
        Shader.PropertyToID("_FacetedGlintMaskStart");
    private static readonly int _FacetedGlintMaskEndPropertyID =
        Shader.PropertyToID("_FacetedGlintMaskEnd");
    private static readonly int _FacetedGlintStrengthPropertyID =
        Shader.PropertyToID("_FacetedGlintStrength");
    private static readonly int _FacetedGlintMixPropertyID =
        Shader.PropertyToID("_FacetedGlintMix");
    private static readonly int _FacetedGlintRiseEndPropertyID =
        Shader.PropertyToID("_FacetedGlintRiseEnd");
    private static readonly int _FacetedGlintFallStartPropertyID =
        Shader.PropertyToID("_FacetedGlintFallStart");
    private static readonly int _FacetedGlintFallEndPropertyID =
        Shader.PropertyToID("_FacetedGlintFallEnd");
    private static readonly int _FacetedGlintSweepDurationPropertyID =
        Shader.PropertyToID("_FacetedGlintSweepDuration");
    private static readonly int _ShimmerChromaFloorPropertyID =
        Shader.PropertyToID("_ShimmerChromaFloor");
    private static readonly int _PrismaticPhaseSpeedPropertyID =
        Shader.PropertyToID("_PrismaticPhaseSpeed");
    private static readonly int _RainbowHueDivisorPropertyID =
        Shader.PropertyToID("_RainbowHueDivisor");
    private static readonly int _PrismaticTintAPropertyID =
        Shader.PropertyToID("_PrismaticTintA");
    private static readonly int _PrismaticTintBPropertyID =
        Shader.PropertyToID("_PrismaticTintB");
    private static readonly int _PrismaticTintCPropertyID =
        Shader.PropertyToID("_PrismaticTintC");
    private static readonly int _PrismaticTintDPropertyID =
        Shader.PropertyToID("_PrismaticTintD");
    private static readonly int _PrismaticTintEPropertyID =
        Shader.PropertyToID("_PrismaticTintE");
    private static readonly int _PremultiplyAlphaFloorPropertyID =
        Shader.PropertyToID("_PremultiplyAlphaFloor");
    private static readonly int _AlphaCutoffPropertyID =
        Shader.PropertyToID("_AlphaCutoff");

    // Форму органического искажения задаёт авторский файл, а не настройка
    // игрока: ручки лежат в TerrainConfigHolder.
    public static void Apply(Material material)
    {
        material.SetFloat(_OrganicBendStrengthPropertyID, TerrainConfigHolder.OrganicBendStrength);
        material.SetFloat(_OrganicBendPivotPropertyID, TerrainConfigHolder.OrganicBendPivot);
        material.SetFloat(
            _RoundableCornerRadiusPropertyID,
            TerrainConfigHolder.RoundableCornerRadiusCells);
        material.SetFloat(
            _ReliefRimDistanceScalePropertyID,
            TerrainConfigHolder.ReliefRimDistanceScale);
        material.SetFloat(
            _ReliefRimQuantizationEnabledPropertyID,
            TerrainConfigHolder.ReliefRimQuantizationEnabled ? 1f : 0f);
        material.SetFloat(
            _ReliefRimFalloffPropertyID,
            TerrainConfigHolder.ReliefRimFalloff);
        material.SetFloat(_GroundDecalStrengthPropertyID, TerrainConfigHolder.GroundDecalStrength);
        material.SetFloat(_StoneDecalStrengthPropertyID, TerrainConfigHolder.StoneDecalStrength);
        material.SetFloat(_DecalPlacementOffsetPropertyID, TerrainConfigHolder.DecalPlacementOffset);
        material.SetFloat(
            _TerrainDebugDeltaContrastPropertyID,
            TerrainConfigHolder.TerrainDebugDeltaContrast);
        material.SetVector(_FacetedGlintDirectionPropertyID, TerrainConfigHolder.FacetedGlintDirection);
        material.SetFloat(_FacetedGlintSweepStartPropertyID, TerrainConfigHolder.FacetedGlintSweepStart);
        material.SetFloat(_FacetedGlintSweepEndPropertyID, TerrainConfigHolder.FacetedGlintSweepEnd);
        material.SetFloat(_FacetedGlintBandStartPropertyID, TerrainConfigHolder.FacetedGlintBandStart);
        material.SetFloat(_FacetedGlintBandEndPropertyID, TerrainConfigHolder.FacetedGlintBandEnd);
        material.SetFloat(_FacetedGlintMaskStartPropertyID, TerrainConfigHolder.FacetedGlintMaskStart);
        material.SetFloat(_FacetedGlintMaskEndPropertyID, TerrainConfigHolder.FacetedGlintMaskEnd);
        material.SetFloat(_FacetedGlintStrengthPropertyID, TerrainConfigHolder.FacetedGlintStrength);
        material.SetFloat(_FacetedGlintMixPropertyID, TerrainConfigHolder.FacetedGlintMix);
        material.SetFloat(_FacetedGlintRiseEndPropertyID, TerrainConfigHolder.FacetedGlintRiseEnd);
        material.SetFloat(_FacetedGlintFallStartPropertyID, TerrainConfigHolder.FacetedGlintFallStart);
        material.SetFloat(_FacetedGlintFallEndPropertyID, TerrainConfigHolder.FacetedGlintFallEnd);
        material.SetFloat(_FacetedGlintSweepDurationPropertyID, TerrainConfigHolder.FacetedGlintSweepDuration);
        material.SetFloat(_ShimmerChromaFloorPropertyID, TerrainConfigHolder.ShimmerChromaFloor);
        material.SetFloat(_PrismaticPhaseSpeedPropertyID, TerrainConfigHolder.PrismaticPhaseSpeed);
        material.SetFloat(_RainbowHueDivisorPropertyID, TerrainConfigHolder.RainbowHueDivisor);
        material.SetColor(_PrismaticTintAPropertyID, TerrainConfigHolder.PrismaticTintA);
        material.SetColor(_PrismaticTintBPropertyID, TerrainConfigHolder.PrismaticTintB);
        material.SetColor(_PrismaticTintCPropertyID, TerrainConfigHolder.PrismaticTintC);
        material.SetColor(_PrismaticTintDPropertyID, TerrainConfigHolder.PrismaticTintD);
        material.SetColor(_PrismaticTintEPropertyID, TerrainConfigHolder.PrismaticTintE);
        material.SetFloat(_PremultiplyAlphaFloorPropertyID, TerrainConfigHolder.PremultiplyAlphaFloor);
        material.SetFloat(_AlphaCutoffPropertyID, TerrainConfigHolder.AlphaCutoff);
    }
}

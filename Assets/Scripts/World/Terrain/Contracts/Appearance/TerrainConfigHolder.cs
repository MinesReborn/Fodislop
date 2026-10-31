#nullable enable

using Kern.Core;
using UnityEngine;

namespace Kern.World.Terrain;

public static class TerrainConfigHolder
{
    public const float ClassicDistortionStrengthSteps = 1.25f;


    // Ключи классического джиттера задают пространственный рисунок.
    // Диапазон хэша нечётный, чтобы свободный узел центрировался точно.
    public const int ClassicJitterRange = 7;
    public const int ClassicXHashA = 5;
    public const int ClassicXHashB = 11;
    public const int ClassicXHashC = 13;
    public const int ClassicXHashD = 7;
    public const int ClassicXHashModulus = 3221;
    public const int ClassicYHashA = 17;
    public const int ClassicYHashB = 19;
    public const int ClassicYHashC = 23;
    public const int ClassicYHashD = 37;
    public const int ClassicYHashModulus = 3469;

    // 2. Приём геометрии пиксельным проходом: ранний discard.
    public const float AlphaCutoff = 0.05f;

    // 3. Поверхностные координаты и базовая цветовая анимация.
    public static Vector2 FlowScale => new(12f, 10f);
    public const float ShimmerSpeedScale = 0.05f;
    public const float BlinkingSpeedScale = 0.5f;
    public static Color ShimmerColor => Color.white;
    public const float ShimmerChromaFloor = 0.65f;

    // Скорости цветовых фаз и параметры профилей.
    public const float PrismaticPhaseSpeed = 0.05f;
    public const float RainbowHueDivisor = 255f;

    public static Color PrismaticTintA => new(0.2f, 1f, 0.2f, 1f);
    public static Color PrismaticTintB => new(0.2f, 0.2f, 1f, 1f);
    public static Color PrismaticTintC => Color.white;
    public static Color PrismaticTintD => new(0.1f, 1f, 1f, 1f);
    public static Color PrismaticTintE => new(1f, 0f, 0f, 1f);

    // Глинт — ещё одна модификация анимированного цвета до освещения.
    public static Vector2 FacetedGlintDirection => new(0.62f, 0.38f);
    public const float FacetedGlintSweepStart = -0.12f;
    public const float FacetedGlintSweepEnd = 1.12f;
    public const float FacetedGlintBandStart = 0.035f;
    public const float FacetedGlintBandEnd = 0.13f;
    public const float FacetedGlintMaskStart = 0.2f;
    public const float FacetedGlintMaskEnd = 0.75f;
    public const float FacetedGlintStrength = 0.45f;
    public const float FacetedGlintMix = 0.72f;
    public const float FacetedGlintRiseEnd = 0.04f;
    public const float FacetedGlintFallStart = 0.28f;
    public const float FacetedGlintFallEnd = 0.40f;
    public const float FacetedGlintSweepDuration = 0.40f;

    // Усиление signed-разницы в диагностике вклада декали и призматического
    // тинта. На сам рендер поверхности этот коэффициент не влияет.
    public const float TerrainDebugDeltaContrast = 128f;

    // 5. Параметры world-surface field публикуются в материал и поле света.
    public const float SurfaceOccupancy = 1f;
    public static Color TransitGlowColor => Color.white;
    public const float TransitGlowStrength = 0.35f;
    public static Color PerspectiveGlowColor => Color.white;
    public const float PerspectiveGlowStrength = 0.12f;

    // Debug emission: shared linear peak-brightness thresholds, before reduction.
    public const float EmissionBrightnessStart = 0.25f;
    public const float EmissionBrightnessEnd = 0.60f;
    // Visible source radiance in paper-white units; transport gain is separate.
    public const float SurfaceEmissionStrength = 1.0f;

    // 6. Контактное затенение: сила контраста и самый тёмный уровень,
    // до которого оно опускает поверхность.
    //
    // Сила масштабирует контактную маску; насыщение
    // остаётся ограничено единицей в KernSampleTerrainAmbientOcclusion.
    // Пол задаёт нижний уровень яркости поверхности.
    //
    // 0.51 — не подбор на глаз. В оригинале тень на полу это 1 - z² при
    // z = 0.7, то есть ровно 0.51, и глубже пол там не темнеет никогда.
    public const float AmbientOcclusionStrength = 0.9f;
    public const float AmbientOcclusionFloor = 0.51f;
    // Максимальная ширина поля контактного затенения за геометрией, в клетках.
    public const float AmbientOcclusionDistanceCells = 0.60f;

    // Радиус скруглённого силуэта loose-террейна, в клетках.
    public const float RoundableCornerRadiusCells = 0.51f;

    // Фаска открытых сторон клетки: масштаб расстояния от геометрического
    // ребра и максимальная доля затемнения у самого ребра.
    public const bool RimQuantizationEnabled = true;
    public const float RimDistanceScale = 32f / 6f;
    public const float RimFalloff = 1f;

    // 7. Выход: premultiplied alpha корректируется после умножения света.
    public const float PremultiplyAlphaFloor = 0.15f;
}

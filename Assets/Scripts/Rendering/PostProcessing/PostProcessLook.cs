#nullable enable

using Fodinae.Core;
using UnityEngine;

namespace Fodinae.Rendering.PostProcessing;

/// <summary>
/// Единственный набор чисел, которым описан вид кадра.
/// </summary>
/// <remarks>
/// ЗАЧЕМ. Раньше каждый параметр постпроцесса был отдельным ползунком в
/// настройках и отдельным полем в `ProjectDefaults.asset`: тридцать пять
/// ползунков на вкладке эффектов, столько же полей в конфиге, и вид кадра
/// зависел от того, куда игрок их подвинул. Вида как решения не существовало —
/// существовал разброс.
///
/// Теперь вид — авторское решение и живёт здесь. Игроку остаётся тумблер на
/// эффект: включить или выключить, без промежуточных значений. Настройка
/// «сколько именно блума» игроку не задаётся, потому что это вопрос
/// художественный, а не пользовательский.
///
/// Крутить вид — значит править этот файл, и только его.
/// </remarks>
public static class PostProcessLook
{
    /// <summary>Свечение ярких участков.</summary>
    public static class Bloom
    {
        public const float Intensity = 0.15f;

        /// <summary>
        /// Порог в сцен-линейных единицах: ниже — не светится.
        /// </summary>
        /// <remarks>
        /// Порог ниже единицы намеренно. Потолок мирового света стоит на 1.0,
        /// то есть яркость пикселя не может превысить его альбедо, и порога
        /// выше единицы не достигало бы ничто — блум просто не существовал бы.
        /// 0.75 означает «светятся только светлые поверхности»: земля с
        /// альбедо около 0.08 остаётся тёмной, а белые части зданий и жилы
        /// руды дают ореол.
        /// </remarks>
        public const float Threshold = 0.75f;
        public const float SoftKnee = 0.5f;
        public const float Radius = 3f;
        public const float Scatter = 0.55f;

        public static Color Tint => Color.white;
    }

    /// <summary>Затемнение к краям кадра.</summary>
    public static class Vignette
    {
        public const float Intensity = 0.28f;
        public const float Smoothness = 0.6f;

        public static Color Color => new(0f, 0f, 0f, 1f);

        public static Vector2 Center => new(0.5f, 0.5f);
    }

    /// <summary>Расхождение каналов к краям — дефект объектива.</summary>
    public static class ChromaticAberration
    {
        public const float Intensity = 0.06f;
    }

    /// <summary>
    /// Цветокоррекция. Работает всегда: это не эффект, а обработка кадра.
    /// Нейтральные значения — точный no-op, так что вид задаётся здесь, а не
    /// накапливается из случайных сдвигов.
    /// </summary>
    public static class ColorGrading
    {
        public const float Exposure = PostProcessSettings.DefaultExposure;
        public const float Contrast = PostProcessSettings.DefaultContrast;
        public const float Saturation = PostProcessSettings.DefaultSaturation;

        /// <summary>
        /// Делитель в ToneMapAgX: сцен-линейная яркость, которая станет белым
        /// на дисплее. Единица — нейтрально.
        /// </summary>
        public const float ToneMappingWhitePoint =
            PostProcessSettings.DefaultToneMappingWhitePoint;

        public static Color Filter => Color.white;
    }

    /// <summary>Плёночное зерно в тёмных участках.</summary>
    public static class FilmGrain
    {
        public const float Intensity = 0.12f;

        /// <summary>Порог темноты в перцептивном пространстве.</summary>
        public const float DarknessThreshold = 0.22f;
        public const float NoiseScale = 0.75f;
        public const float AnimationSpeed = 60f;

        public static Color Color => new(0.018f, 0.02f, 0.028f, 1f);
    }

    /// <summary>Смаз движения.</summary>
    public static class MotionBlur
    {
        public const float Intensity = 0.25f;
    }

    /// <summary>Локальное повышение контраста — резкость без нимбов.</summary>
    public static class LocalContrast
    {
        public const float Intensity = 0.10f;
    }

    /// <summary>Оптика: грязь на линзе, анаморфные лучи, дифракция, блики.</summary>
    public static class Lens
    {
        public const float DirtIntensity = 0.04f;
        public const float DirtScale = 3f;
        public const float AnamorphicIntensity = 0.12f;
        public const float AnamorphicLength = 1.5f;
        public const float DiffractionIntensity = 0.05f;
        public const float GlintIntensity = 0.04f;
        public const float GlintThreshold = 0.85f;
    }

    /// <summary>Среда: объёмная пыль и тепловое искажение.</summary>
    public static class Atmosphere
    {
        public const float DustIntensity = 0.03f;
        public const float DustScale = 1f;
        public const float DustSpeed = 0.1f;
        public const float HeatRefractionIntensity = 0.06f;
        public const float HeatRefractionScale = 2f;
    }

    /// <summary>Физика дисплея: фосфорная маска и дизеринг.</summary>
    public static class Display
    {
        public const float PhosphorMaskIntensity = 0.08f;
        public const float DitheringIntensity = 0.5f;
    }

    /// <summary>Временное накопление: послесвечение и стабилизация света.</summary>
    public static class Temporal
    {
        public const float PersistenceIntensity = 0.15f;
        public const float PersistenceDecay = 0.85f;
        public const float LightStability = 0.35f;
    }
}

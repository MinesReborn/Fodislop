#nullable enable

using Kern.Core;
using UnityEngine;

namespace Kern.World.Lighting;

public static class LightingConfigHolder
{
    // МЕНЯТЬ ЗДЕСЬ: стартовые параметры качества, без зависимости от зума.
    // В игре: инструменты → «Цена света» → «Качество света» → «Применить».
    // Кнопка «Копировать в VisualTuning» выдаёт такой же блок для сохранения.
    public static readonly LightingQualityTuning DefaultQuality = new(
        FieldPixelsPerCell: 2,               // Перенос: material/albedo/glow, DDA; цена ~ плотность.
        LightPixelsPerCell: 2,               // Карта света: static/dynamic direct, итог; ≤ FieldPixelsPerCell.
        CascadeProbePixelsPerCell: 2,         // Пробы статики на клетку; цена ~ плотность².
        MaximumStaticCascadeDirections: 16,   // Удерживает стандартное окно в бюджете статики без автоснижения качества.
        DynamicNearCells: 6f,                 // Зона точного DDA вокруг лампы; цена ~ радиус².
        DynamicAngularSampleCount: 6,         // На 25% меньше выборок динамического света; 8 оставить для A/B сравнения.
        DynamicEmitterPointsPerAxis: 3,       // 3×3 точек источника; цена веера ~ значение².
        DynamicPolarDirectionCount: 64);      // Углы на точку источника вне ближней зоны.

    // Разрешения в пикселях на одну мировую клетку, независимо от зума.
    // FieldPixelsPerCell: material/albedo/glow, по которым идёт DDA;
    // цена лучей растёт с ней только на неоднородных клетках.
    // LightPixelsPerCell: приёмники static/dynamic direct и итоговая карта
    // света; цена приёмников ~ плотность². Пробы каскадов — отдельно.
    // Зум ни одну из плотностей не задаёт.

    // Контактное AO вычисляется сразу на сетке мирового растра 32x32.
    // Лимит текстур транспорта света эту плотность не уменьшает.
    public const int AmbientOcclusionPixelsPerCell = 32;

    // Ограничения выделения памяти: максимальная сторона в пикселях.
    // Плотность геометрии не снижается. Невместившийся регион — ошибка.
    public const int MaximumFieldTextureSize = 8192;
    public const int CascadeAtlasTextureSize = 1280;

    public static LightingFeatureFlags EnabledFeatures
    {
        get; set;
    } =
        LightingFeatureFlags.StaticRC |
        LightingFeatureFlags.DynamicLights;

    // 1. Геометрическая классификация входного материала для транспорта.
    public const float SolidOccupancyThreshold = 0.5f;
    public const float TransportSolidThreshold = 0.4f;

    // 2. Авторские интенсивности света в scene-linear единицах.
    // Общую экспозицию применяет штатный URP Volume перед tonemapping.
    public const float AmbientIntensity = 0.20f;
    public const float GlowScale = 12.0f;
    public const float DynamicLightIntensity = 0.5f;
    public static readonly Color AmbientColor = Color.white;
    public static bool DynamicLightEnabled => (EnabledFeatures & LightingFeatureFlags.DynamicLights) != 0;
    public static readonly Color DynamicLightColor = Color.white;

    // Пропускание однородной среды применяется трассировщиком вдоль пути.
    // Per RGB channel: sigma = ExtinctionRGB * ExtinctionMultiplier.
    // Transmission after d cells = exp(-sigma * d); multiply incoming light by it.
    // sigma: 0 = transparent; 0.2 = 81.87% per cell; 4.60517 = 1% per cell.
    // Solid affects transmission through the wall, not illumination of its front surface.
    public static readonly Color EmptyExtinctionRGB = Color.white;
    public static readonly Color SolidExtinctionRGB = Color.white;
    public const float EmptyExtinctionMultiplier = 0.20f;
    public const float SolidExtinctionMultiplier = 1.25f;

    // 3. Пространственный бюджет и фильтрация трассировки.
    // Плечо отражения поверхности: путь до лицевой грани, в клетках.
    public const float SurfaceReflectionReachCells = 0.5f;

    // Ближняя зона динамики использует прямой DDA, дальше — полярный веер.

    // Угловая плотность сэмплов на источник, стоимость растёт линейно.

    // Число слоёв веера вычисляется из этой плотности автоматически.

    // Билинейный фикс при чтении соседних записей атласа каскада.
    public const bool EnableBilinearFix = true;

    // 4. Производная величина для exposure-зебры после расчёта света.
    // Стеля exposure-зебры (вид 9): всё выше — згорить і після тонмаппа.
    // Шкала в стопах від білого: 8.0 = +3 стопи. Контент HDR by design
    // (емісія до GlowScale), тому стеля 1.0 фарбувала червоним весь
    // робочий HDR-запас.
    public const float MaximumLightMultiplier = 8.0f;

}

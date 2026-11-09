#nullable enable

using System;
using Kern.Rendering;
using UnityEngine;
using Kern.Core.UserInterface;

namespace Kern.Core.Configuration;

internal static class ClientConfigMigrations
{
    private const int RimSourceSchemaVersion = 31;
    private const int RimSchemaVersion = 32;
    private const int DistortionStyleSchemaVersion = 33;
    private const int PresetPairSchemaVersion = 34;
    private const int BloomStyleSchemaVersion = 35;
    private const int KeyBindingsSchemaVersion = 36;
    private const int SchemaVersion37 = 37;
    private const int HdrSwitchPendingSchemaVersion = 38;
    private const int PeakBrightnessFromDisplaySchemaVersion = 39;
    private const int TerrainNamesSchemaVersion = 40;
    private const int HandheldUIScaleSchemaVersion = 41;
    private const int HandheldUIScaleCorrectionSchemaVersion = 42;
    private const int AndroidLightingPresetSchemaVersion = 43;
    private const int PhysicalUIScaleSchemaVersion = 44;
    private const int MergedConfigSchemaVersion = 45;
    private const int TerrainDepthSchemaVersion = 46;
    private const int FisheyeSettingsSchemaVersion = 47;
    private const int InputAndTextSettingsSchemaVersion = 48;
    private const int PresentationTogglesSchemaVersion = 49; // Additive switches default off; strengths are preserved.

    private static void MigrateSchema31To32(ClientConfig config)
    {
        // Schema 31 predates TerrainSettings.EnableRim (then EnableReliefRim).
        // The field was introduced enabled, so migration must make that intent explicit
        // instead of accepting JsonUtility's CLR default for a missing bool.
        config.Terrain.EnableRim = true;
        config.SchemaVersion = RimSchemaVersion;
    }

    private static void MigrateSchema32To33(ClientConfig config)
    {
        config.SchemaVersion = DistortionStyleSchemaVersion;
    }

    private static void MigrateSchema33To34(ClientConfig config, Action<GraphicsPreset> applyPreset)
    {
        // Шесть ступеней (VeryLow…Ultra) и Custom схлопнуты в две: «Классика» и
        // «Стандарт». Старое число ступени не читается — маппить шесть значений
        // в две пары нечего, и все они переводятся в «Стандарт». Снимок
        // настроек следом выравнивает ReconcileStandardPreset.
        applyPreset(GraphicsPreset.Standard);
        config.SchemaVersion = PresetPairSchemaVersion;
    }

    private static void MigrateSchema34To35(ClientConfig config)
    {
        // Схема 35 вводила стиль свечения. Стиль удалён, шаг только поднимает
        // номер, чтобы цепочка миграций оставалась непрерывной.
        config.SchemaVersion = BloomStyleSchemaVersion;
    }

    public static void Apply(
        ClientConfig config,
        string sourceJson,
        int sourceSchemaVersion,
        bool useAndroidLightingPreset,
        Action<GraphicsPreset> applyPreset)
    {
        int schema = sourceSchemaVersion;

        // Before schema 19 visual controls were retired. Schemas 19–21 stored
        // them flat in the root and need an explicit mapping into current
        // sections before the remaining version steps run.
        if (schema < 22)
        {
            if (schema < 19)
            {
                config.Terrain = new TerrainSettings();
                config.Effects = new EffectSettings();
            }
            else
            {
                MigrateFlatVisualsToSections(config, sourceJson);
            }

            schema = 22;
        }

        if (schema < 26)
        {
            config.Display.PixelSampling = PixelSamplingMode.SmoothFiltered;
            schema = 26;
        }

        if (schema < 28)
        {
            if (Mathf.Approximately(config.Interface.UIScale, 1f) &&
                UIScalePolicy.IsRetinaOrHighDpi)
            {
                config.Interface.UIScale = UIScalePolicy.RetinaDefaultScale;
            }

            schema = 28;
        }

        // Schemas 29 and 30 removed SDR gamma/tone mapping. Those fields no
        // longer exist in the runtime model and are ignored by JsonUtility.
        if (schema < 31)
        {
            config.Display.HDRSwitchPending = false;
            schema = 31;
        }

        if (schema == RimSourceSchemaVersion)
        {
            MigrateSchema31To32(config);
            schema = RimSchemaVersion;
        }
        else if (schema < RimSourceSchemaVersion)
        {
            // Older terrain schemas receive the authored default for this new
            // boolean instead of JsonUtility's implicit false value.
            config.Terrain.EnableRim = true;
            schema = RimSchemaVersion;
        }

        if (schema == RimSchemaVersion)
        {
            MigrateSchema32To33(config);
            schema = DistortionStyleSchemaVersion;
        }

        if (schema == DistortionStyleSchemaVersion)
        {
            MigrateSchema33To34(config, applyPreset);
            schema = PresetPairSchemaVersion;
        }

        if (schema == PresetPairSchemaVersion)
        {
            MigrateSchema34To35(config);
            schema = BloomStyleSchemaVersion;
        }

        // 35 -> 36: перебиндиваемые клавиши InterfaceSettings.Key*. В JSON их
        // нет, FromJsonOverwrite оставляет дефолты из инициализаторов модели,
        // поэтому миграция только фиксирует новую версию схемы.
        if (schema == BloomStyleSchemaVersion)
        {
            schema = KeyBindingsSchemaVersion;
        }

        // Schema 37 is retained for existing config files.
        if (schema == KeyBindingsSchemaVersion)
        {
            schema = SchemaVersion37;
        }

        // 37 -> 38: DisplaySettings.HDRSwitchPending. Existing schema-37
        // files predate the safe-start marker; FromJsonOverwrite leaves the
        // default false when the field is absent and preserves it when present.
        if (schema == SchemaVersion37)
        {
            schema = HdrSwitchPendingSchemaVersion;
        }

        // 38 -> 39: DisplaySettings.PeakBrightnessFromDisplay. Поле аддитивное:
        // FromJsonOverwrite оставляет дефолт true, и сохранённый пик заменяется
        // пиком дисплея — до этой схемы автоопределения не было вовсе.
        if (schema == HdrSwitchPendingSchemaVersion)
        {
            schema = PeakBrightnessFromDisplaySchemaVersion;
        }

        // 39 -> 40: поля террейна переименованы — EnableReliefRim → EnableRim,
        // *Emission* → *Glow*, PulseSpeedScale → BlinkingSpeedScale. Схемы
        // 22–39 хранили их под старыми именами в секции Terrain; старее 22 —
        // плоско, их уже перенёс MigrateFlatVisualsToSections.
        if (schema == PeakBrightnessFromDisplaySchemaVersion)
        {
            if (sourceSchemaVersion >= 22)
            {
                MigrateTerrainNames(config, sourceJson);
            }

            schema = TerrainNamesSchemaVersion;
        }

        if (schema == TerrainNamesSchemaVersion)
        {
            // Схема заведена под плотностный масштаб интерфейса на телефоне;
            // от него отказались (см. UIScalePolicy), поэтому здесь остаётся
            // прежнее правило: дефолт 1.0 поднимается до рекомендованного для
            // этого экрана. Масштаб, выставленный игроком, не трогаем.
            if (UIScalePolicy.IsHandheld &&
                (Mathf.Approximately(config.Interface.UIScale, UIScalePolicy.StandardDefaultScale) ||
                 Mathf.Approximately(config.Interface.UIScale, UIScalePolicy.RetinaDefaultScale)))
            {
                config.Interface.UIScale = UIScalePolicy.RecommendedDefaultScale;
            }

            schema = HandheldUIScaleSchemaVersion;
        }

        if (schema == HandheldUIScaleSchemaVersion)
        {
            // Схема 41 поднимала масштаб на карманном устройстве до плотности
            // экрана (2.5) и уводила логический холст до 960×432 — макет на нём
            // рассыпался. Возвращаем рекомендованное значение; точное совпадение
            // с верхом диапазона и есть то самое число.
            if (UIScalePolicy.IsHandheld &&
                Mathf.Approximately(config.Interface.UIScale, UIScalePolicy.UIScaleMax))
            {
                config.Interface.UIScale = UIScalePolicy.RecommendedDefaultScale;
            }

            schema = HandheldUIScaleCorrectionSchemaVersion;
        }

        if (schema == HandheldUIScaleCorrectionSchemaVersion)
        {
            // Android's shipped default used Classic, whose contract disables
            // radiance transport entirely. Upgrade only the untouched Classic
            // selection; explicit Standard selections remain unchanged.
            if (useAndroidLightingPreset)
            {
                applyPreset(GraphicsPreset.Standard);
            }

            schema = AndroidLightingPresetSchemaVersion;
        }

        if (schema == AndroidLightingPresetSchemaVersion)
        {
            // На мобильной платформе панель теперь меряется в dp, и UIScale —
            // множитель поверх физического размера, а не поверх опорных
            // 1920×1080. Старые 1.0/1.35/2.5 значили совсем другой размер,
            // поэтому сохранённое число сбрасывается к единице.
            if (UIScalePolicy.UsesPhysicalScale)
            {
                config.Interface.UIScale = UIScalePolicy.RecommendedDefaultScale;
            }

            schema = PhysicalUIScaleSchemaVersion;
        }

        // Upstream used schema 41 for ApiPort and 42 for chat layout while
        // this branch used 41–44 for mobile UI. Loading into initialized
        // defaults supplies absent additive fields without replacing saved ones.
        if (schema == PhysicalUIScaleSchemaVersion)
        {
            schema = MergedConfigSchemaVersion;
        }

        if (schema == MergedConfigSchemaVersion)
        {
            // 45 -> 46: additive tunnel-wall settings. Loading onto initialized
            // defaults supplies 16 pixels / 35% darkening when absent, while
            // preserving explicitly saved values, including zero depth.
            schema = TerrainDepthSchemaVersion;
        }

        if (schema == TerrainDepthSchemaVersion)
        {
            // Сила рыбьего глаза — эффект удалён, шаг ничего не переносит.
            schema = FisheyeSettingsSchemaVersion;
        }

        if (schema == FisheyeSettingsSchemaVersion)
        {
            InputAndTextConfigMigration.Apply(config.Interface, sourceJson);
            schema = InputAndTextSettingsSchemaVersion;
        }

        if (schema == InputAndTextSettingsSchemaVersion)
        {
            schema = PresentationTogglesSchemaVersion;
        }

        if (schema == PresentationTogglesSchemaVersion)
        {
            if (string.Equals(config.Interface.KeyFastMove, "LeftCtrl", StringComparison.OrdinalIgnoreCase))
            {
                config.Interface.KeyFastMove = InterfaceSettings.DefaultKeyFastMove;
            }

            schema = 50;
        }

        if (schema == 50)
        {
            if (string.Equals(config.Interface.KeyFastMove, "C", StringComparison.OrdinalIgnoreCase))
            {
                config.Interface.KeyFastMove = InterfaceSettings.DefaultKeyFastMove;
            }

            schema = 51;
        }

        if (schema == 51)
        {
            // Помощь в движении появилась включённой: загрузка поверх
            // инициализированных дефолтов даёт true для отсутствующего поля,
            // но намерение фиксируется явно, а не через поведение JsonUtility.
            if (!sourceJson.Contains("\"MovementAssist\"", StringComparison.Ordinal))
            {
                config.Interface.MovementAssist = InterfaceSettings.DefaultMovementAssist;
            }

            schema = 52;
        }

        if (schema == 52)
        {
            // Режим для дальтоников появился выключенным
            config.Effects.ColorVision = ColorVisionMode.Off;
            schema = 53;
        }

        if (schema == 53)
        {
            // Уровень чёрного и яркость SDR появились нейтральными: картинка не меняется до калибровки
            config.Display.BlackLevel = DisplaySettings.DefaultBlackLevel;
            config.Display.Brightness = DisplaySettings.DefaultBrightness;
            schema = ClientConfig.CurrentSchemaVersion;
        }

        config.SchemaVersion = schema;
    }

    private static void MigrateFlatVisualsToSections(ClientConfig config, string sourceJson)
    {
        LegacySchema21? legacy = JsonUtility.FromJson<LegacySchema21>(sourceJson);
        if (legacy == null)
        {
            return;
        }

        config.Terrain = new TerrainSettings
        {
            FlowScale = legacy.TerrainFlowScale,
            ShimmerSpeedScale = legacy.TerrainShimmerSpeedScale,
            BlinkingSpeedScale = legacy.TerrainPulseSpeedScale,
            ShimmerColor = legacy.TerrainShimmerColor,
            EnableDistortion = legacy.EnableTerrainDistortion,
            TransitGlowColor = legacy.TransitEmissionColor,
            TransitGlowStrength = legacy.TransitEmissionStrength,
            PerspectiveGlowColor = legacy.PerspectiveEmissionColor,
            PerspectiveGlowStrength = legacy.PerspectiveEmissionStrength,
            SurfaceOccupancy = legacy.SurfaceOccupancy,
        };
        config.Effects = new EffectSettings
        {
            BloomEnabled = legacy.BloomEnabled,
            VignetteEnabled = legacy.VignetteEnabled,
            EigengrauEnabled = legacy.FilmGrainEnabled,
        };
        SettingSchema.Clamp(config.Terrain);
        SettingSchema.Clamp(config.Effects);
    }

    private static void MigrateTerrainNames(ClientConfig config, string sourceJson)
    {
        LegacyTerrainNames39? legacy = JsonUtility.FromJson<LegacyTerrainNames39>(sourceJson);
        if (legacy?.Terrain == null)
        {
            return;
        }

        LegacyTerrain39 terrain = legacy.Terrain;
        if (Has(sourceJson, nameof(LegacyTerrain39.EnableReliefRim)))
        {
            config.Terrain.EnableRim = terrain.EnableReliefRim;
        }

        if (Has(sourceJson, nameof(LegacyTerrain39.PulseSpeedScale)))
        {
            config.Terrain.BlinkingSpeedScale = terrain.PulseSpeedScale;
        }

        if (Has(sourceJson, nameof(LegacyTerrain39.TransitEmissionColor)))
        {
            config.Terrain.TransitGlowColor = terrain.TransitEmissionColor;
        }

        if (Has(sourceJson, nameof(LegacyTerrain39.TransitEmissionStrength)))
        {
            config.Terrain.TransitGlowStrength = terrain.TransitEmissionStrength;
        }

        if (Has(sourceJson, nameof(LegacyTerrain39.PerspectiveEmissionColor)))
        {
            config.Terrain.PerspectiveGlowColor = terrain.PerspectiveEmissionColor;
        }

        if (Has(sourceJson, nameof(LegacyTerrain39.PerspectiveEmissionStrength)))
        {
            config.Terrain.PerspectiveGlowStrength = terrain.PerspectiveEmissionStrength;
        }

        SettingSchema.Clamp(config.Terrain);

        static bool Has(string json, string field) =>
            json.IndexOf($"\"{field}\"", StringComparison.Ordinal) >= 0;
    }

    [Serializable]
    private sealed class LegacyTerrainNames39
    {
        public LegacyTerrain39? Terrain = default;
    }

    [Serializable]
    private sealed class LegacyTerrain39
    {
        public bool EnableReliefRim = default;
        public float PulseSpeedScale = default;
        public Color TransitEmissionColor = default;
        public float TransitEmissionStrength = default;
        public Color PerspectiveEmissionColor = default;
        public float PerspectiveEmissionStrength = default;
    }

    [Serializable]
    private sealed class LegacySchema21
    {
        // JsonUtility fills these public fields reflectively, which the C#
        // compiler cannot see. Explicit defaults keep that contract warning-free.
        public Vector2 TerrainFlowScale = default;
        public float TerrainShimmerSpeedScale = default;
        public float TerrainPulseSpeedScale = default;
        public Color TerrainShimmerColor = default;
        public bool EnableTerrainDistortion = default;
        public Color TransitEmissionColor = default;
        public float TransitEmissionStrength = default;
        public Color PerspectiveEmissionColor = default;
        public float PerspectiveEmissionStrength = default;
        public float SurfaceOccupancy = default;
        public bool BloomEnabled = default;
        public bool VignetteEnabled = default;
        public bool FilmGrainEnabled = default;
    }

}

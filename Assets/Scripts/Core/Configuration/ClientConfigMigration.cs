#nullable enable

using System;
using Fodinae.Rendering;
using UnityEngine;

namespace Fodinae.Core;

/// <summary>
/// Приводит сохранённый конфиг к текущей схеме.
/// </summary>
/// <remarks>
/// ЧТО ВЫЧИЩЕНО И ПОЧЕМУ. В ладдере было пятнадцать шагов, из которых четыре
/// (5, 11, 17, 18) не делали ничего, кроме инкремента счётчика, а ещё четыре
/// (2, 4, 6, 8) переписывали поля вида, которые шаги 19-21 всё равно
/// перезаписывали целиком. То есть весь хвост ниже 19 был мёртвым: любой файл
/// старее доезжал до 21 с теми же значениями, что и файл схемы 19.
///
/// Ладдер как механизм остался: он и есть то место, где смена авторского
/// значения по умолчанию выражается явно. Раньше эту роль дублировал
/// <c>ProjectDefaultsHash</c> — сверка хэша ассета, которая делала то же самое
/// молча и на каждой загрузке. Ассета больше нет, значения живут в коде, и
/// единственным способом сказать «сбросить вид» стал новый шаг схемы.
/// </remarks>
internal sealed class ClientConfigMigration(GraphicsQualityProfile graphicsQualityProfile)
{
    private readonly GraphicsQualityProfile _graphicsQualityProfile = graphicsQualityProfile ??
        throw new ArgumentNullException(nameof(graphicsQualityProfile));

    /// <param name="rawJson">
    /// Исходный текст файла. Нужен потому, что до схемы 22 поля вида лежали
    /// плоско в корне и в типизированный <see cref="ClientConfig"/> не
    /// попадают: их читает <see cref="ClientConfigLegacySchema21"/>.
    /// </param>
    public bool Migrate(ClientConfig config, string rawJson)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        bool migrated = false;
        if (config.SchemaVersion < 9)
        {
            // Всё, что старее девятой схемы, не имело осмысленного пресета
            // графики: там лежал legacy-индекс качества 0..3.
            GraphicsPreset previousPreset = ClientConfigDefaults.ConvertLegacyGraphicsQuality(
                Mathf.Clamp((int)config.GraphicsPreset, 0, 3));
            config.GraphicsQualitySettings = _graphicsQualityProfile.Get(previousPreset);
            config.GraphicsPreset = GraphicsPreset.Custom;
            config.SchemaVersion = 9;
            migrated = true;
        }

        if (config.SchemaVersion < 12)
        {
            config.GraphicsQualitySettings.LightingMaximumTextureDimension =
                Mathf.Max(
                    config.GraphicsQualitySettings.LightingMaximumTextureDimension,
                    GraphicsQualitySettings.MinimumLightingTextureDimension);
            config.SchemaVersion = 12;
            migrated = true;
        }

        if (config.SchemaVersion < 22)
        {
            MigrateFlatVisualsToSections(config, rawJson);
            config.SchemaVersion = 22;
            migrated = true;
        }

        if (config.SchemaVersion < 23)
        {
            // Схема 23: сброс калибровки SDR-гаммы и белой точки тонмаппинга к
            // каноническим авторским значениям (2.2 и 1.0) при деградации до минимума.
            if (config.Display != null &&
                Mathf.Approximately(config.Display.Gamma, DisplaySettings.GammaMin))
            {
                config.Display.Gamma = DisplaySettings.DefaultGamma;
            }

            if (config.PostProcess != null && config.PostProcess.ToneMappingWhitePoint > 1.5f)
            {
                config.PostProcess.ToneMappingWhitePoint =
                    PostProcessSettings.DefaultToneMappingWhitePoint;
            }

            config.SchemaVersion = 23;
            migrated = true;
        }

        if (config.SchemaVersion < 24)
        {
            // Схема 24: сброс унаследованной эмиссии (8x компенсация старого оценщика)
            // и завышенного фонового света к авторским физически обоснованным значениям (2.0 и 0.08).
            if (config.Lighting != null)
            {
                if (config.Lighting.EmissionScale > 4f)
                {
                    config.Lighting.EmissionScale = WorldLightingSettings.DefaultEmissionScale;
                }

                if (config.Lighting.AmbientIntensity > 0.5f)
                {
                    config.Lighting.AmbientIntensity = WorldLightingSettings.DefaultAmbientIntensity;
                }
            }

            config.SchemaVersion = 24;
            migrated = true;
        }

        if (config.SchemaVersion < 25)
        {
            // Схема 25: добавлен тумблер сжатия динамического диапазона (ToneMappingEnabled).
            if (config.Effects != null)
            {
                config.Effects.ToneMappingEnabled = true;
            }

            config.SchemaVersion = 25;
            migrated = true;
        }

        if (GraphicsQualityProfile.IsStandard(config.GraphicsPreset))
        {
            GraphicsQualitySettings standardSettings =
                _graphicsQualityProfile.Get(config.GraphicsPreset);
            if (config.GraphicsQualitySettings != standardSettings)
            {
                config.GraphicsQualitySettings = standardSettings;
                migrated = true;
            }
        }

        return migrated;
    }

    /// <summary>
    /// Схема 22: поля вида уезжают из корня в секции Lighting/Terrain/Effects.
    /// </summary>
    /// <remarks>
    /// Значения игрока переносятся, а не сбрасываются: настроенный свет — это
    /// его работа, и терять её при смене формы файла нельзя. Файл старее 19-й
    /// схемы плоского хвоста уже не содержит осмысленных величин постпроцесса
    /// (их удалили вместе с тридцатью пятью ползунками), поэтому там секции
    /// остаются авторскими — ровно то, что делал прежний шаг 19.
    /// </remarks>
    private static void MigrateFlatVisualsToSections(ClientConfig config, string rawJson)
    {
        if (config.SchemaVersion < 19)
        {
            config.Lighting = new WorldLightingSettings();
            config.Terrain = new TerrainSettings();
            config.Effects = new EffectSettings();
            config.PostProcess = new PostProcessSettings();
            return;
        }

        ClientConfigLegacySchema21? legacy =
            JsonUtility.FromJson<ClientConfigLegacySchema21>(rawJson);
        if (legacy == null)
        {
            return;
        }

        config.Lighting = legacy.ToLighting();
        config.Terrain = legacy.ToTerrain();
        config.Effects = legacy.ToEffects();

        // Плоский файл мог хранить значения вне нынешних границ: раньше
        // диапазон проверялся не везде, где записывался. Валидатор после
        // миграции падает на таком значении, поэтому границы применяются здесь,
        // при переносе, — это перенос, а не тихая правка живого конфига.
        SettingSchema.Clamp(config.Lighting);
        SettingSchema.Clamp(config.Terrain);
        SettingSchema.Clamp(config.PostProcess);
    }
}

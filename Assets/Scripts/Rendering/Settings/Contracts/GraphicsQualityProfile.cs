#nullable enable

using System;
using System.IO;
using Kern.Core;
using Kern.Rendering.PostProcessing;
using Kern.World.Lighting.Quality;
using UnityEngine;

namespace Kern.Rendering
{
    /// <summary>
    /// Две неизменяемые ступени качества: «Стандарт» и «Overdrive».
    /// </summary>
    ///
    /// Значения живут кодом, а не ассетом: ступеней ровно две, они неизменяемы, и
    /// правка в инспекторе ничего не добавляла бы, зато расхождение ассета с
    /// кодом пришлось бы ловить вручную.
    public sealed class GraphicsQualityProfile
    {
        private readonly GraphicsQualitySettings _standard;
        private readonly GraphicsQualitySettings _overdrive;

        private GraphicsQualityProfile(
            GraphicsQualitySettings standard,
            GraphicsQualitySettings overdrive)
        {
            _standard = standard;
            _overdrive = overdrive;
        }

        public static GraphicsQualityProfile CreateDefault()
        {
            // Standard keeps contact AO while the radiance transport is off.
            GraphicsQualitySettings withoutLighting = new(
                lightingPixelsPerCell: 4,
                lightingMaximumTextureDimension: 1280,
                lightingMaximumLightCount: 512,
                lightingCascadeAtlasLimit: 1280,
                renderScale: 1f,
                antiAliasing: 0,
                lightingQuality: LightingQualityMode.Off);
            GraphicsQualitySettings withLighting = withoutLighting;
            withLighting.LightingQuality = LightingQualityMode.PerPixel;

            var profile = new GraphicsQualityProfile(withoutLighting, withLighting);
            profile.Validate();
            return profile;
        }

        public GraphicsQualitySettings Get(GraphicsPreset preset)
        {
            GraphicsQualitySettings settings = preset switch
            {
                GraphicsPreset.Standard => _standard,
                GraphicsPreset.Overdrive => _overdrive,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(preset),
                    preset,
                    "Unknown graphics preset."),
            };

            ValidateSettings(settings, preset.ToString());
            return settings;
        }

        public void Validate()
        {
            foreach (GraphicsPreset preset in Enum.GetValues(typeof(GraphicsPreset)))
            {
                _ = Get(preset);
            }
        }

        public static void ValidateSettings(
            GraphicsQualitySettings settings,
            string context)
        {
            // Границы объявлены атрибутами [Range] над самими полями: инспектор
            // ими ограничивает правку профиля, схема по ним проверяет, ползунок
            // из них берёт края. Раньше здесь стояла девятая копия тех же
            // чисел литералами, причём в одном условии через ||, поэтому
            // сообщение об ошибке не называло провинившееся поле — «contain
            // invalid technical values» и ищи сам, какое из восьми.
            if (Array.IndexOf(GraphicsQualitySettings.AntiAliasingSampleCounts, settings.AntiAliasing) < 0)
            {
                throw new InvalidOperationException(
                    $"Graphics quality settings '{context}' request MSAA x{settings.AntiAliasing}; " +
                    $"hardware accepts only {string.Join(", ", GraphicsQualitySettings.AntiAliasingSampleCounts)}.");
            }

            try
            {
                SettingSchema.Validate(settings, typeof(GraphicsQualitySettings));
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidOperationException(
                    $"Graphics quality settings '{context}' are invalid: {ex.Message}",
                    ex);
            }

            if (!Enum.IsDefined(typeof(LightingQualityMode), settings.LightingQuality))
            {
                // A value outside the known tiers would otherwise sail
                // through here (it satisfies every check above) and only
                // fail once the pause menu tries to label it.
                throw new InvalidOperationException(
                    $"Graphics quality settings '{context}' has an undefined " +
                    $"LightingQuality value ({(int)settings.LightingQuality}).");
            }

            // Обе ступени получают контактное AO; только Overdrive считает
            // транспорт света. Поэтому LightingQuality здесь означает именно
            // режим транспорта, а не наличие AO.
            // Раньше здесь стоял замок «Ultra обязан быть per-pixel»; теперь
            // инвариант сильнее: выключенный свет разрешён только «Стандарту»,
            // включённый обязателен «Overdrive».
            if (context == nameof(GraphicsPreset.Standard) &&
                settings.LightingQuality != LightingQualityMode.Off)
            {
                throw new InvalidOperationException(
                    $"Graphics quality settings '{context}' must disable lighting: " +
                    $"'{nameof(GraphicsPreset.Standard)}' is the preset without radiance transport.");
            }

            if (context == nameof(GraphicsPreset.Overdrive) &&
                settings.LightingQuality == LightingQualityMode.Off)
            {
                throw new InvalidOperationException(
                    $"Graphics quality settings '{context}' must enable lighting: " +
                    $"'{nameof(GraphicsPreset.Overdrive)}' is the preset with lighting.");
            }
        }
    }
}

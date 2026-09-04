#nullable enable

using System;
using System.IO;
using Fodinae.Core;
using Fodinae.Rendering.PostProcessing;
using Fodinae.World.Lighting.Quality;
using UnityEngine;

namespace Fodinae.Rendering
{
[CreateAssetMenu(fileName = "GraphicsQualityProfile", menuName = "Fodinae/Graphics Quality Profile")]
    public sealed class GraphicsQualityProfile : ScriptableObject
    {
        public const int StandardPresetCount = (int)GraphicsPreset.Custom;

        [SerializeField]
        private GraphicsQualitySettings _veryLow;
        [SerializeField]
        private GraphicsQualitySettings _low;
        [SerializeField]
        private GraphicsQualitySettings _medium;
        [SerializeField]
        private GraphicsQualitySettings _high;
        [SerializeField]
        private GraphicsQualitySettings _veryHigh;
        [SerializeField]
        private GraphicsQualitySettings _ultra;

        public GraphicsQualitySettings Get(GraphicsPreset preset)
        {
            GraphicsQualitySettings settings = preset switch
            {
                GraphicsPreset.VeryLow => _veryLow,
                GraphicsPreset.Low => _low,
                GraphicsPreset.Medium => _medium,
                GraphicsPreset.High => _high,
                GraphicsPreset.VeryHigh => _veryHigh,
                GraphicsPreset.Ultra => _ultra,
                GraphicsPreset.Custom => throw new ArgumentException(
                    "Custom graphics settings are stored in ClientConfig, not in the immutable profile.",
                    nameof(preset)),
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
            for (int index = 0; index < StandardPresetCount; index++)
            {
                _ = Get((GraphicsPreset)index);
            }
        }

        public static bool IsStandard(GraphicsPreset preset)
        {
            return preset is >= GraphicsPreset.VeryLow and <= GraphicsPreset.Ultra;
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
                // fail once PauseMenu tries to index its 3-entry tier-name
                // array with it - a crash on opening Settings instead of a
                // clear error at load/apply time. Catch it at the same
                // boundary every other enum-typed config field is caught at
                // (compare ClientConfigManager's GraphicsPreset check).
                throw new InvalidOperationException(
                    $"Graphics quality settings '{context}' has an undefined " +
                    $"LightingQuality value ({(int)settings.LightingQuality}).");
            }

            if (context == nameof(GraphicsPreset.Ultra) &&
                settings.LightingQuality != LightingQualityMode.PerPixel)
            {
                throw new InvalidOperationException(
                    $"Graphics quality settings '{context}' must use {nameof(LightingQualityMode.PerPixel)} " +
                    "lighting - Ultra is locked to it.");
            }
        }
    }
}

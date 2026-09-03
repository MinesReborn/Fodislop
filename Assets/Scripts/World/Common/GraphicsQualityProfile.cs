#nullable enable

using System;
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
            if (settings.LightingMinimumPixelsPerCell < 1 ||
                settings.LightingMaximumTextureDimension <
                    GraphicsQualitySettings.MinimumLightingTextureDimension ||
                settings.LightingMaximumLightCount < 1 ||
                settings.LightingMaximumRaySteps < 1 ||
                settings.LightingUpdatesPerSecond <= 0f ||
                settings.LightingCascadeAtlasLimit < 128 ||
                settings.RenderScale is < 0.5f or > 1f ||
                settings.AntiAliasing is < 0 or > 8)
            {
                throw new InvalidOperationException(
                    $"Graphics quality settings '{context}' contain invalid technical values.");
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

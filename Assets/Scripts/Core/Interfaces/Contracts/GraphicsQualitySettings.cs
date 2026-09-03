#nullable enable

using System;
using Fodinae.Rendering.PostProcessing;
using Fodinae.World.Lighting.Quality;
using UnityEngine;
using UnityEngine.Serialization;

namespace Fodinae.Rendering
{

    public enum GraphicsPreset
    {
        VeryLow,
        Low,
        Medium,
        High,
        VeryHigh,
        Ultra,
        Custom,
    }

    [Serializable]
    public struct GraphicsQualitySettings : IEquatable<GraphicsQualitySettings>
    {
        public const int MinimumLightingTextureDimension = 256;

        [FormerlySerializedAs("LightingPixelsPerCell")]
        [Min(1)]
        [Tooltip("Нижняя граница lighting-пикселей на клетку. Фактическое разрешение считается от render target базовой камеры.")]
        public int LightingMinimumPixelsPerCell;
        [Min(MinimumLightingTextureDimension)]
        [Tooltip("Максимальный размер lighting field в пикселях.")]
        public int LightingMaximumTextureDimension;
        [Min(1)]
        [Tooltip("Максимальное число dynamic light sources, загружаемых в GPU buffer.")]
        public int LightingMaximumLightCount;
        [Min(1)]
        [Tooltip("Максимальное число шагов одного cascade interval.")]
        public int LightingMaximumRaySteps;
        [Min(1f)]
        [Tooltip("Максимальная частота lighting solve. Изменение геометрии всё равно обрабатывается сразу.")]
        public float LightingUpdatesPerSecond;
        [Min(128)]
        [Tooltip("Бюджет radiance cascade atlas.")]
        public int LightingCascadeAtlasLimit;
        [Range(0.5f, 1f)]
        [Tooltip("URP render scale для данного quality tier.")]
        public float RenderScale;
        [Range(0, 8)]
        [Tooltip("MSAA sample count для данного quality tier.")]
        public int AntiAliasing;
        [Tooltip("Off/PerBlock/PerPixel режим освещения. Ultra всегда принудительно PerPixel.")]
        public LightingQualityMode LightingQuality;

        public GraphicsQualitySettings(
            int lightingPixelsPerCell,
            int lightingMaximumTextureDimension,
            int lightingMaximumLightCount,
            int lightingMaximumRaySteps,
            float lightingUpdatesPerSecond,
            int lightingCascadeAtlasLimit,
            float renderScale,
            int antiAliasing,
            LightingQualityMode lightingQuality = LightingQualityMode.PerBlock)
        {
            LightingMinimumPixelsPerCell = lightingPixelsPerCell;
            LightingMaximumTextureDimension = lightingMaximumTextureDimension;
            LightingMaximumLightCount = lightingMaximumLightCount;
            LightingMaximumRaySteps = lightingMaximumRaySteps;
            LightingUpdatesPerSecond = lightingUpdatesPerSecond;
            LightingCascadeAtlasLimit = lightingCascadeAtlasLimit;
            RenderScale = renderScale;
            AntiAliasing = antiAliasing;
            LightingQuality = lightingQuality;
        }

        public readonly bool Equals(GraphicsQualitySettings other)
        {
            return LightingMinimumPixelsPerCell == other.LightingMinimumPixelsPerCell &&
                LightingMaximumTextureDimension == other.LightingMaximumTextureDimension &&
                LightingMaximumLightCount == other.LightingMaximumLightCount &&
                LightingMaximumRaySteps == other.LightingMaximumRaySteps &&
                LightingUpdatesPerSecond.Equals(other.LightingUpdatesPerSecond) &&
                LightingCascadeAtlasLimit == other.LightingCascadeAtlasLimit &&
                RenderScale.Equals(other.RenderScale) &&
                AntiAliasing == other.AntiAliasing &&
                LightingQuality == other.LightingQuality;
        }

        public override readonly bool Equals(object? obj)
        {
            return obj is GraphicsQualitySettings other && Equals(other);
        }

        public override readonly int GetHashCode()
        {
            return CalculateHash(this);
        }

        private static int CalculateHash(GraphicsQualitySettings settings)
        {
            HashCode hash = default;
            hash.Add(settings.LightingMinimumPixelsPerCell);
            hash.Add(settings.LightingMaximumTextureDimension);
            hash.Add(settings.LightingMaximumLightCount);
            hash.Add(settings.LightingMaximumRaySteps);
            hash.Add(settings.LightingUpdatesPerSecond);
            hash.Add(settings.LightingCascadeAtlasLimit);
            hash.Add(settings.RenderScale);
            hash.Add(settings.AntiAliasing);
            hash.Add(settings.LightingQuality);
            return hash.ToHashCode();
        }

        public static bool operator ==(
            GraphicsQualitySettings left,
            GraphicsQualitySettings right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            GraphicsQualitySettings left,
            GraphicsQualitySettings right)
        {
            return !left.Equals(right);
        }
    }
}

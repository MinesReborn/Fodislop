#nullable enable

using System;
using Fodinae.Core;
using Fodinae.World.Lighting.Quality;
using UnityEngine;
using UnityEngine.Serialization;

namespace Fodinae.Rendering
{

    public enum GraphicsPreset
    {
        [Fodinae.Core.SettingLabel("settings.preset.very_low")]
        VeryLow,
        [Fodinae.Core.SettingLabel("settings.preset.low")]
        Low,
        [Fodinae.Core.SettingLabel("settings.preset.medium")]
        Medium,
        [Fodinae.Core.SettingLabel("settings.preset.high")]
        High,
        [Fodinae.Core.SettingLabel("settings.preset.very_high")]
        VeryHigh,
        [Fodinae.Core.SettingLabel("settings.preset.ultra")]
        Ultra,
        [Fodinae.Core.SettingLabel("settings.preset.custom")]
        Custom,
    }

    [Serializable]
    public struct GraphicsQualitySettings : IEquatable<GraphicsQualitySettings>
    {
        public const int MinimumLightingTextureDimension = 256;

        /// <summary>
        /// Допустимые значения MSAA. Ноль — выключено.
        /// </summary>
        /// <remarks>
        /// Аппаратный MSAA принимает только степени двойки, и [Range(0, 8)]
        /// над полем этого не выражает: тройка проходила проверку и уезжала
        /// в <c>UniversalRenderPipelineAsset.msaaSampleCount</c>, где смысла
        /// не имеет. Диапазон отвечает за края, набор — за то, что внутри.
        ///
        /// Отсюда же берёт значения кнопка в меню: два списка допустимых
        /// значений разошлись бы при первом же изменении.
        ///
        /// ЧЕГО ЭТА НАСТРОЙКА НЕ ДЕЛАЕТ. MSAA сглаживает края геометрии.
        /// Здесь весь мир — спрайты на полностью покрытых квадратах, край
        /// задаётся альфой текстуры, и сглаживать нечего. Ступенчатость и
        /// муар в этой игре идут от несовпадения сетки текселей с сеткой
        /// экрана и лечатся привязкой камеры (см. PixelGrid), а не здесь.
        /// Во всех шести авторских пресетах значение — ноль.
        /// </remarks>
        public static readonly int[] AntiAliasingSampleCounts = [0, 2, 4, 8];

        [FormerlySerializedAs("LightingPixelsPerCell")]
        [Range(1, 8)]
        [SettingLabel("settings.lighting.density")]
        [Tooltip("Нижняя граница lighting-пикселей на клетку. Фактическое разрешение считается от render target базовой камеры.")]
        [SettingConsumer(SettingConsumerTarget.LightingEngine, "LightingEngine field allocation")]
        public int LightingMinimumPixelsPerCell;

        [Range(MinimumLightingTextureDimension, 4096)]
        [SettingLabel("settings.lighting.max_size")]
        [Tooltip("Максимальный размер lighting field в пикселях.")]
        [SettingConsumer(SettingConsumerTarget.LightingEngine, "LightingEngine field allocation")]
        public int LightingMaximumTextureDimension;

        [Range(1, 2048)]
        [SettingLabel("settings.lighting.max_dynamic_lights")]
        [Tooltip("Максимальное число dynamic light sources, загружаемых в GPU buffer.")]
        [SettingConsumer(SettingConsumerTarget.LightingEngine, "LightingEngine GPU buffer capacity")]
        public int LightingMaximumLightCount;

        [Range(1, 128)]
        [SettingLabel("settings.lighting.cascade_steps")]
        [Tooltip("Максимальное число шагов одного cascade interval.")]
        [SettingConsumer(SettingConsumerTarget.LightingEngine, "LightingEngine compute shader interval step limit")]
        public int LightingMaximumRaySteps;

        [Range(1f, ProjectRuntimeContracts.RuntimeLimits.MaximumLightingUpdatesPerSecond)]
        [SettingLabel("settings.lighting.solve_rate")]
        [Tooltip("Максимальная частота lighting solve. Изменение геометрии всё равно обрабатывается сразу.")]
        [SettingConsumer(SettingConsumerTarget.LightingEngine, "LightingEngine compute update rate")]
        public float LightingUpdatesPerSecond;

        [Range(128, 4096)]
        [SettingLabel("settings.lighting.atlas_size")]
        [Tooltip("Бюджет radiance cascade atlas.")]
        [SettingConsumer(SettingConsumerTarget.LightingEngine, "LightingEngine cascade atlas limit")]
        public int LightingCascadeAtlasLimit;

        [Range(0.5f, 1f)]
        [SettingLabel("settings.graphics.render_scale")]
        [Tooltip("URP render scale для данного quality tier.")]
        [SettingConsumer(SettingConsumerTarget.LightingEngine, "LightingEngine.ApplyUnityRenderingSettings -> UniversalRenderPipelineAsset.renderScale")]
        public float RenderScale;

        [Range(0, 8)]
        [SettingLabel("settings.graphics.anti_aliasing")]
        [Tooltip("MSAA sample count для данного quality tier.")]
        [SettingConsumer(SettingConsumerTarget.LightingEngine, "LightingEngine.ApplyUnityRenderingSettings -> UniversalRenderPipelineAsset.msaaSampleCount")]
        public int AntiAliasing;

        [SettingUnbounded("Режим освещения — перечисление; проверяется на определённость.")]
        [Tooltip("Off/PerBlock/PerPixel режим освещения. Ultra всегда принудительно PerPixel.")]
        [SettingConsumer(SettingConsumerTarget.LightingEngine, "LightingQualityResolver -> LightingEngine._lightingQualityMode")]
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

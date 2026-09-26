#nullable enable

using System;
using Kern.Core;
using Kern.World.Lighting.Quality;
using UnityEngine;

namespace Kern.Rendering;

// Ступеней ровно две, и они неизменяемы. Отличие одно — освещение:
// «Стандарт» оставляет только контактное AO, «Overdrive» включает транспорт света.
public enum GraphicsPreset
{
    [Kern.Core.SettingLabel("settings.preset.standard")]
    Standard,
    [Kern.Core.SettingLabel("settings.preset.overdrive")]
    Overdrive,
}

[Serializable]
public struct GraphicsQualitySettings : IEquatable<GraphicsQualitySettings>
{
    public const int MinimumLightingTextureDimension = 256;

    public static readonly int[] AntiAliasingSampleCounts = [0, 2, 4, 8];

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

    [Range(128, 4096)]
    [SettingLabel("settings.lighting.atlas_size")]
    [Tooltip("Бюджет radiance cascade atlas.")]
    [SettingConsumer(SettingConsumerTarget.LightingEngine, "LightingEngine cascade atlas limit")]
    public int LightingCascadeAtlasLimit;

    [Range(0.5f, 1f)]
    [SettingLabel("settings.graphics.render_scale")]
    [Tooltip("URP render scale для данного quality tier.")]
    [SettingConsumer(SettingConsumerTarget.LightingEngine, "LightingUnityQualityApplier.ApplyRenderingSettings -> UniversalRenderPipelineAsset.renderScale")]
    public float RenderScale;

    [Range(0, 8)]
    [SettingLabel("settings.graphics.anti_aliasing")]
    [Tooltip("MSAA sample count для данного quality tier.")]
    [SettingConsumer(SettingConsumerTarget.LightingEngine, "LightingUnityQualityApplier.ApplyRenderingSettings -> UniversalRenderPipelineAsset.msaaSampleCount")]
    public int AntiAliasing;

    [SettingUnbounded("Режим освещения — перечисление; проверяется на определённость.")]
    [Tooltip("Режим транспорта света: «Стандарт» оставляет контактное AO, «Overdrive» считает свет попиксельно.")]
    [SettingConsumer(SettingConsumerTarget.LightingEngine, "LightingQualityController.QualityMode")]
    public LightingQualityMode LightingQuality;

    public GraphicsQualitySettings(
        int lightingPixelsPerCell,
        int lightingMaximumTextureDimension,
        int lightingMaximumLightCount,
        int lightingCascadeAtlasLimit,
        float renderScale,
        int antiAliasing,
        LightingQualityMode lightingQuality = LightingQualityMode.Off)
    {
        LightingMinimumPixelsPerCell = lightingPixelsPerCell;
        LightingMaximumTextureDimension = lightingMaximumTextureDimension;
        LightingMaximumLightCount = lightingMaximumLightCount;
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

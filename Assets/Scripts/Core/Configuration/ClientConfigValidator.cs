#nullable enable

using System;
using System.IO;
using Fodinae.Core.Interfaces;
using Fodinae.Rendering;
using Fodinae.World.Lighting;
using UnityEngine;

namespace Fodinae.Core;

internal sealed class ClientConfigValidator(
    IProjectDefaults projectDefaults,
    GraphicsQualityProfile graphicsQualityProfile)
{
    private readonly IProjectDefaults _projectDefaults = projectDefaults ??
        throw new ArgumentNullException(nameof(projectDefaults));
    private readonly GraphicsQualityProfile _graphicsQualityProfile = graphicsQualityProfile ??
        throw new ArgumentNullException(nameof(graphicsQualityProfile));

    /// <summary>
    /// Проверяет persisted данные без неявной подстановки defaults.
    /// </summary>
    public void Validate(ClientConfig config)
    {
        if (config.SchemaVersion != ClientConfig.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported client config schema {config.SchemaVersion}; " +
                $"expected {ClientConfig.CurrentSchemaVersion}.");
        }

        if (!string.Equals(
                config.ProjectDefaultsHash,
                _projectDefaults.ContentHash,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Client config ProjectDefaultsHash does not match the active ProjectDefaults snapshot.");
        }

        AudioSettings audio = config.Audio ??
            throw new InvalidDataException("Audio settings are missing.");
        DisplaySettings display = config.Display ??
            throw new InvalidDataException("Display settings are missing.");
        InterfaceSettings interfaceSettings = config.Interface ??
            throw new InvalidDataException("Interface settings are missing.");
        AccessibilitySettings accessibility = config.Accessibility ??
            throw new InvalidDataException("Accessibility settings are missing.");
        ConnectionSettings connection = config.Connection ??
            throw new InvalidDataException("Connection settings are missing.");
        ValidateFloat(audio.MasterVolume, 0f, 1f, nameof(audio.MasterVolume));
        ValidateFloat(audio.SfxVolume, 0f, 1f, nameof(audio.SfxVolume));
        ValidateFloat(audio.MusicVolume, 0f, 1f, nameof(audio.MusicVolume));
        ValidateFloat(audio.AmbienceVolume, 0f, 1f, nameof(audio.AmbienceVolume));
        ValidateFloat(audio.VoiceVolume, 0f, 1f, nameof(audio.VoiceVolume));
        ValidateFloat(audio.UIVolume, 0f, 1f, nameof(audio.UIVolume));
        ValidateFloat(interfaceSettings.UIScale, 0.5f, 2f, nameof(interfaceSettings.UIScale));
        ValidateGeneralSettings(config);
        if (!Enum.IsDefined(typeof(GraphicsPreset), config.GraphicsPreset))
        {
            throw new InvalidDataException(
                $"Unknown graphics preset value '{config.GraphicsPreset}'.");
        }

        try
        {
            GraphicsQualityProfile.ValidateSettings(
                config.GraphicsQualitySettings,
                config.GraphicsPreset.ToString());
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidDataException(
                "Client graphics quality settings are invalid.",
                ex);
        }

        if (GraphicsQualityProfile.IsStandard(config.GraphicsPreset) &&
            config.GraphicsQualitySettings != _graphicsQualityProfile.Get(config.GraphicsPreset))
        {
            throw new InvalidDataException(
                $"Standard graphics preset '{config.GraphicsPreset}' was mutated in client config.");
        }

        if (GraphicsQualityProfile.IsStandard(config.GraphicsPreset) &&
            !HasStandardGraphicsValues(config))
        {
            throw new InvalidDataException(
                $"Standard graphics preset '{config.GraphicsPreset}' contains customized visual values. " +
                "Mark the preset as Custom before changing graphics settings.");
        }

        ValidateFloat(config.AmbientIntensity, 0f, 1f, nameof(config.AmbientIntensity));
        ValidateFloat(config.EmissionScale, 0.1f, 8f, nameof(config.EmissionScale));
        ValidateColor(config.AmbientColor, nameof(config.AmbientColor));
        ValidateColor(config.EmptyExtinctionRgb, nameof(config.EmptyExtinctionRgb));
        ValidateColor(config.SolidExtinctionRgb, nameof(config.SolidExtinctionRgb));
        ValidateFloat(config.EmptyExtinctionMultiplier, 0f, 2f, nameof(config.EmptyExtinctionMultiplier));
        ValidateFloat(config.SolidExtinctionMultiplier, 0.25f, 2f, nameof(config.SolidExtinctionMultiplier));
        ValidateFloat(config.BounceStrength, 0f, 1f, nameof(config.BounceStrength));
        ValidateFloat(config.AmbientOcclusionRadiusCells, 0.5f, 8f, nameof(config.AmbientOcclusionRadiusCells));
        ValidateFloat(config.AmbientOcclusionStrength, 0.1f, 8f, nameof(config.AmbientOcclusionStrength));
        ValidateFloat(config.MaximumLightMultiplier, 0.25f, LightingConfigLimits.MaximumLightMultiplier, nameof(config.MaximumLightMultiplier));
        ValidateFloat(config.TransmittanceDebugDistanceCells, 2f, 32f, nameof(config.TransmittanceDebugDistanceCells));
        ValidateFloat(config.MinimumTransmission, 0.0001f, 0.1f, nameof(config.MinimumTransmission));
        ValidateInt(config.LightSafeBorder, 0, 8, nameof(config.LightSafeBorder));
        ValidateFloat(config.DynamicLightIntensity, 0f, 4f, nameof(config.DynamicLightIntensity));
        ValidateColor(config.DynamicLightColor, nameof(config.DynamicLightColor));
        ValidateFloat(config.DynamicLightUpdatesPerSecond, 1f, LightingConfigLimits.DynamicLightUpdatesPerSecond, nameof(config.DynamicLightUpdatesPerSecond));
        ValidateFloat(config.TerrainFlowScale.x, 0.001f, 1024f, nameof(config.TerrainFlowScale.x));
        ValidateFloat(config.TerrainFlowScale.y, 0.001f, 1024f, nameof(config.TerrainFlowScale.y));
        ValidateFloat(config.TerrainShimmerSpeedScale, 0f, 10f, nameof(config.TerrainShimmerSpeedScale));
        ValidateFloat(config.TerrainPulseSpeedScale, 0f, 10f, nameof(config.TerrainPulseSpeedScale));
        ValidateColor(config.TerrainShimmerColor, nameof(config.TerrainShimmerColor));
        ValidateColor(config.TerrainDebugColor, nameof(config.TerrainDebugColor));
        ValidateColor(config.TransitEmissionColor, nameof(config.TransitEmissionColor));
        ValidateFloat(config.TransitEmissionStrength, 0f, 8f, nameof(config.TransitEmissionStrength));
        ValidateColor(config.PerspectiveEmissionColor, nameof(config.PerspectiveEmissionColor));
        ValidateFloat(config.PerspectiveEmissionStrength, 0f, 8f, nameof(config.PerspectiveEmissionStrength));
        ValidateFloat(config.SurfaceOccupancy, 0f, 1f, nameof(config.SurfaceOccupancy));
        // Эффекты постпроцесса — тумблеры; проверять у bool нечего.
        // Величины живут в PostProcessLook и в конфиг не попадают.
        if (string.IsNullOrWhiteSpace(connection.ServerHost))
        {
            throw new InvalidDataException(
                "Client config value 'ServerHost' must be a non-empty host name or IP address.");
        }

        ValidateInt(connection.ServerPort, 1, 65535, nameof(connection.ServerPort));
        if (!Enum.IsDefined(typeof(FullScreenMode), display.FullScreenMode))
        {
            throw new InvalidDataException(
                $"Client config value 'FullScreenMode' must be a valid FullScreenMode value, got {display.FullScreenMode}.");
        }
    }

    private static void ValidateGeneralSettings(ClientConfig config)
    {
        InterfaceSettings interfaceSettings = config.Interface;
        DisplaySettings display = config.Display;
        AccessibilitySettings accessibility = config.Accessibility;
        if (interfaceSettings.Language is not ("ru" or "en" or "zh" or "zh-hant"))
        {
            throw new InvalidDataException(
                $"Client config value '{nameof(interfaceSettings.Language)}' is not a supported locale: " +
                $"'{interfaceSettings.Language}'.");
        }

        bool usesCurrentResolution = display.ResolutionWidth == 0 && display.ResolutionHeight == 0;
        bool usesExplicitResolution = display.ResolutionWidth is >= 320 and <= 16384 &&
            display.ResolutionHeight is >= 200 and <= 16384;
        if (!usesCurrentResolution && !usesExplicitResolution)
        {
            throw new InvalidDataException(
                "Client resolution must be either 0x0 (current display mode) or a valid width and height.");
        }

        if (display.RefreshRate is < 0 or > 1000)
        {
            throw new InvalidDataException(
                $"Client config value '{nameof(display.RefreshRate)}' must be within [0, 1000].");
        }

        if (display.TargetFrameRate != -1 && display.TargetFrameRate is < 30 or > 1000)
        {
            throw new InvalidDataException(
                $"Client config value '{nameof(display.TargetFrameRate)}' must be -1 or within [30, 1000].");
        }

        ValidateInt(accessibility.ColorblindMode, 0, 4, nameof(accessibility.ColorblindMode));
        ValidateInt(interfaceSettings.ControlScheme, 0, 1, nameof(interfaceSettings.ControlScheme));
    }

    private bool HasStandardGraphicsValues(ClientConfig config)
    {
        LightingDefaultsSnapshot lighting = _projectDefaults.Lighting;
        ShaderDefaultsSnapshot shaders = _projectDefaults.Shaders;
        return config.AmbientOcclusionEnabled == lighting.AmbientOcclusionEnabled &&
            config.DiffuseBounceEnabled == lighting.DiffuseBounceEnabled &&
            config.AmbientIntensity == lighting.AmbientIntensity &&
            config.EmissionScale == lighting.EmissionScale &&
            config.AmbientColor == lighting.AmbientColor &&
            config.EmptyExtinctionRgb == lighting.EmptyExtinctionRgb &&
            config.SolidExtinctionRgb == lighting.SolidExtinctionRgb &&
            config.EmptyExtinctionMultiplier == lighting.EmptyExtinctionMultiplier &&
            config.SolidExtinctionMultiplier == lighting.SolidExtinctionMultiplier &&
            config.BounceStrength == lighting.BounceStrength &&
            config.AmbientOcclusionRadiusCells == lighting.AmbientOcclusionRadiusCells &&
            config.AmbientOcclusionStrength == lighting.AmbientOcclusionStrength &&
            config.MaximumLightMultiplier == lighting.MaximumLightMultiplier &&
            config.EnableFinalLightingClamp == lighting.EnableFinalLightingClamp &&
            config.TransmittanceDebugDistanceCells == lighting.TransmittanceDebugDistanceCells &&
            config.MinimumTransmission == lighting.MinimumTransmission &&
            config.LightSafeBorder == lighting.LightSafeBorder &&
            config.DynamicLightIntensity == lighting.DynamicLightIntensity &&
            config.DynamicLightColor == lighting.DynamicLightColor &&
            config.DynamicLightUpdatesPerSecond == lighting.DynamicLightUpdatesPerSecond &&
            config.TerrainFlowScale == shaders.TerrainFlowScale &&
            config.TerrainShimmerSpeedScale == shaders.TerrainShimmerSpeedScale &&
            config.TerrainPulseSpeedScale == shaders.TerrainPulseSpeedScale &&
            config.TerrainShimmerColor == shaders.TerrainShimmerColor &&
            config.TerrainDebugColor == shaders.TerrainDebugColor &&
            config.TerrainDebugMode == shaders.TerrainDebugMode &&
            config.TransitEmissionColor == shaders.TransitEmissionColor &&
            config.TransitEmissionStrength == shaders.TransitEmissionStrength &&
            config.PerspectiveEmissionColor == shaders.PerspectiveEmissionColor &&
            config.PerspectiveEmissionStrength == shaders.PerspectiveEmissionStrength &&
            config.SurfaceOccupancy == shaders.SurfaceOccupancy &&
            config.BloomEnabled == shaders.BloomEnabled &&
            config.VignetteEnabled == shaders.VignetteEnabled &&
            config.ChromaticAberrationEnabled == shaders.ChromaticAberrationEnabled &&
            config.FilmGrainEnabled == shaders.FilmGrainEnabled &&
            config.MotionBlurEnabled == shaders.MotionBlurEnabled &&
            config.LocalContrastEnabled == shaders.LocalContrastEnabled &&
            config.LensEffectsEnabled == shaders.LensEffectsEnabled &&
            config.AtmosphereEnabled == shaders.AtmosphereEnabled &&
            config.DisplayPhysicsEnabled == shaders.DisplayPhysicsEnabled &&
            config.TemporalEnabled == shaders.TemporalEnabled;
    }

    private static void ValidateFloat(float value, float minimum, float maximum, string name)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value < minimum || value > maximum)
        {
            throw new InvalidDataException(
                $"Client config value '{name}' must be finite and within [{minimum}, {maximum}].");
        }
    }

    private static void ValidateInt(int value, int minimum, int maximum, string name)
    {
        if (value < minimum || value > maximum)
        {
            throw new InvalidDataException(
                $"Client config value '{name}' must be within [{minimum}, {maximum}].");
        }
    }

    private static void ValidateColor(Color value, string name)
    {
        ValidateFloat(value.r, 0f, float.MaxValue, $"{name}.r");
        ValidateFloat(value.g, 0f, float.MaxValue, $"{name}.g");
        ValidateFloat(value.b, 0f, float.MaxValue, $"{name}.b");
        ValidateFloat(value.a, 0f, float.MaxValue, $"{name}.a");
    }
}

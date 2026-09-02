#nullable enable

using System.IO;
using Fodinae.Core.Interfaces;
using Fodinae.Rendering;

namespace Fodinae.Core;

internal static class ClientConfigDefaults
{
    public static ClientConfig Create(
        IProjectDefaults projectDefaults,
        GraphicsQualityProfile graphicsQualityProfile)
    {
        if (projectDefaults == null)
        {
            throw new System.ArgumentNullException(nameof(projectDefaults));
        }

        if (graphicsQualityProfile == null)
        {
            throw new System.ArgumentNullException(nameof(graphicsQualityProfile));
        }

        ClientDefaultsSnapshot defaults = projectDefaults.Client;
        LightingDefaultsSnapshot lighting = projectDefaults.Lighting;
        ShaderDefaultsSnapshot shaders = projectDefaults.Shaders;
        GraphicsPreset graphicsPreset = ConvertLegacyGraphicsQuality(
            defaults.GraphicsQuality);
        return new ClientConfig
        {
            SchemaVersion = ClientConfig.CurrentSchemaVersion,
            ProjectDefaultsHash = projectDefaults.ContentHash,
            Audio = new AudioSettings
            {
                MasterVolume = defaults.MasterVolume,
                SfxVolume = defaults.SfxVolume,
                MusicVolume = defaults.MusicVolume,
                AmbienceVolume = defaults.AmbienceVolume,
                VoiceVolume = defaults.VoiceVolume,
                UIVolume = defaults.UIVolume,
                MuteInBackground = true,
            },
            Display = new DisplaySettings
            {
                HDREnabled = ProjectRuntimeContracts.ClientConfiguration.DefaultHDREnabled,
            },
            Interface = new InterfaceSettings
            {
                UIScale = defaults.UIScale,
            },
            Accessibility = new AccessibilitySettings(),
            Connection = new ConnectionSettings(),
            GraphicsPreset = graphicsPreset,
            GraphicsQualitySettings = graphicsQualityProfile.Get(graphicsPreset),
            AmbientOcclusionEnabled = lighting.AmbientOcclusionEnabled,
            DiffuseBounceEnabled = lighting.DiffuseBounceEnabled,
            AmbientIntensity = lighting.AmbientIntensity,
            EmissionScale = lighting.EmissionScale,
            AmbientColor = lighting.AmbientColor,
            EmptyExtinctionRgb = lighting.EmptyExtinctionRgb,
            SolidExtinctionRgb = lighting.SolidExtinctionRgb,
            EmptyExtinctionMultiplier = lighting.EmptyExtinctionMultiplier,
            SolidExtinctionMultiplier = lighting.SolidExtinctionMultiplier,
            BounceStrength = lighting.BounceStrength,
            AmbientOcclusionRadiusCells = lighting.AmbientOcclusionRadiusCells,
            AmbientOcclusionStrength = lighting.AmbientOcclusionStrength,
            MaximumLightMultiplier = lighting.MaximumLightMultiplier,
            EnableFinalLightingClamp = lighting.EnableFinalLightingClamp,
            TransmittanceDebugDistanceCells = lighting.TransmittanceDebugDistanceCells,
            MinimumTransmission = lighting.MinimumTransmission,
            LightSafeBorder = lighting.LightSafeBorder,
            DynamicLightIntensity = lighting.DynamicLightIntensity,
            DynamicLightColor = lighting.DynamicLightColor,
            DynamicLightUpdatesPerSecond = lighting.DynamicLightUpdatesPerSecond,
            TerrainFlowScale = shaders.TerrainFlowScale,
            TerrainShimmerSpeedScale = shaders.TerrainShimmerSpeedScale,
            TerrainPulseSpeedScale = shaders.TerrainPulseSpeedScale,
            TerrainShimmerColor = shaders.TerrainShimmerColor,
            TerrainDebugColor = shaders.TerrainDebugColor,
            TerrainDebugMode = shaders.TerrainDebugMode,
            BloomEnabled = shaders.BloomEnabled,
            VignetteEnabled = shaders.VignetteEnabled,
            ChromaticAberrationEnabled = shaders.ChromaticAberrationEnabled,
            FilmGrainEnabled = shaders.FilmGrainEnabled,
            MotionBlurEnabled = shaders.MotionBlurEnabled,
            LocalContrastEnabled = shaders.LocalContrastEnabled,
            LensEffectsEnabled = shaders.LensEffectsEnabled,
            AtmosphereEnabled = shaders.AtmosphereEnabled,
            DisplayPhysicsEnabled = shaders.DisplayPhysicsEnabled,
            TemporalEnabled = shaders.TemporalEnabled,
            TransitEmissionColor = shaders.TransitEmissionColor,
            TransitEmissionStrength = shaders.TransitEmissionStrength,
            PerspectiveEmissionColor = shaders.PerspectiveEmissionColor,
            PerspectiveEmissionStrength = shaders.PerspectiveEmissionStrength,
            SurfaceOccupancy = shaders.SurfaceOccupancy,
        };
    }

    public static GraphicsPreset ConvertLegacyGraphicsQuality(int legacyQuality)
    {
        return legacyQuality switch
        {
            0 => GraphicsPreset.Low,
            1 => GraphicsPreset.Medium,
            2 => GraphicsPreset.High,
            3 => GraphicsPreset.Ultra,
            _ => throw new InvalidDataException(
                $"Legacy graphics quality '{legacyQuality}' is outside the supported range 0..3."),
        };
    }

    public static void ApplyShaderDefaults(
        ClientConfig config,
        ShaderDefaultsSnapshot shaders)
    {
        config.TerrainFlowScale = shaders.TerrainFlowScale;
        config.TerrainShimmerSpeedScale = shaders.TerrainShimmerSpeedScale;
        config.TerrainPulseSpeedScale = shaders.TerrainPulseSpeedScale;
        config.TerrainShimmerColor = shaders.TerrainShimmerColor;
        config.TerrainDebugColor = shaders.TerrainDebugColor;
        config.TerrainDebugMode = shaders.TerrainDebugMode;
        config.BloomEnabled = shaders.BloomEnabled;
        config.VignetteEnabled = shaders.VignetteEnabled;
        config.ChromaticAberrationEnabled = shaders.ChromaticAberrationEnabled;
        config.FilmGrainEnabled = shaders.FilmGrainEnabled;
        config.MotionBlurEnabled = shaders.MotionBlurEnabled;
        config.LocalContrastEnabled = shaders.LocalContrastEnabled;
        config.LensEffectsEnabled = shaders.LensEffectsEnabled;
        config.AtmosphereEnabled = shaders.AtmosphereEnabled;
        config.DisplayPhysicsEnabled = shaders.DisplayPhysicsEnabled;
        config.TemporalEnabled = shaders.TemporalEnabled;
        config.TransitEmissionColor = shaders.TransitEmissionColor;
        config.TransitEmissionStrength = shaders.TransitEmissionStrength;
        config.PerspectiveEmissionColor = shaders.PerspectiveEmissionColor;
        config.PerspectiveEmissionStrength = shaders.PerspectiveEmissionStrength;
        config.SurfaceOccupancy = shaders.SurfaceOccupancy;
    }

    public static void ApplyLightingDefaults(
        ClientConfig config,
        LightingDefaultsSnapshot lighting)
    {
        config.AmbientOcclusionEnabled = lighting.AmbientOcclusionEnabled;
        config.DiffuseBounceEnabled = lighting.DiffuseBounceEnabled;
        config.AmbientIntensity = lighting.AmbientIntensity;
        config.EmissionScale = lighting.EmissionScale;
        config.AmbientColor = lighting.AmbientColor;
        config.EmptyExtinctionRgb = lighting.EmptyExtinctionRgb;
        config.SolidExtinctionRgb = lighting.SolidExtinctionRgb;
        config.EmptyExtinctionMultiplier = lighting.EmptyExtinctionMultiplier;
        config.SolidExtinctionMultiplier = lighting.SolidExtinctionMultiplier;
        config.BounceStrength = lighting.BounceStrength;
        config.AmbientOcclusionRadiusCells = lighting.AmbientOcclusionRadiusCells;
        config.AmbientOcclusionStrength = lighting.AmbientOcclusionStrength;
        config.MaximumLightMultiplier = lighting.MaximumLightMultiplier;
        config.EnableFinalLightingClamp = lighting.EnableFinalLightingClamp;
        config.TransmittanceDebugDistanceCells = lighting.TransmittanceDebugDistanceCells;
        config.MinimumTransmission = lighting.MinimumTransmission;
        config.LightSafeBorder = lighting.LightSafeBorder;
        config.DynamicLightIntensity = lighting.DynamicLightIntensity;
        config.DynamicLightColor = lighting.DynamicLightColor;
        config.DynamicLightUpdatesPerSecond = lighting.DynamicLightUpdatesPerSecond;
    }
}

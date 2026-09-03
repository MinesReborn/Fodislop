#nullable enable

using System;
using Fodinae.Rendering;
using UnityEngine;
using UnityEngine.Serialization;

namespace Fodinae.Core
{
    [Serializable]
    public sealed class AudioSettings
    {
        public float MasterVolume;
        public float SfxVolume;
        public float MusicVolume;
        public float AmbienceVolume;
        public float VoiceVolume;
        public float UIVolume;
        public bool MuteInBackground = true;
    }

    [Serializable]
    public sealed class DisplaySettings
    {
        public const float GammaMin = 1.8f;
        public const float GammaMax = 2.6f;
        public const float DefaultGamma = 2.2f;
        public const float PaperWhiteMin = 100f;
        public const float PaperWhiteMax = 500f;
        public const float DefaultPaperWhite = 200f;
        public const float PeakBrightnessMin = 400f;
        public const float PeakBrightnessMax = 2000f;
        public const float DefaultPeakBrightness = 1000f;

        public int ResolutionWidth;
        public int ResolutionHeight;
        public int RefreshRate;
        public int FullScreenMode = 1;
        public bool VSync = true;
        [FormerlySerializedAs("HdrEnabled")]
        public bool HDREnabled = ProjectRuntimeContracts.ClientConfiguration.DefaultHDREnabled;
        public int TargetFrameRate = -1;
        public float Gamma = DefaultGamma;
        public float PaperWhiteNits = DefaultPaperWhite;
        public float PeakBrightnessNits = DefaultPeakBrightness;
    }

    [Serializable]
    public sealed class InterfaceSettings
    {
        public float UIScale;
        public string Language = "ru";
        public int ControlScheme;
    }

    [Serializable]
    public sealed class AccessibilitySettings
    {
        public int ColorblindMode;
        public bool ReducePhotosensitivity;
    }

    [Serializable]
    public sealed class ConnectionSettings
    {
        public bool UseDummyConnection = ProjectRuntimeContracts.ClientConfiguration.DefaultUseDummyConnection;
        public string ServerHost = ProjectRuntimeContracts.ClientConfiguration.DefaultServerHost;
        public int ServerPort = ProjectRuntimeContracts.ClientConfiguration.DefaultServerPort;
    }

    [Serializable]
    public sealed class PostProcessSettings
    {
        public const float ExposureMin = -2f;
        public const float ExposureMax = 2f;
        public const float ContrastMin = -0.5f;
        public const float ContrastMax = 0.5f;
        public const float SaturationMin = 0f;
        public const float SaturationMax = 2f;
        public const float ToneMappingWhitePointMin = 0.25f;
        public const float ToneMappingWhitePointMax = 8f;
        public const float DefaultExposure = 0f;
        public const float DefaultContrast = 0f;
        public const float DefaultSaturation = 1f;
        public const float DefaultToneMappingWhitePoint = 1f;

        public float Exposure = DefaultExposure;
        public float Contrast = DefaultContrast;
        public float Saturation = DefaultSaturation;
        public float ToneMappingWhitePoint = DefaultToneMappingWhitePoint;
    }

    [Serializable]
    public class ClientConfig
    {
        public const int CurrentSchemaVersion = 21;

        public int SchemaVersion;
        public string ProjectDefaultsHash = string.Empty;
        public AudioSettings Audio = new();
        public DisplaySettings Display = new();
        public InterfaceSettings Interface = new();
        public AccessibilitySettings Accessibility = new();
        public ConnectionSettings Connection = new();
        public PostProcessSettings PostProcess = new();
        [FormerlySerializedAs("GraphicsQuality")]

        public GraphicsPreset GraphicsPreset;
        public GraphicsQualitySettings GraphicsQualitySettings;
        public bool AmbientOcclusionEnabled;
        public bool DiffuseBounceEnabled;
        public float AmbientIntensity;
        public float EmissionScale;
        public Color AmbientColor;
        public Color EmptyExtinctionRgb;
        public Color SolidExtinctionRgb;
        public float EmptyExtinctionMultiplier;
        public float SolidExtinctionMultiplier;
        public float BounceStrength;
        public float AmbientOcclusionRadiusCells;
        public float AmbientOcclusionStrength;
        public float MaximumLightMultiplier;
        public bool EnableFinalLightingClamp;
        public float TransmittanceDebugDistanceCells;
        public float MinimumTransmission;
        public int LightSafeBorder;
        public float DynamicLightIntensity;
        public Color DynamicLightColor;
        public float DynamicLightUpdatesPerSecond;
        public Vector2 TerrainFlowScale;
        public float TerrainShimmerSpeedScale;
        public float TerrainPulseSpeedScale;
        public Color TerrainShimmerColor;
        public Color TerrainDebugColor;
        public bool TerrainDebugMode;
        public bool EnableTerrainDistortion = true;
        public Color TransitEmissionColor;
        public float TransitEmissionStrength;
        public Color PerspectiveEmissionColor;
        public float PerspectiveEmissionStrength;
        public float SurfaceOccupancy;
        // Дорогие и стилизующие эффекты остаются тумблерами. Базовая
        // экспозиция и цветовой отклик настраиваются отдельно через
        // PostProcessSettings: это калибровка вывода, а не возврат старых
        // десятков независимых художественных параметров.
        public bool BloomEnabled;
        public bool VignetteEnabled;
        public bool ChromaticAberrationEnabled;
        public bool FilmGrainEnabled;
        public bool MotionBlurEnabled;
        public bool LocalContrastEnabled;
        public bool LensEffectsEnabled;
        public bool AtmosphereEnabled;
        public bool DisplayPhysicsEnabled;
        public bool TemporalEnabled;
    }
}

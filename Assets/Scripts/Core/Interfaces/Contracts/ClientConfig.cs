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
        public int ResolutionWidth;
        public int ResolutionHeight;
        public int RefreshRate;
        public int FullScreenMode = 1;
        public bool VSync = true;
        [FormerlySerializedAs("HdrEnabled")]
        public bool HDREnabled = ProjectRuntimeContracts.ClientConfiguration.DefaultHDREnabled;
        public int TargetFrameRate = -1;
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
    public class ClientConfig
    {
        public const int CurrentSchemaVersion = 19;

        public int SchemaVersion;
        public string ProjectDefaultsHash = string.Empty;
        public AudioSettings Audio = new();
        public DisplaySettings Display = new();
        public InterfaceSettings Interface = new();
        public AccessibilitySettings Accessibility = new();
        public ConnectionSettings Connection = new();
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
        // Эффекты постпроцесса — тумблеры, а не числа. Величины задаёт
        // PostProcessLook: вид кадра решает автор, игрок решает, платить ли за
        // эффект. Промежуточных значений нет намеренно — тридцать пять
        // ползунков давали не настройку, а разброс.
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

#nullable enable

using System;
using Fodinae.World.Lighting;
using UnityEngine;

namespace Fodinae.Core
{
    [CreateAssetMenu(fileName = "ProjectDefaults", menuName = "Fodinae/Project Defaults")]
    public sealed class ProjectDefaults : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;
        public const string ResourceName = ProjectRuntimeContracts.ResourcePaths.ProjectDefaultsResourceName;

        [SerializeField]
        private int _schemaVersion;
        [SerializeField]
        private ClientDefaultsGroup _client = new();
        [SerializeField]
        private LightingDefaultsGroup _lighting = new();
        [SerializeField]
        private ShaderDefaultsGroup _shaders = new();

        public int SchemaVersion => _schemaVersion;

        public ProjectDefaultsSnapshot CreateSnapshot()
        {
            Validate();
            return new ProjectDefaultsSnapshot(
                _schemaVersion,
                Hash128.Compute(JsonUtility.ToJson(this)).ToString(),
                _client.CreateSnapshot(),
                _lighting.CreateSnapshot(),
                _shaders.CreateSnapshot());
        }

        public void Validate()
        {
            if (_schemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Project defaults schema {_schemaVersion} is unsupported; " +
                    $"expected {CurrentSchemaVersion}.");
            }

            _client.Validate();
            _lighting.Validate();
            _shaders.Validate();
        }

        [Serializable]
        private sealed class ClientDefaultsGroup
        {
            [SerializeField]
            private float _masterVolume;
            [SerializeField]
            private float _sfxVolume;
            [SerializeField]
            private float _musicVolume;
            [SerializeField]
            private float _ambienceVolume;
            [SerializeField]
            private float _voiceVolume;
            [SerializeField]
            private float _uiVolume;
            [SerializeField]
            private float _uiScale;
            [SerializeField]
            private int _graphicsQuality;

            public ClientDefaultsSnapshot CreateSnapshot()
            {
                return new ClientDefaultsSnapshot(
                    _masterVolume,
                    _sfxVolume,
                    _musicVolume,
                    _ambienceVolume,
                    _voiceVolume,
                    _uiVolume,
                    _uiScale,
                    _graphicsQuality);
            }

            public void Validate()
            {
                ValidateRange(_masterVolume, 0f, 1f, nameof(_masterVolume));
                ValidateRange(_sfxVolume, 0f, 1f, nameof(_sfxVolume));
                ValidateRange(_musicVolume, 0f, 1f, nameof(_musicVolume));
                ValidateRange(_ambienceVolume, 0f, 1f, nameof(_ambienceVolume));
                ValidateRange(_voiceVolume, 0f, 1f, nameof(_voiceVolume));
                ValidateRange(_uiVolume, 0f, 1f, nameof(_uiVolume));
                ValidateRange(_uiScale, 0.5f, 2f, nameof(_uiScale));
                ValidateRange(_graphicsQuality, 0, 3, nameof(_graphicsQuality));
            }
        }

        [Serializable]
        private sealed class LightingDefaultsGroup
        {
            [SerializeField]
            private bool _ambientOcclusionEnabled;
            [SerializeField]
            private bool _diffuseBounceEnabled;
            [SerializeField]
            private float _ambientIntensity;
            [SerializeField]
            private float _emissionScale;
            [SerializeField]
            private Color _ambientColor;
            [SerializeField]
            private Color _emptyExtinctionRgb;
            [SerializeField]
            private Color _solidExtinctionRgb;
            [SerializeField]
            private float _emptyExtinctionMultiplier;
            [SerializeField]
            private float _solidExtinctionMultiplier;
            [SerializeField]
            private float _bounceStrength;
            [SerializeField]
            private float _ambientOcclusionRadiusCells;
            [SerializeField]
            private float _ambientOcclusionStrength;
            [SerializeField]
            private float _maximumLightMultiplier;
            [SerializeField]
            private bool _enableFinalLightingClamp;
            [SerializeField]
            private float _transmittanceDebugDistanceCells;
            [SerializeField]
            private float _minimumTransmission;
            [SerializeField]
            private int _lightSafeBorder;
            [SerializeField]
            private float _dynamicLightIntensity;
            [SerializeField]
            private Color _dynamicLightColor;
            [SerializeField]
            private float _dynamicLightUpdatesPerSecond;

            public LightingDefaultsSnapshot CreateSnapshot()
            {
                return new LightingDefaultsSnapshot(
                    _ambientOcclusionEnabled,
                    _diffuseBounceEnabled,
                    _ambientIntensity,
                    _emissionScale,
                    _ambientColor,
                    _emptyExtinctionRgb,
                    _solidExtinctionRgb,
                    _emptyExtinctionMultiplier,
                    _solidExtinctionMultiplier,
                    _bounceStrength,
                    _ambientOcclusionRadiusCells,
                    _ambientOcclusionStrength,
                    _maximumLightMultiplier,
                    _enableFinalLightingClamp,
                    _transmittanceDebugDistanceCells,
                    _minimumTransmission,
                    _lightSafeBorder,
                    _dynamicLightIntensity,
                    _dynamicLightColor,
                    _dynamicLightUpdatesPerSecond);
            }

            public void Validate()
            {
                ValidateRange(_ambientIntensity, 0f, 1f, nameof(_ambientIntensity));
                ValidateRange(_emissionScale, 0.1f, 8f, nameof(_emissionScale));
                ValidateColor(_ambientColor, nameof(_ambientColor));
                ValidateColor(_emptyExtinctionRgb, nameof(_emptyExtinctionRgb));
                ValidateColor(_solidExtinctionRgb, nameof(_solidExtinctionRgb));
                ValidateRange(
                    _emptyExtinctionMultiplier,
                    0f,
                    2f,
                    nameof(_emptyExtinctionMultiplier));
                ValidateRange(
                    _solidExtinctionMultiplier,
                    0.25f,
                    2f,
                    nameof(_solidExtinctionMultiplier));
                ValidateRange(_bounceStrength, 0f, 1f, nameof(_bounceStrength));
                ValidateRange(
                    _ambientOcclusionRadiusCells,
                    0.5f,
                    8f,
                    nameof(_ambientOcclusionRadiusCells));
                ValidateRange(
                    _ambientOcclusionStrength,
                    0.1f,
                    8f,
                    nameof(_ambientOcclusionStrength));
                ValidateRange(
                    _maximumLightMultiplier,
                    0.25f,
                    LightingConfigLimits.MaximumLightMultiplier,
                    nameof(_maximumLightMultiplier));
                ValidateRange(
                    _transmittanceDebugDistanceCells,
                    2f,
                    32f,
                    nameof(_transmittanceDebugDistanceCells));
                ValidateRange(
                    _minimumTransmission,
                    0.0001f,
                    0.1f,
                    nameof(_minimumTransmission));
                ValidateRange(_lightSafeBorder, 0, 8, nameof(_lightSafeBorder));
                ValidateRange(
                    _dynamicLightIntensity,
                    0f,
                    4f,
                    nameof(_dynamicLightIntensity));
                ValidateColor(_dynamicLightColor, nameof(_dynamicLightColor));
                ValidateRange(
                    _dynamicLightUpdatesPerSecond,
                    1f,
                    LightingConfigLimits.DynamicLightUpdatesPerSecond,
                    nameof(_dynamicLightUpdatesPerSecond));
            }
        }

        [Serializable]
        private sealed class ShaderDefaultsGroup
        {
            [SerializeField]
            private Vector2 _terrainFlowScale;
            [SerializeField]
            private float _terrainShimmerSpeedScale;
            [SerializeField]
            private float _terrainPulseSpeedScale;
            [SerializeField]
            private Color _terrainShimmerColor;
            [SerializeField]
            private Color _terrainDebugColor;
            [SerializeField]
            private bool _terrainDebugMode;
            [SerializeField]
            private Color _transitEmissionColor;
            [SerializeField]
            private float _transitEmissionStrength;
            [SerializeField]
            private Color _perspectiveEmissionColor;
            [SerializeField]
            private float _perspectiveEmissionStrength;
            [SerializeField]
            private float _surfaceOccupancy;
            // Тумблеры, а не величины: вид кадра задаёт PostProcessLook.
            [SerializeField]
            private bool _bloomEnabled = true;
            [SerializeField]
            private bool _vignetteEnabled = true;
            [SerializeField]
            private bool _chromaticAberrationEnabled;
            [SerializeField]
            private bool _filmGrainEnabled = true;
            [SerializeField]
            private bool _motionBlurEnabled;
            [SerializeField]
            private bool _localContrastEnabled = true;
            [SerializeField]
            private bool _lensEffectsEnabled = true;
            [SerializeField]
            private bool _atmosphereEnabled = true;
            [SerializeField]
            private bool _displayPhysicsEnabled;
            [SerializeField]
            private bool _temporalEnabled = true;

            public ShaderDefaultsSnapshot CreateSnapshot() => new(
                _terrainFlowScale,
                _terrainShimmerSpeedScale,
                _terrainPulseSpeedScale,
                _terrainShimmerColor,
                _terrainDebugColor,
                _terrainDebugMode,
                _transitEmissionColor,
                _transitEmissionStrength,
                _perspectiveEmissionColor,
                _perspectiveEmissionStrength,
                _surfaceOccupancy,
                _bloomEnabled,
                _vignetteEnabled,
                _chromaticAberrationEnabled,
                _filmGrainEnabled,
                _motionBlurEnabled,
                _localContrastEnabled,
                _lensEffectsEnabled,
                _atmosphereEnabled,
                _displayPhysicsEnabled,
                _temporalEnabled);

            public void Validate()
            {
                ValidateRange(_terrainFlowScale.x, 0.001f, 1024f, nameof(_terrainFlowScale.x));
                ValidateRange(_terrainFlowScale.y, 0.001f, 1024f, nameof(_terrainFlowScale.y));
                ValidateRange(_terrainShimmerSpeedScale, 0f, 10f, nameof(_terrainShimmerSpeedScale));
                ValidateRange(_terrainPulseSpeedScale, 0f, 10f, nameof(_terrainPulseSpeedScale));
                ValidateColor(_terrainShimmerColor, nameof(_terrainShimmerColor));
                ValidateColor(_terrainDebugColor, nameof(_terrainDebugColor));
                ValidateColor(_transitEmissionColor, nameof(_transitEmissionColor));
                ValidateRange(_transitEmissionStrength, 0f, 8f, nameof(_transitEmissionStrength));
                ValidateColor(_perspectiveEmissionColor, nameof(_perspectiveEmissionColor));
                ValidateRange(_perspectiveEmissionStrength, 0f, 8f, nameof(_perspectiveEmissionStrength));
                ValidateRange(_surfaceOccupancy, 0f, 1f, nameof(_surfaceOccupancy));
                // Тумблеры проверять нечем: bool не выходит за диапазон.
            }
        }

        private static void ValidateColor(Color value, string name)
        {
            ValidateRange(value.r, 0f, float.MaxValue, $"{name}.r");
            ValidateRange(value.g, 0f, float.MaxValue, $"{name}.g");
            ValidateRange(value.b, 0f, float.MaxValue, $"{name}.b");
            ValidateRange(value.a, 0f, float.MaxValue, $"{name}.a");
        }

        private static void ValidateRange(float value, float minimum, float maximum, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < minimum || value > maximum)
            {
                throw new InvalidOperationException(
                    $"Project default '{name}' must be finite and within [{minimum}, {maximum}].");
            }
        }

        private static void ValidateRange(int value, int minimum, int maximum, string name)
        {
            if (value < minimum || value > maximum)
            {
                throw new InvalidOperationException(
                    $"Project default '{name}' must be within [{minimum}, {maximum}].");
            }
        }
    }
}

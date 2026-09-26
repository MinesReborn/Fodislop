#nullable enable

using System;
using Kern.Core;
using Kern.Rendering;
using Kern.World.Lighting.Quality;
using UnityEngine;

namespace Kern.World.Lighting
{
    /// <summary>Applies quality settings and owns lighting quality transition side effects.</summary>
    internal sealed class LightingQualityController
    {
        private readonly LightingResourceManager _resources;
        private readonly LightingRuntimeState _runtimeState;
        private readonly DynamicLightManager _dynamicLightManager;
        private readonly Func<LightingComposition?> _existingComposition;
        private readonly Func<LightingComposition> _getComposition;

        public LightingQualityController(
            LightingResourceManager resources,
            LightingRuntimeState runtimeState,
            DynamicLightManager dynamicLightManager,
            Func<LightingComposition?> existingComposition,
            Func<LightingComposition> getComposition)
        {
            _resources = resources ?? throw new ArgumentNullException(nameof(resources));
            _runtimeState = runtimeState ?? throw new ArgumentNullException(nameof(runtimeState));
            _dynamicLightManager = dynamicLightManager ?? throw new ArgumentNullException(nameof(dynamicLightManager));
            _existingComposition = existingComposition ?? throw new ArgumentNullException(nameof(existingComposition));
            _getComposition = getComposition ?? throw new ArgumentNullException(nameof(getComposition));
        }

        public GraphicsPreset ActivePreset { get; private set; }

        public GraphicsQualitySettings Settings { get; private set; }

        public LightingQualityMode QualityMode { get; private set; } = LightingQualityMode.Off;

        public void Apply(GraphicsPreset preset, GraphicsQualitySettings settings)
        {
            GraphicsQualityProfile.ValidateSettings(settings, preset.ToString());
            bool technicalSettingsChanged = Settings != settings;
            LightingQualityMode previousQuality = QualityMode;
            if (technicalSettingsChanged &&
                (_resources.GPUPipelineInitialized || _resources.AmbientOcclusionField != null))
            {
                _getComposition().Presentation.PublishDisabled();
                LightingGpuTeardown.ReleaseResources(
                    _existingComposition(),
                    _resources,
                    _dynamicLightManager,
                    _runtimeState);
            }

            ActivePreset = preset;
            LightingUnityQualityApplier.ApplyQualityLevel();
            Settings = settings;
            LightingQualityMode resolvedQuality = settings.LightingQuality;
            QualityMode = resolvedQuality;

            if (resolvedQuality == LightingQualityMode.Off &&
                preset == GraphicsPreset.Standard)
            {
                // Standard renders contact AO without the radiance solve.
                // Keep the neutral light binding until its first AO field is published.
                _getComposition().Presentation.PublishDisabled();
            }
            else if (resolvedQuality == LightingQualityMode.Off)
            {
                DisableGpuLighting();
            }
            else
            {
                // The next committed lighting frame publishes the valid field.
                // A preset switch must not expose a texture released above.
                if (technicalSettingsChanged)
                {
                    _getComposition().Presentation.PublishDisabled();
                }
                else
                {
                    _getComposition().Presentation.MarkEnabled();
                    Shader.EnableKeyword(LightingPresentation.WorldLightingKeyword);
                }
            }

            LightingUnityQualityApplier.ApplyRenderingSettings(Settings);
            if (!LightingQualityTransitionPolicy.RequiresFullSolve(
                    technicalSettingsChanged,
                    previousQuality,
                    resolvedQuality))
            {
                return;
            }

            _runtimeState.LastVisibleRegion = new Vector4(float.NaN, float.NaN, float.NaN, float.NaN);
            LightingRuntimeInvalidation.ResetFieldAndRadiance(_runtimeState);
        }

        public void DisableGpuLighting()
        {
            LightingGpuTeardown.ReleasePipeline(
                _existingComposition(),
                _resources,
                _dynamicLightManager);
            _getComposition().Presentation.PublishDisabled();
        }
    }
}

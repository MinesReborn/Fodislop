#if MODERN_SSAO
using System;

namespace UnityEngine.Rendering.Universal
{
    public enum ScreenSpaceAmbientOcclusionMode
    {
        [InspectorName("None")]
        None = 0,

        [InspectorName("Standard")]
        Standard = 1,

        [InspectorName("GTAO")]
        GTAO = 2,

    }

    public enum ScreenSpaceAmbientOcclusionNoiseMethod
    {
        [InspectorName("Blue Noise")]
        BlueNoise = 0,

        [InspectorName("Interleaved Gradient")]
        InterleavedGradient = 1
    }

    public enum ScreenSpaceAmbientOcclusionSampleCount
    {
        [InspectorName("Low")]
        Low = 0,

        [InspectorName("Medium")]
        Medium = 1,

        [InspectorName("High")]
        High = 2
    }

    public enum ScreenSpaceAmbientOcclusionDepthSource
    {
        [InspectorName("Depth")]
        Depth = 0,

        [InspectorName("Depth Normals")]
        DepthNormals = 1
    }

    public enum ScreenSpaceAmbientOcclusionNormalQuality
    {
        [InspectorName("Low")]
        Low = 0,

        [InspectorName("Medium")]
        Medium = 1,

        [InspectorName("High")]
        High = 2
    }

    public enum ScreenSpaceAmbientOcclusionBlurQuality
    {
        [InspectorName("Low (Kawase)")]
        Low = 0,

        [InspectorName("Medium (Gaussian)")]
        Medium = 1,

        [InspectorName("High (Bilateral)")]
        High = 2
    }

    public enum ScreenSpaceAmbientOcclusionSpatialFilter
    {
        [InspectorName("Bilateral")]
        Bilateral = 0,

        [InspectorName("Box")]
        Box = 1
    }

    public enum ScreenSpaceAmbientOcclusionQuality
    {
        [InspectorName("Low")]
        Low = 0,

        [InspectorName("Medium")]
        Medium = 1,

        [InspectorName("High")]
        High = 2,

        [InspectorName("Custom")]
        Custom = 3
    }

    [Serializable, VolumeComponentMenu("Lighting/Screen Space Ambient Occlusion")]
    [DisplayInfo(name = "Screen Space Ambient Occlusion")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [VolumeRequiresRendererFeatures(typeof(ScreenSpaceAmbientOcclusion))]
    [URPHelpURL("urp/post-processing-ssao")]
    public sealed class ScreenSpaceAmbientOcclusionVolumeOverride : VolumeComponent, IPostProcessComponent
    {
        public ScreenSpaceAmbientOcclusionMode mode
        {
            get => m_Mode.value;
            set => m_Mode.value = value;
        }
        [Tooltip("The ambient occlusion algorithm to use. Standard uses the Alchemy SSAO method. GTAO (Ground Truth Ambient Occlusion) provides higher quality.")]
        [SerializeField]
        private ScreenSpaceAmbientOcclusionModeParameter m_Mode = new ScreenSpaceAmbientOcclusionModeParameter(ScreenSpaceAmbientOcclusionMode.None);

        public ScreenSpaceAmbientOcclusionQuality quality
        {
            get => m_Quality.value;
            set => m_Quality.value = value;
        }
        [Tooltip("Quality preset for ambient occlusion. Custom allows manual adjustment of all parameters.")]
        [SerializeField]
        private ScreenSpaceAmbientOcclusionQualityParameter m_Quality = new ScreenSpaceAmbientOcclusionQualityParameter(ScreenSpaceAmbientOcclusionQuality.Medium);

        // ====================
        // Common Parameters
        // ====================

        public float intensity
        {
            get => m_Intensity.value;
            set => m_Intensity.value = value;
        }
        [Tooltip("Controls the strength of the ambient occlusion effect. Increase this value to produce darker areas.")]
        [SerializeField]
        private ClampedFloatParameter m_Intensity = new ClampedFloatParameter(1f, 0f, 5f);

        public float radius
        {
            get => m_Radius.value;
            set => m_Radius.value = value;
        }
        [Tooltip("The radius around a given point where Unity calculates and applies the effect. Larger values cover more area but may reduce performance due to increased texture sampling.")]
        [SerializeField]
        private ClampedFloatParameter m_Radius = new ClampedFloatParameter(0.3f, 0.01f, 5f);

        public float directLightingStrength
        {
            get => m_DirectLightingStrength.value;
            set => m_DirectLightingStrength.value = value;
        }
        [Tooltip("Controls how visible the ambient occlusion effect is on surfaces lit by direct light sources. Higher values apply occlusion more uniformly across all lighting, not just in shadowed areas.")]
        [SerializeField]
        private ClampedFloatParameter m_DirectLightingStrength = new ClampedFloatParameter(0f, 0f, 1f);

        public float falloffDistance
        {
            get => m_FalloffDistance.value;
            set => m_FalloffDistance.value = value;
        }
        [Tooltip("The distance from the camera where Ambient Occlusion should be visible. Beyond this distance, AO will fade out.")]
        [SerializeField]
        private MinFloatParameter m_FalloffDistance = new MinFloatParameter(100f, 0f);

        public ScreenSpaceAmbientOcclusionSampleCount sampleCount
        {
            get
            {
                var q = m_Quality.value;
                return q == ScreenSpaceAmbientOcclusionQuality.Custom ? m_SampleCount.value : GetPresetSampleCount(q);
            }
            set => m_SampleCount.value = value;
        }
        [Tooltip("The quality of the ambient occlusion sampling. Higher quality uses more samples/steps but provides better results.")]
        [SerializeField]
        private ScreenSpaceAmbientOcclusionSampleCountParameter m_SampleCount = new ScreenSpaceAmbientOcclusionSampleCountParameter(ScreenSpaceAmbientOcclusionSampleCount.Medium);

        public bool downsample
        {
            get
            {
                var q = m_Quality.value;
                return q == ScreenSpaceAmbientOcclusionQuality.Custom ? m_Downsample.value : GetPresetDownsample(q);
            }
            set => m_Downsample.value = value;
        }
        [Tooltip("With this option enabled, Unity downsamples the SSAO effect texture to improve performance.")]
        [SerializeField]
        private BoolParameter m_Downsample = new BoolParameter(false);

        public bool afterOpaque
        {
            get => m_AfterOpaque.value;
            set => m_AfterOpaque.value = value;
        }
        [Tooltip("When enabled, SSAO is applied as a multiply on the final opaque image after rendering. This can improve performance on tiled GPUs but may produce slightly different visual results compared to applying SSAO during lighting.")]
        [SerializeField]
        private BoolParameter m_AfterOpaque = new BoolParameter(false);

        public ScreenSpaceAmbientOcclusionBlurQuality blurQuality
        {
            get
            {
                var q = m_Quality.value;
                return q == ScreenSpaceAmbientOcclusionQuality.Custom ? m_BlurQuality.value : GetPresetBlurQuality(q);
            }
            set => m_BlurQuality.value = value;
        }
        [Tooltip("The blur quality to apply to the ambient occlusion texture. Higher quality reduces noise but is more expensive.")]
        [SerializeField]
        private ScreenSpaceAmbientOcclusionBlurQualityParameter m_BlurQuality = new ScreenSpaceAmbientOcclusionBlurQualityParameter(ScreenSpaceAmbientOcclusionBlurQuality.High);

        // ====================
        // Default Mode Parameters
        // ====================

        public ScreenSpaceAmbientOcclusionNoiseMethod method
        {
            get => m_Method.value;
            set => m_Method.value = value;
        }
        [Tooltip("'Interleaved Gradient Noise' generates static SSAO and is more performant. 'Blue Noise' generates dynamic SSAO at a slightly higher cost, producing a more subtle effect when the camera is in motion.")]
        [SerializeField]
        private ScreenSpaceAmbientOcclusionNoiseMethodParameter m_Method = new ScreenSpaceAmbientOcclusionNoiseMethodParameter(ScreenSpaceAmbientOcclusionNoiseMethod.InterleavedGradient);

        public ScreenSpaceAmbientOcclusionDepthSource depthSource
        {
            get
            {
                var q = m_Quality.value;
                return q == ScreenSpaceAmbientOcclusionQuality.Custom ? m_DepthSource.value : GetPresetDepthSource(q);
            }
            set => m_DepthSource.value = value;
        }
        [Tooltip("The source of the depth and normal data. Depth Normals is more accurate but requires the depth-normals prepass.")]
        [SerializeField]
        private ScreenSpaceAmbientOcclusionDepthSourceParameter m_DepthSource = new ScreenSpaceAmbientOcclusionDepthSourceParameter(ScreenSpaceAmbientOcclusionDepthSource.DepthNormals);

        public ScreenSpaceAmbientOcclusionNormalQuality normalQuality
        {
            get
            {
                var q = m_Quality.value;
                return q == ScreenSpaceAmbientOcclusionQuality.Custom ? m_NormalQuality.value : GetPresetNormalQuality(q);
            }
            set => m_NormalQuality.value = value;
        }
        [Tooltip("The number of depth texture samples that Unity takes when computing normals from depth. Only used when Depth Source is set to Depth.")]
        [SerializeField]
        private ScreenSpaceAmbientOcclusionNormalQualityParameter m_NormalQuality = new ScreenSpaceAmbientOcclusionNormalQualityParameter(ScreenSpaceAmbientOcclusionNormalQuality.Medium);

        // ====================
        // GTAO Mode Parameters
        // ====================

        public int minimumRadiusInPixels
        {
            get => m_MinimumRadiusInPixels.value;
            set => m_MinimumRadiusInPixels.value = value;
        }
        [Tooltip("Minimum radius in pixels guaranteed by GTAO. Acts as a screen-space floor on the world-space Radius so the effect remains visible at a distance.")]
        [SerializeField]
        private ClampedIntParameter m_MinimumRadiusInPixels = new ClampedIntParameter(40, 1, 256);

        public bool useComputeShader
        {
            get => m_UseComputeShader.value;
            set => m_UseComputeShader.value = value;
        }
        [Tooltip("When enabled, uses compute shaders for GTAO calculation. Provides temporal filtering support and configurable direction/step counts.")]
        [SerializeField]
        private BoolParameter m_UseComputeShader = new BoolParameter(false);

        public ScreenSpaceAmbientOcclusionSpatialFilter spatialFilter
        {
            get => m_SpatialFilter.value;
            set => m_SpatialFilter.value = value;
        }
        [Tooltip("Spatial filter used by the GTAO compute path.")]
        [SerializeField]
        private ScreenSpaceAmbientOcclusionSpatialFilterParameter m_SpatialFilter = new ScreenSpaceAmbientOcclusionSpatialFilterParameter(ScreenSpaceAmbientOcclusionSpatialFilter.Bilateral);

        // ============================
        // Temporal Filter Parameters
        // ============================

        public bool temporalFilter
        {
            get => m_TemporalFilter.value;
            set => m_TemporalFilter.value = value;
        }
        [Tooltip("Enable temporal filtering to reduce noise and improve stability over time. Requires Motion Vectors.")]
        [SerializeField]
        private BoolParameter m_TemporalFilter = new BoolParameter(false);

        public float ghostingMitigation
        {
            get => m_GhostingMitigation.value;
            set => m_GhostingMitigation.value = value;
        }
        [Tooltip("Controls how aggressively temporal accumulation reduces ghosting. Higher values reject more history but can increase noise.")]
        [SerializeField]
        private ClampedFloatParameter m_GhostingMitigation = new ClampedFloatParameter(0.5f, 0.0f, 1.0f);

        public float historyLength
        {
            get => m_HistoryLength.value;
            set => m_HistoryLength.value = value;
        }
        [Tooltip("Controls the length of the temporal history. Higher values produce smoother, more stable results but can increase ghosting.")]
        [SerializeField]
        private ClampedFloatParameter m_HistoryLength = new ClampedFloatParameter(0.9f, 0.0f, 1.0f);

        // ============================
        // GTAO Compute Parameters
        // ============================

        public int directionCount
        {
            get
            {
                var q = m_Quality.value;
                return q == ScreenSpaceAmbientOcclusionQuality.Custom ? m_DirectionCount.value : GetPresetDirectionCount(q);
            }
            set => m_DirectionCount.value = value;
        }
        [Tooltip("Number of directions to sample for GTAO. More directions improve quality but increase cost.")]
        [SerializeField]
        private ClampedIntParameter m_DirectionCount = new ClampedIntParameter(2, 1, 8);

        public int stepCount
        {
            get
            {
                var q = m_Quality.value;
                return q == ScreenSpaceAmbientOcclusionQuality.Custom ? m_StepCount.value : GetPresetStepCount(q);
            }
            set => m_StepCount.value = value;
        }
        [Tooltip("Number of steps per direction for GTAO. More steps improve quality but increase cost.")]
        [SerializeField]
        private ClampedIntParameter m_StepCount = new ClampedIntParameter(4, 1, 16);

        // ============================
        // Quality Preset Lookups
        // ============================

        internal static bool GetPresetDownsample(ScreenSpaceAmbientOcclusionQuality q) => q switch
        {
            ScreenSpaceAmbientOcclusionQuality.Low      => true,
            ScreenSpaceAmbientOcclusionQuality.Medium   => true,
            _                                           => false, // High
        };

        internal static ScreenSpaceAmbientOcclusionSampleCount GetPresetSampleCount(ScreenSpaceAmbientOcclusionQuality q) => q switch
        {
            ScreenSpaceAmbientOcclusionQuality.Low      => ScreenSpaceAmbientOcclusionSampleCount.Low,
            ScreenSpaceAmbientOcclusionQuality.Medium   => ScreenSpaceAmbientOcclusionSampleCount.Medium,
            _                                           => ScreenSpaceAmbientOcclusionSampleCount.High,
        };

        internal static ScreenSpaceAmbientOcclusionBlurQuality GetPresetBlurQuality(ScreenSpaceAmbientOcclusionQuality q) => q switch
        {
            ScreenSpaceAmbientOcclusionQuality.Low      => ScreenSpaceAmbientOcclusionBlurQuality.Low,
            ScreenSpaceAmbientOcclusionQuality.Medium   => ScreenSpaceAmbientOcclusionBlurQuality.Medium,
            _                                           => ScreenSpaceAmbientOcclusionBlurQuality.High,
        };

        internal static ScreenSpaceAmbientOcclusionDepthSource GetPresetDepthSource(ScreenSpaceAmbientOcclusionQuality q) => q switch
        {
            ScreenSpaceAmbientOcclusionQuality.Low      => ScreenSpaceAmbientOcclusionDepthSource.Depth,
            ScreenSpaceAmbientOcclusionQuality.Medium   => ScreenSpaceAmbientOcclusionDepthSource.DepthNormals,
            _                                           => ScreenSpaceAmbientOcclusionDepthSource.DepthNormals, // High
        };

        internal static ScreenSpaceAmbientOcclusionNormalQuality GetPresetNormalQuality(ScreenSpaceAmbientOcclusionQuality q) => q switch
        {
            ScreenSpaceAmbientOcclusionQuality.Low      => ScreenSpaceAmbientOcclusionNormalQuality.Low,
            ScreenSpaceAmbientOcclusionQuality.Medium   => ScreenSpaceAmbientOcclusionNormalQuality.Medium,
            _                                           => ScreenSpaceAmbientOcclusionNormalQuality.High,
        };

        internal static int GetPresetDirectionCount(ScreenSpaceAmbientOcclusionQuality q) => q switch
        {
            ScreenSpaceAmbientOcclusionQuality.Low      => 1,
            ScreenSpaceAmbientOcclusionQuality.Medium   => 2,
            _                                           => 4, // High
        };

        internal static int GetPresetStepCount(ScreenSpaceAmbientOcclusionQuality q) => q switch
        {
            ScreenSpaceAmbientOcclusionQuality.Low      => 2,
            ScreenSpaceAmbientOcclusionQuality.Medium   => 4,
            _                                           => 6, // High
        };

        /// <returns><c>true</c> if the effect should be rendered, <c>false</c> otherwise.</returns>
        public bool IsActive() => m_Mode.value != ScreenSpaceAmbientOcclusionMode.None && m_Intensity.value > 0f && m_Radius.value > 0f && m_FalloffDistance.value > 0f;

        /// <returns><c>true</c> if using Standard mode, <c>false</c> otherwise.</returns>
        public bool IsStandardMode() => m_Mode.value == ScreenSpaceAmbientOcclusionMode.Standard;

        /// <returns><c>true</c> if blue noise sampling is enabled, <c>false</c> otherwise.</returns>
        public bool IsBlueNoiseEnabled() => m_Method.value == ScreenSpaceAmbientOcclusionNoiseMethod.BlueNoise;
    }

    // ============================
    // Volume Parameter Types
    // ============================

    [Serializable]
    public sealed class ScreenSpaceAmbientOcclusionModeParameter : VolumeParameter<ScreenSpaceAmbientOcclusionMode>
    {
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public ScreenSpaceAmbientOcclusionModeParameter(ScreenSpaceAmbientOcclusionMode value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class ScreenSpaceAmbientOcclusionNoiseMethodParameter : VolumeParameter<ScreenSpaceAmbientOcclusionNoiseMethod>
    {
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public ScreenSpaceAmbientOcclusionNoiseMethodParameter(ScreenSpaceAmbientOcclusionNoiseMethod value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class ScreenSpaceAmbientOcclusionSampleCountParameter : VolumeParameter<ScreenSpaceAmbientOcclusionSampleCount>
    {
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public ScreenSpaceAmbientOcclusionSampleCountParameter(ScreenSpaceAmbientOcclusionSampleCount value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class ScreenSpaceAmbientOcclusionDepthSourceParameter : VolumeParameter<ScreenSpaceAmbientOcclusionDepthSource>
    {
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public ScreenSpaceAmbientOcclusionDepthSourceParameter(ScreenSpaceAmbientOcclusionDepthSource value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class ScreenSpaceAmbientOcclusionNormalQualityParameter : VolumeParameter<ScreenSpaceAmbientOcclusionNormalQuality>
    {
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public ScreenSpaceAmbientOcclusionNormalQualityParameter(ScreenSpaceAmbientOcclusionNormalQuality value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class ScreenSpaceAmbientOcclusionBlurQualityParameter : VolumeParameter<ScreenSpaceAmbientOcclusionBlurQuality>
    {
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public ScreenSpaceAmbientOcclusionBlurQualityParameter(ScreenSpaceAmbientOcclusionBlurQuality value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class ScreenSpaceAmbientOcclusionSpatialFilterParameter : VolumeParameter<ScreenSpaceAmbientOcclusionSpatialFilter>
    {
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public ScreenSpaceAmbientOcclusionSpatialFilterParameter(ScreenSpaceAmbientOcclusionSpatialFilter value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class ScreenSpaceAmbientOcclusionQualityParameter : VolumeParameter<ScreenSpaceAmbientOcclusionQuality>
    {
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public ScreenSpaceAmbientOcclusionQualityParameter(ScreenSpaceAmbientOcclusionQuality value, bool overrideState = false) : base(value, overrideState) { }
    }

}
#endif

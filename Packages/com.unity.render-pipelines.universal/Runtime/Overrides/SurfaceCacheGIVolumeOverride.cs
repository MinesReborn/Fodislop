#if SURFACE_CACHE

using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable, VolumeComponentMenu("Lighting/Surface Cache Global Illumination")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class SurfaceCacheGIVolumeOverride : VolumeComponent
    {
        const PresetQuality k_defaultPresetQuality = PresetQuality.Medium;

        [Tooltip("Enables Surface Cache Global Illumination. When disabled, the feature performs no work.")]
        public BoolParameter enabled = new BoolParameter(true);

        // ====================
        // Sampling Parameters
        // ====================

        [Tooltip("Enable multi-bounce global illumination for more accurate light propagation.")]
        public BoolParameter multiBounce = new BoolParameter(k_Presets[(int)k_defaultPresetQuality].multiBounce);

        [Tooltip("When enabled, new patches are allocated at ray hit locations when multi-bounce cache lookups fail.")]
        public BoolParameter bouncePatchAllocation = new BoolParameter(k_Presets[(int)k_defaultPresetQuality].bouncePatchAllocation);

        public ClampedIntParameter sampleCount = new ClampedIntParameter(k_Presets[(int)k_defaultPresetQuality].sampleCount, 1, 32);

        // ============================
        // Patch Filtering Parameters
        // ============================

        public ClampedFloatParameter temporalSmoothing = new ClampedFloatParameter(k_Presets[(int)k_defaultPresetQuality].temporalSmoothing, 0.0f, 1.0f);

        public BoolParameter spatialFilterEnabled = new BoolParameter(k_Presets[(int)k_defaultPresetQuality].spatialFilterEnabled);

        public ClampedIntParameter spatialSampleCount = new ClampedIntParameter(k_Presets[(int)k_defaultPresetQuality].spatialSampleCount, 1, 8);

        public ClampedFloatParameter spatialRadius = new ClampedFloatParameter(k_Presets[(int)k_defaultPresetQuality].spatialRadius, 0.1f, 4.0f);

        public BoolParameter temporalPostFilter = new BoolParameter(k_Presets[(int)k_defaultPresetQuality].temporalPostFilter);

        // ============================
        // Screen Filtering Parameters
        // ============================

        public ClampedIntParameter lookupSampleCount = new ClampedIntParameter(k_Presets[(int)k_defaultPresetQuality].lookupSampleCount, 0, 8);

        public ClampedFloatParameter upsamplingKernelSize = new ClampedFloatParameter(k_Presets[(int)k_defaultPresetQuality].upsamplingKernelSize, 0.0f, 8.0f);

        public ClampedIntParameter upsamplingSampleCount = new ClampedIntParameter(k_Presets[(int)k_defaultPresetQuality].upsamplingSampleCount, 1, 16);

        // =======================
        // Volume Configuration
        // =======================

        public MinFloatParameter volumeSize = new MinFloatParameter(128.0f, 1.0f);

        public ClampedIntParameter volumeResolution = new ClampedIntParameter(32, 16, 128);

        public ClampedIntParameter volumeCascadeCount = new ClampedIntParameter(4, 1, 8);

        // =======================
        // Volume Behavior
        // =======================

        public BoolParameter cascadeMovement = new BoolParameter(true);

        // =======================
        // Advanced Properties
        // =======================

        [AdditionalProperty]
        public ClampedIntParameter defragCount = new ClampedIntParameter(2, 1, 32);

        [AdditionalProperty]
        [Tooltip("Only renderers whose Rendering Layer Mask intersects this mask contribute to Surface Cache Global Illumination.")]
        public RenderingLayerMaskParameter renderingLayerMask = new RenderingLayerMaskParameter(0xFFFFFFFF);

        // =======================
        // Preset Utility
        // =======================
        public enum PresetQuality
        {
            Low,

            Medium,

            High,

            Ultra,

            Custom
        }

        struct Preset
        {
            public bool multiBounce;
            public bool bouncePatchAllocation;
            public int sampleCount;
            public float temporalSmoothing;
            public bool spatialFilterEnabled;
            public int spatialSampleCount;
            public float spatialRadius;
            public bool temporalPostFilter;
            public int lookupSampleCount;
            public float upsamplingKernelSize;
            public int upsamplingSampleCount;
        }


        // Quality preset definitions; indexed by Quality (Low=0, Medium=1, High=2, Ultra=3).
        static readonly Preset[] k_Presets =
        {
            // Low
            new Preset
            {
                multiBounce = false, bouncePatchAllocation = false, sampleCount = 1,
                temporalSmoothing = 0.9f, spatialFilterEnabled = false, spatialSampleCount = 4, spatialRadius = 1.0f, temporalPostFilter = false,
                lookupSampleCount = 4, upsamplingKernelSize = 2.0f, upsamplingSampleCount = 1,
            },
            // Medium
            new Preset
            {
                multiBounce = true, bouncePatchAllocation = true, sampleCount = 2,
                temporalSmoothing = 0.8f, spatialFilterEnabled = true, spatialSampleCount = 4, spatialRadius = 1.0f, temporalPostFilter = true,
                lookupSampleCount = 6, upsamplingKernelSize = 4.0f, upsamplingSampleCount = 2,
            },
            // High
            new Preset
            {
                multiBounce = true, bouncePatchAllocation = true, sampleCount = 4,
                temporalSmoothing = 0.7f, spatialFilterEnabled = true, spatialSampleCount = 6, spatialRadius = 1.5f, temporalPostFilter = true,
                lookupSampleCount = 8, upsamplingKernelSize = 5.0f, upsamplingSampleCount = 4,
            },
            // Ultra
            new Preset
            {
                multiBounce = true, bouncePatchAllocation = true, sampleCount = 8,
                temporalSmoothing = 0.6f, spatialFilterEnabled = true, spatialSampleCount = 8, spatialRadius = 2.0f, temporalPostFilter = true,
                lookupSampleCount = 8, upsamplingKernelSize = 7.0f, upsamplingSampleCount = 8,
            },
        };

        // Writes preset values into the backing serialized fields so users can inspect them
        // and use them as a starting point when switching to Custom.
        public void ApplyPreset(PresetQuality quality)
        {
            if (quality == PresetQuality.Custom) return;
            ref readonly var settings = ref k_Presets[(int)quality];

            multiBounce.value = settings.multiBounce;
            multiBounce.overrideState = true;
            bouncePatchAllocation.value = settings.bouncePatchAllocation;
            bouncePatchAllocation.overrideState = true;
            sampleCount.value = settings.sampleCount;
            sampleCount.overrideState = true;
            temporalSmoothing.value = settings.temporalSmoothing;
            temporalSmoothing.overrideState = true;
            spatialFilterEnabled.value = settings.spatialFilterEnabled;
            spatialFilterEnabled.overrideState = true;
            spatialSampleCount.value = settings.spatialSampleCount;
            spatialSampleCount.overrideState = true;
            spatialRadius.value = settings.spatialRadius;
            spatialRadius.overrideState = true;
            temporalPostFilter.value = settings.temporalPostFilter;
            temporalPostFilter.overrideState = true;
            lookupSampleCount.value = settings.lookupSampleCount;
            lookupSampleCount.overrideState = true;
            upsamplingKernelSize.value = settings.upsamplingKernelSize;
            upsamplingKernelSize.overrideState = true;
            upsamplingSampleCount.value = settings.upsamplingSampleCount;
            upsamplingSampleCount.overrideState = true;
        }

        public PresetQuality GetPresetQuality()
        {
            if(HasPresetOverridesEnabled())
            {
                if (HasPresetQualityValues (PresetQuality.Low)) return PresetQuality.Low;
                if (HasPresetQualityValues (PresetQuality.Medium)) return PresetQuality.Medium;
                if (HasPresetQualityValues (PresetQuality.High)) return PresetQuality.High;
                if (HasPresetQualityValues(PresetQuality.Ultra)) return PresetQuality.Ultra;
            }
            return PresetQuality.Custom;
        }

        public bool HasPresetOverridesEnabled()
        {
            return multiBounce.overrideState == true
                && bouncePatchAllocation.overrideState == true
                && sampleCount.overrideState == true
                && temporalSmoothing.overrideState == true
                && spatialFilterEnabled.overrideState == true
                && spatialSampleCount.overrideState == true
                && spatialRadius.overrideState == true
                && temporalPostFilter.overrideState == true
                && lookupSampleCount.overrideState == true
                && upsamplingKernelSize.overrideState == true
                && upsamplingSampleCount.overrideState == true;
        }

        public bool HasPresetQualityValues(PresetQuality quality)
        {
            if (quality == PresetQuality.Custom) return true;
            ref readonly var preset = ref k_Presets[(int)quality];

            return multiBounce.value == preset.multiBounce
                && bouncePatchAllocation.value == preset.bouncePatchAllocation
                && sampleCount.value == preset.sampleCount
                && temporalSmoothing.value == preset.temporalSmoothing
                && spatialFilterEnabled.value == preset.spatialFilterEnabled
                && spatialSampleCount.value == preset.spatialSampleCount
                && spatialRadius.value == preset.spatialRadius
                && temporalPostFilter.value == preset.temporalPostFilter
                && lookupSampleCount.value == preset.lookupSampleCount
                && upsamplingKernelSize.value == preset.upsamplingKernelSize
                && upsamplingSampleCount.value == preset.upsamplingSampleCount;
        }

#if UNITY_EDITOR
        internal event Action propertyChanged;
        private void OnValidate() => propertyChanged?.Invoke();
#endif
    }
}

#endif

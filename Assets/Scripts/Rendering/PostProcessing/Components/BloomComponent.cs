#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Kern.Rendering.PostProcessing
{
    [Serializable]
    [VolumeComponentMenu("Kern/Bloom")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class BloomComponent : VolumeComponent, IPostProcessComponent
    {
        // Unity Volume serialization and the existing profile use these stable
        // lower-case field names; changing them would orphan serialized overrides.
#pragma warning disable SA1307
        [Tooltip("Strength of the glow added around pixels brighter than Threshold.")]
        public ClampedFloatParameter intensity = PostProcessDefaults.BloomIntensity();

        [Tooltip("Minimum source brightness that contributes to Bloom.")]
        public ClampedFloatParameter threshold = PostProcessDefaults.BloomThreshold();

        [Tooltip("Threshold transition width as a fraction of Threshold.")]
        public ClampedFloatParameter softKnee = PostProcessDefaults.BloomSoftKnee();

        [Tooltip("Dual Kawase sampling radius in source texels.")]
        public ClampedFloatParameter radius = PostProcessDefaults.BloomRadius();

        [Tooltip("How strongly reconstructed wide glow is mixed with the local glow.")]
        public ClampedFloatParameter scatter = PostProcessDefaults.BloomScatter();

        [Tooltip("Color multiplier applied to the glow.")]
        public ColorParameter tint = PostProcessDefaults.BloomTint();

        public bool IsActive() => intensity.value > 0f;
        public bool IsTileCompatible() => true;
#pragma warning restore SA1307
    }
}

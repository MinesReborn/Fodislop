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
        [Tooltip("Strength of the glow added around pixels brighter than Threshold.")]
        public FloatParameter intensity = new(PostProcessLook.Bloom.Intensity);

        [Tooltip("Minimum source brightness that contributes to Bloom.")]
        public FloatParameter threshold = new(PostProcessLook.Bloom.Threshold);

        [Tooltip("Threshold transition width as a fraction of Threshold.")]
        public FloatParameter softKnee = new(PostProcessLook.Bloom.SoftKnee);

        [Tooltip("Dual Kawase sampling radius in source texels.")]
        public FloatParameter radius = new(PostProcessLook.Bloom.Radius);

        [Tooltip("How strongly reconstructed wide glow is mixed with the local glow.")]
        public FloatParameter scatter = new(PostProcessLook.Bloom.Scatter);

        [Tooltip("Color multiplier applied to the glow.")]
        public ColorParameter tint = new(PostProcessLook.Bloom.Tint);

        public bool IsActive() => intensity.value > 0f;
        public bool IsTileCompatible() => true;
#pragma warning restore SA1307
    }
}

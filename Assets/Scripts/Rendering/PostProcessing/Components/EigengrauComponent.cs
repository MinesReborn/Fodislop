#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Kern.Rendering.PostProcessing
{
    [Serializable]
    [VolumeComponentMenu("Kern/Eigengrau")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class EigengrauComponent : VolumeComponent, IPostProcessComponent
    {
        // Keep the serialized Volume parameter names stable for existing profiles.
#pragma warning disable SA1307
        [Tooltip("Strength of fine animated film grain in near-black areas. This is the effect's only strength control.")]
        public ClampedFloatParameter intensity = PostProcessDefaults.EigengrauIntensity();

        [Tooltip("Subtle tint of the grain in near-black areas.")]
        public ColorParameter color = PostProcessDefaults.EigengrauColor();

        [Tooltip("Maximum perceptual (sRGB) luminance affected by Eigengrau. Fully lit pixels are excluded.")]
        public ClampedFloatParameter darknessThreshold = PostProcessDefaults.EigengrauDarknessThreshold();

        [Tooltip("Film-grain size in physical screen pixels.")]
        public ClampedFloatParameter noiseScale = PostProcessDefaults.EigengrauNoiseScale();

        [Tooltip("How many independent grain patterns are generated per second.")]
        public ClampedFloatParameter animationSpeed = PostProcessDefaults.EigengrauAnimationSpeed();

        public bool IsActive() => intensity.value > 0f;
        public bool IsTileCompatible() => true;
#pragma warning restore SA1307
    }
}

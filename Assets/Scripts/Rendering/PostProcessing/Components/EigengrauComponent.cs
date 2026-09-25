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
        [Tooltip("How far the black point is lifted toward the eigengrau color. 1 means black sits exactly at the eye's own grey.")]
        public ClampedFloatParameter intensity = PostProcessDefaults.EigengrauIntensity();

        [Tooltip("The eigengrau itself: the colour black is lifted to. Default is #16161D, the grey the eye reports in full darkness.")]
        public ColorParameter color = PostProcessDefaults.EigengrauColor();

        [Tooltip("Maximum perceptual (sRGB) luminance affected by Eigengrau. Lit pixels keep their own black point.")]
        public ClampedFloatParameter darknessThreshold = PostProcessDefaults.EigengrauDarknessThreshold();

        [Tooltip("Size of the retinal noise cells, in physical screen pixels.")]
        public ClampedFloatParameter noiseScale = PostProcessDefaults.EigengrauNoiseScale();

        public bool IsActive() => intensity.value > 0f;
        public bool IsTileCompatible() => true;
#pragma warning restore SA1307
    }
}

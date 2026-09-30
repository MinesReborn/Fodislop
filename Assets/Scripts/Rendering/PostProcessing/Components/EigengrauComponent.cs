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
        [Tooltip("Strength of the noise visible in nearly black areas. Does not raise the scene's black point.")]
        public FloatParameter intensity = new(PostProcessLook.FilmGrain.Intensity);

        [Tooltip("Reference level for the noise amplitude. Default is #16161D; it does not tint the scene.")]
        public ColorParameter color = new(PostProcessLook.FilmGrain.Color);

        [Tooltip("Maximum perceptual (sRGB) luminance with visible noise. Brighter pixels are unchanged.")]
        public FloatParameter darknessThreshold = new(PostProcessLook.FilmGrain.DarknessThreshold);

        [Tooltip("Size of the retinal noise cells, in physical screen pixels.")]
        public FloatParameter noiseScale = new(PostProcessLook.FilmGrain.NoiseScale);

        public bool IsActive() => intensity.value > 0f;
        public bool IsTileCompatible() => true;
    }
}

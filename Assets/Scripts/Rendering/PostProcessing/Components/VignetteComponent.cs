#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Kern.Rendering.PostProcessing
{
    [Serializable]
    [VolumeComponentMenu("Kern/Vignette")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class VignetteComponent : VolumeComponent, IPostProcessComponent
    {
        // Keep the serialized Volume parameter names stable for existing profiles.
        [Tooltip("Opacity of the edge darkening. Zero disables the effect.")]
        public FloatParameter intensity = new(PostProcessLook.Vignette.Intensity);

        [Tooltip("Color applied at the screen edges.")]
        public ColorParameter color = new(PostProcessLook.Vignette.Color);

        [Tooltip("Width of the feathered transition between center and edges.")]
        public FloatParameter smoothness = new(PostProcessLook.Vignette.Smoothness);

        [Tooltip("Normalized center of the vignette.")]
        public Vector2Parameter center = new(PostProcessLook.Vignette.Center);

        public bool IsActive() => intensity.value > 0f;
        public bool IsTileCompatible() => true;
#pragma warning restore SA1307
    }
}

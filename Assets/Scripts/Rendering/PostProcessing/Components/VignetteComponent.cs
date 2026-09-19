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
#pragma warning disable SA1307
        [Tooltip("Opacity of the edge darkening. Zero disables the effect.")]
        public ClampedFloatParameter intensity = PostProcessDefaults.VignetteIntensity();

        [Tooltip("Color applied at the screen edges.")]
        public ColorParameter color = PostProcessDefaults.VignetteColor();

        [Tooltip("Width of the feathered transition between center and edges.")]
        public ClampedFloatParameter smoothness = PostProcessDefaults.VignetteSmoothness();

        [Tooltip("Normalized center of the vignette.")]
        public Vector2Parameter center = PostProcessDefaults.VignetteCenter();

        public bool IsActive() => intensity.value > 0f;
        public bool IsTileCompatible() => true;
#pragma warning restore SA1307
    }
}

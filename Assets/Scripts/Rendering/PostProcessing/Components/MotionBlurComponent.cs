#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Kern.Rendering.PostProcessing
{
    [Serializable]
    [VolumeComponentMenu("Kern/Motion Blur")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class MotionBlurComponent : VolumeComponent, IPostProcessComponent
    {
        // Keep the serialized Volume parameter name stable for existing profiles.
#pragma warning disable SA1307
        [Tooltip("Temporal motion blur strength for the gameplay camera.")]
        public ClampedFloatParameter intensity = PostProcessDefaults.MotionBlurIntensity();

        public bool IsActive() => intensity.value > 0f;
        public bool IsTileCompatible() => true;
#pragma warning restore SA1307
    }
}

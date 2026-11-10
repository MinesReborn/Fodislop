#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Kern.Rendering.PostProcessing
{
    [Serializable]
    [VolumeComponentMenu("Kern/Chromatic Aberration")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class ChromaticAberrationComponent : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Radial RGB fringing, strongest at the frame edges. Zero disables the effect.")]
        public FloatParameter intensity = new(PostProcessLook.ChromaticAberration.Intensity);

        public bool IsActive() => intensity.value > 0f;

        public bool IsTileCompatible() => true;
    }
}

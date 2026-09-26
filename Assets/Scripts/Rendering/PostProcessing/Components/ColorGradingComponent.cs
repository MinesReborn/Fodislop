#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Kern.Rendering.PostProcessing
{
    [Serializable]
    [VolumeComponentMenu("Kern/Color Grading")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class ColorGradingComponent : VolumeComponent, IPostProcessComponent
    {
        // Keep the serialized Volume parameter names stable for existing profiles.
        [Tooltip("Exposure compensation in stops. Zero is neutral.")]
        public FloatParameter exposure = new(PostProcessLook.ColorGrading.Exposure);

        [Tooltip("Multiplicative color filter. White is neutral.")]
        public ColorParameter colorFilter = new(PostProcessLook.ColorGrading.Filter);

        [Tooltip("Contrast adjustment. Zero is neutral.")]
        public FloatParameter contrast = new(PostProcessLook.ColorGrading.Contrast);

        [Tooltip("Color saturation. One is neutral, zero is grayscale.")]
        public FloatParameter saturation = new(PostProcessLook.ColorGrading.Saturation);

        // Сжатие динамического диапазона удалено целиком, поэтому
        // компонент активен только при действительной цветокоррекции.
        public bool IsActive() => exposure.value != 0f ||
                                 colorFilter.value != Color.white ||
                                 contrast.value != 0f ||
                                 saturation.value != 1f;
        public bool IsTileCompatible() => true;
#pragma warning restore SA1307
    }
}

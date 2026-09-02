#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Fodinae.Rendering.PostProcessing
{
    [Serializable]
    [VolumeComponentMenu("Fodinae/Color Grading")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class ColorGradingComponent : VolumeComponent, IPostProcessComponent
    {
        // Keep the serialized Volume parameter names stable for existing profiles.
#pragma warning disable SA1307
        [Tooltip("Exposure compensation in stops. Zero is neutral.")]
        public ClampedFloatParameter exposure = PostProcessDefaults.ColorGradingExposure();

        [Tooltip("Multiplicative color filter. White is neutral.")]
        public ColorParameter colorFilter = PostProcessDefaults.ColorGradingFilter();

        [Tooltip("Contrast adjustment. Zero is neutral.")]
        public ClampedFloatParameter contrast = PostProcessDefaults.ColorGradingContrast();

        [Tooltip("Color saturation. One is neutral, zero is grayscale.")]
        public ClampedFloatParameter saturation = PostProcessDefaults.ColorGradingSaturation();

        [Tooltip("HDR luminance mapped to SDR display white.")]
        public ClampedFloatParameter toneMappingWhitePoint = PostProcessDefaults.ColorGradingWhitePoint();

        // Тонмап сюда не входит: он не эффект грейдинга, а обязательное
        // сжатие HDR в диапазон дисплея, и работает независимо от того,
        // нейтрален ли грейдинг.
        public bool IsActive() => exposure.value != 0f ||
                                 colorFilter.value != Color.white ||
                                 contrast.value != 0f ||
                                 saturation.value != 1f;
        public bool IsTileCompatible() => true;
#pragma warning restore SA1307
    }
}

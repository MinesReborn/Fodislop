using System;

namespace UnityEngine.Rendering.Universal
{
    public enum TonemappingMode
    {
        None,

        Neutral, // Neutral tonemapper

        ACES, // ACES Filmic reference tonemapper (custom approximation)
    }

    public enum NeutralRangeReductionMode
    {
        Reinhard = HDRRangeReduction.Reinhard,
        BT2390 = HDRRangeReduction.BT2390
    }

    public enum HDRACESPreset
    {
        ACES1000Nits = HDRRangeReduction.ACES1000Nits,
        ACES2000Nits = HDRRangeReduction.ACES2000Nits,
        ACES4000Nits = HDRRangeReduction.ACES4000Nits,
    }

    /// <example>
    /// <para>This sample code shows how settings can be retrieved and modified in runtime:</para>
    /// <code>
    /// using System;
    /// using UnityEngine;
    /// using UnityEngine.Rendering;
    /// using UnityEngine.Rendering.Universal;
    ///
    /// public class ModifyVolumeComponent : MonoBehaviour
    /// {
    ///     [SerializeField] VolumeProfile volumeProfile;
    ///     [SerializeField] VolumeSettings volumeSettings;
    ///
    ///     private bool m_HasRetrievedVolumeComponent;
    ///     private Tonemapping m_VolumeComponent;
    ///
    ///     [Serializable]
    ///     private struct VolumeSettings
    ///     {
    ///         public bool active;
    ///         public TonemappingModeParameter mode;
    ///         public NeutralRangeReductionModeParameter neutralHDRRangeReductionMode;
    ///         public HDRACESPresetParameter acesPreset;
    ///         public ClampedFloatParameter hueShiftAmount;
    ///         public BoolParameter detectPaperWhite;
    ///         public ClampedFloatParameter paperWhite;
    ///         public BoolParameter detectBrightnessLimits;
    ///         public ClampedFloatParameter minNits;
    ///         public ClampedFloatParameter maxNits;
    ///
    ///
    ///         public void SetVolumeComponentSettings(ref Tonemapping volumeComponent)
    ///         {
    ///             volumeComponent.active = active;
    ///             volumeComponent.mode = mode;
    ///             volumeComponent.neutralHDRRangeReductionMode = neutralHDRRangeReductionMode;
    ///             volumeComponent.acesPreset = acesPreset;
    ///             volumeComponent.hueShiftAmount = hueShiftAmount;
    ///             volumeComponent.detectPaperWhite = detectPaperWhite;
    ///             volumeComponent.paperWhite = paperWhite;
    ///             volumeComponent.detectBrightnessLimits = detectBrightnessLimits;
    ///             volumeComponent.minNits = minNits;
    ///             volumeComponent.maxNits = maxNits;
    ///         }
    ///
    ///         public void GetVolumeComponentSettings(ref Tonemapping volumeComponent)
    ///         {
    ///             active = volumeComponent.active;
    ///             mode = volumeComponent.mode;
    ///             neutralHDRRangeReductionMode = volumeComponent.neutralHDRRangeReductionMode;
    ///             acesPreset = volumeComponent.acesPreset;
    ///             hueShiftAmount = volumeComponent.hueShiftAmount;
    ///             detectPaperWhite = volumeComponent.detectPaperWhite;
    ///             paperWhite = volumeComponent.paperWhite;
    ///             detectBrightnessLimits = volumeComponent.detectBrightnessLimits;
    ///             minNits = volumeComponent.minNits;
    ///             maxNits = volumeComponent.maxNits;
    ///         }
    ///     }
    ///
    ///     private void Start()
    ///     {
    ///         m_HasRetrievedVolumeComponent = GetVolumeComponent(in volumeProfile, ref m_VolumeComponent);
    ///         if (m_HasRetrievedVolumeComponent)
    ///             volumeSettings.GetVolumeComponentSettings(ref m_VolumeComponent);
    ///     }
    ///
    ///     private void Update()
    ///     {
    ///         if (!m_HasRetrievedVolumeComponent)
    ///             return;
    ///
    ///         volumeSettings.SetVolumeComponentSettings(ref m_VolumeComponent);
    ///     }
    ///
    ///     private static bool GetVolumeComponent(in VolumeProfile volumeProfile, ref Tonemapping volumeComponent)
    ///     {
    ///         if (volumeComponent != null)
    ///             return true;
    ///
    ///         if (volumeProfile == null)
    ///         {
    ///             Debug.LogError("ModifyVolumeComponent.GetVolumeComponent():\nvolumeProfile has not been assigned.");
    ///             return false;
    ///         }
    ///
    ///         volumeProfile.TryGet(out Tonemapping component);
    ///         if (component == null)
    ///         {
    ///             Debug.LogError($"ModifyVolumeComponent.GetVolumeComponent():\nMissing component in the \"{volumeProfile.name}\" VolumeProfile ");
    ///             return false;
    ///         }
    ///
    ///         volumeComponent = component;
    ///         return true;
    ///     }
    /// }
    /// </code>
    /// </example>
    /// <seealso cref="VolumeProfile"/>
    /// <seealso cref="VolumeComponent"/>
    /// <seealso cref="IPostProcessComponent"/>
    /// <seealso cref="VolumeParameter{T}"/>
    /// <seealso cref="TonemappingModeParameter"/>
    /// <seealso cref="NeutralRangeReductionModeParameter"/>
    /// <seealso cref="HDRACESPresetParameter"/>
    /// <seealso cref="ClampedFloatParameter"/>
    /// <seealso cref="BoolParameter"/>
    [Serializable, VolumeComponentMenu("Post-processing/Tonemapping")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [URPHelpURL("urp/post-processing-tonemapping")]
    public sealed class Tonemapping : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Select a tonemapping algorithm to use for the color grading process.")]
        public TonemappingModeParameter mode = new TonemappingModeParameter(TonemappingMode.None);

        // -- HDR Output options --

        [AdditionalProperty]
        [Tooltip("Specifies the range reduction mode used when HDR output is enabled and Neutral tonemapping is enabled.")]
        public NeutralRangeReductionModeParameter neutralHDRRangeReductionMode = new NeutralRangeReductionModeParameter(NeutralRangeReductionMode.BT2390);

        [Tooltip("Use the ACES preset for HDR displays.")]
        public HDRACESPresetParameter acesPreset = new HDRACESPresetParameter(HDRACESPreset.ACES1000Nits);

        [Tooltip("Specify how much hue to preserve. Values closer to 0 are likely to preserve hue. As values get closer to 1, Unity doesn't correct hue shifts.")]
        public ClampedFloatParameter hueShiftAmount = new ClampedFloatParameter(0.0f, 0.0f, 1.0f);

        [Tooltip("Enable to use values detected from the output device as paper white. When enabled, output images might differ between SDR and HDR. For best accuracy, set this value manually.")]
        public BoolParameter detectPaperWhite = new BoolParameter(false);

        [Tooltip("The reference brightness of a paper white surface. This property determines the maximum brightness of UI. The brightness of the scene is scaled relative to this value. The value is in nits.")]
        public ClampedFloatParameter paperWhite = new ClampedFloatParameter(300.0f, 0.0f, 400.0f);

        [Tooltip("Enable to use the minimum and maximum brightness values detected from the output device. For best accuracy, considering calibrating these values manually.")]
        public BoolParameter detectBrightnessLimits = new BoolParameter(true);

        [Tooltip("The minimum brightness of the screen (in nits). This value is assumed to be 0.005f with ACES Tonemap.")]
        public ClampedFloatParameter minNits = new ClampedFloatParameter(0.005f, 0.0f, 50.0f);

        [Tooltip("The maximum brightness of the screen (in nits). This value is defined by the preset when using ACES Tonemap.")]
        public ClampedFloatParameter maxNits = new ClampedFloatParameter(1000.0f, 0.0f, 5000.0f);

        /// <returns><c>true</c> if the effect should be rendered, <c>false</c> otherwise.</returns>
        public bool IsActive() => mode.value != TonemappingMode.None;

        /// <returns><c>true</c> if it can run on-tile, <c>false</c> otherwise.</returns>
        [Obsolete("Unused. #from(2023.1)")]
        public bool IsTileCompatible() => true;
    }

    [Serializable]
    public sealed class TonemappingModeParameter : VolumeParameter<TonemappingMode>
    {
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public TonemappingModeParameter(TonemappingMode value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class NeutralRangeReductionModeParameter : VolumeParameter<NeutralRangeReductionMode>
    {
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public NeutralRangeReductionModeParameter(NeutralRangeReductionMode value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class HDRACESPresetParameter : VolumeParameter<HDRACESPreset>
    {
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public HDRACESPresetParameter(HDRACESPreset value, bool overrideState = false) : base(value, overrideState) { }
    }
}

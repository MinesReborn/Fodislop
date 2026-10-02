using System;

namespace UnityEngine.Rendering.Universal
{
    public enum MotionBlurMode
    {
        CameraOnly,

        CameraAndObjects
    }

    public enum MotionBlurQuality
    {
        Low,

        Medium,

        High
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
    ///     private MotionBlur m_VolumeComponent;
    ///
    ///     [Serializable]
    ///     private struct VolumeSettings
    ///     {
    ///         public bool active;
    ///         public MotionBlurModeParameter mode;
    ///         public MotionBlurQualityParameter quality;
    ///         public ClampedFloatParameter intensity;
    ///         public ClampedFloatParameter clamp;
    ///
    ///         public void SetVolumeComponentSettings(ref MotionBlur volumeComponent)
    ///         {
    ///             volumeComponent.active = active;
    ///             volumeComponent.mode = mode;
    ///             volumeComponent.quality = quality;
    ///             volumeComponent.intensity = intensity;
    ///             volumeComponent.clamp = clamp;
    ///         }
    ///
    ///         public void GetVolumeComponentSettings(ref MotionBlur volumeComponent)
    ///         {
    ///             active = volumeComponent.active;
    ///             mode = volumeComponent.mode;
    ///             quality = volumeComponent.quality;
    ///             intensity = volumeComponent.intensity;
    ///             clamp = volumeComponent.clamp;
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
    ///     private static bool GetVolumeComponent(in VolumeProfile volumeProfile, ref MotionBlur volumeComponent)
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
    ///         volumeProfile.TryGet(out MotionBlur component);
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
    /// <seealso cref="MotionBlurModeParameter"/>
    /// <seealso cref="MotionBlurQualityParameter"/>
    /// <seealso cref="ClampedFloatParameter"/>
    [Serializable, VolumeComponentMenu("Post-processing/Motion Blur")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [URPHelpURL("urp/Post-Processing-Motion-Blur")]
    public sealed class MotionBlur : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("The motion blur technique to use. If you don't need object motion blur, CameraOnly will result in better performance.")]
        public MotionBlurModeParameter mode = new MotionBlurModeParameter(MotionBlurMode.CameraOnly);

        [Tooltip("The quality of the effect. Lower presets will result in better performance at the expense of visual quality.")]
        public MotionBlurQualityParameter quality = new MotionBlurQualityParameter(MotionBlurQuality.Low);

        [Tooltip("The strength of the motion blur filter. Acts as a multiplier for velocities.")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0f, 1f);

        [Tooltip("Sets the maximum length, as a fraction of the screen's full resolution, that the velocity resulting from Camera rotation can have. Lower values will improve performance.")]
        public ClampedFloatParameter clamp = new ClampedFloatParameter(0.05f, 0f, 0.2f);

        /// <returns><c>true</c> if the effect should be rendered, <c>false</c> otherwise.</returns>
        public bool IsActive() => intensity.value > 0f;

        /// <returns><c>true</c> if it can run on-tile, <c>false</c> otherwise.</returns>
        [Obsolete("Unused. #from(2023.1)")]
        public bool IsTileCompatible() => false;
    }

    [Serializable]
    public sealed class MotionBlurModeParameter : VolumeParameter<MotionBlurMode>
    {
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public MotionBlurModeParameter(MotionBlurMode value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class MotionBlurQualityParameter : VolumeParameter<MotionBlurQuality>
    {
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public MotionBlurQualityParameter(MotionBlurQuality value, bool overrideState = false) : base(value, overrideState) { }
    }
}

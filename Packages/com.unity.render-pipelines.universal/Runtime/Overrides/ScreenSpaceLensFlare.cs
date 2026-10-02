using System;

namespace UnityEngine.Rendering.Universal
{
    public enum ScreenSpaceLensFlareResolution : int
    {
        Half = 2,

        Quarter = 4,

        Eighth = 8
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
    ///     private ScreenSpaceLensFlare m_VolumeComponent;
    ///
    ///     [Serializable]
    ///     private struct VolumeSettings
    ///     {
    ///         public bool active;
    ///         public MinFloatParameter intensity;
    ///         public ColorParameter tintColor;
    ///         public ClampedIntParameter bloomMip;
    ///         public MinFloatParameter firstFlareIntensity;
    ///         public MinFloatParameter secondaryFlareIntensity;
    ///         public MinFloatParameter warpedFlareIntensity;
    ///         public Vector2Parameter warpedFlareScale;
    ///         public ClampedIntParameter samples;
    ///         public ClampedFloatParameter sampleDimmer;
    ///         public ClampedFloatParameter vignetteEffect;
    ///         public ClampedFloatParameter startingPosition;
    ///         public ClampedFloatParameter scale;
    ///         public MinFloatParameter streaksIntensity;
    ///         public ClampedFloatParameter streaksLength;
    ///         public FloatParameter streaksOrientation;
    ///         public ClampedFloatParameter streaksThreshold;
    ///         public ScreenSpaceLensFlareResolutionParameter resolution;
    ///         public ClampedFloatParameter chromaticAbberationIntensity;
    ///
    ///
    ///         public void SetVolumeComponentSettings(ref ScreenSpaceLensFlare volumeComponent)
    ///         {
    ///             volumeComponent.active = active;
    ///             volumeComponent.firstFlareIntensity = firstFlareIntensity;
    ///             volumeComponent.secondaryFlareIntensity = secondaryFlareIntensity;
    ///             volumeComponent.warpedFlareIntensity = warpedFlareIntensity;
    ///             volumeComponent.warpedFlareScale = warpedFlareScale;
    ///             volumeComponent.samples = samples;
    ///             volumeComponent.sampleDimmer = sampleDimmer;
    ///             volumeComponent.vignetteEffect = vignetteEffect;
    ///             volumeComponent.startingPosition = startingPosition;
    ///             volumeComponent.scale = scale;
    ///             volumeComponent.streaksIntensity = streaksIntensity;
    ///             volumeComponent.streaksLength = streaksLength;
    ///             volumeComponent.streaksOrientation = streaksOrientation;
    ///             volumeComponent.streaksThreshold = streaksThreshold;
    ///             volumeComponent.resolution = resolution;
    ///             volumeComponent.chromaticAbberationIntensity = chromaticAbberationIntensity;
    ///         }
    ///
    ///         public void GetVolumeComponentSettings(ref ScreenSpaceLensFlare volumeComponent)
    ///         {
    ///             active = volumeComponent.active;
    ///             firstFlareIntensity = volumeComponent.firstFlareIntensity;
    ///             secondaryFlareIntensity = volumeComponent.secondaryFlareIntensity;
    ///             warpedFlareIntensity = volumeComponent.warpedFlareIntensity;
    ///             warpedFlareScale = volumeComponent.warpedFlareScale;
    ///             samples = volumeComponent.samples;
    ///             sampleDimmer = volumeComponent.sampleDimmer;
    ///             vignetteEffect = volumeComponent.vignetteEffect;
    ///             startingPosition = volumeComponent.startingPosition;
    ///             scale = volumeComponent.scale;
    ///             streaksIntensity = volumeComponent.streaksIntensity;
    ///             streaksLength = volumeComponent.streaksLength;
    ///             streaksOrientation = volumeComponent.streaksOrientation;
    ///             streaksThreshold = volumeComponent.streaksThreshold;
    ///             resolution = volumeComponent.resolution;
    ///             chromaticAbberationIntensity = volumeComponent.chromaticAbberationIntensity;
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
    ///     private static bool GetVolumeComponent(in VolumeProfile volumeProfile, ref ScreenSpaceLensFlare volumeComponent)
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
    ///         volumeProfile.TryGet(out ScreenSpaceLensFlare component);
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
    /// <seealso cref="MinFloatParameter"/>
    /// <seealso cref="ColorParameter"/>
    /// <seealso cref="ClampedIntParameter"/>
    /// <seealso cref="Vector2Parameter"/>
    /// <seealso cref="ClampedFloatParameter"/>
    /// <seealso cref="FloatParameter"/>
    /// <seealso cref="ScreenSpaceLensFlareResolutionParameter"/>
    [Serializable, VolumeComponentMenu("Post-processing/Screen Space Lens Flare")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [URPHelpURL("urp/shared/lens-flare/lens-flare-component")]
    [DisplayInfo(name = "Screen Space Lens Flare")]
    public class ScreenSpaceLensFlare : VolumeComponent, IPostProcessComponent
    {
        public MinFloatParameter intensity = new MinFloatParameter(0f, 0f);
        public ColorParameter tintColor = new ColorParameter(Color.white);
        [AdditionalProperty]
        public ClampedIntParameter bloomMip = new ClampedIntParameter(1, 0, 5);
        [Header("Flares")]
        public MinFloatParameter firstFlareIntensity = new MinFloatParameter(1f, 0f);
        public MinFloatParameter secondaryFlareIntensity = new MinFloatParameter(1f, 0f);
        public MinFloatParameter warpedFlareIntensity = new MinFloatParameter(1f, 0f);
        [AdditionalProperty]
        public Vector2Parameter warpedFlareScale = new Vector2Parameter(new Vector2(1f, 1f));
        public ClampedIntParameter samples = new ClampedIntParameter(1, 1, 3);
        [AdditionalProperty]
        public ClampedFloatParameter sampleDimmer = new ClampedFloatParameter(0.5f, 0.1f, 1f);
        public ClampedFloatParameter vignetteEffect = new ClampedFloatParameter(1f, 0f, 1f);
        public ClampedFloatParameter startingPosition = new ClampedFloatParameter(1.25f, 1f, 3f);
        public ClampedFloatParameter scale = new ClampedFloatParameter(1.5f, 1f, 4f);
        [Header("Streaks")]
        public MinFloatParameter streaksIntensity = new MinFloatParameter(0f, 0f);
        public ClampedFloatParameter streaksLength = new ClampedFloatParameter(0.5f, 0f, 1f);
        public FloatParameter streaksOrientation = new FloatParameter(0f);
        public ClampedFloatParameter streaksThreshold = new ClampedFloatParameter(0.25f, 0f, 1f);
        [SerializeField]
        [AdditionalProperty]
        public ScreenSpaceLensFlareResolutionParameter resolution = new ScreenSpaceLensFlareResolutionParameter(ScreenSpaceLensFlareResolution.Quarter);
        [Header("Chromatic Abberation")]
        public ClampedFloatParameter chromaticAbberationIntensity = new ClampedFloatParameter(0.5f, 0f, 1f);
        
        /// <returns><c>true</c> if the effect should be rendered, <c>false</c> otherwise.</returns>
        public bool IsActive()
        {
            return intensity.value > 0;
        }

        /// <returns></returns>
        public bool IsStreaksActive()
        {
            return streaksIntensity.value > 0;
        }


        /// <returns><c>true</c> if it can run on-tile, <c>false</c> otherwise.</returns>
        [Obsolete("Unused. #from(2023.1)")]
        public bool IsTileCompatible() => false;
    }

    [Serializable]
    public sealed class ScreenSpaceLensFlareResolutionParameter : VolumeParameter<ScreenSpaceLensFlareResolution>
    {
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public ScreenSpaceLensFlareResolutionParameter(ScreenSpaceLensFlareResolution value, bool overrideState = false) : base(value, overrideState) { }
    }

}

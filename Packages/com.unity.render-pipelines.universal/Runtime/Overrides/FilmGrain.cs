using System;

namespace UnityEngine.Rendering.Universal
{
    public enum FilmGrainLookup
    {
        Thin1,

        Thin2,

        Medium1,

        Medium2,

        Medium3,

        Medium4,

        Medium5,

        Medium6,

        Large01,

        Large02,

        /// <seealso cref="FilmGrain.texture"/>
        Custom
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
    ///     private FilmGrain m_VolumeComponent;
    ///
    ///     [Serializable]
    ///     private struct VolumeSettings
    ///     {
    ///         public bool active;
    ///         public FilmGrainLookupParameter type;
    ///         public ClampedFloatParameter intensity;
    ///         public ClampedFloatParameter response;
    ///         public NoInterpTextureParameter texture;
    ///
    ///         public void SetVolumeComponentSettings(ref FilmGrain volumeComponent)
    ///         {
    ///             volumeComponent.active = active;
    ///             volumeComponent.type = type;
    ///             volumeComponent.intensity = intensity;
    ///             volumeComponent.response = response;
    ///             volumeComponent.texture = texture;
    ///         }
    ///
    ///         public void GetVolumeComponentSettings(ref FilmGrain volumeComponent)
    ///         {
    ///             active = volumeComponent.active;
    ///             type = volumeComponent.type;
    ///             intensity = volumeComponent.intensity;
    ///             response = volumeComponent.response;
    ///             texture = volumeComponent.texture;
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
    ///     private static bool GetVolumeComponent(in VolumeProfile volumeProfile, ref FilmGrain volumeComponent)
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
    ///         volumeProfile.TryGet(out FilmGrain component);
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
    /// <seealso cref="FilmGrainLookupParameter"/>
    /// <seealso cref="ClampedFloatParameter"/>
    /// <seealso cref="NoInterpTextureParameter"/>
    [Serializable, VolumeComponentMenu("Post-processing/Film Grain")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [URPHelpURL("urp/Post-Processing-Film-Grain")]
    public sealed class FilmGrain : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("The type of grain to use. You can select a preset or provide your own texture by selecting Custom.")]
        public FilmGrainLookupParameter type = new FilmGrainLookupParameter(FilmGrainLookup.Thin1);

        [Tooltip("Use the slider to set the strength of the Film Grain effect.")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0f, 1f);

        [Tooltip("Controls the noisiness response curve based on scene luminance. Higher values mean less noise in light areas.")]
        public ClampedFloatParameter response = new ClampedFloatParameter(0.8f, 0f, 1f);

        [Tooltip("A tileable texture to use for the grain. The neutral value is 0.5 where no grain is applied.")]
        public NoInterpTextureParameter texture = new NoInterpTextureParameter(null);

        /// <returns><c>true</c> if the effect should be rendered, <c>false</c> otherwise.</returns>
        public bool IsActive() => intensity.value > 0f && (type.value != FilmGrainLookup.Custom || texture.value != null);

        /// <returns><c>true</c> if it can run on-tile, <c>false</c> otherwise.</returns>
        [Obsolete("Unused. #from(2023.1)")]
        public bool IsTileCompatible() => true;
    }

    [Serializable]
    public sealed class FilmGrainLookupParameter : VolumeParameter<FilmGrainLookup>
    {
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public FilmGrainLookupParameter(FilmGrainLookup value, bool overrideState = false) : base(value, overrideState) { }
    }
}

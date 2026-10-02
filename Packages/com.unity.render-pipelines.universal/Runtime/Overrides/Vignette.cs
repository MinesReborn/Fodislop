using System;

namespace UnityEngine.Rendering.Universal
{
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
    ///     private Vignette m_VolumeComponent;
    ///
    ///     [Serializable]
    ///     private struct VolumeSettings
    ///     {
    ///         public bool active;
    ///         public ColorParameter color;
    ///         public Vector2Parameter center;
    ///         public ClampedFloatParameter intensity;
    ///         public ClampedFloatParameter smoothness;
    ///         public BoolParameter rounded;
    ///
    ///
    ///         public void SetVolumeComponentSettings(ref Vignette volumeComponent)
    ///         {
    ///             volumeComponent.active = active;
    ///             volumeComponent.color = color;
    ///             volumeComponent.center = center;
    ///             volumeComponent.intensity = intensity;
    ///             volumeComponent.smoothness = smoothness;
    ///             volumeComponent.rounded = rounded;
    ///         }
    ///
    ///         public void GetVolumeComponentSettings(ref Vignette volumeComponent)
    ///         {
    ///             active = volumeComponent.active;
    ///             color = volumeComponent.color;
    ///             center = volumeComponent.center;
    ///             intensity = volumeComponent.intensity;
    ///             smoothness = volumeComponent.smoothness;
    ///             rounded = volumeComponent.rounded;
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
    ///     private static bool GetVolumeComponent(in VolumeProfile volumeProfile, ref Vignette volumeComponent)
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
    ///         volumeProfile.TryGet(out Vignette component);
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
    /// <seealso cref="ColorParameter"/>
    /// <seealso cref="BoolParameter"/>
    /// <seealso cref="ClampedFloatParameter"/>
    /// <seealso cref="Vector2Parameter"/>
    [Serializable, VolumeComponentMenu("Post-processing/Vignette")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [URPHelpURL("urp/post-processing-vignette")]
    public sealed class Vignette : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Vignette color.")]
        public ColorParameter color = new ColorParameter(Color.black, false, false, true);

        [Tooltip("Sets the vignette center point (screen center is [0.5,0.5]).")]
        public Vector2Parameter center = new Vector2Parameter(new Vector2(0.5f, 0.5f));

        [Tooltip("Use the slider to set the strength of the Vignette effect.")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0f, 1f);

        [Tooltip("Smoothness of the vignette borders.")]
        public ClampedFloatParameter smoothness = new ClampedFloatParameter(0.2f, 0.01f, 1f);

        [Tooltip("Should the vignette be perfectly round or be dependent on the current aspect ratio?")]
        public BoolParameter rounded = new BoolParameter(false);

        /// <returns><c>true</c> if the effect should be rendered, <c>false</c> otherwise.</returns>
        public bool IsActive() => intensity.value > 0f;

        /// <returns><c>true</c> if it can run on-tile, <c>false</c> otherwise.</returns>
        [Obsolete("Unused. #from(2023.1)")]
        public bool IsTileCompatible() => true;
    }
}

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
    ///     private SplitToning m_VolumeComponent;
    ///
    ///     [Serializable]
    ///     private struct VolumeSettings
    ///     {
    ///         public bool active;
    ///         public ColorParameter shadows;
    ///         public ColorParameter highlights;
    ///         public ClampedFloatParameter balance;
    ///
    ///
    ///         public void SetVolumeComponentSettings(ref SplitToning volumeComponent)
    ///         {
    ///             volumeComponent.active = active;
    ///             volumeComponent.shadows = shadows;
    ///             volumeComponent.highlights = highlights;
    ///             volumeComponent.balance = balance;
    ///         }
    ///
    ///         public void GetVolumeComponentSettings(ref SplitToning volumeComponent)
    ///         {
    ///             active = volumeComponent.active;
    ///             shadows = volumeComponent.shadows;
    ///             highlights = volumeComponent.highlights;
    ///             balance = volumeComponent.balance;
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
    ///     private static bool GetVolumeComponent(in VolumeProfile volumeProfile, ref SplitToning volumeComponent)
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
    ///         volumeProfile.TryGet(out SplitToning component);
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
    /// <seealso cref="ClampedFloatParameter"/>
    [Serializable, VolumeComponentMenu("Post-processing/Split Toning")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [URPHelpURL("urp/Post-Processing-Split-Toning")]
    public sealed class SplitToning : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("The color to use for shadows.")]
        public ColorParameter shadows = new ColorParameter(Color.grey, false, false, true);

        [Tooltip("The color to use for highlights.")]
        public ColorParameter highlights = new ColorParameter(Color.grey, false, false, true);

        [Tooltip("Balance between the colors in the highlights and shadows.")]
        public ClampedFloatParameter balance = new ClampedFloatParameter(0f, -100f, 100f);

        /// <returns><c>true</c> if the effect should be rendered, <c>false</c> otherwise.</returns>
        public bool IsActive() => shadows != Color.grey || highlights != Color.grey;

        /// <returns><c>true</c> if it can run on-tile, <c>false</c> otherwise.</returns>
        [Obsolete("Unused. #from(2023.1)")]
        public bool IsTileCompatible() => true;
    }
}

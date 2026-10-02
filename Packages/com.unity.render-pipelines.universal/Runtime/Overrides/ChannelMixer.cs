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
    ///     private ChannelMixer m_VolumeComponent;
    ///
    ///     [Serializable]
    ///     private struct VolumeSettings
    ///     {
    ///         public bool active;
    ///         public ClampedFloatParameter redOutRedIn;
    ///         public ClampedFloatParameter redOutGreenIn;
    ///         public ClampedFloatParameter redOutBlueIn;
    ///         public ClampedFloatParameter greenOutRedIn;
    ///         public ClampedFloatParameter greenOutGreenIn;
    ///         public ClampedFloatParameter greenOutBlueIn;
    ///         public ClampedFloatParameter blueOutRedIn;
    ///         public ClampedFloatParameter blueOutGreenIn;
    ///         public ClampedFloatParameter blueOutBlueIn;
    ///
    ///         public void SetVolumeComponentSettings(ref ChannelMixer volumeComponent)
    ///         {
    ///             volumeComponent.active = active;
    ///             volumeComponent.redOutRedIn = redOutRedIn;
    ///             volumeComponent.redOutGreenIn = redOutGreenIn;
    ///             volumeComponent.redOutBlueIn = redOutBlueIn;
    ///             volumeComponent.greenOutRedIn = greenOutRedIn;
    ///             volumeComponent.greenOutGreenIn = greenOutGreenIn;
    ///             volumeComponent.greenOutBlueIn = greenOutBlueIn;
    ///             volumeComponent.blueOutRedIn = blueOutRedIn;
    ///             volumeComponent.blueOutGreenIn = blueOutGreenIn;
    ///             volumeComponent.blueOutBlueIn = blueOutBlueIn;
    ///         }
    ///
    ///         public void GetVolumeComponentSettings(ref ChannelMixer volumeComponent)
    ///         {
    ///             active = volumeComponent.active;
    ///             redOutRedIn = volumeComponent.redOutRedIn;
    ///             redOutGreenIn = volumeComponent.redOutGreenIn;
    ///             redOutBlueIn = volumeComponent.redOutBlueIn;
    ///             greenOutRedIn = volumeComponent.greenOutRedIn;
    ///             greenOutGreenIn = volumeComponent.greenOutGreenIn;
    ///             greenOutBlueIn = volumeComponent.greenOutBlueIn;
    ///             blueOutRedIn = volumeComponent.blueOutRedIn;
    ///             blueOutGreenIn = volumeComponent.blueOutGreenIn;
    ///             blueOutBlueIn = volumeComponent.blueOutBlueIn;
    ///         }
    ///     }
    ///
    ///     private void Start()
    ///     {
    ///         m_HasRetrievedVolumeComponent = GetVolumeComponent(in volumeProfile, ref m_VolumeComponent);
    ///
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
    ///     private static bool GetVolumeComponent(in VolumeProfile volumeProfile, ref ChannelMixer volumeComponent)
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
    ///         volumeProfile.TryGet(out ChannelMixer component);
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
    /// <seealso cref="ClampedFloatParameter"/>
    [Serializable, VolumeComponentMenu("Post-processing/Channel Mixer")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [URPHelpURL("urp/Post-Processing-Channel-Mixer")]
    public sealed class ChannelMixer : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Modify influence of the red channel in the overall mix.")]
        public ClampedFloatParameter redOutRedIn = new ClampedFloatParameter(100f, -200f, 200f);

        [Tooltip("Modify influence of the green channel in the overall mix.")]
        public ClampedFloatParameter redOutGreenIn = new ClampedFloatParameter(0f, -200f, 200f);

        [Tooltip("Modify influence of the blue channel in the overall mix.")]
        public ClampedFloatParameter redOutBlueIn = new ClampedFloatParameter(0f, -200f, 200f);

        [Tooltip("Modify influence of the red channel in the overall mix.")]
        public ClampedFloatParameter greenOutRedIn = new ClampedFloatParameter(0f, -200f, 200f);

        [Tooltip("Modify influence of the green channel in the overall mix.")]
        public ClampedFloatParameter greenOutGreenIn = new ClampedFloatParameter(100f, -200f, 200f);

        [Tooltip("Modify influence of the blue channel in the overall mix.")]
        public ClampedFloatParameter greenOutBlueIn = new ClampedFloatParameter(0f, -200f, 200f);

        [Tooltip("Modify influence of the red channel in the overall mix.")]
        public ClampedFloatParameter blueOutRedIn = new ClampedFloatParameter(0f, -200f, 200f);

        [Tooltip("Modify influence of the green channel in the overall mix.")]
        public ClampedFloatParameter blueOutGreenIn = new ClampedFloatParameter(0f, -200f, 200f);

        [Tooltip("Modify influence of the blue channel in the overall mix.")]
        public ClampedFloatParameter blueOutBlueIn = new ClampedFloatParameter(100f, -200f, 200f);

        /// <returns><c>true</c> if the effect should be rendered, <c>false</c> otherwise.</returns>
        public bool IsActive()
        {
            return redOutRedIn.value != 100f
                || redOutGreenIn.value != 0f
                || redOutBlueIn.value != 0f
                || greenOutRedIn.value != 0f
                || greenOutGreenIn.value != 100f
                || greenOutBlueIn.value != 0f
                || blueOutRedIn.value != 0f
                || blueOutGreenIn.value != 0f
                || blueOutBlueIn.value != 100f;
        }

        /// <returns><c>true</c> if it can run on-tile, <c>false</c> otherwise.</returns>
        [Obsolete("Unused. #from(2023.1)")]
        public bool IsTileCompatible() => true;
    }
}

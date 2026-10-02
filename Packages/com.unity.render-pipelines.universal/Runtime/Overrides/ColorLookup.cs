using System;
using UnityEngine.Experimental.Rendering;

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
    ///     private ColorLookup m_VolumeComponent;
    ///
    ///     [Serializable]
    ///     private struct VolumeSettings
    ///     {
    ///         public bool active;
    ///         public TextureParameter texture;
    ///         public ClampedFloatParameter contribution;
    ///
    ///         public void SetVolumeComponentSettings(ref ColorLookup volumeComponent)
    ///         {
    ///             volumeComponent.active = active;
    ///             volumeComponent.texture = texture;
    ///             volumeComponent.contribution = contribution;
    ///         }
    ///
    ///         public void GetVolumeComponentSettings(ref ColorLookup volumeComponent)
    ///         {
    ///             active = volumeComponent.active;
    ///             texture = volumeComponent.texture;
    ///             contribution = volumeComponent.contribution;
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
    ///     private static bool GetVolumeComponent(in VolumeProfile volumeProfile, ref ColorLookup volumeComponent)
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
    ///         volumeProfile.TryGet(out ColorLookup component);
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
    /// <seealso cref="TextureParameter"/>
    /// <seealso cref="ClampedFloatParameter"/>
    [Serializable, VolumeComponentMenu("Post-processing/Color Lookup")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [URPHelpURL("urp/integration-with-post-processing")]
    public sealed class ColorLookup : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("A 2D Lookup Texture (LUT) to use for color grading.")]
        public TextureParameter texture = new TextureParameter(null);

        [Tooltip("How much of the lookup texture will contribute to the color grading effect.")]
        public ClampedFloatParameter contribution = new ClampedFloatParameter(0f, 0f, 1f);

        /// <returns><c>true</c> if the effect should be rendered, <c>false</c> otherwise.</returns>
        public bool IsActive() => contribution.value > 0f && ValidateLUT();

        /// <returns><c>true</c> if it can run on-tile, <c>false</c> otherwise.</returns>
        [Obsolete("Unused. #from(2023.1)")]
        public bool IsTileCompatible() => true;

        /// <returns>True if the texture is valid, false otherwise.</returns>
        public bool ValidateLUT()
        {
            var asset = UniversalRenderPipeline.asset;
            if (asset == null || texture.value == null)
                return false;

            int lutSize = asset.colorGradingLutSize;
            if (texture.value.height != lutSize)
                return false;

            bool valid = false;

            switch (texture.value)
            {
                case Texture2D t:
                    valid |= t.width == lutSize * lutSize
                        && !GraphicsFormatUtility.IsSRGBFormat(t.graphicsFormat);
                    break;
                case RenderTexture rt:
                    valid |= rt.dimension == TextureDimension.Tex2D
                        && rt.width == lutSize * lutSize
                        && !rt.sRGB;
                    break;
            }

            return valid;
        }
    }
}

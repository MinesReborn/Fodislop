using System;

namespace UnityEngine.Rendering.Universal
{
    public enum BloomDownscaleMode
    {
        Half,

        Quarter,
    }

    public enum BloomFilterMode
    {
        [Tooltip("Best quality.")]
        Gaussian,

        [Tooltip("Balanced quality and speed.")]
        Dual,

        [Tooltip("Lowest quality. Fastest at low resolutions. Saves memory.")]
        Kawase
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
    ///     private Bloom m_VolumeComponent;
    ///
    ///     [Serializable]
    ///     private struct VolumeSettings
    ///     {
    ///         public bool active;
    ///         public MinFloatParameter threshold;
    ///         public MinFloatParameter intensity;
    ///         public ClampedFloatParameter scatter;
    ///         public MinFloatParameter clamp;
    ///         public ColorParameter tint;
    ///         public BoolParameter highQualityFiltering;
    ///         public DownscaleParameter downscale;
    ///         public ClampedIntParameter maxIterations;
    ///         public TextureParameter dirtTexture;
    ///         public MinFloatParameter dirtIntensity;
    ///
    ///         public void SetVolumeComponentSettings(ref Bloom volumeComponent)
    ///         {
    ///             volumeComponent.active = active;
    ///             volumeComponent.threshold = threshold;
    ///             volumeComponent.intensity = intensity;
    ///             volumeComponent.scatter = scatter;
    ///             volumeComponent.clamp = clamp;
    ///             volumeComponent.tint = tint;
    ///             volumeComponent.highQualityFiltering = highQualityFiltering;
    ///             volumeComponent.downscale = downscale;
    ///             volumeComponent.maxIterations = maxIterations;
    ///             volumeComponent.dirtTexture = dirtTexture;
    ///             volumeComponent.dirtIntensity = dirtIntensity;
    ///         }
    ///
    ///         public void GetVolumeComponentSettings(ref Bloom volumeComponent)
    ///         {
    ///             active = volumeComponent.active;
    ///             threshold = volumeComponent.threshold;
    ///             intensity = volumeComponent.intensity;
    ///             scatter = volumeComponent.scatter;
    ///             clamp = volumeComponent.clamp;
    ///             tint = volumeComponent.tint;
    ///             highQualityFiltering = volumeComponent.highQualityFiltering;
    ///             downscale = volumeComponent.downscale;
    ///             maxIterations = volumeComponent.maxIterations;
    ///             dirtTexture = volumeComponent.dirtTexture;
    ///             dirtIntensity = volumeComponent.dirtIntensity;
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
    ///     private static bool GetVolumeComponent(in VolumeProfile volumeProfile, ref Bloom volumeComponent)
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
    ///         volumeProfile.TryGet(out Bloom component);
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
    /// <seealso cref="ClampedFloatParameter"/>
    /// <seealso cref="ColorParameter"/>
    /// <seealso cref="BoolParameter"/>
    /// <seealso cref="DownscaleParameter"/>
    /// <seealso cref="BloomFilterModeParameter"/>
    /// <seealso cref="ClampedIntParameter"/>
    /// <seealso cref="TextureParameter"/>
    [Serializable, VolumeComponentMenu("Post-processing/Bloom")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [URPHelpURL("urp/post-processing-bloom")]
    public sealed partial class Bloom : VolumeComponent, IPostProcessComponent
    {
        [Header("Bloom")]
        [Tooltip("Filters out pixels under this level of brightness. Value is in gamma-space.")]
        public MinFloatParameter threshold = new MinFloatParameter(0.9f, 0f);

        [Tooltip("Strength of the bloom filter.")]
        public MinFloatParameter intensity = new MinFloatParameter(0f, 0f);

        [Tooltip("Set the radius of the bloom effect.")]
        public ClampedFloatParameter scatter = new ClampedFloatParameter(0.7f, 0f, 1f);

        [Tooltip("Set the maximum intensity that Unity uses to calculate Bloom. If pixels in your Scene are more intense than this, URP renders them at their current intensity, but uses this intensity value for the purposes of Bloom calculations.")]
        public MinFloatParameter clamp = new MinFloatParameter(65472f, 0f);

        [Tooltip("Use the color picker to select a color for the Bloom effect to tint to.")]
        public ColorParameter tint = new ColorParameter(Color.white, false, false, true);

        [Tooltip("Use bicubic sampling instead of bilinear sampling for the upsampling passes. This is slightly more expensive but helps getting smoother visuals.")]
        public BoolParameter highQualityFiltering = new BoolParameter(false);

        [Tooltip("Set the filtering algorithm for the Bloom effect.")]
        public BloomFilterModeParameter filter = new BloomFilterModeParameter(BloomFilterMode.Gaussian);

        [Tooltip("The starting resolution that this effect begins processing."), AdditionalProperty]
        public DownscaleParameter downscale = new DownscaleParameter(BloomDownscaleMode.Half);

        [Tooltip("The maximum number of iterations in the effect processing sequence."), AdditionalProperty]
        public ClampedIntParameter maxIterations = new ClampedIntParameter(6, 2, 8);

        [Header("Lens Dirt")]
        [Tooltip("Dirtiness texture to add smudges or dust to the bloom effect.")]
        public TextureParameter dirtTexture = new TextureParameter(null);

        [Tooltip("Amount of dirtiness.")]
        public MinFloatParameter dirtIntensity = new MinFloatParameter(0f, 0f);

        /// <returns><c>true</c> if the effect should be rendered, <c>false</c> otherwise.</returns>
        public bool IsActive() => intensity.value > 0f;

        /// <returns><c>true</c> if it can run on-tile, <c>false</c> otherwise.</returns>
        [Obsolete("Unused. #from(2023.1)")]
        public bool IsTileCompatible() => false;
    }

    [Serializable]
    public sealed class DownscaleParameter : VolumeParameter<BloomDownscaleMode>
    {
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public DownscaleParameter(BloomDownscaleMode value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class BloomFilterModeParameter : VolumeParameter<BloomFilterMode>
    {
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public BloomFilterModeParameter(BloomFilterMode value, bool overrideState = false) : base(value, overrideState) { }
    }
}

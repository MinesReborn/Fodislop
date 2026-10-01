using System;

namespace UnityEngine.Rendering.Universal
{
    public static class LightExtensions
    {
        /// <param name="light"></param>
        /// <returns>The <c>UniversalAdditionalLightData</c> for this light.</returns>
        /// <see cref="UniversalAdditionalLightData"/>
        public static UniversalAdditionalLightData GetUniversalAdditionalLightData(this Light light)
        {
            var gameObject = light.gameObject;
            bool componentExists = gameObject.TryGetComponent<UniversalAdditionalLightData>(out var lightData);
            if (!componentExists)
                lightData = gameObject.AddComponent<UniversalAdditionalLightData>();

            return lightData;
        }
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    [URPHelpURL("urp/universal-additional-light-data")]
    public partial class UniversalAdditionalLightData : MonoBehaviour, ISerializationCallbackReceiver, IAdditionalData
    {
        [Tooltip("Controls if light Shadow Bias parameters use pipeline settings.")]
        [SerializeField] bool m_UsePipelineSettings = true;

        public bool usePipelineSettings
        {
            get { return m_UsePipelineSettings; }
            set { m_UsePipelineSettings = value; }
        }

        public static readonly int AdditionalLightsShadowResolutionTierCustom = -1;

        public static readonly int AdditionalLightsShadowResolutionTierLow = 0;

        public static readonly int AdditionalLightsShadowResolutionTierMedium = 1;

        public static readonly int AdditionalLightsShadowResolutionTierHigh = 2;

        public static readonly int AdditionalLightsShadowDefaultResolutionTier = AdditionalLightsShadowResolutionTierHigh;

        public static readonly int AdditionalLightsShadowDefaultCustomResolution = 128;

        [NonSerialized] private Light m_Light;

        internal Light light
        {
            get
            {
                if (!m_Light)
                    TryGetComponent(out m_Light);
                return m_Light;
            }
        }

        public static readonly int AdditionalLightsShadowMinimumResolution = 128;

        [Tooltip("Controls if light shadow resolution uses pipeline settings.")]
        [SerializeField] int m_AdditionalLightsShadowResolutionTier = AdditionalLightsShadowDefaultResolutionTier;

        /// <exception cref="InvalidOperationException">Thrown when attempting to set the value outside of play mode.</exception>
        public int additionalLightsShadowResolutionTier
        {
            get { return m_AdditionalLightsShadowResolutionTier; }
            set
            {
                if (!Application.isPlaying)
                    throw new InvalidOperationException("Cannot modify additionalLightsShadowResolutionTier outside of play mode.");
                m_AdditionalLightsShadowResolutionTier = value;
            }
        }

        [SerializeField] bool m_CustomShadowLayers = false;

        public bool customShadowLayers
        {
            get
            {
                return m_CustomShadowLayers;
            }
            set
            {
                if (m_CustomShadowLayers != value)
                {
                    m_CustomShadowLayers = value;
                    SyncLightAndShadowLayers();
                }
            }
        }

        [Tooltip("Controls the size of the cookie mask currently assigned to the light.")]
        public Vector2 lightCookieSize
        {
            get => m_LightCookieSize;
            set => m_LightCookieSize = value;
        }
        [SerializeField] Vector2 m_LightCookieSize = Vector2.one;

        [Tooltip("Controls the offset of the cookie mask currently assigned to the light.")]
        public Vector2 lightCookieOffset
        {
            get => m_LightCookieOffset;
            set => m_LightCookieOffset = value;
        }
        [SerializeField] Vector2 m_LightCookieOffset = Vector2.zero;

        [Tooltip("Controls the filtering quality of soft shadows. Higher quality has lower performance.")]
        public SoftShadowQuality softShadowQuality
        {
            get => m_SoftShadowQuality;
            set => m_SoftShadowQuality = value;
        }
        [SerializeField] SoftShadowQuality m_SoftShadowQuality = SoftShadowQuality.UsePipelineSettings;
        
        [SerializeField] RenderingLayerMask m_RenderingLayersMask = RenderingLayerMask.defaultRenderingLayerMask;

        public RenderingLayerMask renderingLayers
        {
            get => m_RenderingLayersMask;
            set
            {
                if (m_RenderingLayersMask == value) return;
                m_RenderingLayersMask = value;
                SyncLightAndShadowLayers();
            }
        }
        
        [SerializeField] RenderingLayerMask m_ShadowRenderingLayersMask = RenderingLayerMask.defaultRenderingLayerMask;
        
        public RenderingLayerMask shadowRenderingLayers
        {
            get => m_ShadowRenderingLayersMask;
            set
            {
                if (value == m_ShadowRenderingLayersMask) return;
                m_ShadowRenderingLayersMask = value;
                SyncLightAndShadowLayers();
            }
        }

        void SyncLightAndShadowLayers()
        {
            if (light)
                light.renderingLayerMask = m_CustomShadowLayers ? m_ShadowRenderingLayersMask : m_RenderingLayersMask;
        }
        
        enum Version
        {
            Initial = 0,
            RenderingLayers = 2,
            SoftShadowQuality = 3,
            RenderingLayersMask = 4,
            
            Count
        }
        
        [SerializeField] Version m_Version = Version.Count;

        // This piece of code is needed because some objects could have been created before existence of Version enum
        /// <summary>OnBeforeSerialize needed to handle migration before the versioning system was in place.</summary>
        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            if (m_Version == Version.Count) // serializing a newly created object
                m_Version = Version.Count - 1; // mark as up to date
        }

        /// <summary>OnAfterDeserialize needed to handle migration before the versioning system was in place.</summary>
        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            if (m_Version == Version.Count) // deserializing and object without version
                m_Version = Version.Initial; // reset to run the migration
            
            if (m_Version < Version.RenderingLayers)
            {
#pragma warning disable 618 // Obsolete warning
                m_RenderingLayers = (uint)m_LightLayerMask;
                m_ShadowRenderingLayers = (uint)m_ShadowLayerMask;
#pragma warning restore 618 // Obsolete warning
                m_Version = Version.RenderingLayers;
            }

            if (m_Version < Version.SoftShadowQuality)
            {
                // SoftShadowQuality.UsePipelineSettings added at index 0. Bump existing serialized values by 1. e.g. Low(0) -> Low(1).
                m_SoftShadowQuality = (SoftShadowQuality)(Math.Clamp((int)m_SoftShadowQuality + 1, 0, (int)SoftShadowQuality.High));
                m_Version = Version.SoftShadowQuality;
            }
            
            if (m_Version <  Version.RenderingLayersMask)
            {
#pragma warning disable 618 // Obsolete warning
                m_RenderingLayersMask = m_RenderingLayers;
                m_ShadowRenderingLayersMask = m_ShadowRenderingLayers;
#pragma warning restore 618 // Obsolete warning
                m_Version = Version.RenderingLayersMask;
            }
        }
    }
    
    
}

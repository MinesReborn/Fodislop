using System;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
using System.IO;
using ShaderKeywordFilter = UnityEditor.ShaderKeywordFilter;
#endif
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;
using UnityEngine.Serialization;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Assertions;
using Unity.Mathematics;

namespace UnityEngine.Rendering.Universal
{
    public enum ShadowQuality
    {
        Disabled,
        HardShadows,
        SoftShadows,
    }

    public enum SoftShadowQuality
    {
        [InspectorName("Use settings from Render Pipeline Asset")]
        UsePipelineSettings,

        Low,
        Medium,
        High,
    }

    public enum ShadowResolution
    {
        _256 = 256,

        _512 = 512,

        _1024 = 1024,

        _2048 = 2048,

        _4096 = 4096,

        _8192 = 8192,
    }

    public enum LightCookieResolution
    {
        _256 = 256,

        _512 = 512,

        _1024 = 1024,

        _2048 = 2048,

        _4096 = 4096
    }

    public enum LightCookieFormat
    {
        GrayscaleLow,

        GrayscaleHigh,

        ColorLow,

        ColorHigh,

        ColorHDR,
    }

    public enum HDRColorBufferPrecision
    {
        /// <summary> Typically R11G11B10f for faster rendering. Recommend for mobile.
        /// R11G11B10f can cause a subtle blue/yellow banding in some rare cases due to lower precision of the blue component.</summary>
        [Tooltip("Use 32-bits per pixel for HDR rendering.")]
        _32Bits,
        /// <summary>Typically R16G16B16A16f for better quality. Can reduce banding at the cost of memory and performance.</summary>
        [Tooltip("Use 64-bits per pixel for HDR rendering.")]
        _64Bits,
    }

    public enum MsaaQuality
    {
        Disabled = 1,

        _2x = 2,

        _4x = 4,

        _8x = 8
    }

    public enum Downsampling
    {
        None,

        _2xBilinear,

        _4xBox,

        _4xBilinear
    }

    public enum LightRenderingMode
    {
        Disabled = 0,

        PerVertex = 2,

        PerPixel = 1,
    }

    public enum RendererType
    {
        Custom,

        UniversalRenderer,

        _2DRenderer,
    }

    public enum ColorGradingMode
    {
        LowDynamicRange,

        HighDynamicRange
    }

    [Obsolete("#from(6000.0) #breakingFrom(6000.4)", true)]
    public enum StoreActionsOptimization
    {
        /// <summary>Unity uses the Discard option by default, and falls back to the Store option if it detects any injected Passes.</summary>
        Auto,
        /// <summary>Unity discards the render targets of render Passes that are not reused later (lower memory bandwidth).</summary>
        Discard,
        /// <summary>Unity stores all render targets of each Pass (higher memory bandwidth).</summary>
        Store
    }

    public enum VolumeFrameworkUpdateMode
    {
        [InspectorName("Every Frame")]
        EveryFrame = 0,

        [InspectorName("Via Scripting")]
        ViaScripting = 1,

        [InspectorName("Use Pipeline Settings")]
        UsePipelineSettings = 2,
    }

    ///
#if ENABLE_UPSCALER_FRAMEWORK
    [Obsolete("UpscalingFilterSelection is obsolete. #from(6000.3)", false)]
#endif
    public enum UpscalingFilterSelection
    {
        [InspectorName("Automatic"), Tooltip("Unity selects a filtering option automatically based on the Render Scale value and the current screen resolution.")]
        Auto,

        [InspectorName("Bilinear")]
        Linear,

        [InspectorName("Nearest-Neighbor")]
        Point,

        [InspectorName("FidelityFX Super Resolution 1.0"), Tooltip("If the target device does not support Unity shader model 4.5, Unity falls back to the Automatic option.")]
        FSR,

        [InspectorName("Spatial-Temporal Post-Processing"), Tooltip("If the target device does not support compute shaders or is running GLES, Unity falls back to the Automatic option.")]
        STP
    }

    public enum LODCrossFadeDitheringType
    {
        /// <summary>Unity uses the Bayer matrix texture to compute the LOD cross-fade dithering.</summary>
        BayerMatrix,

        /// <summary>Unity uses the precomputed blue noise texture to compute the LOD cross-fade dithering.</summary>
        BlueNoise,

        /// <summary>Unity uses stencil test to make 2x2 pixel dithering pattern by using 2 stencil bits (4 and 8). This option significantly decreases the number of the shader variants, while GPU performance cost becomes slightly higher.</summary>
        [InspectorName("2x2 Stencil"), Tooltip("2x2 pixel dithering pattern by stencil test with 2 stencil bits (4 and 8). This option decreases the number of the shader variants.")]
        Stencil
    }

    public enum LightProbeSystem
    {
        /// <summary>The light probe group system.</summary>
        [InspectorName("Light Probe Groups")]
        LegacyLightProbes = 0,
        /// <summary>Adaptive Probe Volumes system.</summary>
        [InspectorName("Adaptive Probe Volumes")]
        ProbeVolumes = 1,
    }

    public enum ShEvalMode
    {
        /// <summary>Unity selects a mode automatically.</summary>
        Auto = 0,
        /// <summary>Evaluate lighting per vertex.</summary>
        PerVertex = 1,
        /// <summary>Evaluate lighting partially per vertex, partially per pixel.</summary>
        Mixed = 2,
        /// <summary>Evaluate lighting per pixel.</summary>
        PerPixel = 3,
    }

    /// <see cref="RenderPipelineAsset"/>
    /// <see cref="UniversalRenderPipeline"/>
    [ExcludeFromPreset]
    [URPHelpURL("urp/universalrp-asset")]
    [Icon("UnityEngine/Rendering/RenderPipelineAsset Icon")]
#if UNITY_EDITOR
    [DocumentationInfo.Source(DocumentationInfo.Location.Manual)]
    [ShaderKeywordFilter.ApplyRulesIfTagsEqual("RenderPipeline", "UniversalPipeline")]
#endif
    public partial class UniversalRenderPipelineAsset : RenderPipelineAsset<UniversalRenderPipeline>, ISerializationCallbackReceiver, IProbeVolumeEnabledRenderPipeline, IGPUResidentRenderPipeline, IRenderGraphEnabledRenderPipeline, ISTPEnabledRenderPipeline
    {
        ScriptableRenderer[] m_Renderers = new ScriptableRenderer[1];

        internal bool IsAtLastVersion() => k_LastVersion == k_AssetVersion;

        private const int k_LastVersion = 13;
        // Default values set when a new UniversalRenderPipeline asset is created
        [SerializeField] internal int k_AssetVersion = k_LastVersion;
        [SerializeField] int k_AssetPreviousVersion = k_LastVersion;

        // Deprecated settings for upgrading sakes
        [SerializeField] RendererType m_RendererType = RendererType.UniversalRenderer;
        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Use m_RendererDataList instead. #from(2023.1)")]
        [SerializeField] internal ScriptableRendererData m_RendererData = null;

        // Renderer settings
        [SerializeField] internal ScriptableRendererData[] m_RendererDataList = new ScriptableRendererData[1];
        [SerializeField] internal int m_DefaultRendererIndex = 0;

        // General settings
        [SerializeField] bool m_RequireDepthTexture = false;
        [SerializeField] bool m_RequireOpaqueTexture = false;
        [SerializeField] Downsampling m_OpaqueDownsampling = Downsampling._2xBilinear;
        [SerializeField] bool m_SupportsTerrainHoles = true;

        // Quality settings
        [SerializeField] bool m_SupportsHDR = true;
        [SerializeField] HDRColorBufferPrecision m_HDRColorBufferPrecision = HDRColorBufferPrecision._32Bits;
        [SerializeField] MsaaQuality m_MSAA = MsaaQuality.Disabled;
        [SerializeField] float m_RenderScale = 1.0f;
#if ENABLE_UPSCALER_FRAMEWORK
        [Obsolete("m_UpscalingFilter is replaced by m_SelectedUpscalerName #from(6000.3)")]
#endif
        [SerializeField] UpscalingFilterSelection m_UpscalingFilter = UpscalingFilterSelection.Auto;
        // The upscaler name is null if the upscaling filter is coming from a built-in upscaler. It will be non-null if
        // the upscaling filter is coming from an IUpscaler, which can be a separate package, or part of Unity code.
#if ENABLE_UPSCALER_FRAMEWORK
        [SerializeField] string m_SelectedUpscalerName = "Bilinear";

        [SerializeField]
        [SerializeReference]
        List<UpscalerOptions> m_UpscalerOptions = new List<UpscalerOptions>();
#endif
        [SerializeField] bool m_FsrOverrideSharpness = false;
        [SerializeField] float m_FsrSharpness = FSRUtils.kDefaultSharpnessLinear;

#if UNITY_EDITOR // multi_compile _ LOD_FADE_CROSSFADE
        [ShaderKeywordFilter.RemoveIf(false, keywordNames: ShaderKeywordStrings.LOD_FADE_CROSSFADE)]
#endif
        [SerializeField] bool m_EnableLODCrossFade = true;

        [SerializeField] LODCrossFadeDitheringType m_LODCrossFadeDitheringType = LODCrossFadeDitheringType.BlueNoise;

        // ShEvalMode.Auto is handled in shader preprocessor.
#if UNITY_EDITOR // multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
        [ShaderKeywordFilter.RemoveIf(ShEvalMode.PerPixel,  keywordNames:  new [] { ShaderKeywordStrings.EVALUATE_SH_MIXED, ShaderKeywordStrings.EVALUATE_SH_VERTEX })]
        [ShaderKeywordFilter.SelectIf(ShEvalMode.Mixed,     keywordNames:  new [] { ShaderKeywordStrings.EVALUATE_SH_MIXED })]
        [ShaderKeywordFilter.SelectIf(ShEvalMode.PerVertex, keywordNames: new [] { ShaderKeywordStrings.EVALUATE_SH_VERTEX })]
#endif
        [SerializeField] ShEvalMode m_ShEvalMode = ShEvalMode.Auto;

        // Probe volume settings
#if UNITY_EDITOR
        [ShaderKeywordFilter.RemoveIf(LightProbeSystem.LegacyLightProbes, keywordNames: new [] { ShaderKeywordStrings.ProbeVolumeL1, ShaderKeywordStrings.ProbeVolumeL2 })]
        [ShaderKeywordFilter.SelectIf(LightProbeSystem.ProbeVolumes,      keywordNames: new [] { ShaderKeywordStrings.ProbeVolumeL1, ShaderKeywordStrings.ProbeVolumeL2 })]
#endif
        [SerializeField] LightProbeSystem m_LightProbeSystem = LightProbeSystem.LegacyLightProbes;
        [SerializeField] ProbeVolumeTextureMemoryBudget m_ProbeVolumeMemoryBudget = ProbeVolumeTextureMemoryBudget.MemoryBudgetMedium;
        [SerializeField] ProbeVolumeBlendingTextureMemoryBudget m_ProbeVolumeBlendingMemoryBudget = ProbeVolumeBlendingTextureMemoryBudget.MemoryBudgetMedium;
        [SerializeField] [FormerlySerializedAs("m_SupportProbeVolumeStreaming")] bool m_SupportProbeVolumeGPUStreaming = false;
        [SerializeField] bool m_SupportProbeVolumeDiskStreaming = false;
        [SerializeField] bool m_SupportProbeVolumeScenarios = false;
        [SerializeField] bool m_SupportProbeVolumeScenarioBlending = false;
#if UNITY_EDITOR
        [ShaderKeywordFilter.RemoveIf(ProbeVolumeSHBands.SphericalHarmonicsL1, keywordNames: ShaderKeywordStrings.ProbeVolumeL2)]
        [ShaderKeywordFilter.RemoveIf(ProbeVolumeSHBands.SphericalHarmonicsL2, keywordNames: ShaderKeywordStrings.ProbeVolumeL1)]
#endif
        [SerializeField] ProbeVolumeSHBands m_ProbeVolumeSHBands = ProbeVolumeSHBands.SphericalHarmonicsL1;

        // Main directional light Settings
        [SerializeField] LightRenderingMode m_MainLightRenderingMode = LightRenderingMode.PerPixel;
        [SerializeField] bool m_MainLightShadowsSupported = true;
        [SerializeField] ShadowResolution m_MainLightShadowmapResolution = ShadowResolution._2048;

        // Additional lights settings
        [SerializeField] LightRenderingMode m_AdditionalLightsRenderingMode = LightRenderingMode.PerPixel;

#if UNITY_META_QUEST
#if UNITY_EDITOR // multi_compile _ META_QUEST_LIGHTUNROLL (only on  meta platforms)
        [ShaderKeywordFilter.RemoveIfNot(1, keywordNames: ShaderKeywordStrings.META_QUEST_LIGHTUNROLL)]
#endif
#endif
        [SerializeField] int m_AdditionalLightsPerObjectLimit = 4;
        [SerializeField] bool m_AdditionalLightShadowsSupported = false;
        [SerializeField] ShadowResolution m_AdditionalLightsShadowmapResolution = ShadowResolution._2048;

        [SerializeField] int m_AdditionalLightsShadowResolutionTierLow = AdditionalLightsDefaultShadowResolutionTierLow;
        [SerializeField] int m_AdditionalLightsShadowResolutionTierMedium = AdditionalLightsDefaultShadowResolutionTierMedium;
        [SerializeField] int m_AdditionalLightsShadowResolutionTierHigh = AdditionalLightsDefaultShadowResolutionTierHigh;

        // Reflection Probes
        [SerializeField] bool m_ReflectionProbeBlending = false;
        [SerializeField] bool m_ReflectionProbeBoxProjection = false;
        [SerializeField] bool m_ReflectionProbeAtlas = true;

        // Shadows Settings
        [SerializeField] float m_ShadowDistance = 50.0f;
        [SerializeField] int m_ShadowCascadeCount = 1;
        [SerializeField] float m_Cascade2Split = 0.25f;
        [SerializeField] Vector2 m_Cascade3Split = new Vector2(0.1f, 0.3f);
        [SerializeField] Vector3 m_Cascade4Split = new Vector3(0.067f, 0.2f, 0.467f);
        [SerializeField] float m_CascadeBorder = 0.2f;
        [SerializeField] float m_ShadowDepthBias = 1.0f;
        [SerializeField] float m_ShadowNormalBias = 1.0f;
#if UNITY_EDITOR // multi_compile_fragment _ _SHADOWS_SOFT
        [ShaderKeywordFilter.RemoveIf(false, keywordNames: ShaderKeywordStrings.SoftShadows)]
        [SerializeField] bool m_AnyShadowsSupported = true;

        // No option to force soft shadows -> we'll need to keep the off variant around
        [ShaderKeywordFilter.RemoveIf(false, keywordNames: ShaderKeywordStrings.SoftShadows)]
#endif
        [SerializeField] bool m_SoftShadowsSupported = false;
        [SerializeField] bool m_ConservativeEnclosingSphere = false;
        [SerializeField] int m_NumIterationsEnclosingSphere = 64;
        [SerializeField] SoftShadowQuality m_SoftShadowQuality = SoftShadowQuality.Medium;

        // Light Cookie Settings
        [SerializeField] LightCookieResolution m_AdditionalLightsCookieResolution = LightCookieResolution._2048;
        [SerializeField] LightCookieFormat m_AdditionalLightsCookieFormat = LightCookieFormat.ColorHigh;

        // Advanced settings
        [SerializeField] bool m_UseSRPBatcher = true;
        // Deprecated: Retained for serialized data compatibility and will be removed in a future release.
        [SerializeField] bool m_SupportsDynamicBatching = false;
#if UNITY_EDITOR
        // multi_compile _ LIGHTMAP_SHADOW_MIXING
        [ShaderKeywordFilter.RemoveIf(false, keywordNames: ShaderKeywordStrings.LightmapShadowMixing)]
        // multi_compile _ SHADOWS_SHADOWMASK
        [ShaderKeywordFilter.RemoveIf(false, keywordNames: ShaderKeywordStrings.ShadowsShadowMask)]
#endif
        [SerializeField] bool m_MixedLightingSupported = true;
#if UNITY_EDITOR
        // multi_compile_fragment _ _LIGHT_COOKIES
        [ShaderKeywordFilter.RemoveIf(false, keywordNames: ShaderKeywordStrings.LightCookies)]
#endif
        [SerializeField] bool m_SupportsLightCookies = true;
#if UNITY_EDITOR
        // multi_compile_fragment _ _LIGHT_LAYERS
        [ShaderKeywordFilter.SelectOrRemove(true, keywordNames: ShaderKeywordStrings.LightLayers)]
#endif
        [SerializeField] bool m_SupportsLightLayers = false;
        [SerializeField][Obsolete("#from(6000.0) #breakingFrom(6000.4)", true)] StoreActionsOptimization m_StoreActionsOptimization = StoreActionsOptimization.Auto;

        // Adaptive performance settings
        [SerializeField] bool m_UseAdaptivePerformance = true;

        // Post-processing settings
        [SerializeField] ColorGradingMode m_ColorGradingMode = ColorGradingMode.LowDynamicRange;
        [SerializeField] int m_ColorGradingLutSize = 32;
#if UNITY_EDITOR // multi_compile_fragment _ _ENABLE_ALPHA_OUTPUT
        [ShaderKeywordFilter.SelectOrRemove(true, keywordNames: ShaderKeywordStrings._ENABLE_ALPHA_OUTPUT)]
#endif
        [SerializeField] bool m_AllowPostProcessAlphaOutput = false;
#if UNITY_EDITOR // multi_compile_local_fragment _ _USE_FAST_SRGB_LINEAR_CONVERSION
        [ShaderKeywordFilter.SelectOrRemove(true, keywordNames: ShaderKeywordStrings.UseFastSRGBLinearConversion)]
#endif
        [SerializeField] bool m_UseFastSRGBLinearConversion = false;
        [SerializeField] bool m_SupportDataDrivenLensFlare = true;
        [SerializeField] bool m_SupportScreenSpaceLensFlare = true;

        // GPU Resident Drawer
        [FormerlySerializedAs("m_MacroBatcherMode"), SerializeField]
        private GPUResidentDrawerMode m_GPUResidentDrawerMode = GPUResidentDrawerMode.Disabled;
        [SerializeField] float m_SmallMeshScreenPercentage = 0.0f;

        [SerializeField] private Vector4 m_ShadowSmallMeshScreenPercentages = Vector4.zero;

        [SerializeField] bool m_GPUResidentDrawerEnableOcclusionCullingInCameras;

        GPUResidentDrawerSettings IGPUResidentRenderPipeline.gpuResidentDrawerSettings => new()
        {
            mode = m_GPUResidentDrawerMode,
            enableOcclusionCulling = m_GPUResidentDrawerEnableOcclusionCullingInCameras,
            supportDitheringCrossFade = m_EnableLODCrossFade,
            allowInEditMode = true,
            smallMeshScreenPercentage = m_SmallMeshScreenPercentage,
            shadowSmallMeshScreenPercentages = m_ShadowSmallMeshScreenPercentages,
#if UNITY_EDITOR
            pickingShader = Shader.Find("Hidden/Universal Render Pipeline/BRGPicking"),
#endif
            errorShader = Shader.Find("Hidden/Universal Render Pipeline/FallbackError"),
            loadingShader = Shader.Find("Hidden/Universal Render Pipeline/FallbackLoading"),
        };

        // Deprecated settings
        [SerializeField] ShadowQuality m_ShadowType = ShadowQuality.HardShadows;
        [SerializeField] bool m_LocalShadowsSupported = false;
        [SerializeField] ShadowResolution m_LocalShadowsAtlasResolution = ShadowResolution._256;
        [SerializeField] int m_MaxPixelLights = 0;
        [SerializeField] ShadowResolution m_ShadowAtlasResolution = ShadowResolution._256;

        [SerializeField] VolumeFrameworkUpdateMode m_VolumeFrameworkUpdateMode = VolumeFrameworkUpdateMode.EveryFrame;

        [SerializeField] VolumeProfile m_VolumeProfile;

        // Note: A lut size of 16^3 is barely usable with the HDR grading mode. 32 should be the
        // minimum, the lut being encoded in log. Lower sizes would work better with an additional
        // 1D shaper lut but for now we'll keep it simple.

        public const int k_MinLutSize = 16;

        public const int k_MaxLutSize = 65;

        internal const int k_ShadowCascadeMinCount = 1;
        internal const int k_ShadowCascadeMaxCount = 4;

        public static readonly int AdditionalLightsDefaultShadowResolutionTierLow = 256;

        public static readonly int AdditionalLightsDefaultShadowResolutionTierMedium = 512;

        public static readonly int AdditionalLightsDefaultShadowResolutionTierHigh = 1024;

        public ReadOnlySpan<ScriptableRendererData> rendererDataList => m_RendererDataList;

        public ReadOnlySpan<ScriptableRenderer> renderers => m_Renderers;

        static string[] s_Names;
        static int[] s_Values;

        /// <inheritdoc/>
        public bool isImmediateModeSupported => false;

#if UNITY_EDITOR
        public static readonly string packagePath = "Packages/com.unity.render-pipelines.universal";

        internal void Reset()
        {
            // If the asset path is valid, it means we are explicitly resetting an existing asset, so we will create
            // a new default renderer asset to avoid errors. If the path is invalid, it means we are creating a new asset
            // for which the default renderer is provided through UniversalRenderPipelineAsset.Create.
            string path = AssetDatabase.GetAssetPath(this);
            if (!string.IsNullOrEmpty(path))
            {
                m_RendererDataList[0] = CreateRendererAsset(path, m_RendererType);
            }
        }

        public static UniversalRenderPipelineAsset Create(ScriptableRendererData rendererData = null)
        {
            // Create Universal RP Asset
            var instance = CreateInstance<UniversalRenderPipelineAsset>();

            // Initialize default renderer data
            instance.m_RendererDataList[0] = (rendererData != null) ? rendererData : CreateInstance<UniversalRendererData>();

            // Only enable for new URP assets by default
            instance.m_ConservativeEnclosingSphere = true;

            ResourceReloader.ReloadAllNullIn(instance, packagePath);

            return instance;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1812")]
        internal class CreateUniversalPipelineAsset : AssetCreationEndAction
        {
            public override void Action(EntityId entityId, string pathName, string resourceFile)
            {
                //Create asset
                AssetDatabase.CreateAsset(Create(CreateRendererAsset(pathName, RendererType.UniversalRenderer)), pathName);
            }
        }

        [MenuItem("Assets/Create/Rendering/URP Asset (with Universal Renderer)", priority = CoreUtils.Sections.section2 + CoreUtils.Priorities.assetsCreateRenderingMenuPriority + 1)]
        static void CreateUniversalPipeline()
        {
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(EntityId.None, CreateInstance<CreateUniversalPipelineAsset>(),
                "New Universal Render Pipeline Asset.asset", CoreUtils.GetIconForType<UniversalRenderPipelineAsset>(), null);
        }

        internal static ScriptableRendererData CreateRendererAsset(string path, RendererType type, bool relativePath = true, string suffix = "Renderer")
        {
            ScriptableRendererData data = CreateRendererData(type);
            string dataPath;
            if (relativePath)
                dataPath =
                    $"{Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path))}_{suffix}{Path.GetExtension(path)}";
            else
                dataPath = path;
            AssetDatabase.CreateAsset(data, dataPath);
            ResourceReloader.ReloadAllNullIn(data, packagePath);
            return data;
        }

        static ScriptableRendererData CreateRendererData(RendererType type)
        {
            switch (type)
            {
                case RendererType.UniversalRenderer:
                default:
                {
                    var rendererData = CreateInstance<UniversalRendererData>();
                    rendererData.postProcessData = PostProcessData.GetDefaultPostProcessData();
                    return rendererData;
                }
            }
        }
#endif
        /// <param name="type">The <c>RendererType</c> of the new renderer that is initialized within this asset.</param>
        /// <returns></returns>
        /// <see cref="RendererType"/>
        public ScriptableRendererData LoadBuiltinRendererData(RendererType type = RendererType.UniversalRenderer)
        {
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
            return m_RendererDataList[0] =
                CreateRendererAsset("Assets/UniversalRenderer.asset", type, false);
#else
            m_RendererDataList[0] = null;
            return m_RendererDataList[0];
#endif
        }

        protected override void EnsureGlobalSettings()
        {
            base.EnsureGlobalSettings();

#if UNITY_EDITOR
            UniversalRenderPipelineGlobalSettings.Ensure();
#endif
        }

        /// <returns>Returns a <c>UniversalRenderPipeline</c> created from this UniversalRenderPipelineAsset.</returns>
        /// <see cref="RenderPipeline"/>
        protected override RenderPipeline CreatePipeline()
        {
            if (m_RendererDataList == null)
                m_RendererDataList = new ScriptableRendererData[1];

            // If no default data we can't create pipeline instance
            if (m_DefaultRendererIndex >= m_RendererDataList.Length || m_RendererDataList[m_DefaultRendererIndex] == null)
            {
                // If previous version and current version are miss-matched then we are waiting for the upgrader to kick in
                if (k_AssetPreviousVersion != k_AssetVersion)
                    return null;

                Debug.LogError(
                    $"Default Renderer is missing, make sure there is a Renderer assigned as the default on the current Universal RP asset:{UniversalRenderPipeline.asset.name}",
                    this);
                return null;
            }

            DestroyRenderers();
            var pipeline = new UniversalRenderPipeline(this);
            CreateRenderers();

            IGPUResidentRenderPipeline.ReinitializeGPUResidentDrawer();
            return pipeline;
        }

        internal void DestroyRenderers()
        {
            if (m_Renderers == null)
                return;

            for (int i = 0; i < m_Renderers.Length; i++)
                DestroyRenderer(ref m_Renderers[i]);
        }

        void DestroyRenderer(ref ScriptableRenderer renderer)
        {
            if (renderer != null)
            {
                renderer.Dispose();
                renderer = null;
            }
        }

        protected override void OnDisable()
        {
            DestroyRenderers();

            // This will call RenderPipelineManager.CleanupRenderPipeline that in turn disposes the render pipeline instance and
            // assign pipeline asset reference to null
            base.OnDisable();
        }

        void CreateRenderers()
        {
            if (m_Renderers != null)
            {
                for (int i = 0; i < m_Renderers.Length; ++i)
                {
                    if (m_Renderers[i] != null)
                        Debug.LogError($"Creating renderers but previous instance wasn't properly destroyed: m_Renderers[{i}]");
                }
            }

            if (m_Renderers == null || m_Renderers.Length != m_RendererDataList.Length)
                m_Renderers = new ScriptableRenderer[m_RendererDataList.Length];

            for (int i = 0; i < m_RendererDataList.Length; ++i)
            {
                if (m_RendererDataList[i] != null)
                    m_Renderers[i] = m_RendererDataList[i].InternalCreateRenderer();
            }
        }

        public ScriptableRenderer scriptableRenderer
        {
            get
            {
                if (m_RendererDataList?.Length > m_DefaultRendererIndex && m_RendererDataList[m_DefaultRendererIndex] == null)
                {
                    Debug.LogError("Default renderer is missing from the current Pipeline Asset.", this);
                    return null;
                }

                if (scriptableRendererData.isInvalidated || m_Renderers[m_DefaultRendererIndex] == null)
                {
                    DestroyRenderer(ref m_Renderers[m_DefaultRendererIndex]);
                    m_Renderers[m_DefaultRendererIndex] = scriptableRendererData.InternalCreateRenderer();

                    // GPU Resident Drawer may need to be reinitialized if renderer data has become incompatible/compatible
                    if (gpuResidentDrawerMode != GPUResidentDrawerMode.Disabled)
                    {
                        IGPUResidentRenderPipeline.ReinitializeGPUResidentDrawer();
                    }
                }

                return m_Renderers[m_DefaultRendererIndex];
            }
        }

        /// <param name="index">Index to the renderer. If invalid index is passed, the default renderer is returned instead.</param>
        /// <returns></returns>
        public ScriptableRenderer GetRenderer(int index)
        {
            if (index == -1)
                index = m_DefaultRendererIndex;

            if (index >= m_RendererDataList.Length || index < 0 || m_RendererDataList[index] == null)
            {
                Debug.LogWarning(
                    $"Renderer at index {index.ToString()} is missing, falling back to Default Renderer {m_RendererDataList[m_DefaultRendererIndex].name}",
                    this);
                index = m_DefaultRendererIndex;
            }

            // RendererData list differs from RendererList. Create RendererList.
            if (m_Renderers == null || m_Renderers.Length < m_RendererDataList.Length)
            {
                DestroyRenderers();
                CreateRenderers();
            }

            // This renderer data is outdated or invalid, we recreate the renderer
            // so we construct all render passes with the updated data
            if (m_RendererDataList[index].isInvalidated || m_Renderers[index] == null)
            {
                DestroyRenderer(ref m_Renderers[index]);
                m_Renderers[index] = m_RendererDataList[index].InternalCreateRenderer();

                // GPU Resident Drawer may need to be reinitialized if renderer data has become incompatible/compatible
                if (gpuResidentDrawerMode != GPUResidentDrawerMode.Disabled)
                {
                    IGPUResidentRenderPipeline.ReinitializeGPUResidentDrawer();
                }
            }

            return m_Renderers[index];
        }

        internal ScriptableRendererData scriptableRendererData
        {
            get
            {
                if (m_RendererDataList[m_DefaultRendererIndex] == null)
                    CreatePipeline();

                return m_RendererDataList[m_DefaultRendererIndex];
            }
        }

#if UNITY_EDITOR
        internal bool TryGetRendererData(int index, out ScriptableRendererData result)
        {
            result = null;
            if (m_RendererDataList == null || m_RendererDataList.Length == 0)
                return false;

            if (index < 0 || index >= m_RendererDataList.Length)
            {
                if (m_DefaultRendererIndex < 0 || m_DefaultRendererIndex >= m_RendererDataList.Length)
                    return false;

                index = m_DefaultRendererIndex; //out of range index fallback on default
            }

            result = m_RendererDataList[index];
            return result != null;
        }

        internal GUIContent[] rendererDisplayList
        {
            get
            {
                GUIContent[] list = new GUIContent[m_RendererDataList.Length + 1];
                list[0] = new GUIContent($"Default Renderer ({RendererDataDisplayName(m_RendererDataList[m_DefaultRendererIndex])})");

                for (var i = 1; i < list.Length; i++)
                {
                    list[i] = new GUIContent($"{(i - 1).ToString()}: {RendererDataDisplayName(m_RendererDataList[i - 1])}");
                }
                return list;
            }
        }

        string RendererDataDisplayName(ScriptableRendererData data)
        {
            if (data != null)
                return data.name;

            return "NULL (Missing RendererData)";
        }

#endif
        private static readonly GraphicsFormat[][] k_LightCookieFormatList = new GraphicsFormat[][]
        {
            /* Grayscale Low */ new GraphicsFormat[] {GraphicsFormat.R8_UNorm},
            /* Grayscale High*/ new GraphicsFormat[] {GraphicsFormat.R16_UNorm},
            /* Color Low     */ new GraphicsFormat[] {GraphicsFormat.R5G6B5_UNormPack16, GraphicsFormat.B5G6R5_UNormPack16, GraphicsFormat.R5G5B5A1_UNormPack16, GraphicsFormat.B5G5R5A1_UNormPack16},
            /* Color High    */ new GraphicsFormat[] {GraphicsFormat.A2B10G10R10_UNormPack32, GraphicsFormat.R8G8B8A8_SRGB, GraphicsFormat.B8G8R8A8_SRGB},
            /* Color HDR     */ new GraphicsFormat[] {GraphicsFormat.B10G11R11_UFloatPack32},
        };

        internal GraphicsFormat additionalLightsCookieFormat
        {
            get
            {
                GraphicsFormat result = GraphicsFormat.None;
                foreach (var format in k_LightCookieFormatList[(int)m_AdditionalLightsCookieFormat])
                {
                    if (SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Render))
                    {
                        result = format;
                        break;
                    }
                }

                if (QualitySettings.activeColorSpace == ColorSpace.Gamma)
                    result = GraphicsFormatUtility.GetLinearFormat(result);

                // Fallback
                if (result == GraphicsFormat.None)
                {
                    result = GraphicsFormat.R8G8B8A8_UNorm;
                    Debug.LogWarning($"Additional Lights Cookie Format ({ m_AdditionalLightsCookieFormat.ToString() }) is not supported by the platform. Falling back to {GraphicsFormatUtility.GetBlockSize(result) * 8}-bit format ({GraphicsFormatUtility.GetFormatString(result)})");
                }

                return result;
            }
        }

        internal Vector2Int additionalLightsCookieResolution => new Vector2Int((int)m_AdditionalLightsCookieResolution, (int)m_AdditionalLightsCookieResolution);

        internal int[] rendererIndexList
        {
            get
            {
                int[] list = new int[m_RendererDataList.Length + 1];
                for (int i = 0; i < list.Length; i++)
                {
                    list[i] = i - 1;
                }
                return list;
            }
        }

        public bool supportsCameraDepthTexture
        {
            get => m_RequireDepthTexture;
            set => m_RequireDepthTexture = value;
        }

        public bool supportsCameraOpaqueTexture
        {
            get => m_RequireOpaqueTexture;
            set => m_RequireOpaqueTexture = value;
        }

        public Downsampling opaqueDownsampling => m_OpaqueDownsampling;

        /// <see href="https://docs.unity3d.com/Manual/terrain-PaintHoles.html"/>
        public bool supportsTerrainHoles => m_SupportsTerrainHoles;

        /// <returns>Returns the active store action optimization value.</returns>
        [Obsolete("#from(6000.0) #breakingFrom(6000.4)", true)]
        public StoreActionsOptimization storeActionsOptimization
        {
            get => m_StoreActionsOptimization;
            set => m_StoreActionsOptimization = value;
        }

        /// <see href="https://docs.unity3d.com/Manual/HDR.html"/>
        public bool supportsHDR
        {
            get => m_SupportsHDR;
            set => m_SupportsHDR = value;
        }

        public HDRColorBufferPrecision hdrColorBufferPrecision
        {
            get => m_HDRColorBufferPrecision;
            set => m_HDRColorBufferPrecision = value;
        }

        /// <see cref="MsaaQuality"/>
        public int msaaSampleCount
        {
            get => (int)m_MSAA;
            set => m_MSAA = (MsaaQuality)value;
        }

        public float renderScale
        {
            get => m_RenderScale;
            set => m_RenderScale = ValidateRenderScale(value);
        }

        public bool enableLODCrossFade => m_EnableLODCrossFade;

        public LODCrossFadeDitheringType lodCrossFadeDitheringType => m_LODCrossFadeDitheringType;


#if ENABLE_UPSCALER_FRAMEWORK
        [Obsolete("upscalingFilter is replaced by upscalerName #from(6000.3)", false)]
#endif
        public UpscalingFilterSelection upscalingFilter
        {
            get => m_UpscalingFilter;
            set => m_UpscalingFilter = value;
        }


        public string upscalerName
        {
#if ENABLE_UPSCALER_FRAMEWORK
            get => m_SelectedUpscalerName;
            set => m_SelectedUpscalerName = value;
#else
            get => string.Empty;
#endif
        }

#if ENABLE_UPSCALER_FRAMEWORK
        public List<UpscalerOptions> upscalerOptions
        {
            get => m_UpscalerOptions;
        }

        /// <param name="upscalerName">The unique ID or name of the upscaler (e.g., "DLSS", "FSR2").</param>
        /// <returns>The matching <see cref="UpscalerOptions"/> asset if found; otherwise, null.</returns>
        public UpscalerOptions GetUpscalerOptions(string UpscalerName)
        {
            foreach(UpscalerOptions option in m_UpscalerOptions)
            {
                if (option == null)
                    continue;
                if (option.upscalerName == UpscalerName)
                    return option;
            }
            return null;
        }
#endif

        public bool fsrOverrideSharpness
        {
            get => m_FsrOverrideSharpness;
            set => m_FsrOverrideSharpness = value;
        }

        public float fsrSharpness
        {
            get => m_FsrSharpness;
            set => m_FsrSharpness = value;
        }

        public ShEvalMode shEvalMode
        {
            get => m_ShEvalMode;
            internal set => m_ShEvalMode = value;
        }

        public LightProbeSystem lightProbeSystem
        {
            get => m_LightProbeSystem;
            internal set => m_LightProbeSystem = value;
        }

        public ProbeVolumeTextureMemoryBudget probeVolumeMemoryBudget
        {
            get => m_ProbeVolumeMemoryBudget;
            internal set => m_ProbeVolumeMemoryBudget = value;
        }

        public ProbeVolumeBlendingTextureMemoryBudget probeVolumeBlendingMemoryBudget
        {
            get => m_ProbeVolumeBlendingMemoryBudget;
            internal set => m_ProbeVolumeBlendingMemoryBudget = value;
        }

        [Obsolete( "This is obsolete, use supportProbeVolumeGPUStreaming instead. #from(2023.3)")]
        public bool supportProbeVolumeStreaming
        {
            get => m_SupportProbeVolumeGPUStreaming;
            internal set => m_SupportProbeVolumeGPUStreaming = value;
        }

        public bool supportProbeVolumeGPUStreaming
        {
            get => m_SupportProbeVolumeGPUStreaming;
            internal set => m_SupportProbeVolumeGPUStreaming = value;
        }

        public bool supportProbeVolumeDiskStreaming
        {
            get => m_SupportProbeVolumeDiskStreaming;
            internal set => m_SupportProbeVolumeDiskStreaming = value;
        }

        public bool supportProbeVolumeScenarios
        {
            get { return m_SupportProbeVolumeScenarios; }
            internal set { m_SupportProbeVolumeScenarios = value; }
        }

        public bool supportProbeVolumeScenarioBlending
        {
            get { return m_SupportProbeVolumeScenarioBlending; }
            internal set { m_SupportProbeVolumeScenarioBlending = value; }
        }

        public ProbeVolumeSHBands probeVolumeSHBands
        {
            get => m_ProbeVolumeSHBands;
            internal set => m_ProbeVolumeSHBands = value;
        }

        /// <see cref="LightRenderingMode"/>
        public LightRenderingMode mainLightRenderingMode
        {
            get => m_MainLightRenderingMode;
            internal set => m_MainLightRenderingMode = value;
        }

        public bool supportsMainLightShadows
        {
            get => m_MainLightShadowsSupported;
            internal set
            {
                m_MainLightShadowsSupported = value;
#if UNITY_EDITOR
                m_AnyShadowsSupported = m_MainLightShadowsSupported || m_AdditionalLightShadowsSupported;
#endif
            }
        }

        public int mainLightShadowmapResolution
        {
            get => (int)m_MainLightShadowmapResolution;
            set => m_MainLightShadowmapResolution = (ShadowResolution)value;
        }

        /// <see cref="LightRenderingMode"/>
        public LightRenderingMode additionalLightsRenderingMode
        {
            get => m_AdditionalLightsRenderingMode;
            internal set => m_AdditionalLightsRenderingMode = value;
        }

        public int maxAdditionalLightsCount
        {
            get => m_AdditionalLightsPerObjectLimit;
            set => m_AdditionalLightsPerObjectLimit = ValidatePerObjectLights(value);
        }

        public bool supportsAdditionalLightShadows
        {
            get => m_AdditionalLightShadowsSupported;
            internal set
            {
                m_AdditionalLightShadowsSupported = value;
#if UNITY_EDITOR
                m_AnyShadowsSupported = m_MainLightShadowsSupported || m_AdditionalLightShadowsSupported;
#endif
            }
        }

        public int additionalLightsShadowmapResolution
        {
            get => (int)m_AdditionalLightsShadowmapResolution;
            set => m_AdditionalLightsShadowmapResolution = (ShadowResolution)value;
        }

        public int additionalLightsShadowResolutionTierLow
        {
            get => m_AdditionalLightsShadowResolutionTierLow;
            internal set => m_AdditionalLightsShadowResolutionTierLow = value;
        }

        public int additionalLightsShadowResolutionTierMedium
        {
            get => m_AdditionalLightsShadowResolutionTierMedium;
            internal set => m_AdditionalLightsShadowResolutionTierMedium = value;
        }

        public int additionalLightsShadowResolutionTierHigh
        {
            get => m_AdditionalLightsShadowResolutionTierHigh;
            internal set => m_AdditionalLightsShadowResolutionTierHigh = value;
        }

        internal int GetAdditionalLightsShadowResolution(int additionalLightsShadowResolutionTier)
        {
            if (additionalLightsShadowResolutionTier <= UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow /* 0 */)
                return additionalLightsShadowResolutionTierLow;

            if (additionalLightsShadowResolutionTier == UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierMedium /* 1 */)
                return additionalLightsShadowResolutionTierMedium;

            if (additionalLightsShadowResolutionTier >= UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierHigh /* 2 */)
                return additionalLightsShadowResolutionTierHigh;

            return additionalLightsShadowResolutionTierMedium;
        }

        public bool reflectionProbeBlending
        {
            get => m_ReflectionProbeBlending;
            internal set => m_ReflectionProbeBlending = value;
        }

        internal bool ShouldUseReflectionProbeBlending()
        {
            // The probe blending with atlas code path is always force enabled with GPUResidentDrawer since that is the only path supported here.
            if (gpuResidentDrawerMode != GPUResidentDrawerMode.Disabled)
                return true;

            return reflectionProbeBlending;
        }

        public bool reflectionProbeBoxProjection
        {
            get => m_ReflectionProbeBoxProjection;
            internal set => m_ReflectionProbeBoxProjection = value;
        }

        public bool reflectionProbeAtlas
        {
            get => m_ReflectionProbeAtlas;
            internal set => m_ReflectionProbeAtlas = value;
        }

        internal bool ShouldUseReflectionProbeAtlasBlending(RenderingMode renderingMode)
        {
            var useProbeBlending = ShouldUseReflectionProbeBlending();

            // The probe blending with atlas code path is always force enabled with GPUResidentDrawer since that is the only path supported here.
            if (gpuResidentDrawerMode != GPUResidentDrawerMode.Disabled)
            {
                Assert.IsTrue(useProbeBlending);
                return true;
            }

            return useProbeBlending && (reflectionProbeAtlas || renderingMode == RenderingMode.DeferredPlus);
        }

        public float shadowDistance
        {
            get => m_ShadowDistance;
            set => m_ShadowDistance = Mathf.Max(0.0f, value);
        }

        public int shadowCascadeCount
        {
            get => m_ShadowCascadeCount;
            set
            {
                if (value < k_ShadowCascadeMinCount || value > k_ShadowCascadeMaxCount)
                {
                    throw new ArgumentException($"Value ({value}) needs to be between {k_ShadowCascadeMinCount} and {k_ShadowCascadeMaxCount}.");
                }
                m_ShadowCascadeCount = value;
            }
        }

        /// <returns>Returns a Float with the split value.</returns>
        public float cascade2Split
        {
            get => m_Cascade2Split;
            set => m_Cascade2Split = value;
        }

        /// <returns>Returns a Vector2 with the split values.</returns>
        public Vector2 cascade3Split
        {
            get => m_Cascade3Split;
            set => m_Cascade3Split = value;
        }

        /// <returns>Returns a Vector3 with the split values.</returns>
        public Vector3 cascade4Split
        {
            get => m_Cascade4Split;
            set => m_Cascade4Split = value;
        }

        public float cascadeBorder
        {
            get => m_CascadeBorder;
            set => m_CascadeBorder = value;
        }

        public float shadowDepthBias
        {
            get => m_ShadowDepthBias;
            set => m_ShadowDepthBias = ValidateShadowBias(value);
        }

        public float shadowNormalBias
        {
            get => m_ShadowNormalBias;
            set => m_ShadowNormalBias = ValidateShadowBias(value);
        }

        public bool supportsSoftShadows
        {
            get => m_SoftShadowsSupported;
            internal set => m_SoftShadowsSupported = value;
        }

        internal SoftShadowQuality softShadowQuality
        {
            get => m_SoftShadowQuality;
            set => m_SoftShadowQuality = value;
        }

        /// <see href="https://docs.unity3d.com/Manual/DrawCallBatching.html"/>
        [Obsolete("supportsDynamicBatching is obsolete.", true)]
        public bool supportsDynamicBatching
        {
            get => m_SupportsDynamicBatching;
            set => m_SupportsDynamicBatching = value;
        }

        /// <see href="https://docs.unity3d.com/Manual/LightMode-Mixed.html"/>
        public bool supportsMixedLighting => m_MixedLightingSupported;

        public bool supportsLightCookies => m_SupportsLightCookies;

        [Obsolete("This is obsolete, use useRenderingLayers instead. #from(2023.1) #breakingFrom(2023.1)", true)]
        public bool supportsLightLayers => m_SupportsLightLayers;

        public bool useRenderingLayers
        {
            get => m_SupportsLightLayers;
            internal set => m_SupportsLightLayers = value;
        }

        public VolumeFrameworkUpdateMode volumeFrameworkUpdateMode => m_VolumeFrameworkUpdateMode;

        public VolumeProfile volumeProfile
        {
            get => m_VolumeProfile;
            set => m_VolumeProfile = value;
        }

        /// <see href="https://docs.unity3d.com/Manual/SRPBatcher.html"/>
        public bool useSRPBatcher
        {
            get => m_UseSRPBatcher;
            set => m_UseSRPBatcher = value;
        }

        public ColorGradingMode colorGradingMode
        {
            get => m_ColorGradingMode;
            set => m_ColorGradingMode = value;
        }

        public int colorGradingLutSize
        {
            get => m_ColorGradingLutSize;
            set => m_ColorGradingLutSize = Mathf.Clamp(value, k_MinLutSize, k_MaxLutSize);
        }

        public bool allowPostProcessAlphaOutput => m_AllowPostProcessAlphaOutput;

        public bool useFastSRGBLinearConversion => m_UseFastSRGBLinearConversion;

        public bool supportScreenSpaceLensFlare => m_SupportScreenSpaceLensFlare;

        public bool supportDataDrivenLensFlare => m_SupportDataDrivenLensFlare;

        public bool useAdaptivePerformance
        {
            get => m_UseAdaptivePerformance;
            set => m_UseAdaptivePerformance = value;
        }

        public bool conservativeEnclosingSphere
        {
            get => m_ConservativeEnclosingSphere;
            set => m_ConservativeEnclosingSphere = value;
        }

        public int numIterationsEnclosingSphere
        {
            get => m_NumIterationsEnclosingSphere;
            set => m_NumIterationsEnclosingSphere = value;
        }

        /// <inheritdoc/>
        public override string renderPipelineShaderTag => UniversalRenderPipeline.k_ShaderTagName;

        /// <inheritdoc/>
        protected override bool requiresCompatibleRenderPipelineGlobalSettings => true;

        /// <summary>Names used for display of rendering layer masks.</summary>
        [Obsolete("This property is obsolete. Use RenderingLayerMask API and Tags & Layers project settings instead. #from(2023.3)")]
        public override string[] renderingLayerMaskNames => RenderingLayerMask.GetDefinedRenderingLayerNames();

        /// <summary>Names used for display of rendering layer masks with prefix.</summary>
        [Obsolete("This property is obsolete. Use RenderingLayerMask API and Tags & Layers project settings instead. #from(2023.3)")]
        public override string[] prefixedRenderingLayerMaskNames => Array.Empty<string>();

        [Obsolete("This is obsolete, please use renderingLayerMaskNames instead. #from(2023.1) #breakingFrom(2023.1)", true)]
        public string[] lightLayerMaskNames => new string[0];

        public GPUResidentDrawerMode gpuResidentDrawerMode
        {
            get => m_GPUResidentDrawerMode;
            set
            {
                if (value == m_GPUResidentDrawerMode)
                    return;

                m_GPUResidentDrawerMode = value;
                OnValidate();
            }
        }

        public bool gpuResidentDrawerEnableOcclusionCullingInCameras
        {
            get => m_GPUResidentDrawerEnableOcclusionCullingInCameras;
            set
            {
                if (value == m_GPUResidentDrawerEnableOcclusionCullingInCameras)
                    return;

                m_GPUResidentDrawerEnableOcclusionCullingInCameras = value;
                OnValidate();
            }
        }

        static class Strings
        {
            public static readonly string nullRenderer = $"{nameof(GPUResidentDrawer)} Disabled. One or more Scriptable Renderer in the Render Pipeline Asset is null.";
            public static readonly string notURPRenderer = $"{nameof(GPUResidentDrawer)} Disabled. One or more Scriptable Renderer in the Render Pipeline Asset is not of the type {nameof(UniversalRendererData)}.";
            public static readonly string renderingModeIncompatible = $"{nameof(GPUResidentDrawer)} Disabled due to some configured Universal Renderers not using the Forward+ or Deferred+ rendering paths.";
        }

        /// <inheritdoc/>
        public bool IsGPUResidentDrawerSupportedBySRP(out string message, out LogType severity)
        {
            message = string.Empty;
            severity = LogType.Warning;

            // Only the URP rendering paths using the cluster light loop (F+ lights & probes) can be used with GRD,
            // since BiRP-style per-object lights and reflection probes are incompatible with DOTS instancing.
            foreach (var rendererData in m_RendererDataList)
            {
                if (rendererData == null)
                {
                    message = Strings.nullRenderer;
                    return false;
                }

                if (rendererData is not UniversalRendererData universalRendererData)
                {
                    message = Strings.notURPRenderer;
                    return false;
                }

                if (!universalRendererData.usesClusterLightLoop)
                {
                    message = Strings.renderingModeIncompatible;
                    return false;
                }
            }

            return true;
        }

        public float smallMeshScreenPercentage
        {
            get => m_SmallMeshScreenPercentage;
            set
            {
                if (Math.Abs(value - m_SmallMeshScreenPercentage) < float.Epsilon)
                    return;

                m_SmallMeshScreenPercentage = Mathf.Clamp(value, 0.0f, 20.0f);
                OnValidate();
            }
        }

        public Vector4 shadowSmallMeshScreenPercentages
        {
            get => m_ShadowSmallMeshScreenPercentages;
            set
            {
                if ((value - m_ShadowSmallMeshScreenPercentages).sqrMagnitude < float.Epsilon * float.Epsilon)
                    return;

                m_ShadowSmallMeshScreenPercentages = math.clamp(value, 0.0f, 50.0f);
                OnValidate();
            }
        }

        public void OnBeforeSerialize()
        {
        }

        public void OnAfterDeserialize()
        {
            if (k_AssetVersion < 3)
            {
                m_SoftShadowsSupported = (m_ShadowType == ShadowQuality.SoftShadows);
                k_AssetPreviousVersion = k_AssetVersion;
                k_AssetVersion = 3;
            }

            if (k_AssetVersion < 4)
            {
                m_AdditionalLightShadowsSupported = m_LocalShadowsSupported;
                m_AdditionalLightsShadowmapResolution = m_LocalShadowsAtlasResolution;
                m_AdditionalLightsPerObjectLimit = m_MaxPixelLights;
                m_MainLightShadowmapResolution = m_ShadowAtlasResolution;
                k_AssetPreviousVersion = k_AssetVersion;
                k_AssetVersion = 4;
            }

            if (k_AssetVersion < 5)
            {
                if (m_RendererType == RendererType.Custom)
                {
#pragma warning disable 618 // Obsolete warning
                    m_RendererDataList[0] = m_RendererData;
#pragma warning restore 618 // Obsolete warning
                }
                k_AssetPreviousVersion = k_AssetVersion;
                k_AssetVersion = 5;
            }

            if (k_AssetVersion < 6)
            {
#pragma warning disable 618 // Obsolete warning
                // Adding an upgrade here so that if it was previously set to 2 it meant 4 cascades.
                // So adding a 3rd cascade shifted this value up 1.
                int value = (int)m_ShadowCascades;
                if (value == 2)
                {
                    m_ShadowCascadeCount = 4;
                }
                else
                {
                    m_ShadowCascadeCount = value + 1;
                }
                k_AssetVersion = 6;
#pragma warning restore 618 // Obsolete warning
            }

            if (k_AssetVersion < 7)
            {
                k_AssetPreviousVersion = k_AssetVersion;
                k_AssetVersion = 7;
            }

            if (k_AssetVersion < 8)
            {
                k_AssetPreviousVersion = k_AssetVersion;
                m_CascadeBorder = 0.1f; // In previous version we had this hard coded
                k_AssetVersion = 8;
            }

            if (k_AssetVersion < 9)
            {
                bool assetContainsCustomAdditionalLightShadowResolutions =
                    m_AdditionalLightsShadowResolutionTierHigh != AdditionalLightsDefaultShadowResolutionTierHigh ||
                    m_AdditionalLightsShadowResolutionTierMedium != AdditionalLightsDefaultShadowResolutionTierMedium ||
                    m_AdditionalLightsShadowResolutionTierLow != AdditionalLightsDefaultShadowResolutionTierLow;

                if (!assetContainsCustomAdditionalLightShadowResolutions)
                {
                    // if all resolutions are still the default values, we assume that they have never been customized and that it is safe to upgrade them to fit better the Additional Lights Shadow Atlas size
                    m_AdditionalLightsShadowResolutionTierHigh = (int)m_AdditionalLightsShadowmapResolution;
                    m_AdditionalLightsShadowResolutionTierMedium = Mathf.Max(m_AdditionalLightsShadowResolutionTierHigh / 2, UniversalAdditionalLightData.AdditionalLightsShadowMinimumResolution);
                    m_AdditionalLightsShadowResolutionTierLow = Mathf.Max(m_AdditionalLightsShadowResolutionTierMedium / 2, UniversalAdditionalLightData.AdditionalLightsShadowMinimumResolution);
                }

                k_AssetPreviousVersion = k_AssetVersion;
                k_AssetVersion = 9;
            }

            if (k_AssetVersion < 10)
            {
                k_AssetPreviousVersion = k_AssetVersion;
                k_AssetVersion = 10;
            }

            if (k_AssetVersion < 11)
            {
                k_AssetPreviousVersion = k_AssetVersion;
                k_AssetVersion = 11;
            }

            if (k_AssetVersion < 12)
            {
                k_AssetPreviousVersion = k_AssetVersion;
                k_AssetVersion = 12;
            }

            if (k_AssetVersion < 13)
            {
                k_AssetPreviousVersion = k_AssetVersion;
                k_AssetVersion = 13;
            }

#if UNITY_EDITOR
            if (k_AssetPreviousVersion != k_AssetVersion)
            {
                EditorApplication.delayCall += () => UpgradeAsset(this.GetEntityId());
            }
#endif
        }

#if UNITY_EDITOR
        static void UpgradeAsset(EntityId assetInstanceID)
        {
            UniversalRenderPipelineAsset asset = EditorUtility.EntityIdToObject(assetInstanceID) as UniversalRenderPipelineAsset;

            if (asset.k_AssetPreviousVersion < 5)
            {
                if (asset.m_RendererType == RendererType.UniversalRenderer)
                {
                    var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/UniversalRenderer.asset");
                    if (data)
                    {
                        asset.m_RendererDataList[0] = data;
                    }
                    else
                    {
                        asset.LoadBuiltinRendererData();
                    }
#pragma warning disable 618 // Obsolete warning
                    asset.m_RendererData = null; // Clears the old renderer
#pragma warning restore 618 // Obsolete warning
                }

                asset.k_AssetPreviousVersion = 5;
            }

            if (asset.k_AssetPreviousVersion < 9)
            {
                // The added feature was reverted, we keep this version to avoid breakage in case somebody already has version 7
                asset.k_AssetPreviousVersion = 9;
            }

            if (asset.k_AssetPreviousVersion < 10)
            {
#pragma warning disable 618 // Obsolete warning
                var instance = UniversalRenderPipelineGlobalSettings.Ensure();
                instance.m_ShaderVariantLogLevel = (Rendering.ShaderVariantLogLevel) asset.m_ShaderVariantLogLevel;
#pragma warning restore 618 // Obsolete warning
                asset.k_AssetPreviousVersion = 10;
            }

            if (asset.k_AssetPreviousVersion < 11)
            {
                asset.k_AssetPreviousVersion = 11;
            }

            if (asset.k_AssetPreviousVersion < 12)
            {
                var globalSettings = UniversalRenderPipelineGlobalSettings.Ensure();
#pragma warning disable CS0618 // Type or member is obsolete
                if (asset.apvScenesData != null)
                    globalSettings.apvScenesData = asset.apvScenesData;
#pragma warning restore CS0618 // Type or member is obsolete
                asset.k_AssetPreviousVersion = 12;
            }

            if (asset.k_AssetPreviousVersion < 13)
            {
                asset.k_AssetPreviousVersion = 13;
            }

            ResourceReloader.ReloadAllNullIn(asset, packagePath);
            EditorUtility.SetDirty(asset);
        }

#endif

        float ValidateShadowBias(float value)
        {
            return Mathf.Max(0.0f, Mathf.Min(value, UniversalRenderPipeline.maxShadowBias));
        }

        int ValidatePerObjectLights(int value)
        {
            return System.Math.Max(0, System.Math.Min(value, UniversalRenderPipeline.maxPerObjectLights));
        }

        float ValidateRenderScale(float value)
        {
            return Mathf.Max(UniversalRenderPipeline.minRenderScale, Mathf.Min(value, UniversalRenderPipeline.maxRenderScale));
        }

        /// <param name="partial">This bool controls whether to test against all or any, if false then there has to be no invalid RendererData</param>
        /// <returns></returns>
        internal bool ValidateRendererDataList(bool partial = false)
        {
            var emptyEntries = 0;
            for (int i = 0; i < m_RendererDataList.Length; i++) emptyEntries += ValidateRendererData(i) ? 0 : 1;
            if (partial)
                return emptyEntries == 0;
            return emptyEntries != m_RendererDataList.Length;
        }

        internal bool ValidateRendererData(int index)
        {
            // Check to see if you are asking for the default renderer
            if (index == -1) index = m_DefaultRendererIndex;
            return index < m_RendererDataList.Length ? m_RendererDataList[index] != null : false;
        }

        #region APV
        [SerializeField, Obsolete("Kept for migration. #from(2023.3")]
        internal ProbeVolumeSceneData apvScenesData;
        #endregion

        public bool supportProbeVolume
        {
            get => lightProbeSystem == LightProbeSystem.ProbeVolumes;
        }

        public ProbeVolumeSHBands maxSHBands
        {
            get
            {
                if (lightProbeSystem == LightProbeSystem.ProbeVolumes)
                    return probeVolumeSHBands;
                else
                    return ProbeVolumeSHBands.SphericalHarmonicsL1;
            }
        }

        [Obsolete("This property is no longer necessary. #from(2023.3)")]
        public ProbeVolumeSceneData probeVolumeSceneData => null;

        public bool isStpUsed
        {
            get
            {
#if ENABLE_UPSCALER_FRAMEWORK
                return m_SelectedUpscalerName == STPIUpscaler.upscalerName;
#else
                return m_UpscalingFilter == UpscalingFilterSelection.STP;
#endif
            }
        }

    }
}

// This file should be used as a container for things on its
// way to being deprecated and removed in future releases
using System;
using System.ComponentModel;
using Unity.Scripting.LifecycleManagement;

namespace UnityEngine.Rendering.Universal
{
    public abstract partial class ScriptableRenderPass
    {
        // This callback method will be removed. Please use OnCameraCleanup() instead.
        /// <param name="cmd">Use this CommandBuffer to cleanup any generated data. </param>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public virtual void FrameCleanup(CommandBuffer cmd) => OnCameraCleanup(cmd);
    }

    namespace Internal
    {
        public partial class AdditionalLightsShadowCasterPass
        {
            [Obsolete("AdditionalLightsShadowCasterPass.m_AdditionalShadowsBufferId was deprecated. Shadow slice matrix is now passed to the GPU using an entry in buffer m_AdditionalLightsWorldToShadow_SSBO. Fast Enter Playmode is not supported. #from(2021.1) #breakingFrom(2023.1)", true)]
            [NoAutoStaticsCleanup] // Obsolete API not supported
            public static int m_AdditionalShadowsBufferId;

            [Obsolete("AdditionalLightsShadowCasterPass.m_AdditionalShadowsIndicesId was deprecated. Shadow slice index is now passed to the GPU using last member of an entry in buffer m_AdditionalShadowParams_SSBO #from(2021.1). Fast Enter Playmode is not supported. #breakingFrom(2023.1)", true)]
            [NoAutoStaticsCleanup] // Obsolete API not supported
            public static int m_AdditionalShadowsIndicesId;
        }
    }

    [Obsolete("This is obsolete, please use shadowCascadeCount instead. #from(2021.1) #breakingFrom(2023.1)", true)]
    public enum ShadowCascadesOption
    {
        NoCascades,
        TwoCascades,
        FourCascades,
    }

    [Obsolete("This is obsolete, UnityEngine.Rendering.ShaderVariantLogLevel instead. #from(2022.2) #breakingFrom(2023.1)", true)]
    public enum ShaderVariantLogLevel
    {
        /// <summary>Disable all log for shader variants.</summary>
        Disabled,

        /// <summary>Only logs SRP Shaders when logging shader variants.</summary>
        [InspectorName("Only URP Shaders")]
        OnlyUniversalRPShaders,

        /// <summary>Logs all shader variants.</summary>
        [InspectorName("All Shaders")]
        AllShaders
    }

    public partial class UniversalRenderPipelineAsset
    {
        #if UNITY_EDITOR
        [Obsolete("Editor resources are stored directly into GraphicsSettings. #from(2023.3)")]
        public static readonly string editorResourcesGUID = "a3d8d823eedde654bb4c11a1cfaf1abb";
        #endif

        [SerializeField] int m_ShaderVariantLogLevel;

#pragma warning disable 618 // Obsolete warning
        [Obsolete("Use GraphicsSettings.GetRenderPipelineSettings<ShaderStrippingSetting>().shaderVariantLogLevel instead. #from(2022.2)")]
        public ShaderVariantLogLevel shaderVariantLogLevel
        {
            get => (ShaderVariantLogLevel)GraphicsSettings.GetRenderPipelineSettings<ShaderStrippingSetting>().shaderVariantLogLevel;
            set => GraphicsSettings.GetRenderPipelineSettings<ShaderStrippingSetting>().shaderVariantLogLevel = (Rendering.ShaderVariantLogLevel)value;
        }
#pragma warning restore 618 // Obsolete warning

#pragma warning disable 618 // Obsolete warning
        [Obsolete("This is obsolete, please use shadowCascadeCount instead. #from(2021.1)")]
        [SerializeField] ShadowCascadesOption m_ShadowCascades = ShadowCascadesOption.NoCascades;

        [Obsolete("This is obsolete, please use shadowCascadeCount instead. #from(2021.1) #breakingFrom(2023.1)", true)]
        public ShadowCascadesOption shadowCascadeOption
        {
            get
            {
                switch (shadowCascadeCount)
                {
                    case 1: return ShadowCascadesOption.NoCascades;
                    case 2: return ShadowCascadesOption.TwoCascades;
                    case 4: return ShadowCascadesOption.FourCascades;
                    default: throw new InvalidOperationException("Cascade count is not compatible with obsolete API, please use shadowCascadeCount instead.");
                }
                ;
            }
            set
            {
                switch (value)
                {
                    case ShadowCascadesOption.NoCascades:
                        shadowCascadeCount = 1;
                        break;
                    case ShadowCascadesOption.TwoCascades:
                        shadowCascadeCount = 2;
                        break;
                    case ShadowCascadesOption.FourCascades:
                        shadowCascadeCount = 4;
                        break;
                    default:
                        throw new InvalidOperationException("Cascade count is not compatible with obsolete API, please use shadowCascadeCount instead.");
                }
            }
        }
#pragma warning restore 618 // Obsolete warning

        [Serializable, ReloadGroup]
        [Obsolete("Moved to UniversalRenderPipelineRuntimeTextures on GraphicsSettings. #from(2023.3)")]
        public sealed class TextureResources
        {
            [Reload("Textures/BlueNoise64/L/LDR_LLL1_0.png")]
            public Texture2D blueNoise64LTex;

            [Reload("Textures/BayerMatrix.png")]
            public Texture2D bayerMatrixTex;

            /// <returns>True if any of the textures need reloading.</returns>
            public bool NeedsReload()
            {
                return blueNoise64LTex == null || bayerMatrixTex == null;
            }
        }

        [Obsolete("Moved to UniversalRenderPipelineRuntimeTextures on GraphicsSettings. #from(2023.3)")]
        [SerializeField]
        TextureResources m_Textures;

        [Obsolete("Moved to UniversalRenderPipelineRuntimeTextures on GraphicsSettings. #from(2023.3)")]
        public TextureResources textures
        {
            get
            {
                if (m_Textures == null)
                    m_Textures = new TextureResources();

#if UNITY_EDITOR
                if (m_Textures.NeedsReload())
                    ResourceReloader.ReloadAllNullIn(this, packagePath);
#endif

                return m_Textures;
            }
        }

        [Obsolete("This property is not used. #from(6000.3)", false)]
        public IntermediateTextureMode intermediateTextureMode
        {
            get => default;
            set {}
        }
    }

    public abstract partial class ScriptableRenderer
    {

    }

    public abstract partial class ScriptableRendererData
    {
        [Obsolete("Moved to UniversalRenderPipelineDebugShaders on GraphicsSettings. #from(2023.3)")]
        [Serializable, ReloadGroup]
        public sealed class DebugShaderResources
        {
            [Obsolete("Moved to UniversalRenderPipelineDebugShaders on GraphicsSettings. #from(2023.3)")]
            [Reload("Shaders/Debug/DebugReplacement.shader")]
            public Shader debugReplacementPS;

            [Obsolete("Moved to UniversalRenderPipelineDebugShaders on GraphicsSettings. #from(2023.3)")]
            [Reload("Shaders/Debug/HDRDebugView.shader")]
            public Shader hdrDebugViewPS;

#if UNITY_EDITOR
            [Obsolete("Moved to UniversalRenderPipelineDebugShaders on GraphicsSettings. #from(2023.3)")]
            [Reload("Shaders/Debug/ProbeVolumeSamplingDebugPositionNormal.compute")]
            public ComputeShader probeVolumeSamplingDebugComputeShader;
#endif
        }

        [Obsolete("Moved to UniversalRenderPipelineDebugShaders on GraphicsSettings. #from(2023.3)")]
        public DebugShaderResources debugShaders;

        [Serializable, ReloadGroup]
        [Obsolete("Probe volume debug resource are now in the ProbeVolumeDebugResources class. #from(2023.3)")]
        public sealed class ProbeVolumeResources
        {
            [Obsolete("This shader is now in the ProbeVolumeDebugResources class. #from(2023.3)")]
            public Shader probeVolumeDebugShader;

            [Obsolete("This shader is now in the ProbeVolumeDebugResources class. #from(2023.3)")]
            public Shader probeVolumeFragmentationDebugShader;

            [Obsolete("This shader is now in the ProbeVolumeDebugResources class. #from(2023.3)")]
            public Shader probeVolumeOffsetDebugShader;

            [Obsolete("This shader is now in the ProbeVolumeDebugResources class. #from(2023.3)")]
            public Shader probeVolumeSamplingDebugShader;

            [Obsolete("This shader is now in the ProbeVolumeDebugResources class. #from(2023.3)")]
            public Mesh probeSamplingDebugMesh;

            [Obsolete("This shader is now in the ProbeVolumeDebugResources class. #from(2023.3)")]
            public Texture2D probeSamplingDebugTexture;

            [Obsolete("This shader is now in the ProbeVolumeRuntimeResources class. #from(2023.3)")]
            public ComputeShader probeVolumeBlendStatesCS;
        }

        [Obsolete("Probe volume debug resource are now in the ProbeVolumeDebugResources class. #from(2023.3)")]
        public ProbeVolumeResources probeVolumeResources;

        [Obsolete("Native Render Pass is automatically enabled through Render Graph, useNativeRenderPass is not considered anymore. #from(6000.5)")]
        public bool useNativeRenderPass
        {
            get => true;
            set { }
        }
    }

    public sealed partial class Bloom : VolumeComponent, IPostProcessComponent
    {
        // Deprecated in 13.x.x
        [Obsolete("This is obsolete, please use maxIterations instead. #from(2022.2) #breakingFrom(2023.1)", true)]
        [Tooltip("The number of final iterations to skip in the effect processing sequence.")]
        public ClampedIntParameter skipIterations = new ClampedIntParameter(1, 0, 16);
    }

    /// <seealso cref="Shader"/>
    [Serializable]
    [Obsolete("Moved to UniversalRenderPipelineRuntimeXRResources on GraphicsSettings. #from(2023.3)")]
    public class XRSystemData : ScriptableObject
    {
        [Serializable, ReloadGroup]
        [Obsolete("Moved to UniversalRenderPipelineRuntimeXRResources on GraphicsSettings. #from(2023.3)")]
        public sealed class ShaderResources
        {
            [Reload("Shaders/XR/XROcclusionMesh.shader")]
            public Shader xrOcclusionMeshPS;

            [Reload("Shaders/XR/XRMirrorView.shader")]
            public Shader xrMirrorViewPS;
        }

        [Obsolete("Moved to UniversalRenderPipelineRuntimeXRResources on GraphicsSettings. #from(2023.3)")]
        public ShaderResources shaders;
    }

    public partial class UniversalRendererData
    {
#if ENABLE_VR && ENABLE_XR_MODULE
        [Obsolete("Moved to UniversalRenderPipelineRuntimeXRResources on GraphicsSettings. #from(2023.3)")]
        //[Reload("Runtime/Data/XRSystemData.asset")]
        public XRSystemData xrSystemData;
#endif
    }

    /// Class containing shader and texture resources needed in URP.
    /// </summary>
    /// <seealso cref="Shader"/>
    /// <seealso cref="Material"/>
    [Obsolete("Moved to GraphicsSettings. #from(2023.3)")]
    public class UniversalRenderPipelineEditorResources : ScriptableObject
    {
        [Serializable, ReloadGroup]
        [Obsolete("UniversalRenderPipelineEditorResources.ShaderResources is obsolete GraphicsSettings.TryGetRenderPipelineSettings<UniversalRenderPipelineEditorShaders>(). #from(2023.3)")]
        public sealed class ShaderResources
        {
            [Reload("Shaders/AutodeskInteractive/AutodeskInteractive.shadergraph")]
            public Shader autodeskInteractivePS;

            [Reload("Shaders/AutodeskInteractive/AutodeskInteractiveTransparent.shadergraph")]
            public Shader autodeskInteractiveTransparentPS;

            [Reload("Shaders/AutodeskInteractive/AutodeskInteractiveMasked.shadergraph")]
            public Shader autodeskInteractiveMaskedPS;

            [Reload("Shaders/Terrain/TerrainDetailLit.shader")]
            public Shader terrainDetailLitPS;

            [Reload("Shaders/Terrain/WavingGrass.shader")]
            public Shader terrainDetailGrassPS;

            [Reload("Shaders/Terrain/WavingGrassBillboard.shader")]
            public Shader terrainDetailGrassBillboardPS;

            [Reload("Shaders/Nature/SpeedTree7.shader")]
            public Shader defaultSpeedTree7PS;

            [Reload("Shaders/Nature/SpeedTree8_PBRLit.shadergraph")]
            public Shader defaultSpeedTree8PS;
        }

        [Serializable, ReloadGroup]
        [Obsolete("UniversalRenderPipelineEditorResources.MaterialResources is obsolete GraphicsSettings.TryGetRenderPipelineSettings<UniversalRenderPipelineEditorMaterials>(). #from(2023.3)")]
        public sealed class MaterialResources
        {
            [Reload("Runtime/Materials/Lit.mat")]
            public Material lit;

            // particleLit is the URP default material for new particle systems.
            // ParticlesUnlit.mat is closest match to the built-in shader.
            // This is correct (current 22.2) despite the Lit/Unlit naming conflict.
            [Reload("Runtime/Materials/ParticlesUnlit.mat")]
            public Material particleLit;

            [Reload("Runtime/Materials/TerrainLit.mat")]
            public Material terrainLit;

            [Reload("Runtime/Materials/Decal.mat")]
            public Material decal;
        }

        [Obsolete("UniversalRenderPipelineEditorResources.ShaderResources is obsolete GraphicsSettings.TryGetRenderPipelineSettings<UniversalRenderPipelineEditorShaders>(). #from(2023.3)")]
        public ShaderResources shaders;

        [Obsolete("UniversalRenderPipelineEditorResources.MaterialResources is obsolete GraphicsSettings.TryGetRenderPipelineSettings<UniversalRenderPipelineEditorMaterials>(). #from(2023.3)")]
        public MaterialResources materials;
    }

#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(UniversalRenderPipelineEditorResources), true)]
    [Obsolete("Deprectated alongside with UniversalRenderPipelineEditorResources. #from(2023.3)")]
    class UniversalRenderPipelineEditorResourcesEditor : UnityEditor.Editor
    {
        /// <inheritdoc/>
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            // Add a "Reload All" button in inspector when we are in developer's mode
            if (UnityEditor.EditorPrefs.GetBool("DeveloperMode") && GUILayout.Button("Reload All"))
            {
                var resources = target as UniversalRenderPipelineEditorResources;
                resources.materials = null;
                resources.shaders = null;
                ResourceReloader.ReloadAllNullIn(target, UniversalRenderPipelineAsset.packagePath);
            }
        }
    }
#endif

    [Serializable, ReloadGroup]
    [Obsolete("Moved to UniversalRenderPipelineRuntimeShaders on GraphicsSettings. #from(2023.3)")]
    public sealed class ShaderResources
    {
        [Obsolete("Moved to UniversalRenderPipelineRuntimeShaders on GraphicsSettings. #from(2023.3)")]
        [Reload("Shaders/Utils/Blit.shader")]
        public Shader blitPS;

        [Reload("Shaders/Utils/CopyDepth.shader")]
        [Obsolete("Moved to UniversalRenderPipelineRuntimeShaders on GraphicsSettings. #from(2023.3)")]
        public Shader copyDepthPS;

        [Obsolete("Obsolete, this feature will be supported by new 'ScreenSpaceShadows' renderer feature. #from(2023.3) #breakingFrom(2023.3)", true)]
        public Shader screenSpaceShadowPS = null;

        [Obsolete("Moved to UniversalRenderPipelineRuntimeShaders on GraphicsSettings. #from(2023.3)")]
        [Reload("Shaders/Utils/Sampling.shader")]
        public Shader samplingPS;

        [Reload("Shaders/Utils/StencilDeferred.shader")]
        [Obsolete("Moved to UniversalRenderPipelineRuntimeShaders on GraphicsSettings. #from(2023.3)")]
        public Shader stencilDeferredPS;

        [Reload("Shaders/Utils/FallbackError.shader")]
        [Obsolete("Moved to UniversalRenderPipelineRuntimeShaders on GraphicsSettings. #from(2023.3)")]
        public Shader fallbackErrorPS;

        [Reload("Shaders/Utils/FallbackLoading.shader")]
        [Obsolete("Moved to UniversalRenderPipelineRuntimeShaders on GraphicsSettings. #from(2023.3)")]
        public Shader fallbackLoadingPS;

        [Obsolete("Use fallbackErrorPS instead. #from(2023.3) #breakingFrom(2023.3)", true)]
        public Shader materialErrorPS = null;

        // Core blitter shaders, adapted from HDRP
        // TODO: move to core and share with HDRP
        [Reload("Shaders/Utils/CoreBlit.shader"), SerializeField]
        [Obsolete("Moved to UniversalRenderPipelineRuntimeShaders on GraphicsSettings. #from(2023.3)")]
        internal Shader coreBlitPS;

        [Reload("Shaders/Utils/CoreBlitColorAndDepth.shader"), SerializeField]
        [Obsolete("Moved to UniversalRenderPipelineRuntimeShaders on GraphicsSettings. #from(2023.3)")]
        internal Shader coreBlitColorAndDepthPS;

        [Reload("Shaders/Utils/BlitHDROverlay.shader"), SerializeField]
        [Obsolete("Moved to UniversalRenderPipelineRuntimeShaders on GraphicsSettings. #from(2023.3)")]
        internal Shader blitHDROverlay;

        [Reload("Shaders/CameraMotionVectors.shader")]
        [Obsolete("Moved to UniversalRenderPipelineRuntimeShaders on GraphicsSettings. #from(2023.3)")]
        public Shader cameraMotionVector;

        [Reload("Shaders/PostProcessing/LensFlareScreenSpace.shader")]
        [Obsolete("Moved to UniversalRenderPipelineRuntimeShaders on GraphicsSettings. #from(2023.3)")]
        public Shader screenSpaceLensFlare;

        [Reload("Shaders/PostProcessing/LensFlareDataDriven.shader")]
        [Obsolete("Moved to UniversalRenderPipelineRuntimeShaders on GraphicsSettings. #from(2023.3)")]
        public Shader dataDrivenLensFlare;
    }

    partial class UniversalRenderPipelineGlobalSettings
    {
#pragma warning disable 0414
        [SerializeField, Obsolete("Keep for migration. #from(2023.2)")] internal ShaderStrippingSetting m_ShaderStrippingSetting = new();
        [SerializeField, Obsolete("Keep for migration. #from(2023.2)")] internal URPShaderStrippingSetting m_URPShaderStrippingSetting = new();
        [SerializeField, Obsolete("Keep for migration. #from(2023.2)")] internal Rendering.ShaderVariantLogLevel m_ShaderVariantLogLevel = Rendering.ShaderVariantLogLevel.Disabled;
        [SerializeField, Obsolete("Keep for migration. #from(2023.2)")] internal bool m_ExportShaderVariants = true;
        [SerializeField, Obsolete("Keep for migration. #from(2023.2)")] internal bool m_StripDebugVariants = true;
        [SerializeField, Obsolete("Keep for migration. #from(2023.2)")] internal bool m_StripUnusedPostProcessingVariants = false;
        [SerializeField, Obsolete("Keep for migration. #from(2023.2)")] internal bool m_StripUnusedVariants = true;
        [SerializeField, Obsolete("Keep for migration. #from(2023.2)")] internal bool m_StripScreenCoordOverrideVariants = true;
#pragma warning restore 0414

        [Obsolete("No longer used as Shader Prefiltering automatically strips out unused LOD Crossfade variants. Please use the LOD Crossfade setting in the URP Asset to disable the feature if not used. #from(2023.1)")]
        public bool stripUnusedLODCrossFadeVariants { get => false; set { } }

        [Obsolete("Please use stripRuntimeDebugShaders instead. #from(2023.1)")]
        public bool supportRuntimeDebugDisplay = false;
    }
}

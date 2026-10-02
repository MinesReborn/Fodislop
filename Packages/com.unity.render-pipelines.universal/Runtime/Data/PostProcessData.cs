using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
using System.Reflection;
#endif

namespace UnityEngine.Rendering.Universal
{
    /// <seealso cref="Shader"/>
    /// <seealso cref="Texture"/>
    [Serializable]
    [URPHelpURL("urp/integration-with-post-processing")]
    public class PostProcessData : ScriptableObject
    {
#if UNITY_EDITOR
        [SuppressMessage("Microsoft.Performance", "CA1812")]
        internal class CreatePostProcessDataAsset : AssetCreationEndAction
        {
            public override void Action(EntityId entityId, string pathName, string resourceFile)
            {
                var instance = CreateInstance<PostProcessData>();
                AssetDatabase.CreateAsset(instance, pathName);
                Selection.activeObject = instance;
            }
        }

        [MenuItem("Assets/Create/Rendering/URP Post-process Data", priority = CoreUtils.Sections.section5 + CoreUtils.Priorities.assetsCreateRenderingMenuPriority)]
        static void CreatePostProcessData()
        {
            var icon = EditorGUIUtility.IconContent("ScriptableObject Icon").image as Texture2D;
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(EntityId.None, CreateInstance<CreatePostProcessDataAsset>(), "CustomPostProcessData.asset", icon, null);
        }

        internal static PostProcessData GetDefaultPostProcessData()
        {
            var path = Path.Combine(UniversalRenderPipelineAsset.packagePath, "Runtime/Data/PostProcessData.asset");
            return AssetDatabase.LoadAssetAtPath<PostProcessData>(path);
        }

        internal void Reset()
        {
            LoadResources(true);
        }

        internal void Populate()
        {
            LoadResources(false);
        }

        void LoadResources(bool reset)
        {
            if (GraphicsSettings.TryGetRenderPipelineSettings<ShaderResources>(out var defaultShaderResources))
            {
                if (shaders == null || reset)
                    shaders = new ShaderResources();

                shaders.Populate(defaultShaderResources);
            }

            if (GraphicsSettings.TryGetRenderPipelineSettings<TextureResources>(out var defaultTextureResources))
            {
                if (textures == null || reset)
                    textures = new TextureResources();

                textures.Populate(defaultTextureResources);
            }
        }

#endif

        [Serializable]
        [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
        [Categorization.CategoryInfo(Name = "R: Default PostProcess Shaders", Order = 1000)]
        [Categorization.ElementInfo(Order = 0), HideInInspector]
        public sealed class ShaderResources : IRenderPipelineResources
        {
            [ResourcePath("Shaders/PostProcessing/StopNaN.shader")]
            public Shader stopNanPS;

            [ResourcePath("Shaders/PostProcessing/SubpixelMorphologicalAntialiasing.shader")]
            public Shader subpixelMorphologicalAntialiasingPS;

            [ResourcePath("Shaders/PostProcessing/GaussianDepthOfField.shader")]
            public Shader gaussianDepthOfFieldPS;

            [ResourcePath("Shaders/PostProcessing/BokehDepthOfField.shader")]
            public Shader bokehDepthOfFieldPS;

            [ResourcePath("Shaders/PostProcessing/CameraMotionBlur.shader")]
            public Shader cameraMotionBlurPS;

            [ResourcePath("Shaders/PostProcessing/PaniniProjection.shader")]
            public Shader paniniProjectionPS;

            [ResourcePath("Shaders/PostProcessing/LutBuilderLdr.shader")]
            public Shader lutBuilderLdrPS;

            [ResourcePath("Shaders/PostProcessing/LutBuilderHdr.shader")]
            public Shader lutBuilderHdrPS;

            [ResourcePath("Shaders/PostProcessing/Bloom.shader")]
            public Shader bloomPS;

            [ResourcePath("Shaders/PostProcessing/TemporalAA.shader")]
            public Shader temporalAntialiasingPS;

            [ResourcePath("Shaders/PostProcessing/LensFlareDataDriven.shader")]
            public Shader LensFlareDataDrivenPS;

            [ResourcePath("Shaders/PostProcessing/LensFlareScreenSpace.shader")]
            public Shader LensFlareScreenSpacePS;

            [ResourcePath("Shaders/PostProcessing/ScalingSetup.shader")]
            public Shader scalingSetupPS;

            [ResourcePath("Shaders/PostProcessing/EdgeAdaptiveSpatialUpsampling.shader")]
            public Shader easuPS;

            [ResourcePath("Shaders/PostProcessing/UberPost.shader")]
            public Shader uberPostPS;

            [ResourcePath("Shaders/PostProcessing/FinalPost.shader")]
            public Shader finalPostPassPS;

#if UNITY_EDITOR
            /// <param name="source">
            /// The source <see cref="ShaderResources"/> object to copy data from. This object must not be null.
            /// </param>
            internal void Populate(ShaderResources source)
            {
                CoreUtils.PopulateNullFieldsFrom(source, this);
            }
#endif

            // This name must be unique within the entire PostProcessData set, as PostProcessDataAnalytics retrieves it.
            [SerializeField][HideInInspector] int m_ShaderResourcesVersion = 0;

            /// <value>
            /// The version number of the resource container. This value is incremented when the resource container changes.
            /// </value>
            public int version => m_ShaderResourcesVersion;

            /// <value>
            /// `false`, indicating that the resource is editor-only and unavailable in a player build.
            /// </value>
            public bool isAvailableInPlayerBuild => false;
        }

        [Serializable]
        [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
        [Categorization.CategoryInfo(Name = "R: Default PostProcess Textures", Order = 1000)]
        [Categorization.ElementInfo(Order = 0), HideInInspector]
        public sealed class TextureResources : IRenderPipelineResources
        {
            [ResourceFormattedPaths("Textures/BlueNoise16/L/LDR_LLL1_{0}.png", 0, 32)]
            public Texture2D[] blueNoise16LTex;

            [Obsolete("Film grain textures have been moved to FilmGrainResources in GraphicsSettings. This field is no longer used. #from(6000.6)", false)]
            public Texture2D[] filmGrainTex;

            [ResourcePath("Textures/SMAA/AreaTex.tga")] public Texture2D smaaAreaTex;

            [ResourcePath("Textures/SMAA/SearchTex.tga")]
            public Texture2D smaaSearchTex;

#if UNITY_EDITOR
            /// <param name="source">
            /// The source <see cref="TextureResources"/> object to copy data from. This object must not be null.
            /// </param>
            internal void Populate(TextureResources source)
            {
                CoreUtils.PopulateNullFieldsFrom(source, this);
            }
#endif

            // This name must be unique within the entire PostProcessData set, as PostProcessDataAnalytics retrieves it.
            [SerializeField][HideInInspector] int m_TexturesResourcesVersion = 0;

            /// <value>
            /// The version number of the resource container. This value is incremented when the resource container changes.
            /// </value>
            public int version => m_TexturesResourcesVersion;

            /// <value>
            /// `false`, indicating that the resource is editor-only and unavailable in a player build.
            /// </value>
            public bool isAvailableInPlayerBuild => false;
        }

        public ShaderResources shaders;

        public TextureResources textures;
    }
}

using System;

namespace UnityEngine.Rendering.Universal
{
    /// <seealso cref="IRenderPipelineResources"/>
    /// <example>
    /// <para> Here is an example of how to get the blit shader used by URP. </para>
    /// <code>
    /// using UnityEngine.Rendering;
    /// using UnityEngine.Rendering.Universal;
    ///
    /// public static class URPUniversalRendererRuntimeShadersHelper
    /// {
    ///     public static Shader blit
    ///     {
    ///         get
    ///         {
    ///             var gs = GraphicsSettings.GetRenderPipelineSettings&lt;UniversalRenderPipelineRuntimeShaders&gt;();
    ///             if (gs == null) //not in URP
    ///                 return null;
    ///             return gs.coreBlitPS;
    ///         }
    ///     }
    /// }
    /// </code>
    /// </example>
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [Categorization.CategoryInfo(Name = "R: Runtime Shaders", Order = 1000), HideInInspector]
    public class UniversalRenderPipelineRuntimeShaders : IRenderPipelineResources
    {
        [SerializeField][HideInInspector] private int m_Version = 0;

        /// <summary>Current version of the resource container. Used only for upgrading a project.</summary>
        public int version => m_Version;
        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;

        [SerializeField, ResourcePath("Shaders/Utils/FallbackError.shader")]
        Shader m_FallbackErrorShader;

        public Shader fallbackErrorShader
        {
            get => m_FallbackErrorShader;
            set => this.SetValueAndNotify(ref m_FallbackErrorShader, value, nameof(m_FallbackErrorShader));
        }


        [SerializeField]
        [ResourcePath("Shaders/Utils/BlitHDROverlay.shader")]
        internal Shader m_BlitHDROverlay;

        public Shader blitHDROverlay
        {
            get => m_BlitHDROverlay;
            set => this.SetValueAndNotify(ref m_BlitHDROverlay, value, nameof(m_BlitHDROverlay));
        }

        [SerializeField]
        [ResourcePath("Shaders/Utils/CoreBlit.shader")]
        internal Shader m_CoreBlitPS;

        public Shader coreBlitPS
        {
            get => m_CoreBlitPS;
            set => this.SetValueAndNotify(ref m_CoreBlitPS, value, nameof(m_CoreBlitPS));
        }

        [SerializeField]
        [ResourcePath("Shaders/Utils/CoreBlitColorAndDepth.shader")]
        internal Shader m_CoreBlitColorAndDepthPS;

        public Shader coreBlitColorAndDepthPS
        {
            get => m_CoreBlitColorAndDepthPS;
            set => this.SetValueAndNotify(ref m_CoreBlitColorAndDepthPS, value, nameof(m_CoreBlitColorAndDepthPS));
        }

        [SerializeField]
        [ResourcePath("Shaders/Utils/UIBackdropFilterComposite.shader")]
        internal Shader m_UIBackdropFilterCompositePS;

        internal Shader uiBackdropFilterCompositePS
        {
            get => m_UIBackdropFilterCompositePS;
            set => this.SetValueAndNotify(ref m_UIBackdropFilterCompositePS, value, nameof(m_UIBackdropFilterCompositePS));
        }

        [SerializeField]
        [ResourcePath("Shaders/Utils/Sampling.shader")]
        private Shader m_SamplingPS;

        public Shader samplingPS
        {
            get => m_SamplingPS;
            set => this.SetValueAndNotify(ref m_SamplingPS, value, nameof(m_SamplingPS));
        }

        #region Terrain
        [Header("Terrain")]

        // Original serialized fields preserved for migration purposes.
        // These fields maintain the original serialized data that was moved to UniversalRenderPipelineRuntimeTerrainShaders;
        // when the asset is migrated from version 9 to 10, these fields are copied to their equivalents in
        // UniversalRenderPipelineRuntimeTerrainShaders, and these are then set to null.
        [SerializeField, HideInInspector] private Shader m_TerrainDetailLit;
        [SerializeField, HideInInspector] private Shader m_TerrainDetailGrassBillboard;
        [SerializeField, HideInInspector] private Shader m_TerrainDetailGrass;
        
        // Internal methods to access original serialized fields for migration
        internal Shader GetOriginalTerrainDetailLitShader() => m_TerrainDetailLit;
        internal Shader GetOriginalTerrainDetailGrassBillboardShader() => m_TerrainDetailGrassBillboard;
        internal Shader GetOriginalTerrainDetailGrassShader() => m_TerrainDetailGrass;
        internal void ClearOriginalTerrainDetailShaders()
        {
            m_TerrainDetailLit = null;
            m_TerrainDetailGrassBillboard = null;
            m_TerrainDetailGrass = null;
        }
        
        [Obsolete("terrainDetailLitShader is obsolete. Use UniversalRenderPipelineRuntimeTerrainShaders.terrainDetailLitShader instead.", false)]
        public Shader terrainDetailLitShader
        {
            get
            {
                if (GraphicsSettings.TryGetRenderPipelineSettings<UniversalRenderPipelineRuntimeTerrainShaders>(
                    out var shadersResources))
                {
                    return shadersResources.terrainDetailLitShader;
                }
                return null;
            }
            set
            {
                if (GraphicsSettings.TryGetRenderPipelineSettings<UniversalRenderPipelineRuntimeTerrainShaders>(
                    out var shadersResources))
                {
                    shadersResources.terrainDetailLitShader = value;
                }
            }
        }

        [Obsolete("terrainDetailGrassBillboardShader is obsolete. Use UniversalRenderPipelineRuntimeTerrainShaders.terrainDetailGrassBillboardShader instead.", false)]
        public Shader terrainDetailGrassBillboardShader
        {
            get
            {
                if (GraphicsSettings.TryGetRenderPipelineSettings<UniversalRenderPipelineRuntimeTerrainShaders>(
                    out var shadersResources))
                {
                    return shadersResources.terrainDetailGrassBillboardShader;
                }
                return null;
            }
            set
            {
                if (GraphicsSettings.TryGetRenderPipelineSettings<UniversalRenderPipelineRuntimeTerrainShaders>(
                    out var shadersResources))
                {
                    shadersResources.terrainDetailGrassBillboardShader = value;
                }
            }
        }

        [Obsolete("terrainDetailGrassShader is obsolete; Use UniversalRenderPipelineRuntimeTerrainShaders.terrainDetailGrassShader instead.)", false)]
        public Shader terrainDetailGrassShader
        {
            get
            {
                if (GraphicsSettings.TryGetRenderPipelineSettings<UniversalRenderPipelineRuntimeTerrainShaders>(
                    out var shadersResources))
                {
                    return shadersResources.terrainDetailGrassShader;
                }
                return null;
            }
            set
            {
                if (GraphicsSettings.TryGetRenderPipelineSettings<UniversalRenderPipelineRuntimeTerrainShaders>(
                    out var shadersResources))
                {
                    shadersResources.terrainDetailGrassShader = value;
                }
            }
        }
        #endregion
    }
}

using System;
using UnityEngine;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [Categorization.CategoryInfo(Name = "R: On Tile Post Process Resources", Order = 1000), HideInInspector]
    class OnTilePostProcessResource : IRenderPipelineResources
    {
        [SerializeField, HideInInspector]
        int m_Version = 0;

        /// <summary>Current version of the resource container. Used only for upgrading a project.</summary>
        public int version => m_Version;

        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;

        [SerializeField]
        [ResourcePath("Runtime/RendererFeatures/OnTileUberPost.shader")]
        Shader m_UberPostShader;

        public Shader uberPostShader
        {
            get => m_UberPostShader;
            set => this.SetValueAndNotify(ref m_UberPostShader, value, nameof(m_UberPostShader));
        }
    }
}

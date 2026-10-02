using System;

namespace UnityEngine.Rendering.Universal
{
    /// <seealso cref="IRenderPipelineResources"/>
    /// <example>
    /// <para> Here is an example of how to get the MotionVector shader used by URP for XR. </para>
    /// <code>
    /// using UnityEngine.Rendering;
    /// using UnityEngine.Rendering.Universal;
    /// 
    /// public static class URPUniversalRendererRuntimeXRResourcesHelper
    /// {
    ///     public static Shader motionVector
    ///     {
    ///         get
    ///         {
    ///             var gs = GraphicsSettings.GetRenderPipelineSettings&lt;UniversalRenderPipelineRuntimeXRResources&gt;();
    ///             if (gs == null) //not in URP or XR not enabled
    ///                 return null;
    ///             return gs.xrMotionVector;
    ///         }
    ///     }
    /// }
    /// </code>
    /// </example>
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [Categorization.CategoryInfo(Name = "R: Runtime XR", Order = 1000), HideInInspector]
    public class UniversalRenderPipelineRuntimeXRResources : IRenderPipelineResources
    {
        public int version => 0;

        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;

        [SerializeField]
        [ResourcePath("Shaders/XR/XROcclusionMesh.shader")]
        private Shader m_xrOcclusionMeshPS;

        public Shader xrOcclusionMeshPS
        {
            get => m_xrOcclusionMeshPS;
            set => this.SetValueAndNotify(ref m_xrOcclusionMeshPS, value, nameof(m_xrOcclusionMeshPS));
        }

        [SerializeField]
        [ResourcePath("Shaders/XR/XRMirrorView.shader")]
        private Shader m_xrMirrorViewPS;

        public Shader xrMirrorViewPS
        {
            get => m_xrMirrorViewPS;
            set => this.SetValueAndNotify(ref m_xrMirrorViewPS, value, nameof(m_xrMirrorViewPS));
        }

        [SerializeField]
        [ResourcePath("Shaders/XR/XRMotionVector.shader")]
        private Shader m_xrMotionVector;

        public Shader xrMotionVector
        {
            get => m_xrMotionVector;
            set => this.SetValueAndNotify(ref m_xrMotionVector, value, nameof(m_xrMotionVector));
        }

        internal bool valid
        {
            get
            {
                if (xrOcclusionMeshPS == null)
                    return false;

                if (xrMirrorViewPS == null)
                    return false;

                if (m_xrMotionVector == null)
                    return false;

                return true;
            }
        }
    }
}

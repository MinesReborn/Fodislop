using System;

namespace UnityEngine.Rendering.Universal
{
    /// <seealso cref="IRenderPipelineResources"/>
    /// <example>
    /// <para> Here is an example of how to get the replacement pixel shader used by URP. </para>
    /// <code>
    /// using UnityEngine.Rendering;
    /// using UnityEngine.Rendering.Universal;
    ///
    /// public static class URPUniversalRendererDebugShadersHelper
    /// {
    ///     public static Shader replacementPS
    ///     {
    ///         get
    ///         {
    ///             var gs = GraphicsSettings.GetRenderPipelineSettings&lt;UniversalRenderPipelineDebugShaders&gt;();
    ///             if (gs == null) //not in URP or not in development build
    ///                 return null;
    ///             return gs.debugReplacementPS;
    ///         }
    ///     }
    /// }
    /// </code>
    /// </example>
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [Categorization.CategoryInfo(Name = "R: Debug Shaders", Order = 1000), HideInInspector]
    public class UniversalRenderPipelineDebugShaders : IRenderPipelineResources
    {
        public int version => 0;

        bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;

        [SerializeField]
        [ResourcePath("Shaders/Debug/DebugReplacement.shader")]
        Shader m_DebugReplacementPS;

        public Shader debugReplacementPS
        {
            get => m_DebugReplacementPS;
            set => this.SetValueAndNotify(ref m_DebugReplacementPS, value, nameof(m_DebugReplacementPS));
        }

        [SerializeField]
        [ResourcePath("Shaders/Debug/HDRDebugView.shader")]
        Shader m_HdrDebugViewPS;

        public Shader hdrDebugViewPS
        {
            get => m_HdrDebugViewPS;
            set => this.SetValueAndNotify(ref m_HdrDebugViewPS, value, nameof(m_HdrDebugViewPS));
        }

        [SerializeField]
        [ResourcePath("Shaders/Debug/ProbeVolumeSamplingDebugPositionNormal.compute")]
        ComputeShader m_ProbeVolumeSamplingDebugComputeShader;

        public ComputeShader probeVolumeSamplingDebugComputeShader
        {
            get => m_ProbeVolumeSamplingDebugComputeShader;
            set => this.SetValueAndNotify(ref m_ProbeVolumeSamplingDebugComputeShader, value, nameof(m_ProbeVolumeSamplingDebugComputeShader));
        }
    }
}

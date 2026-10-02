using System;

namespace UnityEngine.Rendering.Universal
{
    [Obsolete("Renderer override is no longer used, renderers are referenced by index on the pipeline asset. #from(2023.1)")]
    public enum RendererOverrideOption
    {
        Custom,

        UsePipelineSettings,
    }
    
    public partial class UniversalAdditionalCameraData
    {
        [Obsolete("This field has been deprecated. #from(6000.2)")]
        public float version => (int)m_Version;
    }
}
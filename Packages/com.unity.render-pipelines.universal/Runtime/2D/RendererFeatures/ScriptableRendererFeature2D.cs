namespace UnityEngine.Rendering.Universal
{
    /// <seealso cref="ScriptableRenderer"/>
    /// <seealso cref="ScriptableRenderPass2D"/>
    [ExcludeFromPreset]
    [SupportedOnRenderer(typeof(Renderer2DData))]
    public abstract partial class ScriptableRendererFeature2D : ScriptableRendererFeature
    {
        [HideInInspector]
        public RenderPassEvent2D injectionPoint2D = RenderPassEvent2D.BeforeRendering;

        [HideInInspector]
        public int sortingLayerID = 0;
    }
}

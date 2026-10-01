using System;

namespace UnityEngine.Rendering.Universal
{
    /// <seealso cref="ScriptableRenderer"/>
    /// <seealso cref="ScriptableRenderPass"/>
    [ExcludeFromPreset]
    public abstract partial class ScriptableRendererFeature : ScriptableObject, IDisposable
    {
        [SerializeField, HideInInspector] private bool m_Active = true;
        public bool isActive => m_Active;
        
        [Obsolete("This enum is not used. #from(6000.3)", false)]
        public enum IntermediateTextureUsage 
        {
            Unknown, 
            Required, 
            NotRequired 
        }

        [Obsolete("This property is not used. #from(6000.3)", false)]
        protected virtual IntermediateTextureUsage useIntermediateTextures => IntermediateTextureUsage.Unknown;

        public abstract void Create();

        /// <param name="renderer">Renderer of callback.</param>
        /// <param name="cameraData">CameraData contains all relevant render target information for the camera.</param>
        public virtual void OnCameraPreCull(ScriptableRenderer renderer, in CameraData cameraData) { }

        /// <param name="renderer">Renderer used for adding render passes.</param>
        /// <param name="renderingData">Rendering state. Use this to setup render passes.</param>
        public abstract void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData);

        void OnEnable()
        {
            // UUM-44048: If the pipeline is not created, don't call Create() as it may allocate RTHandles or do other
            // things that require the pipeline to be constructed. This is safe because once the pipeline is constructed,
            // ScriptableRendererFeature.Create() will be called by ScriptableRenderer constructor.
            if (RenderPipelineManager.currentPipeline is UniversalRenderPipeline)
                Create();
        }

        void OnValidate()
        {
            // See comment in OnEnable.
            if (RenderPipelineManager.currentPipeline is UniversalRenderPipeline)
                Create();
        }

        /// <param name="isDeferred">True if renderer is using deferred rendering mode</param>
        /// <param name="needsGBufferAccurateNormals">True if renderer has Accurate G-Buffer Normals enabled</param>
        /// <param name="atEvent">Requeted event at which rendering layers texture will be produced</param>
        /// <param name="maskSize">Requested bit size of rendering layers texture</param>
        /// <returns></returns>
        internal virtual bool RequireRenderingLayers(bool isDeferred, bool needsGBufferAccurateNormals, out RenderingLayerUtils.Event atEvent, out RenderingLayerUtils.MaskSize maskSize)
        {
            atEvent = RenderingLayerUtils.Event.Opaque;
            maskSize = RenderingLayerUtils.MaskSize.Bits8;
            return false;
        }

        /// <param name="active">The true value activates the ScriptableRenderFeature and the false value deactivates it.</param>
        public void SetActive(bool active)
        {
            m_Active = active;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
        }

        internal ScriptableRenderer.RenderingFeatures supportedRenderingFeatures { get; set; } = new ScriptableRenderer.RenderingFeatures();
    }
}

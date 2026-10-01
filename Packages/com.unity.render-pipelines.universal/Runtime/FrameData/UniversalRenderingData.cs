namespace UnityEngine.Rendering.Universal
{
    public class UniversalRenderingData : ContextItem
    {
        public CullingResults cullResults;

        // True when cullResults was reused from a previous pass.
        // Consumers should avoid mutating the culling state.
        internal bool reuseCullingResult;

        [System.Obsolete("supportsDynamicBatching is obsolete.", true)]
        public bool supportsDynamicBatching;

        public PerObjectData perObjectData;

        public RenderingMode renderingMode { get; internal set; }

        public LayerMask prepassLayerMask { get; internal set; }

        public LayerMask opaqueLayerMask { get; internal set; }

        public LayerMask transparentLayerMask { get; internal set; }

        public bool stencilLodCrossFadeEnabled { get; internal set; }

#if URP_SCREEN_SPACE_REFLECTION
        public bool writesSmoothnessToDepthNormalsAlpha { get; internal set; }
#endif

        /// <inheritdoc/>
        public override void Reset()
        {
            cullResults = default;
            reuseCullingResult = default;
            perObjectData = default;
            renderingMode = default;
            stencilLodCrossFadeEnabled = default;
            prepassLayerMask = -1;
            opaqueLayerMask = -1;
            transparentLayerMask = -1;
#if URP_SCREEN_SPACE_REFLECTION
            writesSmoothnessToDepthNormalsAlpha = false;
#endif
        }
    }
}

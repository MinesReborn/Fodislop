using System;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public partial class UniversalCameraData : ContextItem
    {
        internal Vector2Int worldGridTargetSize;
        internal Vector2 worldGridWorldSize;
        // Internal camera data as we are not yet sure how to expose View in stereo context.
        // We might change this API soon.
        Matrix4x4 m_ViewMatrix;
        Matrix4x4 m_ProjectionMatrix;
        Matrix4x4 m_JitterMatrix;

        internal void SetViewAndProjectionMatrix(Matrix4x4 viewMatrix, Matrix4x4 projectionMatrix)
        {
            m_ViewMatrix = viewMatrix;
            m_ProjectionMatrix = projectionMatrix;
            m_JitterMatrix = Matrix4x4.identity;
        }

        internal void SetViewProjectionAndJitterMatrix(Matrix4x4 viewMatrix, Matrix4x4 projectionMatrix, Matrix4x4 jitterMatrix)
        {
            m_ViewMatrix = viewMatrix;
            m_ProjectionMatrix = projectionMatrix;
            m_JitterMatrix = jitterMatrix;
        }

#if ENABLE_VR && ENABLE_XR_MODULE
        private bool m_CachedRenderIntoTextureXR;
        private bool m_InitBuiltinXRConstants;
#endif
        // Helper function to populate builtin stereo matricies as well as URP stereo matricies
        internal void PushBuiltinShaderConstantsXR(RasterCommandBuffer cmd, bool renderIntoTexture)
        {
#if ENABLE_VR && ENABLE_XR_MODULE
            // Multipass always needs update to prevent wrong view projection matrix set by other passes
            bool needsUpdate = !m_InitBuiltinXRConstants || m_CachedRenderIntoTextureXR != renderIntoTexture || !xr.singlePassEnabled;
            if (needsUpdate && xr.enabled )
            {
                var projection0 = GetProjectionMatrix();
                var view0 = GetViewMatrix();
                cmd.SetViewProjectionMatrices(view0, projection0);

                if (xr.singlePassEnabled)
                {
                    var projection1 = GetProjectionMatrix(1);
                    var view1 = GetViewMatrix(1);
                    XRBuiltinShaderConstants.UpdateBuiltinShaderConstants(view0, projection0, renderIntoTexture, 0);
                    XRBuiltinShaderConstants.UpdateBuiltinShaderConstants(view1, projection1, renderIntoTexture, 1);
                    XRBuiltinShaderConstants.SetBuiltinShaderConstants(cmd);
                }
                else
                {
                    // Update multipass worldSpace camera pos
                    Vector3 worldSpaceCameraPos = Matrix4x4.Inverse(GetViewMatrix(0)).GetColumn(3);
                    cmd.SetGlobalVector(ShaderPropertyId.worldSpaceCameraPos, worldSpaceCameraPos);

                    //Multipass uses the same value as a normal render, and doesn't use the value set for stereo,
                    //which is why you need to set a value like unity_MatrixInvV.
                    //The values below should be the same as set in the SetCameraMatrices function in ScriptableRenderer.cs.
                    Matrix4x4 gpuProjectionMatrix = GetGPUProjectionMatrix(renderIntoTexture); // TODO: invProjection might NOT match the actual projection (invP*P==I) as the target flip logic has diverging paths.
                    Matrix4x4 inverseViewMatrix = Matrix4x4.Inverse(view0);
                    Matrix4x4 inverseProjectionMatrix = Matrix4x4.Inverse(gpuProjectionMatrix);
                    Matrix4x4 inverseViewProjection = inverseViewMatrix * inverseProjectionMatrix;

                    // There's an inconsistency in handedness between unity_matrixV and unity_WorldToCamera
                    // Unity changes the handedness of unity_WorldToCamera (see Camera::CalculateMatrixShaderProps)
                    // we will also change it here to avoid breaking existing shaders. (case 1257518)
                    Matrix4x4 worldToCameraMatrix = Matrix4x4.Scale(new Vector3(1.0f, 1.0f, -1.0f)) * view0;
                    Matrix4x4 cameraToWorldMatrix = worldToCameraMatrix.inverse;
                    cmd.SetGlobalMatrix(ShaderPropertyId.worldToCameraMatrix, worldToCameraMatrix);
                    cmd.SetGlobalMatrix(ShaderPropertyId.cameraToWorldMatrix, cameraToWorldMatrix);

                    cmd.SetGlobalMatrix(ShaderPropertyId.inverseViewMatrix, inverseViewMatrix);
                    cmd.SetGlobalMatrix(ShaderPropertyId.inverseProjectionMatrix, inverseProjectionMatrix);
                    cmd.SetGlobalMatrix(ShaderPropertyId.inverseViewAndProjectionMatrix, inverseViewProjection);
                }
                m_CachedRenderIntoTextureXR = renderIntoTexture;
                m_InitBuiltinXRConstants = true;
            }
#endif
        }

        /// <param name="viewIndex"> View index in case of stereo rendering. By default <c>viewIndex</c> is set to 0. </param>
        /// <returns> The camera view matrix. </returns>
        public Matrix4x4 GetViewMatrix(int viewIndex = 0)
        {
#if ENABLE_VR && ENABLE_XR_MODULE
            if (xr.enabled)
                return xr.GetViewMatrix(viewIndex);
#endif
            return m_ViewMatrix;
        }

        /// <param name="viewIndex"> View index in case of stereo rendering. By default <c>viewIndex</c> is set to 0. </param>
        /// <returns> The camera projection matrix. </returns>
        public Matrix4x4 GetProjectionMatrix(int viewIndex = 0)
        {
#if ENABLE_VR && ENABLE_XR_MODULE
            if (xr.enabled)
                return m_JitterMatrix * xr.GetProjMatrix(viewIndex);
#endif
            return m_JitterMatrix * m_ProjectionMatrix;
        }

        internal Matrix4x4 GetProjectionMatrixNoJitter(int viewIndex = 0)
        {
#if ENABLE_VR && ENABLE_XR_MODULE
            if (xr.enabled)
                return xr.GetProjMatrix(viewIndex);
#endif
            return m_ProjectionMatrix;
        }

        internal Matrix4x4 GetGPUProjectionMatrix(bool renderIntoTexture, int viewIndex = 0)
        {
            return GL.GetGPUProjectionMatrix(GetProjectionMatrix(viewIndex), renderIntoTexture);
        }

        // The jitter matrix is Matrix4x4.Translate(offsetX, offsetY, 0), so the offsets sit in column 3.
        // Jitter values are in NDC space (-1 to 1).
        internal Vector2 jitter => new Vector2(m_JitterMatrix.m03, m_JitterMatrix.m13);

        public Camera camera;

        public int scaledWidth;

        public int scaledHeight;

#if ENABLE_UPSCALER_FRAMEWORK
        // The hardware dynamic resolution (ScalableBufferManager) scale captured once per camera at setup, so upscaler
        // passes read a stable per-camera value instead of the live, global, mid-frame-mutable SBM factors (which other
        // passes could change between the camera color being aliased and the upscaler reading it). Vector2.one when the
        // camera does not use hardware dynamic resolution.
        internal Vector2 hardwareDynamicResolutionScale;
#endif

        // NOTE: This is internal instead of private to allow ref return in the old CameraData compatibility property.
        // We can make this private when it is removed.
        //
        // A (non-owning) reference of full writable camera history for internal and injected render passes.
        // Only passes/code executing inside the pipeline should have access.
        // Use the "historyManager" property below to access.
        internal UniversalCameraHistory m_HistoryManager;

        /// <seealso cref="ScriptableRenderPass"/>
        public UniversalCameraHistory historyManager { get => m_HistoryManager; set => m_HistoryManager = value; }

        public CameraRenderType renderType;

        public RenderTexture targetTexture;

        public RenderTextureDescriptor cameraTargetDescriptor;
        internal RenderTargetInfo backbufferColor;
        internal RenderTargetInfo backbufferDepth;
        internal Rect pixelRect;
        internal bool useScreenCoordOverride;
        internal Vector4 screenSizeOverride;
        internal Vector4 screenCoordScaleBias;
        internal int pixelWidth;
        internal int pixelHeight;
        internal float aspectRatio;

        public float renderScale;
        internal ImageScalingMode imageScalingMode;

#if ENABLE_UPSCALER_FRAMEWORK
        internal int resolvedUpscalerHash; // hash generated by Shader.PropertyToID()

        [Obsolete("upscalingFilter is no longer used #from(6000.3)")]
#endif
        internal ImageUpscalingFilter upscalingFilter;
        internal bool fsrOverrideSharpness;
        internal float fsrSharpness;

        internal HDRColorBufferPrecision hdrColorBufferPrecision;

        public bool clearDepth;

        public CameraType cameraType;

        public bool isDefaultViewport;

        public bool isHdrEnabled;

        public bool allowHDROutput;

        public bool isAlphaOutputEnabled;

        public bool requiresDepthTexture;

        public bool requiresOpaqueTexture;

        public bool postProcessingRequiresDepthTexture;

        public bool xrRendering;

        // True if GPU occlusion culling should be used when rendering this camera.
        internal bool useGPUOcclusionCulling;

        internal bool requireSrgbConversion
        {
            get
            {
#if ENABLE_VR && ENABLE_XR_MODULE
                // For some XR platforms we need to encode in SRGB but can't use a _SRGB format texture, only required for 8bit per channel 32 bit formats.
                if (xr.enabled)
                    return !xr.renderTargetDesc.sRGB && (xr.renderTargetDesc.graphicsFormat == GraphicsFormat.R8G8B8A8_UNorm || xr.renderTargetDesc.graphicsFormat == GraphicsFormat.B8G8R8A8_UNorm) && (QualitySettings.activeColorSpace == ColorSpace.Linear);
#endif

                return targetTexture == null && Display.main.requiresSrgbBlitToBackbuffer;
            }
        }

        public bool isGameCamera => cameraType == CameraType.Game;

        public bool isSceneViewCamera => cameraType == CameraType.SceneView;

        public bool isPreviewCamera => cameraType == CameraType.Preview;

        internal bool isRenderPassSupportedCamera => (cameraType == CameraType.Game || cameraType == CameraType.Reflection);

        internal bool resolveToScreen => targetTexture == null && resolveFinalTarget && (cameraType == CameraType.Game || camera.cameraType == CameraType.VR);

        public bool isHDROutputActive
        {
            get
            {
                bool hdrDisplayOutputActive = UniversalRenderPipeline.HDROutputForMainDisplayIsActive();
#if ENABLE_VR && ENABLE_XR_MODULE
                // If we are rendering to xr then we need to look at the XR Display rather than the main non-xr display.
                if (xr.enabled)
                    hdrDisplayOutputActive = xr.isHDRDisplayOutputActive;
#endif
                return hdrDisplayOutputActive && allowHDROutput && resolveToScreen;
            }
        }

        internal bool stackLastCameraOutputToHDR;

        public HDROutputUtils.HDRDisplayInformation hdrDisplayInformation
        {
            get
            {
                HDROutputUtils.HDRDisplayInformation displayInformation;
#if ENABLE_VR && ENABLE_XR_MODULE
                // If we are rendering to xr then we need to look at the XR Display rather than the main non-xr display.
                if (xr.enabled)
                {
                    displayInformation = xr.hdrDisplayOutputInformation;
                }
                else
#endif
                {
                    HDROutputSettings displaySettings = HDROutputSettings.main;
                    displayInformation = new HDROutputUtils.HDRDisplayInformation(displaySettings.maxFullFrameToneMapLuminance,
                        displaySettings.maxToneMapLuminance,
                        displaySettings.minToneMapLuminance,
                        displaySettings.paperWhiteNits);
                }

                return displayInformation;
            }
        }

        public ColorGamut hdrDisplayColorGamut
        {
            get
            {
#if ENABLE_VR && ENABLE_XR_MODULE
                // If we are rendering to xr then we need to look at the XR Display rather than the main non-xr display.
                if (xr.enabled)
                {
                    return xr.hdrDisplayOutputColorGamut;
                }
                else
#endif
                {
                    HDROutputSettings displaySettings = HDROutputSettings.main;
                    return displaySettings.displayColorGamut;
                }
            }
        }

        public bool rendersOverlayUI => SupportedRenderingFeatures.active.rendersUIOverlay && resolveToScreen;

        internal bool rendersOffscreenUI;

        internal bool blitsOffscreenUICover;

        /// <param name="handle">Handle to check the flipped status on.</param>
        /// <returns>True is the content is flipped in y.</returns>
        public bool IsHandleYFlipped(RTHandle handle)
        {
            if (!SystemInfo.graphicsUVStartsAtTop)
                return true;

            if (cameraType == CameraType.SceneView || cameraType == CameraType.Preview)
                return true;

            var handleID = new RenderTargetIdentifier(handle.nameID, 0, CubemapFace.Unknown, 0);
            bool isBackbuffer = handleID == BuiltinRenderTextureType.CameraTarget || handleID == BuiltinRenderTextureType.Depth;
#if ENABLE_VR && ENABLE_XR_MODULE
            if (xr.enabled)
                isBackbuffer |= handleID == new RenderTargetIdentifier(xr.renderTarget, 0, CubemapFace.Unknown, 0);
#endif
            return !isBackbuffer;
        }

        /// <param name="color">Color render target to check whether the matrix is flipped.</param>
        /// <param name="depth">Depth render target which is used if color is null. By default <c>depth</c> is set to null.</param>
        /// <returns> True if the render target's projection matrix is flipped. </returns>
        public bool IsRenderTargetProjectionMatrixFlipped(RTHandle color, RTHandle depth = null)
        {
            if (!SystemInfo.graphicsUVStartsAtTop)
                return true;

            return targetTexture != null || IsHandleYFlipped(color ?? depth);
        }

        /// <returns>True if TAA is requested</returns>
        internal bool IsTemporalAARequested()
        {
            return antialiasing == AntialiasingMode.TemporalAntiAliasing;
        }

        /// <returns>True if TAA is enabled</returns>
        internal bool IsTemporalAAEnabled()
        {
            UniversalAdditionalCameraData additionalCameraData;
            camera.TryGetComponent(out additionalCameraData);

            return IsTemporalAARequested()                                                                                            // Requested
                   && postProcessEnabled                                                                                              // Postprocessing Enabled
                   && (taaHistory != null)                                                                                            // Initialized
                   && (cameraTargetDescriptor.msaaSamples == 1)                                                                       // No MSAA
                   && !(additionalCameraData?.renderType == CameraRenderType.Overlay || additionalCameraData?.cameraStack.Count > 0)  // No Camera stack
                   && !camera.allowDynamicResolution                                                                                  // No Dynamic Resolution
                   && renderer.SupportsMotionVectors();                                                                               // Motion Vectors implemented
        }

        /// <returns>True if STP is requested</returns>
        internal bool IsSTPRequested()
        {
            return (imageScalingMode == ImageScalingMode.Upscaling) &&
#if ENABLE_UPSCALER_FRAMEWORK
                (resolvedUpscalerHash == UniversalRenderPipeline.k_UpscalerHash_STP)
#else
                (upscalingFilter == ImageUpscalingFilter.STP)
#endif
                ;
        }

        /// <returns>True if STP is enabled</returns>
        internal bool IsSTPEnabled()
        {
            return IsSTPRequested() && IsTemporalAAEnabled();
        }

        /// <seealso cref="SortingCriteria"/>
        public SortingCriteria defaultOpaqueSortFlags;

        public XRPass xr { get; internal set; }

        internal XRPassUniversal xrUniversal => xr as XRPassUniversal;

        public float maxShadowDistance;

        public bool postProcessEnabled;

        internal bool stackAnyPostProcessingEnabled;

        public IEnumerator<Action<RenderTargetIdentifier, CommandBuffer>> captureActions;

        public LayerMask volumeLayerMask;

        public Transform volumeTrigger;

        public bool isStopNaNEnabled;

        public bool isDitheringEnabled;

        public AntialiasingMode antialiasing;

        public AntialiasingQuality antialiasingQuality;

        public ScriptableRenderer renderer;

        public bool resolveFinalTarget;

        public Vector3 worldSpaceCameraPos;

        public Color backgroundColor;

        internal TaaHistory taaHistory;

        internal StpHistory stpHistory;

        // TAA settings.
        internal TemporalAA.Settings taaSettings;

        // Sub-pixel jitter applied to the projection matrix this frame.
        // Computed once during camera setup, read by upscalers during post-process.
        internal Vector2 subpixelJitter;

        // Post-process history reset has been triggered for this camera.
        internal bool resetHistory
        {
            get => taaSettings.resetHistoryFrames != 0;
        }

        public Camera baseCamera;

        internal bool isLastBaseCamera;

        ///<inheritdoc/>
        public override void Reset()
        {
            worldGridTargetSize = default;
            worldGridWorldSize = default;
            m_ViewMatrix = default;
            m_ProjectionMatrix = default;
            m_JitterMatrix = default;
#if ENABLE_VR && ENABLE_XR_MODULE
            m_CachedRenderIntoTextureXR = false;
            m_InitBuiltinXRConstants = false;
#endif
            camera = null;
            renderType = CameraRenderType.Base;
            targetTexture = null;
            cameraTargetDescriptor = default;
            backbufferColor = default;
            backbufferDepth = default;
            pixelRect = default;
            useScreenCoordOverride = false;
            screenSizeOverride = default;
            screenCoordScaleBias = default;
            pixelWidth = 0;
            pixelHeight = 0;
            aspectRatio = 0.0f;
            renderScale = 1.0f;
            imageScalingMode = ImageScalingMode.None;
#if ENABLE_UPSCALER_FRAMEWORK
            resolvedUpscalerHash = -1;
            hardwareDynamicResolutionScale = Vector2.one;
#else
            upscalingFilter = ImageUpscalingFilter.Point;
#endif
            fsrOverrideSharpness = false;
            fsrSharpness = 0.0f;
            hdrColorBufferPrecision = HDRColorBufferPrecision._32Bits;
            clearDepth = false;
            cameraType = CameraType.Game;
            isDefaultViewport = false;
            isHdrEnabled = false;
            allowHDROutput = false;
            isAlphaOutputEnabled = false;
            requiresDepthTexture = false;
            requiresOpaqueTexture = false;
            postProcessingRequiresDepthTexture = false;
            xrRendering = false;
            useGPUOcclusionCulling = false;
            defaultOpaqueSortFlags = SortingCriteria.None;
            xr = default;
            maxShadowDistance = 0.0f;
            postProcessEnabled = false;
            captureActions = default;
            volumeLayerMask = 0;
            volumeTrigger = default;
            isStopNaNEnabled = false;
            isDitheringEnabled = false;
            antialiasing = AntialiasingMode.None;
            antialiasingQuality = AntialiasingQuality.Low;
            renderer = null;
            resolveFinalTarget = false;
            worldSpaceCameraPos = default;
            backgroundColor = Color.black;
            taaHistory = null;
            stpHistory = null;
            taaSettings = default;
            subpixelJitter = default;
            baseCamera = null;
            isLastBaseCamera = false;
            stackAnyPostProcessingEnabled = false;
            stackLastCameraOutputToHDR = false;
            rendersOffscreenUI = false;
            blitsOffscreenUICover = false;
        }
    }
}

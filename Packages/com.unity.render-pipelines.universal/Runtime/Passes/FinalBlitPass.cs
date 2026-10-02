using System;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.Universal.Internal
{
    public partial class FinalBlitPass : ScriptableRenderPass
    {
        static readonly int s_KernFinalVignetteID = Shader.PropertyToID("_KernFinalVignette");


        // Use specialed URP fragment shader pass for debug draw support and color space conversion/encoding support.
        // See CoreBlit.shader and BlitHDROverlay.shader
        static class BlitPassNames
        {
            public const string NearestSampler = "NearestDebugDraw";
            public const string BilinearSampler = "BilinearDebugDraw";
        }

        enum BlitType
        {
            Core = 0, // Core blit
            HDR = 1, // Blit with HDR encoding and overlay UI compositing
            Count = 2
        }

        struct BlitMaterialData
        {
            public Material material;
            public int nearestSamplerPass;
            public int bilinearSamplerPass;
        }

        BlitMaterialData[] m_BlitMaterialData;

        /// <param name="evt">The <c>RenderPassEvent</c> to use.</param>
        /// <param name="blitMaterial">The <c>Material</c> to use for copying the executing the final blit.</param>
        /// <param name="blitHDRMaterial">The <c>Material</c> to use for copying the executing the final blit when HDR output is active.</param>
        /// <seealso cref="RenderPassEvent"/>
        public FinalBlitPass(RenderPassEvent evt, Material blitMaterial, Material blitHDRMaterial)
        {
            profilingSampler = URPProfilingSamplers.BlitFinalToBackBuffer;
            renderPassEvent = evt;

            // Find sampler passes by name
            const int blitTypeCount = (int)BlitType.Count;
            m_BlitMaterialData = new BlitMaterialData[blitTypeCount];
            for (int i = 0; i < blitTypeCount; ++i)
            {
                m_BlitMaterialData[i].material = i == (int)BlitType.Core ? blitMaterial : blitHDRMaterial;
                m_BlitMaterialData[i].nearestSamplerPass = m_BlitMaterialData[i].material?.FindPass(BlitPassNames.NearestSampler) ?? -1;
                m_BlitMaterialData[i].bilinearSamplerPass = m_BlitMaterialData[i].material?.FindPass(BlitPassNames.BilinearSampler) ?? -1;
            }
        }

        public void Dispose()
        {
        }

        /// <param name="baseDescriptor"></param>
        /// <param name="colorHandle"></param>
        [Obsolete("Use RTHandles for colorHandle. #from(2022.1) #breakingFrom(2023.1)", true)]
        public void Setup(RenderTextureDescriptor baseDescriptor, RenderTargetHandle colorHandle)
        {
            throw new NotSupportedException("Setup with RenderTargetHandle has been deprecated. Use it with RTHandles instead.");
        }

        /// <param name="baseDescriptor"></param>
        /// <param name="colorHandle"></param>
        public void Setup(RenderTextureDescriptor baseDescriptor, RTHandle colorHandle)
        {
        }

        static void SetupHDROutput(ColorGamut hdrDisplayColorGamut, Material material, HDROutputUtils.Operation hdrOperation, Vector4 hdrOutputParameters, bool rendersOverlayUI)
        {
            material.SetVector(ShaderPropertyId.hdrOutputLuminanceParams, hdrOutputParameters);
            HDROutputUtils.ConfigureHDROutput(material, hdrDisplayColorGamut, hdrOperation);
            CoreUtils.SetKeyword(material, ShaderKeywordStrings.HDROverlay, rendersOverlayUI);
        }


        private static void ExecutePass(RasterCommandBuffer cmd, PassData data, RTHandle source, RTHandle destination, UniversalCameraData cameraData, Vector4 scaleBias)
        {
            bool isRenderToBackBufferTarget = !cameraData.isSceneViewCamera;
#if ENABLE_VR && ENABLE_XR_MODULE
            if (cameraData.xr.enabled)
                isRenderToBackBufferTarget = new RenderTargetIdentifier(destination.nameID, 0, CubemapFace.Unknown, -1) == new RenderTargetIdentifier(cameraData.xr.renderTarget, 0, CubemapFace.Unknown, -1);
#endif            
            var pixelRect = data.useFullScreenViewport ? new Rect(0f, 0f, Screen.width, Screen.height) : cameraData.pixelRect;
            RenderingUtils.SetupOffscreenUIViewportParams(data.blitMaterialData.material, ref pixelRect, isRenderToBackBufferTarget);
            if (isRenderToBackBufferTarget)
                cmd.SetViewport(pixelRect);

            // turn off any global wireframe & "scene view wireframe shader hijack" settings for doing blits:
            // we never want them to show up as wireframe
            cmd.SetWireframe(false);

            CoreUtils.SetKeyword(data.blitMaterialData.material, ShaderKeywordStrings._ENABLE_ALPHA_OUTPUT, data.enableAlphaOutput);

            bool linearFilter = data.worldGridActive ? data.worldGridLinearFilter : source.rt?.filterMode == FilterMode.Bilinear;
            int shaderPassIndex = linearFilter ? data.blitMaterialData.bilinearSamplerPass : data.blitMaterialData.nearestSamplerPass;
            Blitter.BlitTexture(cmd, source, scaleBias, data.blitMaterialData.material, shaderPassIndex);
        }

        private class PassData
        {
            internal TextureHandle source;
            internal TextureHandle destination;
            internal int sourceID;
            internal Vector4 hdrOutputLuminanceParams;
            internal bool requireSrgbConversion;
            internal bool enableAlphaOutput;
            internal BlitMaterialData blitMaterialData;
            internal UniversalCameraData cameraData;
            internal bool useFullScreenViewport;
            internal bool worldGridActive;
            internal Vector4 worldGridCrop;
            internal bool worldGridLinearFilter;
        }

        /// <param name="passData"></param>
        private void InitPassData(UniversalCameraData cameraData, ref PassData passData, BlitType blitType, bool enableAlphaOutput, bool useFullScreenViewport)
        {
            passData.cameraData = cameraData;
            passData.requireSrgbConversion = cameraData.requireSrgbConversion;
            passData.enableAlphaOutput = enableAlphaOutput;
            passData.useFullScreenViewport = useFullScreenViewport;

            passData.blitMaterialData = m_BlitMaterialData[(int)blitType];
        }

        // Shared by both FinalBlitPass and OffscreenUICoverPrepass.
        internal void Render(RenderGraph renderGraph, UniversalCameraData cameraData, UniversalResourceData resourceData, TextureHandle sourceTexture, bool useFullScreenViewport = false, Renderer2DWorldGridLayout? worldGrid = null)
        {
            var destinationTexture = resourceData.backBufferColor; //By definition this pass blits to the backbuffer
            var overlayUITexture = resourceData.overlayUITexture;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData, profilingSampler))
            {
                bool outputsToHDR = cameraData.isHDROutputActive;
                bool outputsAlpha = cameraData.isAlphaOutputEnabled;
                InitPassData(cameraData, ref passData, outputsToHDR ? BlitType.HDR : BlitType.Core, outputsAlpha, useFullScreenViewport);

                passData.sourceID = ShaderPropertyId.sourceTex;
                passData.source = sourceTexture;
                passData.worldGridActive = worldGrid.HasValue;
                if (worldGrid.HasValue)
                {
                    var grid = worldGrid.Value;
                    passData.worldGridCrop = grid.ViewportToWorldUv;
                    passData.worldGridLinearFilter =
                        grid.Width * grid.ViewportToWorldUv.x > cameraData.pixelWidth + 0.001f ||
                        grid.Height * grid.ViewportToWorldUv.y > cameraData.pixelHeight + 0.001f;
                }
                builder.UseTexture(sourceTexture, AccessFlags.Read);
                passData.destination = destinationTexture;

                // The access flag can be set to WriteAll if there is a full screen blit and no alpha blending,
                // so engine will set loadOperation to DontCare down to the pipe.
                AccessFlags targetAccessFlag = cameraData.isDefaultViewport && !outputsAlpha ? AccessFlags.WriteAll : AccessFlags.Write;

#if ENABLE_VR && ENABLE_XR_MODULE
                // This is a screen-space pass, make sure foveated rendering is disabled for non-uniform renders
                bool passSupportsFoveation = !XRSystem.foveatedRenderingCaps.HasFlag(FoveatedRenderingCaps.NonUniformRaster);
                builder.EnableFoveatedRasterization(cameraData.xr.supportsFoveatedRendering && passSupportsFoveation);
                // Multiview render regions are incompatible with the inner (foveal) pass in Quad View
                if (!cameraData.xr.isQuadViewInnerPass)
                {
                    builder.SetExtendedFeatureFlags(ExtendedFeatureFlags.MultiviewRenderRegionsCompatible);
                }
#endif
                builder.SetRenderAttachment(passData.destination, 0, targetAccessFlag);

                if (outputsToHDR && overlayUITexture.IsValid())
                {
                    VolumeStack stack = VolumeManager.instance.stack;
                    Tonemapping tonemapping = stack.GetComponent<Tonemapping>();
                    UniversalRenderPipeline.GetHDROutputLuminanceParameters(passData.cameraData.hdrDisplayInformation, passData.cameraData.hdrDisplayColorGamut, tonemapping, out passData.hdrOutputLuminanceParams);

                    builder.UseTexture(overlayUITexture, AccessFlags.Read);
                }
                else
                {
                    passData.hdrOutputLuminanceParams = new Vector4(-1.0f, -1.0f, -1.0f, -1.0f);
                }

                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    data.blitMaterialData.material.enabledKeywords = null;

                    context.cmd.SetKeyword(ShaderGlobalKeywords.LinearToSRGBConversion, data.requireSrgbConversion);
                    data.blitMaterialData.material.SetTexture(data.sourceID, data.source);

                    DebugHandler debugHandler = GetActiveDebugHandler(data.cameraData);
                    bool resolveToDebugScreen = debugHandler != null && debugHandler.WriteToDebugScreenTexture(data.cameraData.resolveFinalTarget);

                    
                    if (data.hdrOutputLuminanceParams.w >= 0)
                    {
                        HDROutputUtils.Operation hdrOperation = HDROutputUtils.Operation.None;
                        // If the HDRDebugView is on, we don't want the encoding
                        if (debugHandler == null || !debugHandler.HDRDebugViewIsActive(data.cameraData.resolveFinalTarget))
                            hdrOperation |= HDROutputUtils.Operation.ColorEncoding;

                        // Color conversion may have happened in the Uber post process through color grading, so we don't want to reapply it
                        if (!data.cameraData.postProcessEnabled)
                            hdrOperation |= HDROutputUtils.Operation.ColorConversion;

                        SetupHDROutput(data.cameraData.hdrDisplayColorGamut, data.blitMaterialData.material, hdrOperation, data.hdrOutputLuminanceParams, data.cameraData.rendersOverlayUI);
                    }

                    if (resolveToDebugScreen)
                    {
                        RTHandle sourceTex = data.source;
                        Vector2 viewportScale = sourceTex.useScaling ? new Vector2(sourceTex.rtHandleProperties.rtHandleScale.x, sourceTex.rtHandleProperties.rtHandleScale.y) : Vector2.one;

                        int shaderPassIndex = sourceTex.rt?.filterMode == FilterMode.Bilinear ? data.blitMaterialData.bilinearSamplerPass : data.blitMaterialData.nearestSamplerPass;
                        Blitter.BlitTexture(context.cmd, sourceTex, viewportScale, data.blitMaterialData.material, shaderPassIndex);
                    }
                    else
                    {
                        Vector4 scaleBias = RenderingUtils.GetFinalBlitScaleBias(context, in data.source, in data.destination);
                        data.blitMaterialData.material.SetFloat("_KernFinalWorldGridActive", data.worldGridActive ? 1f : 0f);
                        if (data.worldGridActive)
                        {
                            Vector4 crop = data.worldGridCrop;
                            if (context.GetTextureUVOrigin(data.source) == TextureUVOrigin.TopLeft)
                                crop.w = 1f - crop.w - crop.y;
                            data.blitMaterialData.material.SetVector("_KernFinalWorldUv", crop);
                        }
                        ExecutePass(context.cmd, data, data.source, data.destination, data.cameraData, scaleBias);
                        // Kern's display pass sets the screen vignette for this
                        // frame only; a frame without that pass must not reuse it.
                        context.cmd.SetGlobalVector(s_KernFinalVignetteID, Vector4.zero);
                    }
                        
                });
            }
        }

        /// <inheritdoc cref="IRenderGraphRecorder.RecordRenderGraph"/>
        override public void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();
            Renderer2DWorldGridLayout? grid = null;
            if (frameData.Contains<Renderer2DWorldGridData>())
            {
                var worldGrid = frameData.Get<Renderer2DWorldGridData>();
                if (worldGrid.Active)
                    grid = worldGrid.Layout;
            }
            Render(renderGraph, cameraData, resourceData, resourceData.cameraColor, worldGrid: grid);
            resourceData.SwitchActiveTexturesToBackbuffer();
        }
    }
}

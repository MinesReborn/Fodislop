using System;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule.Util;
using static UnityEngine.Rendering.RenderGraphModule.Util.RenderGraphUtils;

namespace UnityEngine.Rendering.Universal
{
    [URPHelpURL("urp/renderer-features/renderer-feature-full-screen-pass")]
    public partial class FullScreenPassRendererFeature : ScriptableRendererFeature
    {
        public enum InjectionPoint
        {
            BeforeRenderingTransparents = RenderPassEvent.BeforeRenderingTransparents,

            BeforeRenderingPostProcessing = RenderPassEvent.BeforeRenderingPostProcessing,

            AfterRenderingPostProcessing = RenderPassEvent.AfterRenderingPostProcessing
        }

        public InjectionPoint injectionPoint = InjectionPoint.AfterRenderingPostProcessing;

        public bool fetchColorBuffer = true;

        public ScriptableRenderPassInput requirements = ScriptableRenderPassInput.None;

        public Material passMaterial;

        public int passIndex = 0;

        public bool bindDepthStencilAttachment = false;

        private FullScreenRenderPass m_FullScreenPass;

        /// <inheritdoc/>
        public override void Create()
        {
            m_FullScreenPass = new FullScreenRenderPass(name);
        }

        internal override bool RequireRenderingLayers(bool isDeferred, bool needsGBufferAccurateNormals, out RenderingLayerUtils.Event atEvent, out RenderingLayerUtils.MaskSize maskSize)
        {
            atEvent = RenderingLayerUtils.Event.Opaque;
            maskSize = RenderingLayerUtils.MaskSize.Bits8;
            return false;
        }

        internal bool IsCompatibleWithTileOnlyMode()
        {
            // These checks must match the Tile-Only Mode validation in FullScreenPassRendererFeatureEditor.
            if (fetchColorBuffer)
                return false;
            return RenderingUtils.IsCompatibleWithTileOnlyMode(requirements, (RenderPassEvent)injectionPoint);
        }

        /// <inheritdoc/>
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType == CameraType.Preview
                || renderingData.cameraData.cameraType == CameraType.Reflection
                || UniversalRenderer.IsOffscreenDepthTexture(ref renderingData.cameraData))
                return;

            if (passMaterial == null)
                return;

            if (passIndex < 0 || passIndex >= passMaterial.passCount)
            {
                Debug.LogWarningFormat("The full screen feature \"{0}\" will not execute - the pass index is out of bounds for the material.", name);
                return;
            }

            if (renderer is UniversalRenderer universalRenderer && universalRenderer.useTileOnlyMode && !IsCompatibleWithTileOnlyMode())
            {
                Debug.LogErrorFormat(
                    "Full Screen Renderer Feature \"{0}\": the current settings are not compatible with Tile-Only Mode. Open the Universal Renderer \"{1}\" in the Inspector for more information.",
                    name, universalRenderer.name);
                return;
            }

            m_FullScreenPass.renderPassEvent = (RenderPassEvent)injectionPoint;
            m_FullScreenPass.ConfigureInput(requirements);
            m_FullScreenPass.SetupMembers(passMaterial, passIndex, fetchColorBuffer, bindDepthStencilAttachment);

            m_FullScreenPass.requiresIntermediateTexture = fetchColorBuffer;
        
            renderer.EnqueuePass(m_FullScreenPass);
        }


        internal class FullScreenRenderPass : ScriptableRenderPass
        {
            private Material m_Material;
            private int m_PassIndex;
            private bool m_FetchActiveColor;
            private bool m_BindDepthStencilAttachment;

            private readonly MaterialPropertyBlock m_MaterialPropertyBlock = new MaterialPropertyBlock();

            public FullScreenRenderPass(string passName)
            {
                profilingSampler = new ProfilingSampler(passName);
            }

            public void SetupMembers(Material material, int passIndex, bool fetchActiveColor, bool bindDepthStencilAttachment)
            {
                m_Material = material;
                m_PassIndex = passIndex;
                m_FetchActiveColor = fetchActiveColor;
                m_BindDepthStencilAttachment = bindDepthStencilAttachment;
            }

            private static void ExecuteMainPass(RasterCommandBuffer cmd, MaterialPropertyBlock mbp, RTHandle sourceTexture, Material material, int passIndex, Vector4 blitScaleBias)
            {
                mbp.Clear();
                if (sourceTexture != null)
                    mbp.SetTexture(ShaderPropertyId.blitTexture, sourceTexture);

                // We need to set the "_BlitScaleBias" uniform for user materials with shaders relying on core Blit.hlsl to work
                mbp.SetVector(ShaderPropertyId.blitScaleBias, blitScaleBias);

                cmd.DrawProcedural(Matrix4x4.identity, material, passIndex, MeshTopology.Triangles, 3, 1, mbp);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourcesData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

                TextureHandle source, destination;

                if (m_FetchActiveColor)
                {
                    // The pass requests the intermediate textures so this should always be valid
                    Debug.Assert(resourcesData.cameraColor.IsValid());                    

                    var targetDesc = renderGraph.GetTextureDesc(resourcesData.cameraColor);
                    targetDesc.name = "_CameraColorFullScreenPass";
                    targetDesc.clearBuffer = false;

                    source = resourcesData.cameraColor;
                    destination = renderGraph.CreateTexture(targetDesc);

                    renderGraph.AddBlitPass(source, destination, Vector2.one, Vector2.zero, passName: "Copy Color Full Screen");

                    // Swap for next pass;
                    source = destination;
                }
                else
                {
                    source = TextureHandle.nullHandle;
                }

                // If resourcesData.isActiveTargetBackBuffer == true, then the backbuffer is alread written to and this could overwrite it.
                // However, the user might want to blend into the backbuffer so we allow it here.
                destination = resourcesData.activeColorTexture;
                
                AddFullscreenRenderPassInputPass(renderGraph, resourcesData, cameraData, source, destination);                
            }

            private void AddFullscreenRenderPassInputPass(RenderGraph renderGraph, UniversalResourceData resourcesData, UniversalCameraData cameraData, in TextureHandle source, in TextureHandle destination)
            {
                using (var builder = renderGraph.AddRasterRenderPass<MainPassData>(passName, out var passData, profilingSampler))
                {
                    passData.material = m_Material;
                    passData.materialPropertyBlock = m_MaterialPropertyBlock;
                    passData.passIndex = m_PassIndex;

                    passData.source = source;
                    passData.destination = destination;

                    if (passData.source.IsValid())
                        builder.UseTexture(passData.source, AccessFlags.Read);

                    bool needsColor = (input & ScriptableRenderPassInput.Color) != ScriptableRenderPassInput.None;
                    bool needsDepth = (input & ScriptableRenderPassInput.Depth) != ScriptableRenderPassInput.None;
                    bool needsMotion = (input & ScriptableRenderPassInput.Motion) != ScriptableRenderPassInput.None;
                    bool needsNormal = (input & ScriptableRenderPassInput.Normal) != ScriptableRenderPassInput.None;

                    if (needsColor && cameraData.renderer.SupportsCameraOpaque())
                    {
                        Debug.Assert(resourcesData.cameraOpaqueTexture.IsValid());
                        builder.UseTexture(resourcesData.cameraOpaqueTexture);
                    }

                    if (needsDepth)
                    {
                        Debug.Assert(resourcesData.cameraDepthTexture.IsValid());
                        builder.UseTexture(resourcesData.cameraDepthTexture);
                    }

                    if (needsMotion)
                    {
                        Debug.Assert(cameraData.renderer.SupportsMotionVectors(), "Current renderer does not support motion vectors.");

                        if (cameraData.renderer.SupportsMotionVectors())
                        {
                            Debug.Assert(resourcesData.motionVectorColor.IsValid());
                            builder.UseTexture(resourcesData.motionVectorColor);
                            Debug.Assert(resourcesData.motionVectorDepth.IsValid());
                            builder.UseTexture(resourcesData.motionVectorDepth);
                        }
                    }

                    if (needsNormal && cameraData.renderer.SupportsCameraNormals())
                    {
                        Debug.Assert(resourcesData.cameraNormalsTexture.IsValid());
                        builder.UseTexture(resourcesData.cameraNormalsTexture);
                    }

                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);

                    if (m_BindDepthStencilAttachment)
                        builder.SetRenderAttachmentDepth(resourcesData.activeDepthTexture, AccessFlags.ReadWrite);

                    builder.SetRenderFunc(static (MainPassData data, RasterGraphContext rgContext) =>
                    {
                        Vector4 scaleBias = RenderingUtils.GetFinalBlitScaleBias(rgContext, in data.source, in data.destination);
                        ExecuteMainPass(rgContext.cmd, data.materialPropertyBlock, data.source, data.material, data.passIndex, scaleBias);
                    });
                }
            }
            private class MainPassData
            {
                internal Material material;
                internal MaterialPropertyBlock materialPropertyBlock;
                internal int passIndex;
                internal TextureHandle source;
                internal TextureHandle destination;
            }
        }
    }
}

using System;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.Universal
{
    public abstract class DepthHistory : CameraHistoryItem
    {
        // Currently we are storing up to 2, one for each eye in the case of XR stereo rendering
        private int[] m_Ids = new int[2];

        protected readonly string[] m_Names = new[]
        {
            "DepthHistory0",
            "DepthHistory1"
        };
        private RenderTextureDescriptor m_Descriptor;
        private Hash128 m_DescKey;
        // Store one jitter offset per eye per history RT. Currently the jitter offset
        // is the same for both eyes, but first of all this could change, but mostly
        // it's because eye jitters could be updated at different times.
        private Vector2[] m_JitterOffsets = new Vector2[2*2];

        /// <inheritdoc />
        public override void OnCreate(BufferedRTHandleSystem owner, uint typeId)
        {
            base.OnCreate(owner, typeId);
            m_Ids[0] = MakeId(0);
            m_Ids[1] = MakeId(1);
            Array.Fill(m_JitterOffsets, Vector2.zero);
        }

        /// <param name="eyeIndex">Eye index, typically XRPass.multipassId.</param>
        /// <returns>The texture.</returns>
        public RTHandle GetCurrentTexture(int eyeIndex = 0)
        {
            if ((uint)eyeIndex >= m_Ids.Length)
                return null;

            return GetCurrentFrameRT(m_Ids[eyeIndex]);
        }

        /// <param name="eyeIndex">Eye index, typically XRPass.multipassId.</param>
        /// <returns>The texture.</returns>
        public RTHandle GetPreviousTexture(int eyeIndex = 0)
        {
            if ((uint)eyeIndex >= m_Ids.Length)
                return null;

            return GetPreviousFrameRT(m_Ids[eyeIndex]);
        }

        /// <param name="eyeIndex">Eye index, typically XRPass.multipassId.</param>
        /// <returns>The jitter value in NDC space [-1,1].</returns>
        public Vector2 GetCurrentJitter(int eyeIndex = 0)
        {
            if ((uint)eyeIndex >= m_Ids.Length)
                return Vector2.zero;

            int stableIndex = GetCurrentFrameRTStableIndex(m_Ids[eyeIndex]);
            if (stableIndex < 0)
                return Vector2.zero;

            return m_JitterOffsets[stableIndex + 2 * eyeIndex];
        }

        /// <param name="eyeIndex">Eye index, typically XRPass.multipassId.</param>
        /// <returns>The jitter value in NDC space [-1,1].</returns>
        public Vector2 GetPreviousJitter(int eyeIndex = 0)
        {
            if ((uint)eyeIndex >= m_Ids.Length)
                return Vector2.zero;

            int stableIndex = GetPreviousFrameRTStableIndex(m_Ids[eyeIndex]);
            if (stableIndex < 0)
                return Vector2.zero;

            return m_JitterOffsets[stableIndex + 2 * eyeIndex];
        }

        private bool IsAllocated()
        {
            return GetCurrentTexture() != null;
        }

        // True if the desc changed, graphicsFormat etc.
        private bool IsDirty(ref RenderTextureDescriptor desc)
        {
            return m_DescKey != Hash128.Compute(ref desc);
        }

        private void Alloc(ref RenderTextureDescriptor desc, bool xrMultipassEnabled)
        {
            // Generic type, we need double buffering.
            AllocHistoryFrameRT(m_Ids[0], 2, ref desc, m_Names[0]);

            if(xrMultipassEnabled)
                AllocHistoryFrameRT(m_Ids[1], 2, ref desc, m_Names[1]);

            m_Descriptor = desc;
            m_DescKey = Hash128.Compute(ref desc);
        }

        public override void Reset()
        {
            for(int i = 0; i < m_Ids.Length; i++)
                ReleaseHistoryFrameRT(m_Ids[i]);
        }

        internal RenderTextureDescriptor GetHistoryDescriptor(ref RenderTextureDescriptor cameraDesc)
        {
            var depthDesc = cameraDesc;
            depthDesc.mipCount = 0;
            depthDesc.msaaSamples = 1;  // History copy should not have MSAA.

            return depthDesc;
        }

        // Return true if the RTHandles were reallocated.
        internal bool Update(UniversalCameraData cameraData, bool xrMultipassEnabled, in RenderTextureDescriptor? cameraDescOverride = null)
        {
#if ENABLE_VR && ENABLE_XR_MODULE
            int eyeIndex = (cameraData.xr.enabled && !cameraData.xr.singlePassEnabled) ? cameraData.xr.multipassId : 0;
#else
            const int eyeIndex = 0;
#endif
            Debug.Assert(eyeIndex < m_JitterOffsets.Length);
            int stableIndex = GetCurrentFrameRTStableIndex(m_Ids[eyeIndex]);
            if (stableIndex >= 0)
            {
                // Update the current jitter value for the current eye. Note that we could handle the case where
                // this is the first time or if we reallocate the buffers except there's no clear definition of
                // what we should use for a sensible previous jitter value in that case. So might just leave it as is.
                m_JitterOffsets[stableIndex + 2 * eyeIndex] = cameraData.jitter;
            }

            RenderTextureDescriptor cameraDesc;
            if (cameraDescOverride.HasValue)
            {
                cameraDesc = cameraDescOverride.Value;
            }
            else
            {
                cameraDesc = cameraData.cameraTargetDescriptor;

                // On GLES we don't support sampling the MSAA targets, so if auto depth resolve is not available, the only thing that works is rendering to a color target.
                // This has been the behavior from at least 6.0. However, it results in the format mostly being color on the different graphics APIs, even when
                // it could be a depth format if MSAA sampling for depth is allowed.
                if (RenderingUtils.MultisampleDepthResolveSupported())
                {
                    cameraDesc.graphicsFormat = GraphicsFormat.None;
                }
                else
                {
                    cameraDesc.graphicsFormat = GraphicsFormat.R32_SFloat;
                    cameraDesc.depthStencilFormat = GraphicsFormat.None;
                }
            }

            if (cameraDesc.width > 0 && cameraDesc.height > 0 && (cameraDesc.depthStencilFormat != GraphicsFormat.None || cameraDesc.graphicsFormat != GraphicsFormat.None) )
            {
                var historyDesc = GetHistoryDescriptor(ref cameraDesc);

                if (IsDirty(ref historyDesc))
                    Reset();

                if (!IsAllocated())
                {
                    Alloc(ref historyDesc, xrMultipassEnabled);
                    return true;
                }
            }

            return false;
        }
    }
}

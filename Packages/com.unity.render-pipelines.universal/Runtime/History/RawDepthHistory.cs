using System;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.Universal
{
    public sealed class RawDepthHistory : DepthHistory
    {
        /// <inheritdoc />
        public override void OnCreate(BufferedRTHandleSystem owner, uint typeId)
        {
            m_Names[0] = "RawDepthHistory0";
            m_Names[1] = "RawDepthHistory1";
            base.OnCreate(owner, typeId);
        }
    }
}

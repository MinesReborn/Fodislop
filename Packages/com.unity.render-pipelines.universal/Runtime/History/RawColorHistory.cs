using System;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.Universal
{
    public sealed class RawColorHistory : ColorHistory
    {
        /// <inheritdoc />
        public override void OnCreate(BufferedRTHandleSystem owner, uint typeId)
        {
            m_Names[0] = "RawColorHistory0";
            m_Names[1] = "RawColorHistory1";
            base.OnCreate(owner, typeId);
        }
    }
}

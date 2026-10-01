namespace UnityEngine.Rendering.Universal
{
    internal sealed class BeforeTransparentsColorHistory : ColorHistory
    {
        /// <inheritdoc />
        public override void OnCreate(BufferedRTHandleSystem owner, uint typeId)
        {
            m_Names[0] = "BeforeTransparentsColorHistory0";
            m_Names[1] = "BeforeTransparentsColorHistory1";
            base.OnCreate(owner, typeId);
        }
    }
}

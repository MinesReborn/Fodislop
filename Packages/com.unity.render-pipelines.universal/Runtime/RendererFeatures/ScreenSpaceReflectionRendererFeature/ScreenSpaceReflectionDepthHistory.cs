#if URP_SCREEN_SPACE_REFLECTION
namespace UnityEngine.Rendering.Universal
{
    internal sealed class ScreenSpaceReflectionDepthHistory : DepthHistory
    {
        /// <inheritdoc />
        public override void OnCreate(BufferedRTHandleSystem owner, uint typeId)
        {
            m_Names[0] = "ScreenSpaceReflectionDepthHistory0";
            m_Names[1] = "ScreenSpaceReflectionDepthHistory1";
            base.OnCreate(owner, typeId);
        }
    }
}
#endif

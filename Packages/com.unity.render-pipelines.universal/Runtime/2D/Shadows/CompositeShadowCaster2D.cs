using UnityEngine.Scripting.APIUpdating;


namespace UnityEngine.Rendering.Universal
{
    [CoreRPHelpURL("2DShadows", "com.unity.render-pipelines.universal")]
    [Icon("UnityEngine/UI/Shadow Icon")]
    [AddComponentMenu("Rendering/2D/Composite Shadow Caster 2D")]
    [MovedFrom(false, "UnityEngine.Experimental.Rendering.Universal", "com.unity.render-pipelines.universal")]
    [ExecuteInEditMode]
    public class CompositeShadowCaster2D : ShadowCasterGroup2D
    {
        protected void OnEnable()
        {
            ShadowCasterGroup2DManager.AddGroup(this);
        }

        protected void OnDisable()
        {
            ShadowCasterGroup2DManager.RemoveGroup(this);
        }
    }
}

using UnityEngine.Scripting.APIUpdating;

namespace UnityEngine.Rendering.Universal
{
    [AddComponentMenu("")] // Hide in menu
    [MovedFrom(true, "UnityEngine.Experimental.Rendering.Universal", "Unity.RenderPipelines.Universal.Runtime")]
    public class CinemachineUniversalPixelPerfect : MonoBehaviour
    {
        void OnEnable()
        {
            Debug.LogError("CinemachineUniversalPixelPerfect is now deprecated and doesn't function properly. Instead, use the one from Cinemachine v2.4.0 or newer.");
        }
    }
}

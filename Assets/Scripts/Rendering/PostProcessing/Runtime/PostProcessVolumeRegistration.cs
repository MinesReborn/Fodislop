#nullable enable

using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.Rendering.PostProcessing;

#if UNITY_EDITOR
using UnityEditor;

[InitializeOnLoad]
#endif
public static class PostProcessVolumeRegistration
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void RegisterComponents()
    {
        // Ensures custom VolumeComponent types are registered in VolumeManager stack
        var stack = VolumeManager.instance.stack;
        if (stack != null)
        {
            _ = stack.GetComponent<BloomComponent>();
            _ = stack.GetComponent<VignetteComponent>();
            _ = stack.GetComponent<ColorGradingComponent>();
            _ = stack.GetComponent<EigengrauComponent>();
            _ = stack.GetComponent<MotionBlurComponent>();
        }
    }
}

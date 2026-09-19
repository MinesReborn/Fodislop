#nullable enable

namespace Kern.UI;
public static class GatewayDevFlags
{
    public const string ForceGatesPrefsKey = "Kern.Gateway.ForceGates";

    public static bool ForceGates
    {
        get
        {
#if UNITY_EDITOR
            return UnityEditor.EditorPrefs.GetBool(ForceGatesPrefsKey, true);
#else
            return false;
#endif
        }
    }
}

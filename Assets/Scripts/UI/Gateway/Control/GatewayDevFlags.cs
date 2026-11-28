#nullable enable

namespace Kern.UI.Gateway;
public static class GatewayDevFlags
{
    public const string ForceGatesPrefsKey = "Kern.Gateway.ForceGates";

    public static bool ForceGates
    {
        get
        {
#if UNITY_EDITOR
            // Дефолт false: гейт/онбординг — первый запуск (Interface.OnboardingDone),
            // а не каждый вход в меню (репорт: «выход в меню — снова прелоадер»).
            // Для повторного показа онбординга в редакторе — выставить ключ вручную.
            return UnityEditor.EditorPrefs.GetBool(ForceGatesPrefsKey, false);
#else
            return false;
#endif
        }
    }
}

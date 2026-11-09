#nullable enable

using Kern.Core.Localization;
using Kern.Rendering;
using Kern.Rendering.Display;
using UnityEngine.UIElements;

namespace Kern.UI.Settings;

// Переключатель HDR — в настройках и в онбординге. Смена режима вывода может оставить экран нечитаемым, поэтому
// изменение требует подтверждения с откатом: молчание означает откат, а не согласие.
internal static class HDRSwitch
{
    // Сколько держать окно отката HDR: столько человек тратит, чтобы понять, что экран нечитаем, и найти мышь
    private const int ConfirmSeconds = 15;

    public static void Request(UIDocument doc, DisplayManager display, ILocalizationService loc, Toggle toggle, bool enabled)
    {
        bool previous = !enabled;
        HDROutput.ApplyRequestResult result = display.SetHDREnabled(enabled);

        // Выбор лёг в стартовый режим, экран сейчас не меняется: спрашивать не о чем
        if (result == HDROutput.ApplyRequestResult.AppliesAtNextStart)
        {
            display.ConfirmHDRSwitchSeen();
            return;
        }

        // Отказ: режим не сменился, метку безопасного старта снимаем, переключатель возвращаем
        if (result is HDROutput.ApplyRequestResult.RejectedUnsupported
            or HDROutput.ApplyRequestResult.RejectedNotSwitchable
            or HDROutput.ApplyRequestResult.Failed)
        {
            display.ConfirmHDRSwitchSeen();
            toggle.SetValueWithoutNotify(previous);
            return;
        }

        PauseMenuUIFactory.ShowTimedConfirmation(
            doc,
            loc.Get("settings.display.hdr_confirm_title"),
            loc.Get("settings.display.hdr_confirm_desc"),
            loc.Get("settings.display.hdr_confirm_keep"),
            loc.Get("settings.display.hdr_confirm_revert"),
            ConfirmSeconds,
            () => display.ConfirmHDRSwitchSeen(),
            () =>
            {
                display.SetHDREnabled(previous);
                display.ConfirmHDRSwitchSeen();
                toggle.SetValueWithoutNotify(previous);
            });
    }
}

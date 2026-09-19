#nullable enable

using System;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI;

internal static class PauseMenuConfirmation
{
    public static void ConfirmQuitGame(UIDocument doc, ILocalizationService loc)
    {
        PauseMenuUIFactory.ShowConfirmation(
            doc,
            loc.Get("pause.quit_confirm_title"),
            loc.Get("pause.quit_confirm_msg"),
            loc.Get("pause.quit_confirm_btn"),
            () =>
            {
#if UNITY_EDITOR
                Debug.Log("[PauseMenu] Выход из игры");
#else
                Application.Quit();
#endif
            },
            loc);
    }

    public static void ConfirmExitToMainMenu(
        UIDocument doc,
        IMainMenuNavigation mainMenuNavigation,
        Action onConfirmed,
        ILocalizationService loc)
    {
        PauseMenuUIFactory.ShowConfirmation(
            doc,
            loc.Get("pause.quit"),
            loc.Get("pause.exit_menu_confirm_msg"),
            loc.Get("pause.exit_menu_btn"),
            () =>
            {
                onConfirmed();
                mainMenuNavigation.ReturnToMainMenu();
            },
            loc);
    }
}

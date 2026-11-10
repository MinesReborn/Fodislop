#nullable enable

using System;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Settings;

internal static class PauseMenuConfirmation
{
    /// <summary>
    /// Клиентское окно подтверждения сброса управления до первоначального.
    /// Сам сброс выполняет вызывающий в onConfirmed — после нажатия кнопки
    /// подтверждения; «Отмена» просто закрывает оверлей.
    /// </summary>
    public static void ConfirmResetControls(UIDocument doc, Action onConfirmed, ILocalizationService loc)
    {
        PauseMenuUIFactory.ShowConfirmation(
            doc,
            loc.Get("settings.controls.reset_confirm_title"),
            loc.Get("settings.controls.reset_confirm_msg"),
            loc.Get("settings.controls.reset_confirm_btn"),
            onConfirmed,
            loc);
    }
}

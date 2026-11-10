#nullable enable

using System;
using Kern.Core;
using Kern.Core.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Settings;
internal static class PauseMenuUIFactory
{
    // Подтверждение с обратным отсчётом: изменение уже применено, и вопрос
    // стоит не «применять ли», а «видно ли вам это окно». Отсюда и устройство:
    // молчание означает откат, а не согласие. Для режима вывода это
    // единственный способ выбраться, когда экран после переключения стал
    // нечитаемым и найти ползунок глазами уже нельзя.
    public static void ShowTimedConfirmation(
        UIDocument doc,
        string title,
        string description,
        string keepText,
        string revertFormat,
        int seconds,
        Action onKeep,
        Action onRevert)
    {
        if (doc == null || doc.rootVisualElement == null)
        {
            // Молчание в этом окне означает откат, поэтому и здесь оно обязано
            // означать откат. Просто выйти значило бы оставить изменение
            // применённым без единственного окна, которым его отменяют — а
            // применено оно как раз тогда, когда экран мог стать нечитаемым.
            Debug.LogError(
                "[PauseMenu] The timed confirmation has nowhere to appear; " +
                "reverting the change instead of leaving it applied.");
            onRevert();
            return;
        }

        if (seconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        VisualElement root = doc.rootVisualElement;

        var overlay = new VisualElement();
        overlay.name = "TimedConfirmOverlay";
        overlay.AddToClassList("pause-confirm-overlay");
        overlay.AddToClassList("ui-overlay");
        overlay.AddToClassList("ui-overlay--modal");

        var panel = new VisualElement();
        panel.AddToClassList("pause-confirm-panel");
        panel.AddToClassList("ui-panel");
        panel.AddToClassList("ui-panel--modal");

        var titleLabel = new Label(title);
        titleLabel.AddToClassList("pause-confirm-title");
        panel.Add(titleLabel);

        var descLabel = new Label(description);
        descLabel.AddToClassList("pause-confirm-desc");
        panel.Add(descLabel);

        var buttonsRow = new VisualElement();
        buttonsRow.AddToClassList("pause-confirm-buttons");
        buttonsRow.AddToClassList("ui-actions-row");

        int remaining = seconds;
        IVisualElementScheduledItem? countdown = null;
        bool resolved = false;

        void Resolve(Action outcome)
        {
            // Отсчёт и обе кнопки ведут в одну точку: иначе истекший таймер
            // успевает откатить то, что пользователь уже подтвердил.
            if (resolved)
            {
                return;
            }

            resolved = true;
            countdown?.Pause();
            if (overlay.parent != null)
            {
                overlay.RemoveFromHierarchy();
            }

            outcome();
        }

        var keepBtn = new Button(() => Resolve(onKeep));
        keepBtn.text = keepText;
        keepBtn.AddToClassList("pause-btn-confirm");

        var revertBtn = new Button(() => Resolve(onRevert));
        revertBtn.text = string.Format(revertFormat, remaining);
        revertBtn.AddToClassList("pause-btn");

        buttonsRow.Add(keepBtn);
        buttonsRow.Add(revertBtn);
        panel.Add(buttonsRow);

        overlay.Add(panel);
        root.Add(overlay);

        countdown = overlay.schedule.Execute(() =>
        {
            remaining--;
            if (remaining <= 0)
            {
                Resolve(onRevert);
                return;
            }

            revertBtn.text = string.Format(revertFormat, remaining);
        }).Every(1000);
    }

    public static void ShowConfirmation(UIDocument doc, string title, string description, string confirmText, Action onConfirm, ILocalizationService loc)
    {
        if (doc == null || doc.rootVisualElement == null)
        {
            // Защитный гард: без готового документа показывать подтверждение
            // негде; вызывающий сам решает, когда документ готов.
            return;
        }

        var root = doc.rootVisualElement;

        var overlay = new VisualElement();
        overlay.name = "ConfirmOverlay";
        overlay.AddToClassList("pause-confirm-overlay");
        overlay.AddToClassList("ui-overlay");
        overlay.AddToClassList("ui-overlay--modal");

        var panel = new VisualElement();
        panel.AddToClassList("pause-confirm-panel");
        panel.AddToClassList("ui-panel");
        panel.AddToClassList("ui-panel--modal");

        var titleLabel = new Label(title);
        titleLabel.AddToClassList("pause-confirm-title");
        panel.Add(titleLabel);

        var descLabel = new Label(description);
        descLabel.AddToClassList("pause-confirm-desc");
        panel.Add(descLabel);

        var buttonsRow = new VisualElement();
        buttonsRow.AddToClassList("pause-confirm-buttons");
        buttonsRow.AddToClassList("ui-actions-row");

        var confirmBtn = new Button(() =>
        {
            root.Remove(overlay);
            onConfirm();
        });
        confirmBtn.text = confirmText;
        confirmBtn.AddToClassList("pause-btn-confirm");

        var cancelBtn = new Button(() => root.Remove(overlay));
        cancelBtn.text = loc.Get("common.cancel");
        cancelBtn.AddToClassList("pause-btn");

        buttonsRow.Add(confirmBtn);
        buttonsRow.Add(cancelBtn);
        panel.Add(buttonsRow);

        overlay.Add(panel);
        root.Add(overlay);
    }
}

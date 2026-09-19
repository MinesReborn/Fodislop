#nullable enable

using Kern.Core.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI;
public static class UILocalizer
{
    public static void Apply(VisualElement root, ILocalizationService loc)
    {
        if (root == null || loc == null)
        {
            return;
        }

        foreach (var label in root.Query<Label>().Build())
        {
            if (loc.HasKey(label.text))
            {
                label.text = loc.Get(label.text);
            }
        }

        foreach (var button in root.Query<Button>().Build())
        {
            if (loc.HasKey(button.text))
            {
                button.text = loc.Get(button.text);
            }
        }

        // Tooltip-атрибуты тоже могут нести ключи (tooltip="hud.tooltip.clan");
        // кнопки, у которых тултип вешается кодом через Tooltip.AttachTo(Func),
        // переживают это безвредно — их тултип уже переопределён.
        foreach (var element in root.Query<VisualElement>().Build())
        {
            if (!string.IsNullOrEmpty(element.tooltip) && loc.HasKey(element.tooltip))
            {
                element.tooltip = loc.Get(element.tooltip);
            }
        }
    }

    public static VisualElement Localize(this VisualElement root, ILocalizationService loc)
    {
        Apply(root, loc);
        return root;
    }
    public static void AssertLocalizationServiceAvailable(ILocalizationService? loc, string viewName)
    {
#if UNITY_EDITOR || UNITY_ENABLE_CHECKS
        if (loc == null)
        {
            Debug.LogError($"[UILocalizer] Вьюха '{viewName}' применяет локализацию без ILocalizationService — инжекция мертва (мост/скоуп не сработал), текст останется сырыми ключами.");
        }
#endif
    }

    public static void AssertLocalized(VisualElement root, ILocalizationService loc)
    {
#if UNITY_EDITOR || UNITY_ENABLE_CHECKS
        if (root == null || loc == null)
        {
            return;
        }

        foreach (var label in root.Query<Label>().Build())
        {
            if (loc.HasKey(label.text))
            {
                Debug.LogError($"[UILocalizer] Неразрешённый ключ '{label.text}' в text элемента '{label.name}' — ApplyLocalizedText не отработал (мёртвая инжекция/сборка).");
            }
        }

        foreach (var button in root.Query<Button>().Build())
        {
            if (loc.HasKey(button.text))
            {
                Debug.LogError($"[UILocalizer] Неразрешённый ключ '{button.text}' в text кнопки '{button.name}' — ApplyLocalizedText не отработал (мёртвая инжекция/сборка).");
            }
        }

        foreach (var element in root.Query<VisualElement>().Build())
        {
            if (!string.IsNullOrEmpty(element.tooltip) && loc.HasKey(element.tooltip))
            {
                Debug.LogError($"[UILocalizer] Неразрешённый ключ '{element.tooltip}' в tooltip элемента '{element.name}' — ApplyLocalizedText не отработал (мёртвая инжекция/сборка).");
            }
        }
#endif
    }
}

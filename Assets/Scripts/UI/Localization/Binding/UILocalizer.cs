#nullable enable

using Kern.Core.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Localization;
public static class UILocalizer
{
    public const string CapsClass = "caps";

    // Высота строки многострочного текста (line-height макета): в USS свойства нет, TextCore понимает тег <line-height>.
    // Класс задаёт шаг строк в долях кегля; первая строка остаётся по метрикам шрифта — её добирают отступы в USS
    private static readonly (string Class, string Tag)[] s_lineHeights =
    {
        ("lh-100", "<line-height=1em>"),
        ("lh-145", "<line-height=1.45em>"),
        ("lh-150", "<line-height=1.5em>"),
    };

    // Текст, который код ставит сам после локализации, теряет тег высоты строки: класс lh-* ставит его снова
    public static void ApplyLineHeight(TextElement text)
    {
        foreach ((string cls, string tag) in s_lineHeights)
        {
            if (text.ClassListContains(cls) && !text.text.StartsWith(tag, System.StringComparison.Ordinal))
            {
                text.text = tag + text.text;
            }
        }
    }

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

        // Подсказка пустого поля (placeholder-text="gateway.auth.login_field") — тоже ключ
        foreach (var field in root.Query<TextField>().Build())
        {
            string placeholder = field.textEdition.placeholder;
            if (!string.IsNullOrEmpty(placeholder) && loc.HasKey(placeholder))
            {
                field.textEdition.placeholder = loc.Get(placeholder);
            }
        }

        // Заглавные (text-transform: uppercase макета): в USS свойства нет — класс caps поднимает регистр текста
        foreach (var text in root.Query<TextElement>(className: CapsClass).Build())
        {
            text.text = text.text.ToUpperInvariant();
        }

        foreach (var text in root.Query<TextElement>().Build())
        {
            ApplyLineHeight(text);
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

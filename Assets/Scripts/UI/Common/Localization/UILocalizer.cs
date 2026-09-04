#nullable enable

using Fodinae.Core.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI
{
    /// <summary>
    /// Разрешает статические тексты UXML, заданные ключами локализации
    /// (text="hud.mission"), в актуальный перевод. Единственный источник правды —
    /// словарь: UXML не несёт пользовательского текста, а ключ в атрибуте text
    /// заставляет линтер считать ключ использованным.
    ///
    /// Динамические лейблы (которые код форматирует значениями: «Стр. 1/1»,
    /// «УР. 4», счётчики) этим хелпером не трогаются: их текст в UXML пуст, и за
    /// него отвечает код. HasKey защищает и от обратной ситуации — если текст
    /// элемента вдруг совпал с ключом по случайности.
    /// </summary>
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

        /// <summary>
        /// Удобный extension-метод для декларативной локализации любого VisualElement дерева.
        /// </summary>
        public static VisualElement Localize(this VisualElement root, ILocalizationService loc)
        {
            Apply(root, loc);
            return root;
        }

        /// <summary>
        /// Extension-метод для быстрого получения локализованной строки с форматированием.
        /// </summary>
        public static string Loc(this string key, ILocalizationService loc, params object[] args)
        {
            return loc != null ? loc.Get(key, args) : key;
        }

        /// <summary>
        /// Сердцебиение инжекции: ApplyLocalizedText без сервиса — это тихая
        /// смерть вьюхи (сырые ключи). Кричим в dev-сборках вместо молчания.
        /// </summary>
        public static void AssertLocalizationServiceAvailable(ILocalizationService? loc, string viewName)
        {
#if UNITY_EDITOR || UNITY_ENABLE_CHECKS
            if (loc == null)
            {
                Debug.LogError($"[UILocalizer] Вьюха '{viewName}' применяет локализацию без ILocalizationService — инжекция мертва (мост/скоуп не сработал), текст останется сырыми ключами.");
            }
#endif
        }

        /// <summary>
        /// Сердцебиение локализации: после ApplyLocalizedText сцена вьюхи не
        /// должна содержать ни одного неразрешённого ключа. Если Apply вообще не
        /// отработал (мёртвая инжекция, пустой словарь, не то дерево) — здесь
        /// останутся сырые ключи, и LogError пройдёт через FailFastLogHandler:
        /// сущность кричит вместо того, чтобы тихо показывать ключи.
        ///
        /// HasKey-гейт отсекает легитимные dotted-строки (версии вида v0.9.0,
        /// плейсхолдеры): флагается только то, что сервис ЗНАЕТ как ключ.
        /// Работает только в dev-сборках: в релизе скан отключается, чтобы
        /// косметический дефект текста не ронял игру через fail-fast.
        /// </summary>
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
}

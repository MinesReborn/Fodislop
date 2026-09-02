#nullable enable

using UnityEngine.UIElements;

namespace Fodinae.UI
{
    /// <summary>
    /// Видимость и состояние — классом, а не инлайном.
    /// </summary>
    /// <remarks>
    /// Инлайн-стиль в UI Toolkit выигрывает у любого правила таблицы стилей.
    /// Поэтому <c>element.style.display = DisplayStyle.None</c> не «скрывает
    /// элемент», а навсегда выводит его из-под власти темы, тира и состояний:
    /// снять инлайн можно только другим инлайном. На вкладках настроек это
    /// уже стоило работающего механизма — разметка и USS объявляли пару
    /// <c>mm-tab-pane</c> / <c>mm-tab-pane--active</c>, а код писал поверх
    /// неё пиксели, и класс не значил ничего.
    ///
    /// Имя <c>is-hidden</c> не придумано здесь: оно взято из дизайн-системы
    /// (visual/fodinae-ui-lab/css/components.css) и печатается в
    /// Assets/Resources/Styles/TokenUtilities.uss генератором токенов.
    /// Что класс существует в USS и лист подключён к теме, проверяет
    /// scripts/check-architecture.js.
    /// </remarks>
    public static class UIState
    {
        /// <summary>Класс скрытия из утилитарного слоя дизайн-системы.</summary>
        public const string Hidden = "is-hidden";

        /// <summary>Скрыть или показать элемент. <c>null</c> игнорируется.</summary>
        public static void SetHidden(VisualElement? element, bool hidden)
        {
            element?.EnableInClassList(Hidden, hidden);
        }

        /// <summary>Показать элемент. <c>null</c> игнорируется.</summary>
        public static void Show(VisualElement? element) => SetHidden(element, false);

        /// <summary>Скрыть элемент. <c>null</c> игнорируется.</summary>
        public static void Hide(VisualElement? element) => SetHidden(element, true);

        /// <summary>Скрыт ли элемент нашим классом.</summary>
        public static bool IsHidden(VisualElement? element)
            => element == null || element.ClassListContains(Hidden);
    }
}

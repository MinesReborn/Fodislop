#nullable enable

using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI
{
    /// <summary>
    /// Тир раскладки — замена медиазапросам, которых в USS не существует.
    ///
    /// Дизайн-система (visual/fodinae-ui-lab) описывает три тира: compact,
    /// standard и wide. В браузере они переключаются через @media, здесь —
    /// классом на корневом элементе панели.
    ///
    /// ПОЧЕМУ НЕ ОДНА ТОЛЬКО ШИРИНА
    ///
    /// В браузере CSS-пиксель привязан к физическому размеру, поэтому телефон
    /// честно получает узкий вьюпорт (≈390 px) и @media (max-width: 900px)
    /// срабатывает сам собой. В Unity это не так: PanelSettings работает в
    /// режиме ScaleWithScreenSize и держит логическую высоту равной 1080 на
    /// любом устройстве, а логическая ширина выходит из соотношения сторон.
    /// Телефон в ландшафте даёт 2340×1080 логических пикселей — по ширине это
    /// «широкий» тир, то есть десктопная раскладка на шести дюймах.
    ///
    /// Перейти на ConstantPhysicalSize (буквальную браузерную модель) нельзя:
    /// Screen.dpi отдаёт сырое DPI матрицы, а не системный коэффициент. На
    /// Retina-макбуке это 299 против браузерного DPR 2 — вьюпорт схлопывается
    /// до 658 px, интерфейс раздувается втрое. Проверено замером.
    ///
    /// Поэтому решение разнесено надвое: PanelSettings отвечает за то, чтобы
    /// раскладка была пропорциональна экрану, а тир выбирается по физическому
    /// размеру устройства и только потом — по доступной ширине. Если система
    /// соврёт про DPI, испортится максимум выбор тира, а не каждый пиксель на
    /// экране.
    /// </summary>
    public static class UILayoutTier
    {
        public const string CompactClass = "tier--compact";
        public const string StandardClass = "tier--standard";
        public const string WideClass = "tier--wide";

        /// <summary>Границы по ширине совпадают с брейкпоинтами css/tokens.css §3.</summary>
        private const float CompactMaxWidth = 900f;
        private const float WideMinWidth = 1600f;

        /// <summary>
        /// Диагональ, ниже которой устройство считается телефоном независимо от
        /// того, сколько логических пикселей насчитала панель. Семь дюймов —
        /// общепринятая граница между телефоном и планшетом.
        /// </summary>
        private const float HandheldMaxInches = 7f;

        /// <summary>
        /// Привязывает автоматическое переключение тира к корневому элементу.
        /// Вызывать один раз после CloneTree; отписка не нужна — колбэк живёт
        /// ровно столько же, сколько сам элемент.
        /// </summary>
        public static void Attach(VisualElement root)
        {
            root.RegisterCallback<GeometryChangedEvent>(_ => Apply(root));
            Apply(root);
        }

        /// <summary>Проставляет ровно один тир-класс по текущему устройству и ширине панели.</summary>
        public static void Apply(VisualElement root)
        {
            float width = root.resolvedStyle.width;

            // До первой раскладки ширина равна NaN — тогда решать ещё не по чему.
            if (float.IsNaN(width) || width <= 0f)
            {
                return;
            }

            string tier = ResolveTier(width);

            // Выходим, если тир не изменился. Смена класса переопределяет токены,
            // те меняют раскладку, а раскладка снова шлёт GeometryChangedEvent —
            // без этой проверки на самой границе тира возможна осцилляция.
            if (root.ClassListContains(tier))
            {
                return;
            }

            root.EnableInClassList(CompactClass, tier == CompactClass);
            root.EnableInClassList(StandardClass, tier == StandardClass);
            root.EnableInClassList(WideClass, tier == WideClass);

#if UNITY_EDITOR
            // Логический размер панели — единственное число, по которому вообще
            // можно судить о масштабе интерфейса: физическое разрешение экрана
            // ничего не говорит, пока не известен коэффициент PanelSettings.
#endif
        }

        private static string ResolveTier(float panelWidth)
        {
            // Карманное устройство получает компактный тир при любой ширине
            // вьюпорта: на шести дюймах десктопная раскладка нечитаема и
            // непопадаема пальцем, сколько бы логических пикселей там ни было.
            //
            // Проверка ограничена мобильными платформами намеренно. В редакторе
            // Screen.width/height — это размер Game View, а Screen.dpi — DPI
            // настоящего монитора; их частное диагональю не является (на этой
            // машине выходит 7.9" при 14-дюймовом экране). На десктопе тир
            // решается шириной, и это правильно: там окно и правда бывает узким.
            if (Application.isMobilePlatform)
            {
                float inches = ScreenDiagonalInches();
                if (inches > 0f && inches < HandheldMaxInches)
                {
                    return CompactClass;
                }
            }

            if (panelWidth < CompactMaxWidth)
            {
                return CompactClass;
            }

            return panelWidth >= WideMinWidth ? WideClass : StandardClass;
        }

        /// <summary>
        /// Диагональ экрана в дюймах, либо 0, если система не сообщила DPI.
        /// Ноль здесь означает «не знаю» и трактуется вызывающим кодом как
        /// «решай по ширине» — это безопаснее, чем подставлять выдуманное DPI.
        /// </summary>
        private static float ScreenDiagonalInches()
        {
            float dpi = Screen.dpi;
            if (dpi <= 0f)
            {
                return 0f;
            }

            return Mathf.Sqrt((Screen.width * Screen.width) + (Screen.height * Screen.height)) / dpi;
        }
    }
}

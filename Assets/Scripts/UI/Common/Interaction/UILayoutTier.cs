#nullable enable

using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI;
public static class UILayoutTier
{
    public const string CompactClass = "tier--compact";
    public const string StandardClass = "tier--standard";
    public const string WideClass = "tier--wide";

    private const float CompactMaxWidth = 900f;
    private const float WideMinWidth = 1600f;

    private const float HandheldMaxInches = 7f;

    public static void Attach(VisualElement root)
    {
        root.RegisterCallback<GeometryChangedEvent>(_ => Apply(root));
        Apply(root);
    }

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

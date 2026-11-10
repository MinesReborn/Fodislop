#nullable enable

using UnityEngine.UIElements;

namespace Kern.UI.Screens;

// Штрихкод значения (хеш сборки, ID игрока) — как kernBars макета (visual/kern-ui/js/hud.js): каждый знак —
// четыре бита (parseInt(знак, 36) % 16), бит — узкий (1 px) или широкий (3 px) штрих, по краям опорные штрихи.
// Декор строится из самих данных: другое значение — другой код
public static class KernBarcode
{
    private const string BarClass = "idcode__bar";
    private const string WideClass = "idcode__bar--wide";

    public static void Fill(VisualElement host, string value)
    {
        host.Clear();
        Add(host, wide: true);
        Add(host, wide: false);
        foreach (char ch in value.ToLowerInvariant())
        {
            int v = Base36(ch) % 16;
            for (int bit = 8; bit > 0; bit >>= 1)
            {
                Add(host, (v & bit) != 0);
            }
        }

        Add(host, wide: false);
        Add(host, wide: true);
    }

    // parseInt(ch, 36): цифра или латинская буква; прочее в макете даёт NaN, а NaN % 16 & bit — ноль: узкие штрихи
    private static int Base36(char ch) => ch switch
    {
        >= '0' and <= '9' => ch - '0',
        >= 'a' and <= 'z' => ch - 'a' + 10,
        _ => 0,
    };

    private static void Add(VisualElement host, bool wide)
    {
        var bar = new VisualElement { pickingMode = PickingMode.Ignore };
        bar.AddToClassList(BarClass);
        bar.EnableInClassList(WideClass, wide);
        host.Add(bar);
    }
}

#nullable enable

using UnityEngine;

namespace Kern.UI.Screens;

// Параметры рамки панели, прочитанные из USS (--panel-*)
internal readonly struct KernPanelStyle
{
    public KernPanelStyle(Color fill, Color grain, Color edge, Color highlight, float notch, float tick, float tickWidth, Color tickColor)
    {
        Fill = fill;
        Grain = grain;
        Edge = edge;
        Highlight = highlight;
        Notch = notch;
        Tick = tick;
        TickWidth = tickWidth;
        TickColor = tickColor;
    }

    public Color Fill { get; }

    // Зерно корпуса (--grain макета: статичный шум bone с прозрачностью .05) — его средний тон поверх заливки
    public Color Grain { get; }

    public Color Edge { get; }

    public Color Highlight { get; }

    public float Notch { get; }

    public float Tick { get; }

    public float TickWidth { get; }

    public Color TickColor { get; }
}

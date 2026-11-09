#nullable enable

using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Screens;

// Что рисовать на элементе — прочитано из его собственных свойств USS (--panel-*, --btn-*, --quant-*, --dash-*,
// --rules-*, --dots-*, --hatch-*, --chevron-*, --check-color). Нет свойства — нет слоя. Порядок слоёв — KernVector.Draw.
internal sealed class KernVectorStyle
{
    public Color? HatchColor { get; set; }
    public float HatchStep { get; set; } = 4f;

    // Полоса штриховки у края (background-size: 100% 3px макета): высота и край; 0 — штриховка на весь элемент
    public float HatchBand { get; set; }
    public bool HatchBandTop { get; set; }

    public Color? DotsColor { get; set; }
    public float DotsStep { get; set; } = 32f;
    public float DotsSize { get; set; } = 2f;

    public Color? PanelFill { get; set; }
    public Color PanelEdge { get; set; }
    public Color PanelHighlight { get; set; }

    public Color PanelGrain { get; set; }
    public float PanelNotch { get; set; } = 10f;
    public float PanelTick { get; set; }
    public float PanelTickWidth { get; set; } = 1f;
    public Color PanelTickColor { get; set; }

    public Color? ShapeColor { get; set; }
    public float ShapeNotch { get; set; } = 8f;
    public bool ShapeBottomRight { get; set; }

    public Color? QuantColor { get; set; }
    public float QuantCell { get; set; } = 6f;
    public float QuantGap { get; set; } = 2f;

    public Color? RulesColor { get; set; }
    public float RulesStep { get; set; } = 4f;

    public Color? DashColor { get; set; }

    public Color? TickColor { get; set; }

    public Color? CheckColor { get; set; }

    public Color? ChevronColor { get; set; }
    public float ChevronRight { get; set; } = 14f;

    // Подложка текстового элемента — векторная картинка его размера (KernVector.RenderUnderlay)
    public VectorImage? Underlay { get; set; }

    public bool Drawing { get; set; }

    // Есть видимая рамка USS: при дробном масштабе её дорисовывает KernShapes.Edges
    public bool Edges { get; set; }

    public bool Any =>
        Edges || HatchColor.HasValue || DotsColor.HasValue || PanelFill.HasValue || ShapeColor.HasValue || QuantColor.HasValue ||
        RulesColor.HasValue || DashColor.HasValue || TickColor.HasValue || ChevronColor.HasValue || CheckColor.HasValue;
}

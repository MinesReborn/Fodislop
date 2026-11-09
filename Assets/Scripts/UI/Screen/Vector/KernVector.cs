#nullable enable

using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Screens;

// Векторная микрографика макета на любом элементе панели: USS задаёт свойства (--panel-fill, --quant-color…),
// элемент рисует их через Painter2D. Текстур нет: всё в пикселях панели при любом масштабе.
// Подключается к корню документа в UIScreen.Bind; новые элементы (собранные кодом позже) подхватывает пересмотр дерева.
public static class KernVector
{
    private const long RescanMs = 500;

    private static readonly ConditionalWeakTable<VisualElement, KernVectorStyle> s_styles = new();
    private static readonly ConditionalWeakTable<VisualElement, object> s_roots = new();

    private static readonly CustomStyleProperty<Color> s_hatchColor = new("--hatch-color");
    private static readonly CustomStyleProperty<float> s_hatchStep = new("--hatch-step");
    private static readonly CustomStyleProperty<float> s_hatchBand = new("--hatch-band");
    private static readonly CustomStyleProperty<string> s_hatchBandEdge = new("--hatch-band-edge");
    private static readonly CustomStyleProperty<Color> s_dotsColor = new("--dots-color");
    private static readonly CustomStyleProperty<float> s_dotsStep = new("--dots-step");
    private static readonly CustomStyleProperty<float> s_dotsSize = new("--dots-size");
    private static readonly CustomStyleProperty<Color> s_panelFill = new("--panel-fill");
    private static readonly CustomStyleProperty<Color> s_panelEdge = new("--panel-edge");
    private static readonly CustomStyleProperty<Color> s_panelHighlight = new("--panel-highlight");
    private static readonly CustomStyleProperty<Color> s_panelGrain = new("--panel-grain");
    private static readonly CustomStyleProperty<float> s_panelNotch = new("--panel-notch");
    private static readonly CustomStyleProperty<float> s_panelTick = new("--panel-tick");
    private static readonly CustomStyleProperty<float> s_panelTickWidth = new("--panel-tick-width");
    private static readonly CustomStyleProperty<Color> s_panelTickColor = new("--panel-tick-color");
    private static readonly CustomStyleProperty<Color> s_shapeColor = new("--shape-color");
    private static readonly CustomStyleProperty<float> s_shapeNotch = new("--shape-notch");
    private static readonly CustomStyleProperty<string> s_shapeCorner = new("--shape-corner");
    private static readonly CustomStyleProperty<Color> s_quantColor = new("--quant-color");
    private static readonly CustomStyleProperty<float> s_quantCell = new("--quant-cell");
    private static readonly CustomStyleProperty<float> s_quantGap = new("--quant-gap");
    private static readonly CustomStyleProperty<Color> s_rulesColor = new("--rules-color");
    private static readonly CustomStyleProperty<float> s_rulesStep = new("--rules-step");
    private static readonly CustomStyleProperty<Color> s_dashColor = new("--dash-color");
    private static readonly CustomStyleProperty<Color> s_tickColor = new("--tick-color");
    private static readonly CustomStyleProperty<Color> s_checkColor = new("--check-color");
    private static readonly CustomStyleProperty<Color> s_chevronColor = new("--chevron-color");
    private static readonly CustomStyleProperty<float> s_chevronRight = new("--chevron-right");

    public static void Attach(VisualElement root)
    {
        if (s_roots.TryGetValue(root, out _))
        {
            Scan(root);
            return;
        }

        s_roots.Add(root, new object());
        Scan(root);
        root.schedule.Execute(() => Scan(root)).Every(RescanMs);
    }

    private static void Scan(VisualElement root)
    {
        root.Query<VisualElement>().ForEach(Register);
    }

    private static void Register(VisualElement element)
    {
        if (s_styles.TryGetValue(element, out _))
        {
            return;
        }

        var style = new KernVectorStyle();
        s_styles.Add(element, style);
        element.RegisterCallback<CustomStyleResolvedEvent>(OnStyleResolved);
        // Цвет рамки с переходом доходит до конечного уже после события стиля: чёткая рамка догоняет его здесь
        element.RegisterCallback<TransitionEndEvent>(OnTransitionEnd);
        if (element.customStyle != null)
        {
            Read(element, style);
        }
    }

    // Браузер макета смешивает полупрозрачное в sRGB, Painter2D в линейном проекте — в линейном пространстве:
    // точки 8 % кости выходили #42 вместо #15. Прозрачность пересчитывается так, чтобы на фоне экранов (void #06050a)
    // линейное смешивание дало тот же пиксель, что браузер. На тёмных поверхностях интерфейса ошибка — единицы цвета
    private static readonly Color s_backdrop = new(6 / 255f, 5 / 255f, 10 / 255f, 1f);

    private static bool TryGetColor(ICustomStyle style, CustomStyleProperty<Color> property, out Color color)
    {
        if (!style.TryGetValue(property, out color))
        {
            return false;
        }

        if (QualitySettings.activeColorSpace == ColorSpace.Linear && color.a > 0f && color.a < 1f)
        {
            color.a = LinearAlpha(color, color.a);
        }

        return true;
    }

    private static float LinearAlpha(Color c, float a)
    {
        float Lum(Color x) => (x.r + x.g + x.b) / 3f;
        Color blendGamma = Color.Lerp(s_backdrop, new Color(c.r, c.g, c.b, 1f), a);
        float target = Lum(blendGamma.linear), from = Lum(s_backdrop.linear), to = Lum(new Color(c.r, c.g, c.b, 1f).linear);
        return to - from > 1e-4f ? Mathf.Clamp01((target - from) / (to - from)) : a;
    }

    private static void OnTransitionEnd(TransitionEndEvent evt)
    {
        var element = (VisualElement)evt.currentTarget;
        if (s_styles.TryGetValue(element, out KernVectorStyle? style) && style.Edges)
        {
            element.MarkDirtyRepaint();
        }
    }

    private static void OnStyleResolved(CustomStyleResolvedEvent evt)
    {
        var element = (VisualElement)evt.currentTarget;
        if (s_styles.TryGetValue(element, out KernVectorStyle? style))
        {
            Read(element, style);
        }
    }

    private static void Read(VisualElement element, KernVectorStyle s)
    {
        ICustomStyle c = element.customStyle;
        s.HatchColor = TryGetColor(c, s_hatchColor, out Color hatch) ? hatch : null;
        s.HatchStep = c.TryGetValue(s_hatchStep, out float hatchStep) ? hatchStep : 4f;
        s.HatchBand = c.TryGetValue(s_hatchBand, out float hatchBand) ? hatchBand : 0f;
        s.HatchBandTop = c.TryGetValue(s_hatchBandEdge, out string? bandEdge) && bandEdge.Trim('"') == "top";
        s.DotsColor = TryGetColor(c, s_dotsColor, out Color dots) ? dots : null;
        s.DotsStep = c.TryGetValue(s_dotsStep, out float dotsStep) ? dotsStep : 32f;
        s.DotsSize = c.TryGetValue(s_dotsSize, out float dotsSize) ? dotsSize : 2f;
        s.PanelFill = TryGetColor(c, s_panelFill, out Color fill) ? fill : null;
        s.PanelEdge = TryGetColor(c, s_panelEdge, out Color edge) ? edge : Color.clear;
        s.PanelHighlight = TryGetColor(c, s_panelHighlight, out Color highlight) ? highlight : Color.clear;
        // Зерно без поправки прозрачности: оно смешивается с заливкой заранее, в гамме (KernShapes.Panel)
        s.PanelGrain = c.TryGetValue(s_panelGrain, out Color grain) ? grain : Color.clear;
        s.PanelNotch = c.TryGetValue(s_panelNotch, out float notch) ? notch : 10f;
        s.PanelTick = c.TryGetValue(s_panelTick, out float tick) ? tick : 0f;
        s.PanelTickWidth = c.TryGetValue(s_panelTickWidth, out float tickWidth) ? tickWidth : 1f;
        s.PanelTickColor = TryGetColor(c, s_panelTickColor, out Color tickColor) ? tickColor : Color.clear;
        s.ShapeColor = TryGetColor(c, s_shapeColor, out Color shape) ? shape : null;
        s.ShapeNotch = c.TryGetValue(s_shapeNotch, out float shapeNotch) ? shapeNotch : 8f;
        s.ShapeBottomRight = c.TryGetValue(s_shapeCorner, out string corner) && corner == "bottom-right";
        s.QuantColor = TryGetColor(c, s_quantColor, out Color quant) ? quant : null;
        s.QuantCell = c.TryGetValue(s_quantCell, out float cell) ? cell : 6f;
        s.QuantGap = c.TryGetValue(s_quantGap, out float gap) ? gap : 2f;
        s.RulesColor = TryGetColor(c, s_rulesColor, out Color rules) ? rules : null;
        s.RulesStep = c.TryGetValue(s_rulesStep, out float rulesStep) ? rulesStep : 4f;
        s.DashColor = TryGetColor(c, s_dashColor, out Color dash) ? dash : null;
        s.TickColor = TryGetColor(c, s_tickColor, out Color tickMark) && tickMark.a > 0 ? tickMark : null;
        s.CheckColor = TryGetColor(c, s_checkColor, out Color check) && check.a > 0 ? check : null;
        s.ChevronColor = TryGetColor(c, s_chevronColor, out Color chevron) ? chevron : null;
        s.ChevronRight = c.TryGetValue(s_chevronRight, out float chevronRight) ? chevronRight : 14f;
        s.Edges = HasEdges(element.resolvedStyle);

        if (s.Any && !s.Drawing)
        {
            element.generateVisualContent += Draw;
            if (element is TextElement)
            {
                element.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            }

            s.Drawing = true;
        }
        else if (!s.Any && s.Drawing)
        {
            element.generateVisualContent -= Draw;
            element.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            s.Drawing = false;
        }

        if (element is TextElement)
        {
            RenderUnderlay(element, s);
        }

        element.MarkDirtyRepaint();
    }

    private static void OnGeometryChanged(GeometryChangedEvent evt)
    {
        var element = (VisualElement)evt.currentTarget;
        if (s_styles.TryGetValue(element, out KernVectorStyle? style))
        {
            RenderUnderlay(element, style);
        }
    }

    // Текст элемента рисуется его же содержимым, и всё, что добавлено в generateVisualContent, ложится поверх
    // букв. Подложку (штриховка, точки, корпус, форма со срезом) текстовому элементу отдаём фоном — векторной
    // картинкой его размера: фон всегда под текстом
    private static void RenderUnderlay(VisualElement element, KernVectorStyle s)
    {
        var r = new Rect(0, 0, element.layout.width, element.layout.height);
        bool underlay = s.HatchColor.HasValue || s.DotsColor.HasValue || s.PanelFill.HasValue || s.ShapeColor.HasValue;
        if (!underlay || float.IsNaN(r.width) || r.width <= 0 || r.height <= 0)
        {
            ReleaseUnderlay(element, s);
            return;
        }

        using var painter = new Painter2D();
        DrawUnderlay(painter, r, s, KernPixelGrid.Of(element), element);
        VectorImage image = s.Underlay ??= ScriptableObject.CreateInstance<VectorImage>();
        if (painter.SaveToVectorImage(image))
        {
            element.style.backgroundImage = new StyleBackground(image);
            element.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
        }
    }

    private static void ReleaseUnderlay(VisualElement element, KernVectorStyle s)
    {
        if (s.Underlay == null)
        {
            return;
        }

        element.style.backgroundImage = StyleKeyword.Null;
        element.style.backgroundSize = StyleKeyword.Null;
        UnityEngine.Object.Destroy(s.Underlay);
        s.Underlay = null;
    }

    private static void DrawUnderlay(Painter2D p, Rect r, KernVectorStyle s, KernPixelGrid g, VisualElement element)
    {
        if (s.HatchColor is Color hatch)
        {
            KernShapes.Hatch(p, HatchArea(r, s, element), s.HatchStep, hatch);
        }

        if (s.DotsColor is Color dots)
        {
            KernShapes.Dots(p, r, s.DotsStep, s.DotsSize, dots, g);
        }

        if (s.PanelFill is Color fill)
        {
            KernShapes.Panel(p, r, new KernPanelStyle(fill, s.PanelGrain, s.PanelEdge, s.PanelHighlight, s.PanelNotch, s.PanelTick, s.PanelTickWidth, s.PanelTickColor), g);
        }

        if (s.ShapeColor is Color shape)
        {
            KernShapes.Notched(p, r, s.ShapeNotch, shape, s.ShapeBottomRight);
        }
    }

    // Полоса у края поля внутри рамки (background-origin: padding-box), иначе весь элемент
    private static Rect HatchArea(Rect r, KernVectorStyle s, VisualElement element)
    {
        if (s.HatchBand <= 0f)
        {
            return r;
        }

        IResolvedStyle st = element.resolvedStyle;
        float y = s.HatchBandTop ? r.yMin + st.borderTopWidth : r.yMax - st.borderBottomWidth - s.HatchBand;
        return new Rect(r.xMin + st.borderLeftWidth, y, r.width - st.borderLeftWidth - st.borderRightWidth, s.HatchBand);
    }

    private static bool HasEdges(IResolvedStyle st) =>
        (st.borderTopWidth > 0 && st.borderTopColor.a > 0) || (st.borderRightWidth > 0 && st.borderRightColor.a > 0) ||
        (st.borderBottomWidth > 0 && st.borderBottomColor.a > 0) || (st.borderLeftWidth > 0 && st.borderLeftColor.a > 0);

    private static Color Blend(Color color) =>
        QualitySettings.activeColorSpace == ColorSpace.Linear && color.a > 0f && color.a < 1f
            ? new Color(color.r, color.g, color.b, LinearAlpha(color, color.a))
            : color;

    // Слои снизу вверх: штриховка, точки, корпус панели, форма клавиши, ячейки, риски, пунктир, засечка, галочка, стрелка
    private static void Draw(MeshGenerationContext context)
    {
        VisualElement element = context.visualElement;
        if (!s_styles.TryGetValue(element, out KernVectorStyle? s))
        {
            return;
        }

        Painter2D p = context.painter2D;
        var r = new Rect(0, 0, element.layout.width, element.layout.height);
        KernPixelGrid g = KernPixelGrid.Of(element);
        if (r.width <= 0 || r.height <= 0)
        {
            return;
        }

        // У текстового элемента подложка уже в фоне (RenderUnderlay), здесь — только то, что лежит поверх
        if (element is not TextElement)
        {
            DrawUnderlay(p, r, s, g, element);
        }

        // Рамка USS чётко по пикселям экрана — только при дробном масштабе: при целом она и так ровная
        if (s.Edges && g.Fractional)
        {
            IResolvedStyle st = element.resolvedStyle;
            KernShapes.Edges(p, r, st, Blend(st.borderTopColor), Blend(st.borderRightColor), Blend(st.borderBottomColor), Blend(st.borderLeftColor), g);
        }

        if (s.QuantColor is Color quant)
        {
            KernShapes.Cells(p, r, s.QuantCell, s.QuantGap, quant, g);
        }

        if (s.RulesColor is Color rules)
        {
            KernShapes.Rules(p, r, s.RulesStep, rules, g);
        }

        if (s.DashColor is Color dash)
        {
            // Пунктир идёт вдоль элемента: у вертикальной черты (разделитель кнопок) — сверху вниз
            if (r.width <= 2f && r.height > r.width)
            {
                KernShapes.DashedLeft(p, r, 2f, 2f, dash, g);
            }
            else
            {
                KernShapes.DashedBottom(p, r, 2f, 2f, dash, g);
            }
        }

        if (s.TickColor is Color tick)
        {
            KernShapes.CornerTick(p, r, 6f, 3f, tick, g);
        }

        if (s.CheckColor is Color check)
        {
            KernShapes.Check(p, r, check);
        }

        if (s.ChevronColor is Color chevron)
        {
            KernShapes.Chevron(p, r, s.ChevronRight, chevron);
        }
    }
}

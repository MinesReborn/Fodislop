#nullable enable

using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Screens;

// Формы микрографики макета visual/kern-ui — векторно (Painter2D), без текстур: рамка панели со срезом угла
// и Г-уголками, клавиша со срезом, засечка, шкала ступенями, пунктир, точки сетки, штриховка, стрелка списка.
// Линии — прямоугольниками по пикселям экрана (KernPixelGrid): волосяная линия остаётся в один пиксель при любом масштабе.
internal static class KernShapes
{
    // Корпус со срезом правого верхнего угла (--notch-clip). Слои — как в CSS макета: фон с уголками, над ним
    // отсвет кромки (inset box-shadow), сверху рамка. Уголки — слои фона от края рамки (background-origin: border-box),
    // поэтому непрозрачная рамка закрывает их внешний пиксель: уголок 1 px не виден совсем, у 2 px виден внутренний
    public static void Panel(Painter2D p, Rect r, KernPanelStyle s, KernPixelGrid g)
    {
        float n = s.Notch;
        // Зерно ложится на корпус как в браузере — смешением в гамме, одним цветом: полупрозрачный слой поверх
        // смешивался бы в линейном пространстве и выходил бы темнее макета
        Color body = s.Fill;
        if (s.Grain.a > 0)
        {
            Color mixed = Color.Lerp(body, s.Grain, s.Grain.a);
            body = new Color(mixed.r, mixed.g, mixed.b, s.Fill.a);
        }

        Notched(p, r, n, body);

        // Г-уголки: слева сверху, слева снизу, справа снизу (справа сверху — срез)
        float t = s.Tick, w = s.TickWidth;
        if (w > 1f)
        {
            // Уголок толще рамки остаётся толще и на мелком экране: рамка в пиксель, уголок не меньше двух
            w = Mathf.Max(w, 2f * g.Pixel);
        }

        float e = Mathf.Max(1f, g.Pixel);   // толщина рамки на экране
        if (t > 0 && w > e && s.TickColor.a > 0)
        {
            p.fillColor = s.TickColor;
            // Только видимое из-под рамки: плечи сдвинуты внутрь на её пиксель, уголок 1 px не рисуется вовсе
            float v = w - e;
            FillRect(p, g, r.xMin + e, r.yMin + e, t - e, v);
            FillRect(p, g, r.xMin + e, r.yMin + w, v, t - w);
            FillRect(p, g, r.xMin + e, r.yMax - w, t - e, v);
            FillRect(p, g, r.xMin + e, r.yMax - t, v, t - w);
            FillRect(p, g, r.xMax - t, r.yMax - w, t - e, v);
            FillRect(p, g, r.xMax - w, r.yMax - t, v, t - w);
        }

        // Отсвет: линия под верхней рамкой, до среза
        if (s.Highlight.a > 0)
        {
            p.fillColor = s.Highlight;
            FillRect(p, g, r.xMin + 1, r.yMin + 1, r.width - n - 1, 1);
        }

        // Рамка: верх до среза, правый край от среза, низ, левый край; диагональ среза — линией той же толщины
        p.fillColor = s.Edge;
        FillRect(p, g, r.xMin, r.yMin, r.width - n, 1);
        FillRect(p, g, r.xMax - 1, r.yMin + n, 1, r.height - n);
        FillRect(p, g, r.xMin, r.yMax - 1, r.width, 1);
        FillRect(p, g, r.xMin, r.yMin, 1, r.height);
        float line = Mathf.Max(1f, g.Pixel);
        p.strokeColor = s.Edge;
        p.lineWidth = 1.4f * line;
        p.BeginPath();
        p.MoveTo(new Vector2(r.xMax - n, r.yMin + (line * .5f)));
        p.LineTo(new Vector2(r.xMax - (line * .5f), r.yMin + n));
        p.Stroke();
    }

    // Рамка элемента (USS border) по пикселям экрана: UI Toolkit рисует её в долях пикселя, и при масштабе 0.667
    // волосяная линия тает — браузер макета держит её целым пикселем. Чёткая линия того же цвета ложится поверх.
    // Верх и низ — во всю ширину, бока — между ними: полупрозрачный цвет не удваивается в углах
    public static void Edges(Painter2D p, Rect r, IResolvedStyle st, Color top, Color right, Color bottom, Color left, KernPixelGrid g)
    {
        float t = st.borderTopWidth, rw = st.borderRightWidth, b = st.borderBottomWidth, l = st.borderLeftWidth;
        float inner = Mathf.Max(0f, r.height - t - b);
        if (t > 0 && top.a > 0)
        {
            p.fillColor = top;
            FillRect(p, g, r.xMin, r.yMin, r.width, t);
        }

        if (b > 0 && bottom.a > 0)
        {
            p.fillColor = bottom;
            FillRect(p, g, r.xMin, r.yMax - b, r.width, b);
        }

        if (l > 0 && left.a > 0)
        {
            p.fillColor = left;
            FillRect(p, g, r.xMin, r.yMin + t, l, inner);
        }

        if (rw > 0 && right.a > 0)
        {
            p.fillColor = right;
            FillRect(p, g, r.xMax - rw, r.yMin + t, rw, inner);
        }
    }

    // Форма со срезом угла: правого верхнего — главная клавиша (--notch-btn-clip), правого нижнего — табличка раздела
    // (.sec-plate: «Груз», «Геология»)
    public static void Notched(Painter2D p, Rect r, float notch, Color fill, bool bottomRight = false)
    {
        p.fillColor = fill;
        p.BeginPath();
        p.MoveTo(new Vector2(r.xMin, r.yMin));
        if (bottomRight)
        {
            p.LineTo(new Vector2(r.xMax, r.yMin));
            p.LineTo(new Vector2(r.xMax, r.yMax - notch));
            p.LineTo(new Vector2(r.xMax - notch, r.yMax));
        }
        else
        {
            p.LineTo(new Vector2(r.xMax - notch, r.yMin));
            p.LineTo(new Vector2(r.xMax, r.yMin + notch));
            p.LineTo(new Vector2(r.xMax, r.yMax));
        }

        p.LineTo(new Vector2(r.xMin, r.yMax));
        p.ClosePath();
        p.Fill();
    }

    // Засечка клавиши: Г-риска size × size в правом нижнем углу с отступом inset
    public static void CornerTick(Painter2D p, Rect r, float size, float inset, Color color, KernPixelGrid g)
    {
        p.fillColor = color;
        FillRect(p, g, r.xMax - inset - size, r.yMax - inset - 1, size, 1);
        FillRect(p, g, r.xMax - inset - 1, r.yMax - inset - size, 1, size - 1);
    }

    // Шкала ступенями: ячейка cell, зазор gap, на всю ширину элемента; неполная ячейка в конце обрезается
    public static void Cells(Painter2D p, Rect r, float cell, float gap, Color color, KernPixelGrid g)
    {
        if (r.width <= 0 || color.a <= 0)
        {
            return;
        }

        p.fillColor = color;
        for (float x = r.xMin; x < r.xMax; x += cell + gap)
        {
            FillRect(p, g, x, r.yMin, Mathf.Min(cell, r.xMax - x), r.height);
        }

    }

    // Пунктир по нижнему краю (border-bottom: 1px dashed): штрих dash, просвет gap
    public static void DashedBottom(Painter2D p, Rect r, float dash, float gap, Color color, KernPixelGrid g)
    {
        p.fillColor = color;
        for (float x = r.xMin; x < r.xMax; x += dash + gap)
        {
            FillRect(p, g, x, r.yMax - 1, Mathf.Min(dash, r.xMax - x), 1);
        }

    }

    // Вертикальный пунктир по левому краю (repeating-linear-gradient: прозрачно gap, цвет dash)
    public static void DashedLeft(Painter2D p, Rect r, float dash, float gap, Color color, KernPixelGrid g)
    {
        p.fillColor = color;
        for (float y = r.yMin + gap; y < r.yMax; y += dash + gap)
        {
            FillRect(p, g, r.xMin, y, 1, Mathf.Min(dash, r.yMax - y));
        }

    }

    // Риски дорожки: 1 px через step (repeating-linear-gradient 90deg 0 1px, transparent 1px step)
    public static void Rules(Painter2D p, Rect r, float step, Color color, KernPixelGrid g)
    {
        p.fillColor = color;
        for (float x = r.xMin; x < r.xMax; x += step)
        {
            FillRect(p, g, x, r.yMin, 1, r.height);
        }

    }

    // Точки по сетке клеток мира: квадрат size через step (bg.svg: circle r=1 через 32 px)
    public static void Dots(Painter2D p, Rect r, float step, float size, Color color, KernPixelGrid g)
    {
        p.fillColor = color;
        for (float y = r.yMin; y < r.yMax; y += step)
        {
            for (float x = r.xMin; x < r.xMax; x += step)
            {
                FillRect(p, g, x, y, size, size);
            }
        }

    }

    // Штриховка пустого (--hatch): линии 1 px под 135° через step, обрезанные по рамке
    public static void Hatch(Painter2D p, Rect r, float step, Color color)
    {
        p.strokeColor = color;
        p.lineWidth = 1f;
        p.lineCap = LineCap.Butt;
        p.BeginPath();
        // Прямая x + y = c; проходим c от верхнего левого угла до нижнего правого. step — период поперёк линий,
        // как у repeating-linear-gradient(135deg, … 0 1px, transparent 1px 4px) макета: по горизонтали это step·√2
        float along = step * 1.41421356f;
        for (float c = r.xMin + r.yMin + along; c < r.xMax + r.yMax; c += along)
        {
            float x0 = Mathf.Max(r.xMin, c - r.yMax), x1 = Mathf.Min(r.xMax, c - r.yMin);
            if (x1 <= x0)
            {
                continue;
            }

            p.MoveTo(new Vector2(x0, c - x0));
            p.LineTo(new Vector2(x1, c - x1));
        }

        p.Stroke();
    }

    // Стрелка списка — как фон select макета: SVG 16 px (viewBox 24, путь M6 9l6 6 6-6) в right px от края по центру
    // высоты — излом 8 × 4 px, линия 2/24 кегля значка, скруглённые концы
    public static void Chevron(Painter2D p, Rect r, float right, Color color)
    {
        const float icon = 16f, k = icon / 24f;
        float x0 = r.xMax - right - icon, y0 = r.center.y - (icon * .5f);
        p.strokeColor = color;
        p.lineWidth = 2f * k;
        p.lineJoin = LineJoin.Round;
        p.lineCap = LineCap.Round;
        p.BeginPath();
        p.MoveTo(new Vector2(x0 + (6f * k), y0 + (9f * k)));
        p.LineTo(new Vector2(x0 + (12f * k), y0 + (15f * k)));
        p.LineTo(new Vector2(x0 + (18f * k), y0 + (9f * k)));
        p.Stroke();
    }

    // Галочка флажка: ломаная по центру элемента, толщина 2 px
    public static void Check(Painter2D p, Rect r, Color color)
    {
        float k = Mathf.Min(r.width, r.height) / 14f;
        Vector2 c = r.center;
        p.strokeColor = color;
        p.lineWidth = 2f * k;
        p.lineJoin = LineJoin.Miter;
        p.lineCap = LineCap.Butt;
        p.BeginPath();
        p.MoveTo(c + new Vector2(-3.5f, 0) * k);
        p.LineTo(c + new Vector2(-1f, 2.5f) * k);
        p.LineTo(c + new Vector2(3.5f, -2.5f) * k);
        p.Stroke();
    }

    private static void FillRect(Painter2D p, KernPixelGrid g, float x, float y, float w, float h)
    {
        if (w <= 0 || h <= 0)
        {
            return;
        }

        // Каждый прямоугольник — своим путём: несколько замкнутых контуров в одном пути Painter2D сливает
        // в общий многоугольник, и между плечами уголка заливается треугольник
        Rect q = g.Snap(x, y, w, h);
        p.BeginPath();
        p.MoveTo(new Vector2(q.xMin, q.yMin));
        p.LineTo(new Vector2(q.xMax, q.yMin));
        p.LineTo(new Vector2(q.xMax, q.yMax));
        p.LineTo(new Vector2(q.xMin, q.yMax));
        p.ClosePath();
        p.Fill();
    }
}

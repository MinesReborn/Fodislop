#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

using Kern.AssetPipeline.Loading;

namespace Kern.UI.Input;

// Курсоры ресайза для перетаскиваемых окон: при наведении на зону границы
// аппаратный курсор меняется на соответствующую стрелку, при уходе —
// возвращается обычный. Рисуются процедурно один раз и кэшируются: текстур
// курсоров в проекте нет, а UnityEngine.Cursor.SetCursor гарантированно
// работает в рантайме (style.cursor в UI Toolkit зависит от версии).
//
// Полностью квалифицированный UnityEngine.Cursor обязателен: вместе с
// UnityEngine.UIElements в области видимости есть и его struct Cursor.
internal static class ResizeCursors
{
    public enum Shape
    {
        WestEast,
        NorthSouth,
        NorthwestSoutheast,
        NortheastSouthwest,
        MoveAll,
    }

    private const int Size = 32;
    private static readonly Vector2 s_hotspot = new(Size / 2, Size / 2);

    private static readonly Dictionary<Shape, Texture2D> s_cache = new();

    // Подписки хранятся, чтобы Dispose мог снять их вместе с элементами чата.
    // EventCallback, а не Action: RegisterCallback/UnregisterCallback принимают
    // только UI-делегат, из Action<> тип-аргумент они не выводят.
    private readonly static List<(VisualElement Element, EventCallback<PointerEnterEvent> Enter, EventCallback<PointerLeaveEvent> Leave)> Bound = new();

    public static void Bind(VisualElement element, Shape shape)
    {
        EventCallback<PointerEnterEvent> enter = _ => UnityEngine.Cursor.SetCursor(Get(shape), s_hotspot, CursorMode.Auto);
        EventCallback<PointerLeaveEvent> leave = _ => Reset();
        element.RegisterCallback(enter);
        element.RegisterCallback(leave);
        Bound.Add((element, enter, leave));
    }

    // Вызывается владельцем при скрытии окна или разрушении: если курсор был
    // над зоной в момент display:none, PointerLeave не успевает прийти.
    public static void Reset() => UnityEngine.Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);

    public static void UnbindAll()
    {
        foreach ((VisualElement element, EventCallback<PointerEnterEvent> enter, EventCallback<PointerLeaveEvent> leave) in Bound)
        {
            element.UnregisterCallback(enter);
            element.UnregisterCallback(leave);
        }

        Bound.Clear();
        Reset();
    }

    private static Texture2D Get(Shape shape)
    {
        if (!s_cache.TryGetValue(shape, out Texture2D? texture))
        {
            texture = Build(shape);
            s_cache[shape] = texture;
        }

        return texture;
    }

    private static Texture2D Build(Shape shape)
    {
        // Направления стрелок в экранных осях (Y вниз). Диагонали —
        // нормированные (±1, ±1): кончики на диагонали квадрата.
        List<Vector2> directions = shape switch
        {
            Shape.WestEast => [new Vector2(-1f, 0f), new Vector2(1f, 0f)],
            Shape.NorthSouth => [new Vector2(0f, -1f), new Vector2(0f, 1f)],
            Shape.NorthwestSoutheast => [new Vector2(-0.7071f, -0.7071f), new Vector2(0.7071f, 0.7071f)],
            Shape.NortheastSouthwest => [new Vector2(0.7071f, -0.7071f), new Vector2(-0.7071f, 0.7071f)],
            Shape.MoveAll =>
            [
                new Vector2(-1f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, -1f), new Vector2(0f, 1f),
            ],
            _ => [new Vector2(1f, 0f)],
        };

        Vector2 center = new(Size / 2f, Size / 2f);
        const float headLength = 7f;
        const float headHalfWidth = 4.5f;
        const float shaftLength = 9f;
        const float shaftHalfThickness = 1.4f;

        var segments = new List<(Vector2 A, Vector2 B)>();
        var heads = new List<(Vector2 Tip, Vector2 BaseA, Vector2 BaseB)>();
        foreach (Vector2 direction in directions)
        {
            Vector2 perpendicular = new(-direction.y, direction.x);
            Vector2 shaftStart = center - direction * shaftLength;
            Vector2 baseCenter = center + direction * shaftLength;
            segments.Add((shaftStart, baseCenter));
            heads.Add((
                center + direction * (shaftLength + headLength),
                baseCenter + perpendicular * headHalfWidth,
                baseCenter - perpendicular * headHalfWidth));
        }

        var pixels = new Color32[Size * Size];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                var point = new Vector2(x + 0.5f, y + 0.5f);
                // Чёрная кромка: сначала расширенная фигура, поверх — ядро.
                // Так курсор читается и на тёмном фоне игры, и на светлой заливке.
                bool core = Hit(point, segments, heads, shaftHalfThickness, 0f);
                bool rim = Hit(point, segments, heads, shaftHalfThickness + 1f, 1f);
                pixels[y * Size + x] = core
                    ? new Color32(255, 255, 255, 255)
                    : rim ? new Color32(0, 0, 0, 230) : new Color32(0, 0, 0, 0);
            }
        }

        Texture2D texture = RuntimeTextureFactory.CreateRGBA32NoMip(
            Size, Size, $"resize-cursor-{shape}", RuntimeTextureColorSpace.Srgb,
            FilterMode.Bilinear, TextureWrapMode.Clamp);
        texture.SetPixels32(pixels);
        texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        return texture;
    }

    private static bool Hit(
        Vector2 point,
        List<(Vector2 A, Vector2 B)> segments,
        List<(Vector2 Tip, Vector2 BaseA, Vector2 BaseB)> heads,
        float halfThickness,
        float outset)
    {
        foreach ((Vector2 a, Vector2 b) in segments)
        {
            if (DistanceToSegment(point, a, b) <= halfThickness + outset)
            {
                return true;
            }
        }

        foreach ((Vector2 tip, Vector2 baseA, Vector2 baseB) in heads)
        {
            if (InTriangle(point, tip, baseA, baseB, outset))
            {
                return true;
            }
        }

        return false;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lengthSquared = ab.sqrMagnitude;
        if (lengthSquared < 1e-6f)
        {
            return Vector2.Distance(point, a);
        }

        float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSquared);
        return Vector2.Distance(point, a + ab * t);
    }

    // Знаковый тест барицентрических координат; outset расширяет треугольник
    // от его центроида — кромка вокруг наконечника.
    private static bool InTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c, float outset)
    {
        if (outset > 0f)
        {
            Vector2 centroid = (a + b + c) / 3f;
            a = centroid + (a - centroid) * (1f + outset);
            b = centroid + (b - centroid) * (1f + outset);
            c = centroid + (c - centroid) * (1f + outset);
        }

        float d1 = Sign(point, a, b);
        float d2 = Sign(point, b, c);
        float d3 = Sign(point, c, a);
        bool hasNegative = d1 < 0f || d2 < 0f || d3 < 0f;
        bool hasPositive = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(hasNegative && hasPositive);
    }

    private static float Sign(Vector2 point, Vector2 a, Vector2 b) =>
        (point.x - b.x) * (a.y - b.y) - (a.x - b.x) * (point.y - b.y);
}

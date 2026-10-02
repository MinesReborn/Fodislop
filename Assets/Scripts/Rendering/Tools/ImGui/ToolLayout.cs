#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kern.Tools.ImGui;

// Замена GUILayout.*Scope и GUILayout.Width/ExpandWidth без мусора.
//
// GUILayout.HorizontalScope и родня — классы: `using (new ...Scope())`
// выделял объект на каждую группу в каждом событии IMGUI, а групп в окнах
// инструментов десятки. GUILayoutOption — тоже класс, и GUILayout.Width
// создаёт новый при каждом вызове. Здесь группы — структуры (using на
// структуре не упаковывает), а опции кешируются: они неизменяемые.
public static class ToolLayout
{
    // Динамические ширины (окно тянут мышью) дали бы бесконечный рост кеша.
    private const int MaximumCachedWidths = 256;

    private static readonly Dictionary<float, GUILayoutOption> s_widths = [];
    private static readonly GUILayoutOption s_expandWidthTrue = GUILayout.ExpandWidth(true);
    private static readonly GUILayoutOption s_expandWidthFalse = GUILayout.ExpandWidth(false);

    public static GUILayoutOption Width(float width)
    {
        if (s_widths.TryGetValue(width, out GUILayoutOption? option))
        {
            return option;
        }

        if (s_widths.Count >= MaximumCachedWidths)
        {
            s_widths.Clear();
        }

        option = GUILayout.Width(width);
        s_widths[width] = option;
        return option;
    }

    public static GUILayoutOption ExpandWidth(bool expand) =>
        expand ? s_expandWidthTrue : s_expandWidthFalse;

    public static HorizontalGroup Horizontal()
    {
        GUILayout.BeginHorizontal();
        return default;
    }

    public static VerticalGroup Vertical()
    {
        GUILayout.BeginVertical();
        return default;
    }

    public static VerticalGroup Vertical(GUIStyle style)
    {
        GUILayout.BeginVertical(style);
        return default;
    }

    public static ScrollGroup ScrollView(ref Vector2 scrollPosition)
    {
        scrollPosition = GUILayout.BeginScrollView(scrollPosition);
        return default;
    }

    public readonly struct HorizontalGroup : IDisposable
    {
        public void Dispose() => GUILayout.EndHorizontal();
    }

    public readonly struct VerticalGroup : IDisposable
    {
        public void Dispose() => GUILayout.EndVertical();
    }

    public readonly struct ScrollGroup : IDisposable
    {
        public void Dispose() => GUILayout.EndScrollView();
    }
}

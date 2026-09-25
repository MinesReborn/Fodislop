#nullable enable

using System;
using System.Collections.Generic;
using Kern.Tools.Imgui.Profiling;
using UnityEngine;

namespace Kern.Tools.Imgui.Windows;

internal sealed class FrameBreakdownRowRenderer
{
    private const float RowCullMargin = 120f;
    private float[] _rowHeights = [];
    private float[] _measuredHeights = [];
    private int _rowHeightsTabKey = -1;
    private float _viewportHeight = float.MaxValue;

    public void UpdateViewportHeight(float height)
    {
        _viewportHeight = height;
    }

    public void DrawRows(
        List<FrameBreakdownRow> rows,
        int tabKey,
        Vector2 scroll,
        GUIStyle mutedLabelStyle,
        GUIStyle secondaryButtonStyle,
        MarkerSearch search)
    {
        if (rows.Count == 0)
        {
            GUILayout.Label("Замеров ещё нет.", mutedLabelStyle);
            return;
        }

        if (_rowHeightsTabKey != tabKey || _rowHeights.Length != rows.Count)
        {
            if (_rowHeightsTabKey != tabKey)
            {
                _rowHeights = [];
            }

            Array.Resize(ref _rowHeights, rows.Count);
            _measuredHeights = new float[rows.Count];
            _rowHeightsTabKey = tabKey;
        }

        bool repaint = Event.current.type == EventType.Repaint;
        float top = scroll.y - RowCullMargin;
        float bottom = scroll.y + _viewportHeight + RowCullMargin;
        float y = 0f;

        for (int i = 0; i < rows.Count; i++)
        {
            float known = _rowHeights[i];
            if (known > 0f && (y + known < top || y > bottom))
            {
                GUILayout.Space(known);
                _measuredHeights[i] = known;
                y += known;
                continue;
            }

            Rect start = GUILayoutUtility.GetRect(0f, 0f);
            DrawRow(rows[i], mutedLabelStyle, secondaryButtonStyle, search);
            Rect end = GUILayoutUtility.GetRect(0f, 0f);
            _measuredHeights[i] = repaint ? end.y - start.y : known;
            y += known > 0f ? known : 0f;
        }

        if (repaint)
        {
            Array.Copy(_measuredHeights, _rowHeights, rows.Count);
        }
    }

    private static void DrawRow(
        FrameBreakdownRow row,
        GUIStyle mutedLabelStyle,
        GUIStyle secondaryButtonStyle,
        MarkerSearch search)
    {
        switch (row.Kind)
        {
            case FrameBreakdownRowKind.Header:
                ToolChrome.SectionHeader(row.Text);
                return;
            case FrameBreakdownRowKind.Warning:
                GUILayout.Label(row.Text, ToolTheme.WarningLabel);
                return;
        }

        if (row.Pin == null)
        {
            GUILayout.Label(row.Text, mutedLabelStyle);
        }
        else
        {
            using (ToolLayout.Horizontal())
            {
                GUILayout.Label(row.Text, mutedLabelStyle);
                bool pinned = search.IsPinned(row.Pin);
                if (GUILayout.Button(pinned ? "★" : "☆", secondaryButtonStyle, ToolLayout.Width(26f)))
                {
                    search.RequestTogglePin(row.Pin);
                }
            }
        }

        if (row.Share >= 0f)
        {
            ToolChrome.MeterLine(row.Share, row.Color, 3f);
            GUILayout.Space(2f);
        }
    }
}

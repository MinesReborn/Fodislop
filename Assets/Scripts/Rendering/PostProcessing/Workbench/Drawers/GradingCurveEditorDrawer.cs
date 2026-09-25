#nullable enable

using System.Collections.Generic;
using Kern.Tools.Imgui;
using UnityEngine;

namespace Kern.Rendering.PostProcessing.Workbench;

internal sealed class GradingCurveEditorDrawer
{
    private ColorGradeCurve? _selectedCurve;
    private int _selectedCurvePoint = -1;
    private bool _draggingCurvePoint;
    private ColorGradeCurve? _shownCurve;
    private int _shownCurvePoint = -1;
    private readonly Dictionary<string, string[]> _pointSliderIds = [];

    public void ResetState()
    {
        _selectedCurve = null;
        _selectedCurvePoint = -1;
        _draggingCurvePoint = false;
        _shownCurve = null;
        _shownCurvePoint = -1;
    }

    public void DrawCurveEditor(string id, string title, ColorGradeCurve curve, GradingLayerControlsDrawer drawer)
    {
        GUILayout.Label(title, ToolTheme.FieldLabel);
        Rect graph = GUILayoutUtility.GetRect(300f, 150f, ToolLayout.ExpandWidth(true));
        DrawCurveGraph(graph, curve);

        using (ToolLayout.Horizontal())
        {
            if (GUILayout.Button("+ точка", ToolTheme.SecondaryButton, ToolLayout.Width(74f)))
            {
                _selectedCurve = curve;
                _selectedCurvePoint = curve.AddPoint(new Vector2(0.5f, curve.Evaluate(0.5f)));
            }

            if (GUILayout.Button("reset", ToolTheme.SecondaryButton, ToolLayout.Width(58f)))
            {
                curve.Reset();
                if (ReferenceEquals(_selectedCurve, curve))
                {
                    _selectedCurvePoint = -1;
                }
            }

            ColorCurveInterpolation interpolation = curve.Interpolation;
            bool smooth = GUILayout.Toggle(
                interpolation == ColorCurveInterpolation.Smooth,
                "smooth",
                ToolTheme.SegmentedButton);
            curve.Interpolation = smooth
                ? ColorCurveInterpolation.Smooth
                : ColorCurveInterpolation.Linear;
        }

        if (Event.current.type == EventType.Layout)
        {
            _shownCurve = _selectedCurve;
            _shownCurvePoint = _selectedCurvePoint;
        }

        if (ReferenceEquals(_shownCurve, curve) && _shownCurvePoint >= 0)
        {
            int pointIndex = Mathf.Min(_shownCurvePoint, curve.PointCount - 1);
            Vector2 point = curve.GetPoint(pointIndex);
            Vector2 edited = new(
                drawer.Slider(PointSliderId(id, 0), "  X", point.x, 0f, 1f),
                drawer.Slider(PointSliderId(id, 1), "  Y", point.y, 0f, 1f));
            if (edited != point)
            {
                curve.SetPoint(pointIndex, edited);
            }
        }
    }

    private string PointSliderId(string id, int axis)
    {
        if (!_pointSliderIds.TryGetValue(id, out string[]? ids))
        {
            ids = [id + ".point.x", id + ".point.y"];
            _pointSliderIds[id] = ids;
        }

        return ids[axis];
    }

    private void DrawCurveGraph(Rect graph, ColorGradeCurve curve)
    {
        if (Event.current.type == EventType.Repaint)
        {
            Color previousColor = GUI.color;
            GUI.color = new Color(0.08f, 0.1f, 0.13f, 1f);
            GUI.DrawTexture(graph, Texture2D.whiteTexture);
            GUI.color = new Color(0.22f, 0.25f, 0.3f, 1f);
            for (int index = 1; index < 4; index++)
            {
                float x = graph.x + graph.width * index / 4f;
                float y = graph.y + graph.height * index / 4f;
                GUI.DrawTexture(new Rect(x, graph.y, 1f, graph.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(graph.x, y, graph.width, 1f), Texture2D.whiteTexture);
            }

            if (curve.Kind != ColorGradeCurveKind.Tone)
            {
                GUI.color = new Color(0.55f, 0.6f, 0.68f, 1f);
                GUI.DrawTexture(
                    new Rect(graph.x, graph.yMax - ColorGradeCurve.NeutralLevel * graph.height, graph.width, 1f),
                    Texture2D.whiteTexture);
            }

            GUI.color = new Color(0.25f, 0.85f, 1f, 1f);
            const float lineThickness = 2f;
            int samples = Mathf.Clamp(Mathf.CeilToInt(graph.width), 16, 1024);
            float previousY = graph.yMax - curve.Evaluate(0f) * graph.height;
            for (int index = 1; index <= samples; index++)
            {
                float x = index / (float)samples;
                float y = graph.yMax - curve.Evaluate(x) * graph.height;
                float top = Mathf.Max(graph.y, Mathf.Min(previousY, y) - lineThickness * 0.5f);
                float bottom = Mathf.Min(graph.yMax, Mathf.Max(previousY, y) + lineThickness * 0.5f);
                float columnX = graph.x + (index - 1) * graph.width / samples;
                GUI.DrawTexture(
                    new Rect(columnX, top, Mathf.Max(1f, graph.width / samples + 0.5f), Mathf.Max(1f, bottom - top)),
                    Texture2D.whiteTexture);
                previousY = y;
            }

            for (int index = 0; index < curve.PointCount; index++)
            {
                Vector2 point = curve.GetPoint(index);
                Rect pointRect = new(
                    Mathf.Clamp(graph.x + point.x * graph.width - 4f, graph.x, graph.xMax - 8f),
                    Mathf.Clamp(graph.yMax - point.y * graph.height - 4f, graph.y, graph.yMax - 8f),
                    8f,
                    8f);
                GUI.color = ReferenceEquals(_selectedCurve, curve) &&
                    _selectedCurvePoint == index
                    ? Color.yellow
                    : Color.white;
                GUI.DrawTexture(pointRect, Texture2D.whiteTexture);
            }

            GUI.color = previousColor;
        }

        Event current = Event.current;
        if (current.type == EventType.MouseDown && graph.Contains(current.mousePosition))
        {
            int nearest = FindCurvePoint(graph, curve, current.mousePosition);
            if (current.button == 1)
            {
                if (nearest >= 0 && curve.RemovePoint(nearest))
                {
                    _selectedCurve = curve;
                    _selectedCurvePoint = -1;
                    current.Use();
                }
            }
            else if (current.button == 0)
            {
                _selectedCurve = curve;
                _selectedCurvePoint = nearest >= 0
                    ? nearest
                    : curve.AddPoint(GraphToCurve(graph, current.mousePosition));
                _draggingCurvePoint = _selectedCurvePoint >= 0;
                GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                current.Use();
            }
        }
        else if (current.type == EventType.MouseDrag &&
                 _draggingCurvePoint &&
                 ReferenceEquals(_selectedCurve, curve))
        {
            curve.SetPoint(_selectedCurvePoint, GraphToCurve(graph, current.mousePosition));
            current.Use();
        }
        else if (current.type == EventType.MouseUp && _draggingCurvePoint)
        {
            _draggingCurvePoint = false;
            GUIUtility.hotControl = 0;
            current.Use();
        }
    }

    private static int FindCurvePoint(Rect graph, ColorGradeCurve curve, Vector2 mousePosition)
    {
        int nearest = -1;
        float nearestDistance = 12f;
        for (int index = 0; index < curve.PointCount; index++)
        {
            Vector2 point = curve.GetPoint(index);
            Vector2 graphPoint = new(
                graph.x + point.x * graph.width,
                graph.yMax - point.y * graph.height);
            float distance = Vector2.Distance(mousePosition, graphPoint);
            if (distance < nearestDistance)
            {
                nearest = index;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    private static Vector2 GraphToCurve(Rect graph, Vector2 position) => new(
        Mathf.InverseLerp(graph.x, graph.xMax, position.x),
        Mathf.InverseLerp(graph.yMax, graph.y, position.y));
}

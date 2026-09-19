#nullable enable

using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Kern.World;
public static class KernGizmos
{
#if UNITY_EDITOR
    private static GUIStyle? _labelStyle;

    private static GUIStyle LabelStyle
    {
        get
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                };
            }

            return _labelStyle;
        }
    }
#endif
    public static void DrawLabel(Vector3 position, string text, Color color)
    {
#if UNITY_EDITOR
        LabelStyle.normal.textColor = color;
        Handles.Label(position, text, LabelStyle);
#endif
    }

    public static void DrawLine(Vector3 start, Vector3 end, Color color, float thickness = 1f)
    {
#if UNITY_EDITOR
        Handles.color = color;
        Handles.DrawLine(start, end, thickness);
#else
        Debug.DrawLine(start, end, color);
#endif
    }

    public static void DrawBounds(Vector3 center, Vector2 size, Color color)
    {
        Vector3 half = new Vector3(size.x * 0.5f, size.y * 0.5f, 0f);
        Vector3 p1 = center + new Vector3(-half.x, -half.y, 0);
        Vector3 p2 = center + new Vector3(half.x, -half.y, 0);
        Vector3 p3 = center + new Vector3(half.x, half.y, 0);
        Vector3 p4 = center + new Vector3(-half.x, half.y, 0);

#if UNITY_EDITOR
        Handles.color = color;
        Handles.DrawLine(p1, p2);
        Handles.DrawLine(p2, p3);
        Handles.DrawLine(p3, p4);
        Handles.DrawLine(p4, p1);
#else
        Debug.DrawLine(p1, p2, color);
        Debug.DrawLine(p2, p3, color);
        Debug.DrawLine(p3, p4, color);
        Debug.DrawLine(p4, p1, color);
#endif
    }
    public static void DrawArrow(Vector3 pos, Vector3 direction, Color color, float length = 1f, float arrowHeadLength = 0.25f, float arrowHeadAngle = 20f)
    {
        if (direction == Vector3.zero)
        {
            return;
        }

        Vector3 dir = direction.normalized;
        Vector3 end = pos + (dir * length);

        DrawLine(pos, end, color);

        // 2D-наконечник: поворачиваем «хвост» стрелки в ±angle вокруг оси Z (плоскость XY)
        Vector3 headBase = -dir * arrowHeadLength;
        Vector3 right = Quaternion.Euler(0, 0, arrowHeadAngle) * headBase;
        Vector3 left = Quaternion.Euler(0, 0, -arrowHeadAngle) * headBase;
        DrawLine(end, end + right, color);
        DrawLine(end, end + left, color);
    }

    public static void DrawDottedLine(Vector3 start, Vector3 end, Color color, float dashSize = 2f)
    {
#if UNITY_EDITOR
        Handles.color = color;
        Handles.DrawDottedLine(start, end, dashSize);
#else
        Debug.DrawLine(start, end, color);
#endif
    }

    public static void DrawSolidRect(Vector3 center, Vector2 size, Color fillColor, Color outlineColor)
    {
#if UNITY_EDITOR
        Vector3[] verts =
        {
            center + new Vector3(-size.x * 0.5f, -size.y * 0.5f, 0),
            center + new Vector3(size.x * 0.5f, -size.y * 0.5f, 0),
            center + new Vector3(size.x * 0.5f, size.y * 0.5f, 0),
            center + new Vector3(-size.x * 0.5f, size.y * 0.5f, 0),
        };
        Handles.DrawSolidRectangleWithOutline(verts, fillColor, outlineColor);
#else
        DrawBounds(center, size, outlineColor);
#endif
    }
}

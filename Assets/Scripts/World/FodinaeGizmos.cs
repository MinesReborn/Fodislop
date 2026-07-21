#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Fodinae.Scripts.World
{
    /// <summary>
    /// Utility class to draw consistent and pretty Gizmos in the Editor.
    /// </summary>
    public static class FodinaeGizmos
    {
        public static void DrawCircle(Vector3 center, float radius, Color color, float thickness = 2f)
        {
            Handles.color = color;
            Handles.DrawWireDisc(center, Vector3.forward, radius, thickness);
        }

        public static void DrawLabel(Vector3 position, string text, Color color)
        {
            GUIStyle style = new GUIStyle();
            style.normal.textColor = color;
            style.fontSize = 12;
            style.fontStyle = FontStyle.Bold;
            style.alignment = TextAnchor.MiddleCenter;

            Handles.Label(position, text, style);
        }

        public static void DrawLine(Vector3 start, Vector3 end, Color color, float thickness = 1f)
        {
            Handles.color = color;
            Handles.DrawLine(start, end, thickness);
        }

        public static void DrawBounds(Vector3 center, Vector2 size, Color color)
        {
            Gizmos.color = color;
            Gizmos.DrawWireCube(center, new Vector3(size.x, size.y, 0.1f));
        }

        public static void DrawGrid(Vector3 origin, int width, int height, float cellSize, Color color)
        {
            Handles.color = color;
            for (int i = 0; i <= width; i++)
            {
                Handles.DrawLine(origin + new Vector3(i * cellSize, 0, 0), origin + new Vector3(i * cellSize, height * cellSize, 0));
            }

            for (int j = 0; j <= height; j++)
            {
                Handles.DrawLine(origin + new Vector3(0, j * cellSize, 0), origin + new Vector3(width * cellSize, j * cellSize, 0));
            }
        }

        public static void DrawArrow(Vector3 pos, Vector3 direction, Color color, float length = 1f, float arrowHeadLength = 0.25f, float arrowHeadAngle = 20f)
        {
            Handles.color = color;
            Vector3 end = pos + (direction * length);
            Handles.DrawLine(pos, end);

            Vector3 right = Quaternion.LookRotation(direction, Vector3.forward) * Quaternion.Euler(0, 180 + arrowHeadAngle, 0) * Vector3.forward;
            Vector3 left = Quaternion.LookRotation(direction, Vector3.forward) * Quaternion.Euler(0, 180 - arrowHeadAngle, 0) * Vector3.forward;
            Handles.DrawLine(end, end + (right * arrowHeadLength));
            Handles.DrawLine(end, end + (left * arrowHeadLength));
        }

        public static void DrawDottedLine(Vector3 start, Vector3 end, Color color, float dashSize = 2f)
        {
            Handles.color = color;
            Handles.DrawDottedLine(start, end, dashSize);
        }

        public static void DrawSolidRect(Vector3 center, Vector2 size, Color fillColor, Color outlineColor)
        {
            Vector3[] verts = new Vector3[]
            {
                center + new Vector3(-size.x * 0.5f, -size.y * 0.5f, 0),
                center + new Vector3(size.x * 0.5f, -size.y * 0.5f, 0),
                center + new Vector3(size.x * 0.5f, size.y * 0.5f, 0),
                center + new Vector3(-size.x * 0.5f, size.y * 0.5f, 0),
            };
            Handles.DrawSolidRectangleWithOutline(verts, fillColor, outlineColor);
        }
    }
}
#endif

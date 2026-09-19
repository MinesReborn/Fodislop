#nullable enable

using System;
using Kern.Tools.Imgui;
using UnityEngine;

namespace Kern.Rendering.PostProcessing.Workbench;

internal static class GradingPrimaryWheelDrawer
{
    private const float WheelSize = 128f;
    private static readonly GUILayoutOption[] _WheelOptions =
    [
        GUILayout.Width(WheelSize),
        GUILayout.Height(WheelSize),
    ];

    private static readonly float[] _ChannelCos = [1f, -0.5f, -0.5f];
    private static readonly float[] _ChannelSin = [0f, 0.8660254f, -0.8660254f];
    private static Texture2D? _wheelTexture;

    public static void DrawPrimaryWheel(
        string title,
        ref Vector3 value,
        Vector3 neutral,
        float minimum,
        float maximum,
        string controlID)
    {
        GUILayout.Label(title, ToolTheme.SectionLabel);
        Rect rect = GUILayoutUtility.GetRect(WheelSize, WheelSize, _WheelOptions);

        float side = Mathf.Min(rect.width, rect.height);
        rect = new Rect(rect.x + (rect.width - side) * 0.5f, rect.y, side, side);
        float wheelRadius = side * 0.5f;
        float amplitude = (maximum - minimum) * 0.25f;

        int id = GUIUtility.GetControlID(controlID.GetHashCode(), FocusType.Passive, rect);
        Event current = Event.current;
        switch (current.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (GUI.enabled && current.button == 0 && IsInsideWheel(rect, current.mousePosition))
                {
                    if (current.clickCount == 2)
                    {
                        value = neutral + Vector3.one * ChannelMean(value - neutral);
                    }
                    else
                    {
                        GUIUtility.hotControl = id;
                        value = WheelValue(rect, current.mousePosition, value, neutral, amplitude, minimum, maximum);
                    }

                    GUI.changed = true;
                    current.Use();
                }

                break;

            case EventType.MouseDrag:
                if (GUIUtility.hotControl == id)
                {
                    value = WheelValue(rect, current.mousePosition, value, neutral, amplitude, minimum, maximum);
                    GUI.changed = true;
                    current.Use();
                }

                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl == id)
                {
                    GUIUtility.hotControl = 0;
                    current.Use();
                }

                break;

            case EventType.Repaint:
                EnsureWheelTexture();
                if (_wheelTexture != null)
                {
                    GUI.DrawTexture(rect, _wheelTexture, ScaleMode.StretchToFill, true);
                }

                DrawWheelMarker(rect, WheelPosition(value - neutral, amplitude) * wheelRadius, GUIUtility.hotControl == id);
                break;

            default:
                break;
        }

        GUILayout.Label("центр = нейтраль · направление = оттенок · радиус = сила · двойной клик = сброс", ToolTheme.MutedLabel);
    }

    public static void ReleaseWheelTexture()
    {
        if (_wheelTexture == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(_wheelTexture);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(_wheelTexture);
        }

        _wheelTexture = null;
    }

    private static bool IsInsideWheel(Rect rect, Vector2 mouse) =>
        (mouse - rect.center).sqrMagnitude <= rect.width * rect.width * 0.25f;

    private static float ChannelMean(Vector3 value) => (value.x + value.y + value.z) / 3f;

    private static Vector3 WheelValue(
        Rect rect,
        Vector2 mouse,
        Vector3 current,
        Vector3 neutral,
        float amplitude,
        float minimum,
        float maximum)
    {
        Vector2 centered = (mouse - rect.center) / (rect.width * 0.5f);
        centered.y = -centered.y;
        float radius = Mathf.Clamp01(centered.magnitude);
        float angle = Mathf.Atan2(centered.y, centered.x);
        float cos = Mathf.Cos(angle) * radius * amplitude;
        float sin = Mathf.Sin(angle) * radius * amplitude;

        float mean = ChannelMean(current - neutral);
        return new Vector3(
            Mathf.Clamp(neutral.x + mean + cos * _ChannelCos[0] + sin * _ChannelSin[0], minimum, maximum),
            Mathf.Clamp(neutral.y + mean + cos * _ChannelCos[1] + sin * _ChannelSin[1], minimum, maximum),
            Mathf.Clamp(neutral.z + mean + cos * _ChannelCos[2] + sin * _ChannelSin[2], minimum, maximum));
    }

    private static Vector2 WheelPosition(Vector3 offset, float amplitude)
    {
        if (amplitude <= 0f)
        {
            return Vector2.zero;
        }

        float x = (offset.x * _ChannelCos[0] + offset.y * _ChannelCos[1] + offset.z * _ChannelCos[2]) * (2f / 3f);
        float y = (offset.x * _ChannelSin[0] + offset.y * _ChannelSin[1] + offset.z * _ChannelSin[2]) * (2f / 3f);
        Vector2 position = new Vector2(x, -y) / amplitude;
        return position.sqrMagnitude > 1f ? position.normalized : position;
    }

    private static void DrawWheelMarker(Rect rect, Vector2 offset, bool active)
    {
        Color previous = GUI.color;
        Vector2 center = rect.center + offset;
        float outer = active ? 10f : 8f;
        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(center.x - outer * 0.5f, center.y - outer * 0.5f, outer, outer), Texture2D.whiteTexture);
        GUI.color = active ? Color.yellow : Color.white;
        float inner = outer - 4f;
        GUI.DrawTexture(new Rect(center.x - inner * 0.5f, center.y - inner * 0.5f, inner, inner), Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private static void EnsureWheelTexture()
    {
        if (_wheelTexture != null)
        {
            return;
        }

        const int size = 128;
        _wheelTexture = Kern.RuntimeTextureFactory.CreateRGBA32NoMip(
            size,
            size,
            "Kern.PrimaryColorWheel",
            Kern.RuntimeTextureColorSpace.Linear,
            FilterMode.Bilinear,
            TextureWrapMode.Clamp);
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 centered = new(
                    (x + 0.5f) / size * 2f - 1f,
                    (y + 0.5f) / size * 2f - 1f);
                float radius = centered.magnitude;
                if (radius > 1f)
                {
                    pixels[y * size + x] = Color.clear;
                    continue;
                }

                float hue = Mathf.Repeat(Mathf.Atan2(centered.y, centered.x) / (Mathf.PI * 2f), 1f);
                Color color = Color.HSVToRGB(hue, radius, 1f);
                color.a = 1f;
                pixels[y * size + x] = color;
            }
        }

        _wheelTexture.SetPixels(pixels);
        _wheelTexture.Apply(false, true);
    }
}

#nullable enable

using System;
using Kern.Tools.Imgui;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Kern.Rendering.PostProcessing;

public static class ColorGradeScreenSampler
{
    private static Action<Color>? _callback;

    public static bool IsArmed => _callback != null;

    public static void Arm(Action<Color> callback)
    {
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
    }

    public static void Cancel() => _callback = null;

    public static void Tick()
    {
        Mouse? mouse = Mouse.current;
        if (_callback == null || mouse == null || !mouse.leftButton.wasPressedThisFrame)
        {
            return;
        }

        Vector2 mousePosition = mouse.position.ReadValue();
        if (ToolWindows.ContainsScreenPoint(mousePosition))
        {
            return;
        }

        Action<Color> callback = _callback;
        _callback = null;
        Texture2D? capture = null;
        try
        {
            capture = ScreenCapture.CaptureScreenshotAsTexture();
            if (capture == null)
            {
                return;
            }

            int x = Mathf.Clamp(Mathf.RoundToInt(mousePosition.x), 0, capture.width - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(mousePosition.y), 0, capture.height - 1);
            callback(capture.GetPixel(x, y));
        }
        finally
        {
            if (capture != null)
            {
                UnityEngine.Object.Destroy(capture);
            }
        }
    }
}

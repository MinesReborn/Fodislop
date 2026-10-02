#nullable enable

using System.Collections.Generic;
using Kern.Core;
using UnityEngine;

namespace Kern.Tools.ImGui;

public static class ToolWindows
{
    private const int FirstWindowId = 0x7700;
    private const float ScreenMargin = 8f;

    private static readonly List<ToolWindow> s_windows = [];
    private static readonly Dictionary<ToolWindow, bool> s_pendingVisibility = [];
    private static int s_nextId = FirstWindowId;
    private static bool s_enabled;
    private static bool s_keyboardCaptured;
    private static bool s_pointerCaptured;
    private static bool s_layoutResetRequested;
    private static bool s_releaseCaptureRequested;
    private static ToolWindow? s_focusedWindow;
    private static ToolWindow? s_pendingFocus;
    private static bool s_layoutDirty;
    private static float s_nextLayoutSaveTime;
    private static float? s_pendingScale;

    public static int SessionGeneration { get; private set; }

    public static float Scale => ToolLayoutStore.Scale > 0f
        ? ToolLayoutStore.Scale
        : UIScaleUtility.IsRetinaOrHighDpi ? 2f : 1f;

    public static void RequestScale(float scale) => s_pendingScale = scale;

    public static bool Enabled
    {
        get => s_enabled;
        set
        {
            s_enabled = value;
            if (!value)
            {
                ReleaseInputCapture();
            }
        }
    }

    public static IReadOnlyList<ToolWindow> All => s_windows;

    public static bool HasKeyboardCapture => Enabled && s_keyboardCaptured;

    public static bool HasPointerCapture => Enabled && s_pointerCaptured;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlaySession()
    {
        foreach (ToolWindow window in s_windows)
        {
            window.ResetForPlaySession();
            window.Dispose();
        }

        s_windows.Clear();
        s_pendingVisibility.Clear();
        ToolTheme.Reset();
        s_nextId = FirstWindowId;
        s_enabled = false;
        s_keyboardCaptured = false;
        s_pointerCaptured = false;
        s_layoutResetRequested = false;
        s_releaseCaptureRequested = true;
        s_focusedWindow = null;
        s_pendingFocus = null;
        s_pendingScale = null;
        SessionGeneration = unchecked(SessionGeneration + 1);
    }

    public static void Register(ToolWindow window)
    {
        if (s_windows.Contains(window))
        {
            return;
        }

        window.CaptureInitialState();
        window.Id = s_nextId++;
        s_windows.Add(window);
        ToolLayoutStore.Load(window);
    }

    public static bool IsRegistered(ToolWindow window) => s_windows.Contains(window);

    public static void Unregister(ToolWindow window)
    {
        s_pendingVisibility.Remove(window);
        if (ReferenceEquals(s_focusedWindow, window))
        {
            s_focusedWindow = null;
        }

        if (ReferenceEquals(s_pendingFocus, window))
        {
            s_pendingFocus = null;
        }

        if (s_windows.Remove(window))
        {
            // The removed window may own a text field or slider hot control.
            // Keeping that invisible control alive blocks gameplay input.
            ReleaseInputCapture();
        }
    }

    public static void RequestVisibility(ToolWindow window, bool visible)
    {
        s_pendingVisibility[window] = visible;
        s_layoutDirty = true;
        if (!visible)
        {
            if (ReferenceEquals(s_focusedWindow, window))
            {
                s_focusedWindow = null;
            }

            ReleaseInputCapture();
        }
    }

    public static void RequestFocus(ToolWindow window)
    {
        if (s_windows.Contains(window))
        {
            s_pendingFocus = window;
        }
    }

    public static bool IsFocused(ToolWindow window) =>
        Enabled && ReferenceEquals(s_focusedWindow, window);

    internal static void NotifyWindowFocused(ToolWindow window)
    {
        s_focusedWindow = window;
        GUI.FocusWindow(window.Id);
    }

    public static bool ContainsScreenPoint(Vector2 screenPoint)
    {
        if (!Enabled)
        {
            return false;
        }

        float scale = Scale;
        Vector2 guiPoint = new(screenPoint.x / scale, (Screen.height - screenPoint.y) / scale);
        foreach (ToolWindow window in s_windows)
        {
            if (window.Visible && window.Rect.Contains(guiPoint))
            {
                return true;
            }
        }

        return false;
    }

    public static bool AnySampling
    {
        get
        {
            if (!Enabled)
            {
                return false;
            }

            foreach (ToolWindow window in s_windows)
            {
                if (window.WantsSampling)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public static void Tick()
    {
        if (!Enabled)
        {
            return;
        }

        foreach (ToolWindow window in s_windows)
        {
            window.TickMeasured();
        }
    }

    public static void ReleaseInputCapture()
    {
        s_keyboardCaptured = false;
        s_pointerCaptured = false;
        s_releaseCaptureRequested = true;
    }

    public static void ResetLayout()
    {
        s_layoutResetRequested = true;
    }

    internal static void NotifyLayoutChanged()
    {
        s_layoutDirty = true;
    }

    public static void SaveLayout(bool immediate = false)
    {
        if (!s_layoutDirty && !immediate)
        {
            return;
        }

        if (!immediate && Time.unscaledTime < s_nextLayoutSaveTime)
        {
            return;
        }

        foreach (ToolWindow window in s_windows)
        {
            ToolLayoutStore.Save(window);
        }

        if (immediate)
        {
            ToolLayoutStore.Flush();
        }

        s_layoutDirty = false;
        s_nextLayoutSaveTime = Time.unscaledTime + 1f;
    }

    public static void Draw()
    {
        if (!Enabled)
        {
            return;
        }

        // Keep Layout and Repaint on the same coordinate system.
        if (Event.current.type == EventType.Layout && s_pendingScale.HasValue)
        {
            ToolLayoutStore.Scale = s_pendingScale.Value;
            s_pendingScale = null;
            NotifyLayoutChanged();
        }

        float scale = Scale;
        if (Event.current.type == EventType.Layout && s_layoutResetRequested)
        {
            s_layoutResetRequested = false;
            foreach (ToolWindow window in s_windows)
            {
                window.ResetPosition();
                window.Rect = ConstrainToScreen(window, window.Rect, scale);
                ToolLayoutStore.Discard(window);
            }

            ToolLayoutStore.Flush();
            s_layoutDirty = false;
        }

        if (Event.current.type == EventType.Layout && s_pendingVisibility.Count > 0)
        {
            foreach ((ToolWindow window, bool visible) in s_pendingVisibility)
            {
                if (s_windows.Contains(window))
                {
                    window.Visible = visible;
                }
            }

            s_pendingVisibility.Clear();
        }

        if (s_releaseCaptureRequested)
        {
            s_releaseCaptureRequested = false;
            GUI.FocusControl(null);
            GUIUtility.hotControl = 0;
        }

        if (!Enabled)
        {
            s_keyboardCaptured = false;
            s_pointerCaptured = false;
            return;
        }

        Matrix4x4 previousMatrix = GUI.matrix;
        GUISkin previousSkin = GUI.skin;
        bool scaled = Mathf.Abs(scale - 1f) > 0.001f;
        GUI.skin = ToolTheme.ResolveSkin(previousSkin);
        if (scaled)
        {
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        }

        try
        {
            foreach (ToolWindow window in s_windows)
            {
                if (!window.Visible)
                {
                    continue;
                }

                Rect drawnRect = GUI.Window(
                    window.Id,
                    window.Rect,
                    window.DrawFunction,
                    GUIContent.none);
                drawnRect = window.ApplyPendingSize(drawnRect);
                if (!IsFinite(drawnRect))
                {
                    window.ResetPosition();
                    drawnRect = window.Rect;
                }

                Rect constrained = ConstrainToScreen(window, drawnRect, scale);
                if (constrained != window.Rect)
                {
                    s_layoutDirty = true;
                }

                window.Rect = constrained;
            }

            if (Event.current.type == EventType.Repaint)
            {
                SaveLayout();
            }

            if (Event.current.type == EventType.Layout && s_pendingFocus != null)
            {
                if (s_pendingFocus.Visible && s_windows.Contains(s_pendingFocus))
                {
                    s_focusedWindow = s_pendingFocus;
                    GUI.FocusWindow(s_pendingFocus.Id);
                }

                s_pendingFocus = null;
            }
        }
        finally
        {
            GUI.matrix = previousMatrix;
            GUI.skin = previousSkin;
        }

        s_keyboardCaptured = GUIUtility.keyboardControl != 0;
        s_pointerCaptured = GUIUtility.hotControl != 0;
    }

    private static Rect ConstrainToScreen(ToolWindow window, Rect rect, float scale = 1f)
    {
        float availableWidth = Mathf.Max(1f, (Screen.width / scale) - ScreenMargin * 2f);
        float availableHeight = Mathf.Max(1f, (Screen.height / scale) - ScreenMargin * 2f);
        float minimumWidth = Mathf.Min(window.MinimumSize.x, availableWidth);

        // Свёрнутому окну нижняя граница не по содержимому, а по полосе
        // заголовка: иначе общее правило тут же разворачивало бы его обратно.
        float minimumHeight = window.Collapsed
            ? Mathf.Min(ToolWindow.CollapsedHeight, availableHeight)
            : Mathf.Min(window.MinimumSize.y, availableHeight);
        rect.width = Mathf.Clamp(rect.width, minimumWidth, availableWidth);
        rect.height = Mathf.Clamp(rect.height, minimumHeight, availableHeight);
        rect.x = Mathf.Clamp(rect.x, ScreenMargin, Mathf.Max(ScreenMargin, (Screen.width / scale) - rect.width - ScreenMargin));
        rect.y = Mathf.Clamp(rect.y, ScreenMargin, Mathf.Max(ScreenMargin, (Screen.height / scale) - rect.height - ScreenMargin));
        return rect;
    }

    private static bool IsFinite(Rect rect) =>
        IsFinite(rect.x) &&
        IsFinite(rect.y) &&
        IsFinite(rect.width) &&
        IsFinite(rect.height);

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

#nullable enable

using System.Collections.Generic;
using Kern.Core;
using UnityEngine;

namespace Kern.Tools.Imgui;

public static class ToolWindows
{
    private const int FirstWindowID = 0x7700;
    private const float ScreenMargin = 8f;

    private static readonly List<ToolWindow> _Windows = [];
    private static readonly Dictionary<ToolWindow, bool> _PendingVisibility = [];
    private static int _nextID = FirstWindowID;
    private static bool _enabled;
    private static bool _keyboardCaptured;
    private static bool _pointerCaptured;
    private static bool _layoutResetRequested;
    private static bool _releaseCaptureRequested;
    private static ToolWindow? _focusedWindow;
    private static ToolWindow? _pendingFocus;
    private static bool _layoutDirty;
    private static float _nextLayoutSaveTime;
    private static float? _pendingScale;

    public static int SessionGeneration { get; private set; }

    public static float Scale => ToolLayoutStore.Scale > 0f
        ? ToolLayoutStore.Scale
        : UIScaleUtility.IsRetinaOrHighDpi ? 2f : 1f;

    public static void RequestScale(float scale) => _pendingScale = scale;

    public static bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!value)
            {
                ReleaseInputCapture();
            }
        }
    }

    public static IReadOnlyList<ToolWindow> All => _Windows;

    public static bool HasKeyboardCapture => Enabled && _keyboardCaptured;

    public static bool HasPointerCapture => Enabled && _pointerCaptured;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlaySession()
    {
        foreach (ToolWindow window in _Windows)
        {
            window.ResetForPlaySession();
            window.Dispose();
        }

        _Windows.Clear();
        _PendingVisibility.Clear();
        ToolTheme.Reset();
        _nextID = FirstWindowID;
        _enabled = false;
        _keyboardCaptured = false;
        _pointerCaptured = false;
        _layoutResetRequested = false;
        _releaseCaptureRequested = true;
        _focusedWindow = null;
        _pendingFocus = null;
        _pendingScale = null;
        SessionGeneration = unchecked(SessionGeneration + 1);
    }

    public static void Register(ToolWindow window)
    {
        if (_Windows.Contains(window))
        {
            return;
        }

        window.CaptureInitialState();
        window.ID = _nextID++;
        _Windows.Add(window);
        ToolLayoutStore.Load(window);
    }

    public static bool IsRegistered(ToolWindow window) => _Windows.Contains(window);

    public static void Unregister(ToolWindow window)
    {
        _PendingVisibility.Remove(window);
        if (ReferenceEquals(_focusedWindow, window))
        {
            _focusedWindow = null;
        }

        if (ReferenceEquals(_pendingFocus, window))
        {
            _pendingFocus = null;
        }

        if (_Windows.Remove(window))
        {
            // The removed window may own a text field or slider hot control.
            // Keeping that invisible control alive blocks gameplay input.
            ReleaseInputCapture();
        }
    }

    public static void RequestVisibility(ToolWindow window, bool visible)
    {
        _PendingVisibility[window] = visible;
        _layoutDirty = true;
        if (!visible)
        {
            if (ReferenceEquals(_focusedWindow, window))
            {
                _focusedWindow = null;
            }

            ReleaseInputCapture();
        }
    }

    public static void RequestFocus(ToolWindow window)
    {
        if (_Windows.Contains(window))
        {
            _pendingFocus = window;
        }
    }

    public static bool IsFocused(ToolWindow window) =>
        Enabled && ReferenceEquals(_focusedWindow, window);

    internal static void NotifyWindowFocused(ToolWindow window)
    {
        _focusedWindow = window;
        GUI.FocusWindow(window.ID);
    }

    public static bool ContainsScreenPoint(Vector2 screenPoint)
    {
        if (!Enabled)
        {
            return false;
        }

        float scale = Scale;
        Vector2 guiPoint = new(screenPoint.x / scale, (Screen.height - screenPoint.y) / scale);
        foreach (ToolWindow window in _Windows)
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

            foreach (ToolWindow window in _Windows)
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

        foreach (ToolWindow window in _Windows)
        {
            window.TickMeasured();
        }
    }

    public static void ReleaseInputCapture()
    {
        _keyboardCaptured = false;
        _pointerCaptured = false;
        _releaseCaptureRequested = true;
    }

    public static void ResetLayout()
    {
        _layoutResetRequested = true;
    }

    internal static void NotifyLayoutChanged()
    {
        _layoutDirty = true;
    }

    public static void SaveLayout(bool immediate = false)
    {
        if (!_layoutDirty && !immediate)
        {
            return;
        }

        if (!immediate && Time.unscaledTime < _nextLayoutSaveTime)
        {
            return;
        }

        foreach (ToolWindow window in _Windows)
        {
            ToolLayoutStore.Save(window);
        }

        if (immediate)
        {
            ToolLayoutStore.Flush();
        }

        _layoutDirty = false;
        _nextLayoutSaveTime = Time.unscaledTime + 1f;
    }

    public static void Draw()
    {
        if (!Enabled)
        {
            return;
        }

        // Keep Layout and Repaint on the same coordinate system.
        if (Event.current.type == EventType.Layout && _pendingScale.HasValue)
        {
            ToolLayoutStore.Scale = _pendingScale.Value;
            _pendingScale = null;
            NotifyLayoutChanged();
        }

        float scale = Scale;
        if (Event.current.type == EventType.Layout && _layoutResetRequested)
        {
            _layoutResetRequested = false;
            foreach (ToolWindow window in _Windows)
            {
                window.ResetPosition();
                window.Rect = ConstrainToScreen(window, window.Rect, scale);
                ToolLayoutStore.Discard(window);
            }

            ToolLayoutStore.Flush();
            _layoutDirty = false;
        }

        if (Event.current.type == EventType.Layout && _PendingVisibility.Count > 0)
        {
            foreach ((ToolWindow window, bool visible) in _PendingVisibility)
            {
                if (_Windows.Contains(window))
                {
                    window.Visible = visible;
                }
            }

            _PendingVisibility.Clear();
        }

        if (_releaseCaptureRequested)
        {
            _releaseCaptureRequested = false;
            GUI.FocusControl(null);
            GUIUtility.hotControl = 0;
        }

        if (!Enabled)
        {
            _keyboardCaptured = false;
            _pointerCaptured = false;
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
            foreach (ToolWindow window in _Windows)
            {
                if (!window.Visible)
                {
                    continue;
                }

                Rect drawnRect = GUI.Window(
                    window.ID,
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
                    _layoutDirty = true;
                }

                window.Rect = constrained;
            }

            if (Event.current.type == EventType.Repaint)
            {
                SaveLayout();
            }

            if (Event.current.type == EventType.Layout && _pendingFocus != null)
            {
                if (_pendingFocus.Visible && _Windows.Contains(_pendingFocus))
                {
                    _focusedWindow = _pendingFocus;
                    GUI.FocusWindow(_pendingFocus.ID);
                }

                _pendingFocus = null;
            }
        }
        finally
        {
            GUI.matrix = previousMatrix;
            GUI.skin = previousSkin;
        }

        _keyboardCaptured = GUIUtility.keyboardControl != 0;
        _pointerCaptured = GUIUtility.hotControl != 0;
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

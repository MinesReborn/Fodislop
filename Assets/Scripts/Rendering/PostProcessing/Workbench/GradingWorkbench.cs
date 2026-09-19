#nullable enable

using System;
using System.Collections.Generic;
using Kern.Rendering.PostProcessing.Scopes;
using Kern.Tools.Imgui;
using Kern.Tools.Imgui.Windows;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Kern.Rendering.PostProcessing.Workbench;

public sealed class GradingWorkbench : IDisposable
{
    private readonly ColorGradeState _state = new();
    private readonly ColorGradeZones _zones = new();
    private readonly GradingZonesWindow _zonesWindow;
    private readonly GradingLayersWindow _layersWindow;
    private readonly GradingScopesWindow _scopesWindow = new();
    private readonly GradingQualifierWindow _qualifierWindow;
    private readonly List<ToolWindow> _hiddenForWorkspace = [];

    private bool _registered;
    private bool _loaded;
    private bool _wasApplying;
    private bool _disposed;
    private bool _workspaceActive;
    private bool _scopesWereVisible;
    private int _sessionGeneration = -1;

    public GradingWorkbench()
    {
        _zones.Enabled = false;
        _layersWindow = new GradingLayersWindow(_state, _zones);
        _zonesWindow = new GradingZonesWindow(_state, _zones);
        _qualifierWindow = new GradingQualifierWindow(_state);
    }

    public ColorGradeZones Zones => _zones;

    public ColorGradeState State => _state;

    public bool IsApplying => ToolWindows.Enabled &&
        (_layersWindow.Visible || _qualifierWindow.Visible || _zonesWindow.Visible);

    public bool StoppedApplying { get; private set; }

    public void Tick()
    {
        if (_disposed)
        {
            return;
        }

        ColorGradeScreenSampler.Tick();
        Keyboard? keyboard = Keyboard.current;
        PostProcessRuntimeState.TemporaryBypass =
            ToolWindows.Enabled && keyboard != null && keyboard.backslashKey.isPressed;

        if (_sessionGeneration != ToolWindows.SessionGeneration)
        {
            ResetForPlaySession();
        }

        if (!_registered ||
            !ToolWindows.IsRegistered(_layersWindow) ||
            !ToolWindows.IsRegistered(_scopesWindow) ||
            !ToolWindows.IsRegistered(_zonesWindow) ||
            !ToolWindows.IsRegistered(_qualifierWindow))
        {
            _registered = true;
            ToolWindows.Register(_layersWindow);
            ToolWindows.Register(_scopesWindow);
            ToolWindows.Register(_zonesWindow);
            ToolWindows.Register(_qualifierWindow);
        }

        HandleWorkspaceShortcut();

        if (_layersWindow.Visible || _scopesWindow.Visible || _zonesWindow.Visible || _qualifierWindow.Visible)
        {
            LoadOnce();
        }

        bool workspaceActive = ToolWindows.Enabled &&
            (_layersWindow.Visible || _scopesWindow.Visible || _zonesWindow.Visible || _qualifierWindow.Visible);
        if (workspaceActive && !_workspaceActive)
        {
            EnterWorkspace();
        }
        else if (!workspaceActive && _workspaceActive)
        {
            ExitWorkspace();
        }

        bool scopesVisible = ToolWindows.Enabled && _scopesWindow.Visible;
        if (!scopesVisible && _scopesWereVisible)
        {
            ResetPreviewTools();
        }

        _scopesWereVisible = scopesVisible;
        ScopesRenderPass.Enabled = scopesVisible && _scopesWindow.ScopesRequested;
        HandleCompareDrag(scopesVisible);

        bool applying = IsApplying;
        _zonesWindow.CaptureEnabled = applying;
        StoppedApplying = !applying && _wasApplying;
        if (StoppedApplying)
        {
            ResetPreviewTools();
        }

        _wasApplying = applying;
    }

    // Шторку сравнения можно тянуть прямо по кадру. Ползунок в окне приборов
    // оставался единственным способом, и сама граница на экране не была видна.
    private const float CompareGrabDistancePixels = 18f;
    private bool _draggingCompareSplit;

    private void HandleCompareDrag(bool scopesVisible)
    {
        Mouse? mouse = Mouse.current;
        CompareMode mode = PostProcessRuntimeState.CompareMode;
        bool wipe = mode is CompareMode.VerticalWipe or CompareMode.HorizontalWipe;
        if (mouse == null || !scopesVisible || !wipe || Screen.width <= 0 || Screen.height <= 0)
        {
            _draggingCompareSplit = false;
            return;
        }

        Vector2 position = mouse.position.ReadValue();
        bool vertical = mode == CompareMode.VerticalWipe;

        // Позиция мыши — от левого нижнего угла, как и экранная координата
        // шейдера: доля ширины или высоты совпадает со _CompareSplit напрямую.
        float along = vertical ? position.x / Screen.width : position.y / Screen.height;
        float extent = vertical ? Screen.width : Screen.height;

        if (mouse.leftButton.wasPressedThisFrame &&
            !ToolWindows.ContainsScreenPoint(position) &&
            Mathf.Abs(along - PostProcessRuntimeState.CompareSplit) * extent <= CompareGrabDistancePixels)
        {
            _draggingCompareSplit = true;
        }

        if (!mouse.leftButton.isPressed)
        {
            _draggingCompareSplit = false;
        }

        if (_draggingCompareSplit)
        {
            PostProcessRuntimeState.CompareSplit = Mathf.Clamp01(along);
        }
    }

    private void ResetForPlaySession()
    {
        _sessionGeneration = ToolWindows.SessionGeneration;
        _loaded = false;
        _wasApplying = false;
        StoppedApplying = false;
        _workspaceActive = false;
        _scopesWereVisible = false;
        _zonesWindow.CaptureEnabled = false;
        _hiddenForWorkspace.Clear();
        _state.ResetToLook();
        _zones.Clear();
        _zones.Enabled = false;
        ResetPreviewTools();
    }

    private void LoadOnce()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        if (ColorGradeFile.TryLoad(_state, _zones))
        {
            Debug.Log($"[ColorGrade] Загружено из {ColorGradeFile.Path}");
        }
    }

    private void HandleWorkspaceShortcut()
    {
        Keyboard? keyboard = Keyboard.current;
        if (keyboard == null ||
            ToolWindows.HasKeyboardCapture ||
            !keyboard.f5Key.wasPressedThisFrame)
        {
            return;
        }

        bool open = !ToolWindows.Enabled || !_layersWindow.Visible;
        ToolWindows.Enabled = true;
        _layersWindow.Visible = open;
        if (open)
        {
            ToolWindows.RequestFocus(_layersWindow);
        }

        if (!open)
        {
            ToolWindows.ReleaseInputCapture();
        }
    }

    public void Deactivate()
    {
        ColorGradeScreenSampler.Cancel();
        _layersWindow.Visible = false;
        _scopesWindow.Visible = false;
        _zonesWindow.Visible = false;
        _qualifierWindow.Visible = false;
        _zonesWindow.CaptureEnabled = false;
        _scopesWereVisible = false;
        ExitWorkspace();
        StoppedApplying = _wasApplying;
        _wasApplying = false;
        ToolWindows.ReleaseInputCapture();
        ResetPreviewTools();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Deactivate();
        if (_registered)
        {
            ToolWindows.Unregister(_layersWindow);
            ToolWindows.Unregister(_scopesWindow);
            ToolWindows.Unregister(_zonesWindow);
            ToolWindows.Unregister(_qualifierWindow);
            _layersWindow.Dispose();
            _scopesWindow.Dispose();
            _zonesWindow.Dispose();
            _qualifierWindow.Dispose();
            _registered = false;
        }
    }

    private static void ResetPreviewTools()
    {
        PostProcessRuntimeState.DebugView = PostProcessDebugView.None;
        PostProcessRuntimeState.CompareSplit = 0f;
        PostProcessRuntimeState.CompareMode = CompareMode.Off;
        PostProcessRuntimeState.CompareBefore = false;
        ScopesRenderPass.SourceMode = ScopesSourceMode.After;
        ScopesRenderPass.WaveformMode = ScopeWaveformMode.Overlay;
        ScopesRenderPass.Enabled = false;
    }

    private void EnterWorkspace()
    {
        _workspaceActive = true;
        _hiddenForWorkspace.Clear();
        foreach (ToolWindow window in ToolWindows.All)
        {
            if (ReferenceEquals(window, _layersWindow) ||
                ReferenceEquals(window, _scopesWindow) ||
                ReferenceEquals(window, _zonesWindow) ||
                ReferenceEquals(window, _qualifierWindow) ||
                window is ToolbarWindow ||
                !window.Visible)
            {
                continue;
            }

            window.Visible = false;
            _hiddenForWorkspace.Add(window);
        }
    }

    private void ExitWorkspace()
    {
        if (!_workspaceActive)
        {
            return;
        }

        _workspaceActive = false;
        foreach (ToolWindow window in _hiddenForWorkspace)
        {
            window.Visible = true;
        }

        _hiddenForWorkspace.Clear();
    }
}

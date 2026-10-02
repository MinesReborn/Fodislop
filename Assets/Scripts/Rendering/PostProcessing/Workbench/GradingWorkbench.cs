#nullable enable

using System;
using System.Collections.Generic;
using Kern.Rendering.PostProcessing.Scopes;
using Kern.Tools.ImGui;
using Kern.Tools.ImGui.Windows;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Kern.Rendering.PostProcessing.Workbench;

public sealed class GradingWorkbench : IDisposable
{
    private readonly GradingLUTWindow _lutWindow = new();
    private readonly GradingScopesWindow _scopesWindow = new();
    private readonly List<ToolWindow> _hiddenForWorkspace = [];

    private bool _registered;
    private bool _disposed;
    private bool _workspaceActive;
    private bool _scopesWereVisible;
    private int _sessionGeneration = -1;

    public void Tick()
    {
        if (_disposed)
        {
            return;
        }

        Keyboard? keyboard = Keyboard.current;
        PostProcessRuntimeState.TemporaryBypass =
            ToolWindows.Enabled && keyboard != null && keyboard.backslashKey.isPressed;

        if (_sessionGeneration != ToolWindows.SessionGeneration)
        {
            ResetForPlaySession();
        }

        if (!_registered ||
            !ToolWindows.IsRegistered(_lutWindow) ||
            !ToolWindows.IsRegistered(_scopesWindow))
        {
            _registered = true;
            ToolWindows.Register(_lutWindow);
            ToolWindows.Register(_scopesWindow);
        }

        HandleWorkspaceShortcut();

        bool workspaceActive = ToolWindows.Enabled &&
            (_lutWindow.Visible || _scopesWindow.Visible);
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
        _workspaceActive = false;
        _scopesWereVisible = false;
        _hiddenForWorkspace.Clear();
        ResetPreviewTools();
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

        bool open = !ToolWindows.Enabled || !_lutWindow.Visible;
        ToolWindows.Enabled = true;
        _lutWindow.Visible = open;
        if (open)
        {
            ToolWindows.RequestFocus(_lutWindow);
        }

        if (!open)
        {
            ToolWindows.ReleaseInputCapture();
        }
    }

    public void Deactivate()
    {
        _lutWindow.Visible = false;
        _scopesWindow.Visible = false;
        _scopesWereVisible = false;
        ExitWorkspace();
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
            ToolWindows.Unregister(_lutWindow);
            ToolWindows.Unregister(_scopesWindow);
            _lutWindow.Dispose();
            _scopesWindow.Dispose();
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
            if (ReferenceEquals(window, _lutWindow) ||
                ReferenceEquals(window, _scopesWindow) ||
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

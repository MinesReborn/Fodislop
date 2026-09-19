#nullable enable

using Kern.Core.Interfaces;
using Kern.Tools.Imgui;
using UnityEngine.InputSystem;

namespace Kern.UI;

public sealed class InputBlockState : IInputBlocker
{
    private readonly ServerWindowPresenter _windows;
    private readonly MapModeState _mapMode;
    private readonly UIInputManager _uiInput;

    public InputBlockState(
        ServerWindowPresenter windows,
        MapModeState mapMode,
        UIInputManager uiInput)
    {
        _windows = windows;
        _mapMode = mapMode;
        _uiInput = uiInput;
    }

    public bool IsInputBlocked =>
        _uiInput.IsInputBlocked ||
        _windows.HasOpenWindows ||
        _windows.IsModalShowing ||
        _mapMode.IsOpen ||
        IsToolInputCaptured();

    public string? TopWindowTag => _windows.TopWindowTag;

    private static bool IsToolInputCaptured()
    {
        if (ToolWindows.HasKeyboardCapture || ToolWindows.HasPointerCapture)
        {
            return true;
        }

        Pointer? pointer = Pointer.current;
        return pointer != null && ToolWindows.ContainsScreenPoint(pointer.position.ReadValue());
    }
}

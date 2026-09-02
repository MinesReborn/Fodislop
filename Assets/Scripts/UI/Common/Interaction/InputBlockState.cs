#nullable enable

using Fodinae.Core.Interfaces;
namespace Fodinae.UI;

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
        _mapMode.IsOpen;

    public string? TopWindowTag => _windows.TopWindowTag;
}

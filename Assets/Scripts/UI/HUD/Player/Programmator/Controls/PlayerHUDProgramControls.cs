#nullable enable

using System;
using Kern.Core.Localization;
using Kern.Networking.Processing;
using Kern.UI.Tooltips;
using Kern.UI.Programmator;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Programmator;
using UnityEngine.UIElements;

namespace Kern.UI.HUD;

internal sealed class PlayerHUDProgramControls : IDisposable
{
    private ProgrammatorProcessor? _protocol;
    private ProgrammatorGrid? _grid;
    private ILocalizationService? _localization;
    private Button? _programButton;
    private Button? _runButton;
    private bool _isRunning;

    public void Initialize(
        VisualElement tree,
        ProgrammatorGrid? grid,
        ProgrammatorProcessor protocol,
        ILocalizationService localization,
        Tooltip tooltip)
    {
        _grid = grid;
        _protocol = protocol;
        _localization = localization;
        _programButton = tree.Q<Button>("ProgrammatorButton") ??
            throw new InvalidOperationException("[PlayerHUD] ProgrammatorButton is missing from PlayerHUD.uxml.");
        _programButton.clicked += protocol.RequestProgramList;
        _runButton = tree.Q<Button>("ProgramRunButton") ??
            throw new InvalidOperationException("[PlayerHUD] ProgramRunButton is missing from PlayerHUD.uxml.");
        _runButton.clicked += HandleRunClicked;
        Tooltip.AttachTo(_runButton, () => localization.Get(
            _isRunning ? "hud.tooltip.program_stop" : "hud.tooltip.program_start"), tooltip);
        protocol.StateChanged += HandleStateChanged;
        if (grid?.Programs is { } programs)
        {
            programs.ActiveProgramChanged += UpdateProgramLabel;
            programs.RunStateChanged += ApplyRunState;
        }

        UpdateProgramLabel();
    }

    public void Dispose()
    {
        if (_grid?.Programs is { } programs)
        {
            programs.ActiveProgramChanged -= UpdateProgramLabel;
            programs.RunStateChanged -= ApplyRunState;
        }

        if (_protocol != null)
        {
            _protocol.StateChanged -= HandleStateChanged;
        }
    }

    private void UpdateProgramLabel()
    {
        if (_programButton == null || _localization == null)
        {
            return;
        }

        string? activeName = _grid?.Programs?.ActiveProgramName;
        _programButton.text = string.IsNullOrWhiteSpace(activeName)
            ? _localization.Get("hud.programmator")
            : activeName;
    }

    private void HandleRunClicked()
    {
        if (_isRunning)
        {
            _protocol?.StopProgram();
        }
        else
        {
            _protocol?.StartProgram();
        }
    }

    private void HandleStateChanged(ProgramStatePacket packet) =>
        ApplyRunState(packet.State is ProgramState.Running or ProgramState.Paused);

    private void ApplyRunState(bool isRunning)
    {
        _isRunning = isRunning;
        if (_runButton != null)
        {
            _runButton.text = isRunning ? "■" : "▶";
            _runButton.EnableInClassList("hud-program-run-btn--running", isRunning);
        }
    }
}

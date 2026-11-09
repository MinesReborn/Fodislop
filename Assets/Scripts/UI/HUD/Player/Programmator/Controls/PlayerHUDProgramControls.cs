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
    private Button? _programButton;
    private Label? _programLabel;
    private Label? _programName;
    private VisualElement? _runIcon;
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
        _programButton = tree.Q<Button>("ProgrammatorButton") ??
            throw new InvalidOperationException("[PlayerHUD] ProgrammatorButton is missing from PlayerHUD.uxml.");
        _programButton.clicked += protocol.RequestProgramList;
        _programLabel = tree.Q<Label>("ProgramLabel") ??
            throw new InvalidOperationException("[PlayerHUD] ProgramLabel is missing from PlayerHUD.uxml.");
        _programName = tree.Q<Label>("ProgramNameLabel") ??
            throw new InvalidOperationException("[PlayerHUD] ProgramNameLabel is missing from PlayerHUD.uxml.");
        _runButton = tree.Q<Button>("ProgramRunButton") ??
            throw new InvalidOperationException("[PlayerHUD] ProgramRunButton is missing from PlayerHUD.uxml.");
        _runButton.clicked += HandleRunClicked;
        _runIcon = _runButton.Q<VisualElement>(className: "ico");
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

    // Запущенная программа подписывает кнопку своим именем вместо «Программатор» — одна строка, высота не меняется
    // (макет: js/hud.js setProg)
    private void UpdateProgramLabel()
    {
        if (_programName == null || _programLabel == null)
        {
            return;
        }

        string? activeName = _grid?.Programs?.ActiveProgramName;
        bool named = _isRunning && !string.IsNullOrWhiteSpace(activeName);
        _programName.text = named ? activeName : string.Empty;
        _programName.EnableInClassList("is-hidden", !named);
        _programLabel.EnableInClassList("is-hidden", named);
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
            _runButton.EnableInClassList("is-running", isRunning);
            _runIcon?.EnableInClassList("ico--play", !isRunning);
            _runIcon?.EnableInClassList("ico--stop", isRunning);
        }

        UpdateProgramLabel();
    }
}

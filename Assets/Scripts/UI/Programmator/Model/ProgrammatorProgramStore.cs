#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Localization;
using Kern.Networking.Processors;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Programmator;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Programmator;

// Program storage, page navigation, and run/stop state for the programmator.
// Reads and writes ProgrammatorData directly (page/cell contents are global,
// shared with the rest of the programmator), and reaches into the shared
// selection model and the UI view for the handful of things it needs to
// repaint or clear when switching pages/programs.
internal sealed class ProgrammatorProgramStore
{
    private readonly ProgrammatorGridUIFactory _view;
    private readonly ProgrammatorSelectionModel _selection;
    private readonly ProgrammatorRadialController _radial;
    private readonly ILocalizationService _loc;
    private readonly ProgrammatorData _data;
    private readonly ProgrammatorProcessor _protocol;
    private readonly ProgrammatorProgramFile _file;

    private readonly List<ProgrammatorProgramItem> _programItems = new();
    private int _activeIndex = -1;
    private bool _isRunning;

    /// <summary>
    /// Локальное изменение состояния выполнения (Run/Stop/серверный пакет).
    /// HUD подписывается, чтобы кнопка ▶/■ менялась сразу, а окно
    /// программатора закрывалось при запуске.
    /// </summary>
    public event Action<bool>? RunStateChanged;

    public ProgrammatorProgramStore(
        ProgrammatorGridUIFactory view,
        ProgrammatorSelectionModel selection,
        ProgrammatorRadialController radial,
        ILocalizationService loc,
        ProgrammatorData data,
        ProgrammatorProcessor protocol)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _radial = radial ?? throw new ArgumentNullException(nameof(radial));
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
        _file = new ProgrammatorProgramFile(_loc);
        _protocol.ProgramUpdated += OnProgramUpdated;
        _protocol.StateChanged += OnStateChanged;
        _protocol.BreakpointHit += OnBreakpointHit;
        _protocol.MemoryReceived += OnMemoryReceived;
        _programItems.AddRange(_file.Load());
    }

    public bool IsRunning => _isRunning;

    public int ProgramCount => _programItems.Count;

    /// <summary>Активная (открытая в редакторе) программа сменилась или сбросилась.</summary>
    public event Action? ActiveProgramChanged;

    /// <summary>Имя активной программы или null, если открыт список программ.</summary>
    public string? ActiveProgramName =>
        (_activeIndex >= 0 && _activeIndex < _programItems.Count) ? _programItems[_activeIndex].Name : null;

    private void RaiseActiveProgramChanged() => ActiveProgramChanged?.Invoke();

    // Invoked (via ProgrammatorRadialController.OnLastCellPlaced) when an
    // operator is placed in the very last cell of the last page.
    public void AdvancePageIfAtEnd()
    {
        _data.AddPage();
        _view.UpdatePageLabel();
    }

    public void SaveProgram()
    {
        StoreActiveProgram();
        _file.Save(_programItems);
        if (TryBuildNetworkProgram(out List<(ProgAction Operator, string Label, string Value)> program))
        {
            _protocol.Save(_activeIndex, false, program, Array.Empty<int>());
        }

        Debug.Log("[Programmator] Programs saved");
    }

    private void StoreActiveProgram()
    {
        if (_activeIndex < 0 || _activeIndex >= _programItems.Count)
        {
            return;
        }

        ProgrammatorProgramItem item = _programItems[_activeIndex];
        item.Codes = new List<int>(_data.Codes);
        item.Labels = new List<string?>(_data.Labels);
        item.Values = new List<string?>(_data.Values);
    }

    public void PrevPage()
    {
        if (_data.CurrentPage > 0)
        {
            _selection.ClearSelection();
            _radial.HideMenus();
            _data.CurrentPage--;
            RefreshAllCells();
        }
    }

    public void NextPage()
    {
        if (_data.CurrentPage < _data.PageCount - 1)
        {
            _selection.ClearSelection();
            _radial.HideMenus();
            _data.CurrentPage++;
            RefreshAllCells();
        }
    }

    public void AddPageClick()
    {
        if (_data.PageCount >= 100)
        {
            return;
        }

        _data.AddPage();
        _view.UpdatePageLabel();
    }

    public void RemovePageClick()
    {
        if (_data.RemoveLastPage())
        {
            RefreshAllCells();
        }
    }

    public void ShowProgramList()
    {
        _selection.ClearSelection();
        _radial.HideAll();
        if (_isRunning)
        {
            StopProgram();
        }

        _view.ProgramTitle.text = _loc.Get("programmator.title");
        RefreshProgramList();
        _view.Panel.style.display = DisplayStyle.None;
        _view.ProgramListPanel.style.display = DisplayStyle.Flex;
        _activeIndex = -1;
        RaiseActiveProgramChanged();
    }

    public void OpenProgram(int index)
    {
        if (index < 0 || index >= _programItems.Count)
        {
            return;
        }

        var item = _programItems[index];
        _data.Codes = new List<int>(item.Codes);
        _data.Labels = new List<string?>(item.Labels);
        _data.Values = new List<string?>(item.Values);
        _activeIndex = index;
        _data.CurrentPage = 0;
        _view.ProgramTitle.text = item.Name;
        _view.ProgramListPanel.style.display = DisplayStyle.None;
        _view.Panel.style.display = DisplayStyle.Flex;
        RefreshAllCells();
        RaiseActiveProgramChanged();
    }

    public void CloseProgram()
    {
        if (_isRunning)
        {
            StopProgram();
        }

        StoreActiveProgram();
        _file.Save(_programItems);

        ShowProgramList();
    }

    public void CreateNewProgram(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            name = _loc.Get("programmator.program", _programItems.Count + 1);
        }

        var item = new ProgrammatorProgramItem
        {
            Name = name,
            Codes = new List<int>(new int[ProgrammatorData.CELLS_PER_PAGE]),
            Labels = new List<string?>(new string?[ProgrammatorData.CELLS_PER_PAGE]),
            Values = new List<string?>(new string?[ProgrammatorData.CELLS_PER_PAGE]),
        };
        _programItems.Add(item);
        _file.Save(_programItems);
        HideCreateInput();
        OpenProgram(_programItems.Count - 1);
    }

    public void ShowCreateInput()
    {
        _view.CreateInput.value = _loc.Get("programmator.program", _programItems.Count + 1);
        _view.CreateDialog.style.display = DisplayStyle.Flex;
        _view.CreateInput.Focus();
    }

    public void HideCreateInput()
    {
        _view.CreateDialog.style.display = DisplayStyle.None;
    }

    public void DeleteProgram(int index)
    {
        if (index < 0 || index >= _programItems.Count)
        {
            return;
        }

        _programItems.RemoveAt(index);
        _protocol.DeleteProgram();
        _file.Save(_programItems);
        RefreshProgramList();
    }

    public void RefreshProgramList()
    {
        _view.ListScroll.Clear();
        for (int i = 0; i < _programItems.Count; i++)
        {
            int idx = i;
            var item = _programItems[i];
            var row = new VisualElement();
            row.AddToClassList("prog-list-row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingTop = 6;
            row.style.paddingBottom = 6;
            row.style.paddingLeft = 8;
            row.style.paddingRight = 8;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(0.2f, 0.2f, 0.2f, 1f);
            var nameLabel = new Label(item.Name);
            nameLabel.AddToClassList("prog-list-name");
            nameLabel.style.flexGrow = 1;
            nameLabel.style.color = new Color(0.8f, 0.8f, 0.8f, 1f);
            nameLabel.style.fontSize = 14;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(nameLabel);

            var delBtn = new Button(() => DeleteProgram(idx));
            delBtn.text = "\u00d7";
            delBtn.AddToClassList("prog-del-btn");
            delBtn.style.width = 22;
            delBtn.style.height = 22;
            delBtn.style.backgroundColor = new Color(0.3f, 0f, 0f, 0.3f);
            delBtn.style.color = new Color(0.9f, 0.3f, 0.3f, 1f);
            delBtn.style.fontSize = 14;
            delBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            delBtn.style.borderTopWidth = 0;
            delBtn.style.borderBottomWidth = 0;
            delBtn.style.borderLeftWidth = 0;
            delBtn.style.borderRightWidth = 0;
            delBtn.style.paddingTop = 0;
            delBtn.style.paddingBottom = 0;
            delBtn.style.paddingLeft = 0;
            delBtn.style.paddingRight = 0;
            delBtn.style.marginLeft = 8;
            row.Add(delBtn);

            var renameBtn = new Button(_protocol.RenameProgram);
            renameBtn.text = "✎";
            renameBtn.AddToClassList("prog-rename-btn");
            renameBtn.style.width = 22;
            renameBtn.style.height = 22;
            renameBtn.style.marginLeft = 4;
            renameBtn.style.paddingTop = 0;
            renameBtn.style.paddingBottom = 0;
            renameBtn.style.paddingLeft = 0;
            renameBtn.style.paddingRight = 0;
            row.Add(renameBtn);

            row.RegisterCallback<ClickEvent>(_ => OpenProgram(idx));
            row.RegisterCallback<MouseEnterEvent>(_ =>
                row.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f));
            row.RegisterCallback<MouseLeaveEvent>(_ =>
                row.style.backgroundColor = Color.clear);

            _view.ListScroll.Add(row);
        }
    }

    public void RunProgram()
    {
        if (!TryBuildNetworkProgram(out List<(ProgAction Operator, string Label, string Value)> program))
        {
            return;
        }

        // Канонический запуск в протоколе MinesServer — один пакет Save с
        // флагом SaveAndRun: сервер атомарно сохраняет программу и стартует её
        // виртуальную машину. Отдельный StartProgramPacket лишь возобновляет
        // выполнение ранее загруженной программы, поэтому пара
        // Save(saveAndRun:false) + Start у реального сервера ничего не
        // запускала — менялся только локальный визуал.
        StoreActiveProgram();
        _file.Save(_programItems);

        // Программы хранятся на сервере в слотах; пока программа не открыта из
        // списка, активный слот не выбран (_activeIndex = -1) — пишем в слот 0.
        int programId = _activeIndex >= 0 ? _activeIndex : 0;
        _protocol.Save(programId, saveAndRun: true, program, Array.Empty<int>());
        _isRunning = true;
        _view.RunBtn.SetEnabled(false);
        _view.StopBtn.SetEnabled(true);
        _view.Panel.AddToClassList("prog-panel--running");
        Debug.Log($"[Programmator] Program running (slot {programId}, {program.Count} instructions)");
        RunStateChanged?.Invoke(true);
    }

    public void StopProgram()
    {
        _protocol.StopProgram();
        _isRunning = false;
        _view.RunBtn.SetEnabled(true);
        _view.StopBtn.SetEnabled(false);
        _view.Panel.RemoveFromClassList("prog-panel--running");
        Debug.Log("[Programmator] Program stopped");
        RunStateChanged?.Invoke(false);
    }

    public void PauseProgram() => _protocol.PauseProgram();

    public void StepIn() => _protocol.StepIn();

    public void StepOut() => _protocol.StepOut();

    public void StepOver() => _protocol.StepOver();

    public void QueryMemory(IReadOnlyList<string> variables, ushort arrayStart, ushort arrayStop) =>
        _protocol.QueryMemory(variables, arrayStart, arrayStop);

    private void OnProgramUpdated(UpdateProgramPacket packet)
    {
        if (packet.ProgramId != _activeIndex || packet.Instructions.Count == 0)
        {
            return;
        }

        _data.Codes = new List<int>(packet.Instructions.Count);
        _data.Labels = new List<string?>(packet.Instructions.Count);
        _data.Values = new List<string?>(packet.Instructions.Count);
        foreach ((ProgAction op, string label, string value) in packet.Instructions)
        {
            _data.Codes.Add((int)op);
            _data.Labels.Add(label);
            _data.Values.Add(value);
        }

        ProgrammatorProgramItem item = _programItems[_activeIndex];
        item.Name = packet.DisplayName;
        item.Codes = new List<int>(_data.Codes);
        item.Labels = new List<string?>(_data.Labels);
        item.Values = new List<string?>(_data.Values);
        _file.Save(_programItems);
        RefreshAllCells();
        RaiseActiveProgramChanged();
        Debug.Log($"[Programmator] Server updated program '{packet.DisplayName}'.");
    }

    private void OnStateChanged(ProgramStatePacket packet)
    {
        _isRunning = packet.State == ProgramState.Running;
        _view.RunBtn.SetEnabled(packet.State != ProgramState.Running);
        _view.StopBtn.SetEnabled(packet.State == ProgramState.Running || packet.State == ProgramState.Paused);
        Debug.Log($"[Programmator] Server state: {packet.State}");
        RunStateChanged?.Invoke(_isRunning);
    }

    private static void OnBreakpointHit(BreakpointHitPacket packet)
    {
        Debug.Log($"[Programmator] Breakpoint hit; call stack depth={packet.CallStack.Length}.");
    }

    private static void OnMemoryReceived(ProgramMemoryPacket packet)
    {
        Debug.Log($"[Programmator] Memory received: variables={packet.RequestedVariables.Length}, array={packet.RequestedArraySlice.Length}.");
    }

    public void Dispose()
    {
        _protocol.ProgramUpdated -= OnProgramUpdated;
        _protocol.StateChanged -= OnStateChanged;
        _protocol.BreakpointHit -= OnBreakpointHit;
        _protocol.MemoryReceived -= OnMemoryReceived;
    }

    // ProgAction объявлен с базовым типом byte (darkar25.kern.data), а
    // Enum.IsDefined в рантайме Unity строго требует, чтобы тип boxed-значения
    // совпадал с базовым типом enum'а: int вместо byte кидает
    // ArgumentException. Значение приводится к базовому типу; код, не влезающий
    // в него (OverflowException от ChangeType), заведомо не определён.
    private static readonly Type s_progActionUnderlyingType =
        Enum.GetUnderlyingType(typeof(ProgAction));

    private static bool IsDefinedAction(int rawCode)
    {
        try
        {
            return Enum.IsDefined(
                typeof(ProgAction),
                Convert.ChangeType(rawCode, s_progActionUnderlyingType));
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private bool TryBuildNetworkProgram(
        out List<(ProgAction Operator, string Label, string Value)> program)
    {
        program = new List<(ProgAction Operator, string Label, string Value)>(_data.Codes.Count);
        for (int index = 0; index < _data.Codes.Count; index++)
        {
            int rawCode = _data.Codes[index];
            if (!IsDefinedAction(rawCode))
            {
                string message = _loc.Get("programmator.error.invalid_instruction", rawCode, index);
                _view.ShowProtocolError(message);
                Debug.LogError($"[Programmator] {message}");
                return false;
            }

            program.Add((
                (ProgAction)rawCode,
                _data.Labels[index] ?? string.Empty,
                _data.Values[index] ?? string.Empty));
        }

        _view.ClearProtocolError();
        return true;
    }

    public void RefreshAllCells()
    {
        _selection.SelectedCells.Clear();
        _selection.HasSelection = false;
        _view.UpdatePageLabel();
        for (int i = 0; i < ProgrammatorData.ROWS; i++)
        {
            for (int j = 0; j < ProgrammatorData.COLS; j++)
            {
                _view.UpdateCell(i, j);
            }
        }
    }
}

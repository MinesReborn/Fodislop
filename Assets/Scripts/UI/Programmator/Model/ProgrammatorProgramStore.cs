#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Fodinae.Core.Localization;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI.Programmator;

// Program storage, page navigation, and run/stop state for the programmator.
// Reads and writes ProgrammatorData directly (page/cell contents are global,
// shared with the rest of the programmator), and reaches into the shared
// selection model and the UI view for the handful of things it needs to
// repaint or clear when switching pages/programs.
internal sealed class ProgrammatorProgramStore
{
    private sealed class ProgramItem
    {
        public string Name = string.Empty;
        public List<int> Codes = new();
        public List<string?> Labels = new();
        public List<string?> Values = new();
    }

    private readonly ProgrammatorGridUIFactory _view;
    private readonly ProgrammatorSelectionModel _selection;
    private readonly ProgrammatorRadialController _radial;
    private readonly ILocalizationService _loc;
    private readonly ProgrammatorData _data;

    private readonly List<ProgramItem> _programItems = new();
    private int _activeIndex = -1;
    private bool _isRunning;

    public ProgrammatorProgramStore(
        ProgrammatorGridUIFactory view,
        ProgrammatorSelectionModel selection,
        ProgrammatorRadialController radial,
        ILocalizationService loc,
        ProgrammatorData data)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _radial = radial ?? throw new ArgumentNullException(nameof(radial));
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    public bool IsRunning => _isRunning;

    public int ProgramCount => _programItems.Count;

    // Invoked (via ProgrammatorRadialController.OnLastCellPlaced) when an
    // operator is placed in the very last cell of the last page.
    public void AdvancePageIfAtEnd()
    {
        _data.AddPage();
        _view.UpdatePageLabel();
    }

        [System.Serializable]
        private class ProgrammatorSave
        {
            public int[] Codes = Array.Empty<int>();
            public string?[] Labels = null!;
            public string?[] Values = null!;
        }

        private string SavePath => Path.Combine(Application.persistentDataPath, "programmator.json");

        public void SaveProgram()
        {
            var data = new ProgrammatorSave
            {
                Codes = _data.Codes.ToArray(),
                Labels = _data.Labels.ToArray(),
                Values = _data.Values.ToArray(),
            };
            File.WriteAllText(SavePath, JsonUtility.ToJson(data));
            Debug.Log("[Programmator] Program saved");
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
        }

        public void CloseProgram()
        {
            if (_isRunning)
            {
                StopProgram();
            }

            if (_activeIndex >= 0 && _activeIndex < _programItems.Count)
            {
                var item = _programItems[_activeIndex];
                item.Codes = new List<int>(_data.Codes);
                item.Labels = new List<string?>(_data.Labels);
                item.Values = new List<string?>(_data.Values);
            }

            ShowProgramList();
        }

        public void CreateNewProgram(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                name = _loc.Get("programmator.program", _programItems.Count + 1);
            }

            var item = new ProgramItem
            {
                Name = name,
                Codes = new List<int>(new int[ProgrammatorData.CELLS_PER_PAGE]),
                Labels = new List<string?>(new string?[ProgrammatorData.CELLS_PER_PAGE]),
                Values = new List<string?>(new string?[ProgrammatorData.CELLS_PER_PAGE]),
            };
            _programItems.Add(item);
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
            _isRunning = true;
            _view.RunBtn.SetEnabled(false);
            _view.StopBtn.SetEnabled(true);
            _view.Panel.AddToClassList("prog-panel--running");
            Debug.Log("[Programmator] Program running");
        }

        public void StopProgram()
        {
            _isRunning = false;
            _view.RunBtn.SetEnabled(true);
            _view.StopBtn.SetEnabled(false);
            _view.Panel.RemoveFromClassList("prog-panel--running");
            Debug.Log("[Programmator] Program stopped");
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

#nullable enable

using System;
using Kern.Core.Localization;
using UnityEngine;
using UnityEngine.UIElements;
using MinesServer.Data;

namespace Kern.UI.Programmator;

// Owns the operator palette and the observer joystick used to place operators
// into a grid cell. Cell repaints and the "auto-advance to a new page when the
// last cell of the last page is filled" behavior are reported back to the
// owner via callbacks so this class never reaches into the grid or the program
// list directly. Insertion rules are unchanged from the radial menu era: a
// palette item click or a joystick pick writes the operator into the target
// cell and closes the menu.
internal sealed class ProgrammatorRadialController
{
    private readonly UIDocument _doc;
    private readonly Action<int, int> _updateCell;
    private readonly ProgrammatorPalette _palette;
    private readonly ObserverJoystick _joystick;
    private readonly ProgrammatorData _data;

    private bool _shown;
    private int _cellIndex = -1;
    private VisualElement? _currentCell;

    // Invoked when an operator is placed in the very last cell of the last
    // page, so the owner can add a new page and refresh the page label.
    public Action? OnLastCellPlaced { get; set; }

    public ProgrammatorRadialController(
        UIDocument doc,
        Action<int, int> updateCell,
        ILocalizationService loc,
        ProgrammatorData data,
        IProgrammatorTextureCatalog textures)
    {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _updateCell = updateCell ?? throw new ArgumentNullException(nameof(updateCell));
        _data = data ?? throw new ArgumentNullException(nameof(data));

        _palette = new ProgrammatorPalette(loc, textures, doc);
        _palette.OnOperatorClicked += OnPaletteOperatorClicked;
        _palette.OnCloseClicked += HideAll;
        _palette.OnJoystickRequested += ShowJoystick;

        _joystick = new ObserverJoystick(textures);
        _joystick.OnOperatorSelected += OnJoystickOperatorSelected;
    }

    public bool IsShown => _shown;

    public void HideAll()
    {
        _joystick.Hide();
        _palette.Close();
        _shown = false;
        _cellIndex = -1;
    }

    // Same as HideAll but leaves the last targeted cell index untouched —
    // matches the page-navigation call sites in the original code, which
    // hid the menus without resetting _cellIndex.
    public void HideMenus()
    {
        _palette.Close();
        _joystick.Hide();
        _shown = false;
    }

    public void HandleCellRightClick(int row, int col, VisualElement cell)
    {
        _cellIndex = (row * ProgrammatorData.COLS) + col;
        _currentCell = cell;
        _shown = true;
        ShowAtCellCenter(cell, center => _palette.OpenAt(_doc.rootVisualElement, center));
    }

    // DEL clears the cell when the palette is open (Tick's radialShown branch).
    public void HandleDeleteKeyWhileShown()
    {
        if (_cellIndex >= 0)
        {
            int row = _cellIndex / ProgrammatorData.COLS;
            int col = _cellIndex % ProgrammatorData.COLS;
            int idx = (_data.CurrentPage * ProgrammatorData.CELLS_PER_PAGE)
                      + (row * ProgrammatorData.COLS) + col;
            _data.PushUndo();
            _data.Codes[idx] = 0;
            _updateCell(row, col);
        }

        HideAll();
    }

    // Layout race guard: on the frame the popup becomes visible UI Toolkit hasn't
    // run layout yet, so cell.worldBound is (0,0) and the menu spawns in the
    // top-left corner. Wait for one GeometryChangedEvent when bounds aren't ready.
    private static void ShowAtCellCenter(VisualElement cell, Action<Vector2> show)
    {
        // Обе размерности должны быть готовы: при одной нулевой worldBound ещё
        // не валиден, и меню уедет в (0,0) — верхний левый угол экрана.
        if (cell.worldBound.width > 0f && cell.worldBound.height > 0f)
        {
            show(cell.worldBound.center);
            return;
        }

        EventCallback<GeometryChangedEvent>? callback = null;
        callback = _ =>
        {
            cell.UnregisterCallback(callback);
            show(cell.worldBound.center);
        };
        cell.RegisterCallback(callback);
    }

    private void ShowJoystick()
    {
        if (_currentCell == null)
        {
            return;
        }

        _palette.Close();
        ShowAtCellCenter(_currentCell, center => _joystick.ShowAt(_doc.rootVisualElement, center));
    }

    private void OnJoystickOperatorSelected(ProgAction action)
    {
        if (_cellIndex < 0)
        {
            return;
        }

        PlaceOperator(action);

        _joystick.Hide();
        _shown = false;
        _cellIndex = -1;
    }

    private void OnPaletteOperatorClicked(ProgAction action)
    {
        if (_cellIndex < 0)
        {
            return;
        }

        PlaceOperator(action);

        _palette.Close();
        _shown = false;
        _cellIndex = -1;
    }

    // Единственное место записи оператора в ячейку; вызывается и палитрой,
    // и джойстиком.
    private void PlaceOperator(ProgAction action)
    {
        int row = _cellIndex / ProgrammatorData.COLS;
        int col = _cellIndex % ProgrammatorData.COLS;
        int idx = (_data.CurrentPage * ProgrammatorData.CELLS_PER_PAGE)
                  + (row * ProgrammatorData.COLS) + col;
        _data.PushUndo();
        _data.Codes[idx] = (int)action;
        _updateCell(row, col);

        if ((row * ProgrammatorData.COLS) + col == ProgrammatorData.CELLS_PER_PAGE - 1
            && _data.CurrentPage == _data.PageCount - 1)
        {
            OnLastCellPlaced?.Invoke();
        }
    }
}

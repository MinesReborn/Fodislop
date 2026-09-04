#nullable enable

using System;
using Fodinae.Core.Localization;
using UnityEngine;
using UnityEngine.UIElements;
using MinesServer.Data;

namespace Fodinae.UI.Programmator;

// Owns the radial category/operator menu and the observer joystick used to
// place operators into a grid cell. Cell repaints and the "auto-advance to a
// new page when the last cell of the last page is filled" behavior are
// reported back to the owner via callbacks so this class never reaches into
// the grid or the program list directly.
internal sealed class ProgrammatorRadialController
{
    private readonly UIDocument _doc;
    private readonly Action<int, int> _updateCell;
    private readonly RadialMenu _radial;
    private readonly ObserverJoystick _joystick;
    private readonly ProgrammatorData _data;

    private bool _radialShown;
    private int _radialCellIndex = -1;
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

        _radial = new RadialMenu(loc, textures);
        _radial.OnCategoryClicked += OnRadialCategoryClicked;
        _radial.OnItemClicked += OnRadialItemClicked;
        _radial.OnBackClicked += OnRadialBackClicked;

        _joystick = new ObserverJoystick(textures);
        _joystick.OnOperatorSelected += OnJoystickOperatorSelected;
    }

    public bool IsShown => _radialShown;

    public void HideAll()
    {
        _joystick.Hide();
        _radial.Hide();
        _radialShown = false;
        _radialCellIndex = -1;
    }

    // Same as HideAll but leaves the last targeted cell index untouched —
    // matches the page-navigation call sites in the original code, which
    // hid the menus without resetting _radialCellIndex.
    public void HideMenus()
    {
        _radial.Hide();
        _joystick.Hide();
        _radialShown = false;
    }

    public void HandleCellRightClick(int row, int col, VisualElement cell)
    {
        _radialCellIndex = (row * ProgrammatorData.COLS) + col;
        _currentCell = cell;
        ShowCategoryRing();
        _radialShown = true;
        ShowAtCellCenter(cell, center => _radial.ShowAt(_doc.rootVisualElement, center));
    }

    // DEL clears the cell when the radial menu is open (Tick's radialShown branch).
    public void HandleDeleteKeyWhileShown()
    {
        if (_radialCellIndex >= 0)
        {
            int row = _radialCellIndex / ProgrammatorData.COLS;
            int col = _radialCellIndex % ProgrammatorData.COLS;
            int idx = (_data.CurrentPage * ProgrammatorData.CELLS_PER_PAGE)
                      + (row * ProgrammatorData.COLS) + col;
            _data.PushUndo();
            _data.Codes[idx] = 0;
            _updateCell(row, col);
        }

        HideAll();
    }

    private void ShowCategoryRing()
    {
        _joystick.Hide();
        var cats = ProgrammatorData.CATEGORIES;
        var colors = new Color[cats.Length];
        for (int i = 0; i < cats.Length; i++)
        {
            colors[i] = ProgrammatorData.CATEGORY_COLORS[cats[i]];
        }

        _radial.SetInnerItems(cats, colors);
        _radial.ClearOuterItems();
    }

    // Layout race guard: on the frame the popup becomes visible UI Toolkit hasn't
    // run layout yet, so cell.worldBound is (0,0) and the radial spawns in the
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

    private void OnRadialCategoryClicked(int categoryId)
    {
        // Category clicked — populate outer ring with operators
        if (!ProgrammatorData.CATEGORY_OPERATORS.TryGetValue(categoryId, out var ops))
        {
            return;
        }

        // CAT_OBSERVER uses a joystick instead of the outer ring
        if (categoryId == ProgrammatorData.CAT_OBSERVER)
        {
            _radial.ClearOuterItems();
            _joystick.Hide();
            var cell = _currentCell!;
            ShowAtCellCenter(cell, center => _joystick.ShowAt(_doc.rootVisualElement, center));
            return;
        }

        // Other categories: populate standard outer ring
        _joystick.Hide();

        if (!ProgrammatorData.CATEGORY_COLORS.TryGetValue(categoryId, out var catColor))
        {
            catColor = Color.white;
        }

        var colors = new Color[ops.Length];
        for (int i = 0; i < ops.Length; i++)
        {
            colors[i] = catColor;
        }

        _radial.SetOuterItems(Array.ConvertAll(ops, op => (int)op), colors);
    }

    private void OnJoystickOperatorSelected(ProgAction action)
    {
        if (_radialCellIndex < 0)
        {
            return;
        }

        int row = _radialCellIndex / ProgrammatorData.COLS;
        int col = _radialCellIndex % ProgrammatorData.COLS;
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

        _joystick.Hide();
        _radial.Hide();
        _radialShown = false;
        _radialCellIndex = -1;
    }

    private void OnRadialItemClicked(int selectedId)
    {
        // Outer ring item clicked — place the operator in the cell
        if (_radialCellIndex < 0)
        {
            return;
        }

        int row = _radialCellIndex / ProgrammatorData.COLS;
        int col = _radialCellIndex % ProgrammatorData.COLS;
        int idx = (_data.CurrentPage * ProgrammatorData.CELLS_PER_PAGE)
                  + (row * ProgrammatorData.COLS) + col;
        _data.PushUndo();
        _data.Codes[idx] = selectedId;
        _updateCell(row, col);

        if ((row * ProgrammatorData.COLS) + col == ProgrammatorData.CELLS_PER_PAGE - 1
            && _data.CurrentPage == _data.PageCount - 1)
        {
            OnLastCellPlaced?.Invoke();
        }

        _radial.Hide();
        _radialShown = false;
        _radialCellIndex = -1;
    }

    private void OnRadialBackClicked()
    {
        // Back button — clear outer ring and joystick, keep inner ring visible
        _radial.ClearOuterItems();
        _joystick.Hide();
    }
}

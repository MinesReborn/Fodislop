#nullable enable

using System;
using System.Collections.Generic;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.UI.Programmator;

// Selection state for the programmator grid: which cells are currently
// selected (either a single rectangular drag range or an arbitrary
// ctrl-clicked set) and the border highlighting that reflects it.
// Visual updates go through the setSelectionBorder callback supplied by
// the owner so this class never touches VisualElements directly.
internal sealed class ProgrammatorSelectionModel
{
    private readonly Action<int, int, bool> _setSelectionBorder;
    private readonly ProgrammatorData _data;

    private bool _hasSelection;
    private int _selStartRow;
    private int _selStartCol;
    private int _selEndRow;
    private int _selEndCol;
    private readonly HashSet<long> _selectedCells = new HashSet<long>();

    public ProgrammatorSelectionModel(
        Action<int, int, bool> setSelectionBorder,
        ProgrammatorData data)
    {
        _setSelectionBorder = setSelectionBorder ?? throw new ArgumentNullException(nameof(setSelectionBorder));
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    public bool HasSelection
    {
        get => _hasSelection;
        set => _hasSelection = value;
    }

    public int SelStartRow
    {
        get => _selStartRow;
        set => _selStartRow = value;
    }

    public int SelStartCol
    {
        get => _selStartCol;
        set => _selStartCol = value;
    }

    public int SelEndRow
    {
        get => _selEndRow;
        set => _selEndRow = value;
    }

    public int SelEndCol
    {
        get => _selEndCol;
        set => _selEndCol = value;
    }

    public HashSet<long> SelectedCells => _selectedCells;

    public bool IsSelected(int row, int col)
    {
        if (_selectedCells.Count > 0)
        {
            return _selectedCells.Contains(((long)row * ProgrammatorData.COLS) + col);
        }

        if (!_hasSelection)
        {
            return false;
        }

        int minRow = Mathf.Min(_selStartRow, _selEndRow);
        int maxRow = Mathf.Max(_selStartRow, _selEndRow);
        int minCol = Mathf.Min(_selStartCol, _selEndCol);
        int maxCol = Mathf.Max(_selStartCol, _selEndCol);
        return row >= minRow && row <= maxRow && col >= minCol && col <= maxCol;
    }

    public void SetSelectionBorder(int row, int col, bool selected)
    {
        _setSelectionBorder(row, col, selected);
    }

    public void RefreshSelectionBorders()
    {
        for (int r = 0; r < ProgrammatorData.ROWS; r++)
        {
            for (int c = 0; c < ProgrammatorData.COLS; c++)
            {
                if (IsSelected(r, c))
                {
                    SetSelectionBorder(r, c, true);
                }
                else if (_data.HoveredCell != (r * ProgrammatorData.COLS) + c)
                {
                    SetSelectionBorder(r, c, false);
                }
            }
        }
    }

    public void ToggleCellSelection(int row, int col)
    {
        long key = ((long)row * ProgrammatorData.COLS) + col;
        if (!_selectedCells.Remove(key))
        {
            if (_hasSelection)
            {
                int minR = Mathf.Min(_selStartRow, _selEndRow);
                int maxR = Mathf.Max(_selStartRow, _selEndRow);
                int minC = Mathf.Min(_selStartCol, _selEndCol);
                int maxC = Mathf.Max(_selStartCol, _selEndCol);
                for (int r = minR; r <= maxR; r++)
                {
                    for (int c = minC; c <= maxC; c++)
                    {
                        _selectedCells.Add(((long)r * ProgrammatorData.COLS) + c);
                        SetSelectionBorder(r, c, true);
                    }
                }

                _hasSelection = false;
            }

            _selectedCells.Add(key);
            SetSelectionBorder(row, col, true);
        }
        else
        {
            SetSelectionBorder(row, col, false);
        }
    }

    public void SelectCell(int row, int col)
    {
        ClearSelection();
        _selStartRow = _selEndRow = row;
        _selStartCol = _selEndCol = col;
        _hasSelection = true;
        SetSelectionBorder(row, col, true);
    }

    public void ExtendSelection(int row, int col)
    {
        if (_selectedCells.Count > 0)
        {
            foreach (long key in _selectedCells)
            {
                int r = (int)(key / ProgrammatorData.COLS);
                int c = (int)(key % ProgrammatorData.COLS);
                SetSelectionBorder(r, c, false);
            }

            _selectedCells.Clear();
            _hasSelection = false;
        }

        if (!_hasSelection)
        {
            SelectCell(row, col);
            return;
        }

        int oldMinRow = Mathf.Min(_selStartRow, _selEndRow);
        int oldMaxRow = Mathf.Max(_selStartRow, _selEndRow);
        int oldMinCol = Mathf.Min(_selStartCol, _selEndCol);
        int oldMaxCol = Mathf.Max(_selStartCol, _selEndCol);
        _selEndRow = row;
        _selEndCol = col;
        int newMinRow = Mathf.Min(_selStartRow, _selEndRow);
        int newMaxRow = Mathf.Max(_selStartRow, _selEndRow);
        int newMinCol = Mathf.Min(_selStartCol, _selEndCol);
        int newMaxCol = Mathf.Max(_selStartCol, _selEndCol);
        for (int r = Mathf.Min(oldMinRow, newMinRow); r <= Mathf.Max(oldMaxRow, newMaxRow); r++)
        {
            for (int c = Mathf.Min(oldMinCol, newMinCol); c <= Mathf.Max(oldMaxCol, newMaxCol); c++)
            {
                bool nowSelected = r >= newMinRow && r <= newMaxRow && c >= newMinCol && c <= newMaxCol;
                if (nowSelected)
                {
                    SetSelectionBorder(r, c, true);
                }
                else if (_data.HoveredCell != (r * ProgrammatorData.COLS) + c)
                {
                    SetSelectionBorder(r, c, false);
                }
            }
        }
    }

    public void ClearSelection()
    {
        if (_selectedCells.Count > 0)
        {
            foreach (long key in _selectedCells)
            {
                int r = (int)(key / ProgrammatorData.COLS);
                int c = (int)(key % ProgrammatorData.COLS);
                if (_data.HoveredCell != (r * ProgrammatorData.COLS) + c)
                {
                    SetSelectionBorder(r, c, false);
                }
            }

            _selectedCells.Clear();
        }

        if (_hasSelection)
        {
            int minRow = Mathf.Min(_selStartRow, _selEndRow);
            int maxRow = Mathf.Max(_selStartRow, _selEndRow);
            int minCol = Mathf.Min(_selStartCol, _selEndCol);
            int maxCol = Mathf.Max(_selStartCol, _selEndCol);
            for (int r = minRow; r <= maxRow; r++)
            {
                for (int c = minCol; c <= maxCol; c++)
                {
                    if (_data.HoveredCell != (r * ProgrammatorData.COLS) + c)
                    {
                        SetSelectionBorder(r, c, false);
                    }
                }
            }

            _hasSelection = false;
        }
    }

    public (int minRow, int maxRow, int minCol, int maxCol) GetSetBounds()
    {
        int minR = int.MaxValue, maxR = int.MinValue;
        int minC = int.MaxValue, maxC = int.MinValue;
        foreach (long key in _selectedCells)
        {
            int r = (int)(key / ProgrammatorData.COLS);
            int c = (int)(key % ProgrammatorData.COLS);
            if (r < minR)
            {
                minR = r;
            }

            if (r > maxR)
            {
                maxR = r;
            }

            if (c < minC)
            {
                minC = c;
            }

            if (c > maxC)
            {
                maxC = c;
            }
        }

        return (minR, maxR, minC, maxC);
    }

    public bool HasAnySelection() => _hasSelection || _selectedCells.Count > 0;
}

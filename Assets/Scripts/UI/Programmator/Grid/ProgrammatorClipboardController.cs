#nullable enable

using System;
using System.Collections.Generic;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.UI.Programmator;

// Clipboard (copy/cut/paste) and shift-with-push logic for the programmator
// grid. Operates on the shared selection model and reports cell repaints
// back to the owner via the updateCell callback.
internal sealed class ProgrammatorClipboardController
{
    private readonly ProgrammatorSelectionModel _selection;
    private readonly Action<int, int> _updateCell;
    private readonly ProgrammatorData _data;

    private int[]? _clipboardCodes;
    private string?[]? _clipboardLabels;
    private string?[]? _clipboardValues;
    private int _clipboardWidth;
    private int _clipboardHeight;
    private bool _hasClipboard;

    public ProgrammatorClipboardController(
        ProgrammatorSelectionModel selection,
        Action<int, int> updateCell,
        ProgrammatorData data)
    {
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _updateCell = updateCell ?? throw new ArgumentNullException(nameof(updateCell));
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    public bool HasClipboard => _hasClipboard;

    public void CopySelection()
    {
        if (!_selection.HasAnySelection())
        {
            return;
        }

        var (minRow, maxRow, minCol, maxCol) = GetEffectiveBounds();

        _clipboardWidth = (maxCol - minCol) + 1;
        _clipboardHeight = (maxRow - minRow) + 1;
        _clipboardCodes = new int[_clipboardWidth * _clipboardHeight];
        _clipboardLabels = new string?[_clipboardWidth * _clipboardHeight];
        _clipboardValues = new string?[_clipboardWidth * _clipboardHeight];
        for (int r = minRow; r <= maxRow; r++)
        {
            for (int c = minCol; c <= maxCol; c++)
            {
                int srcIdx = (_data.CurrentPage * ProgrammatorData.CELLS_PER_PAGE)
                             + (r * ProgrammatorData.COLS) + c;
                int dstIdx = ((r - minRow) * _clipboardWidth) + (c - minCol);
                _clipboardCodes[dstIdx] = _data.Codes[srcIdx];
                _clipboardLabels[dstIdx] = _data.Labels[srcIdx];
                _clipboardValues[dstIdx] = _data.Values[srcIdx];
            }
        }

        _hasClipboard = true;
    }

    public void CutSelection()
    {
        if (!_selection.HasAnySelection())
        {
            return;
        }

        CopySelection();
        _data.PushUndo();
        var (minRow, maxRow, minCol, maxCol) = GetEffectiveBounds();

        for (int r = minRow; r <= maxRow; r++)
        {
            for (int c = minCol; c <= maxCol; c++)
            {
                if (_selection.SelectedCells.Count > 0 && !_selection.SelectedCells.Contains(((long)r * ProgrammatorData.COLS) + c))
                {
                    continue;
                }

                int idx = (_data.CurrentPage * ProgrammatorData.CELLS_PER_PAGE)
                          + (r * ProgrammatorData.COLS) + c;
                _data.Codes[idx] = 0;
                _updateCell(r, c);
            }
        }
    }

    public void PasteClipboard()
    {
        if (!_hasClipboard)
        {
            return;
        }

        _data.PushUndo();
        int anchorRow = 0, anchorCol = 0;
        if (_selection.HasAnySelection())
        {
            var bounds = GetEffectiveBounds();
            anchorRow = bounds.minRow;
            anchorCol = bounds.minCol;
        }

        for (int r = 0; r < _clipboardHeight; r++)
        {
            for (int c = 0; c < _clipboardWidth; c++)
            {
                int targetRow = anchorRow + r;
                int targetCol = anchorCol + c;
                if (targetRow >= ProgrammatorData.ROWS || targetCol >= ProgrammatorData.COLS)
                {
                    continue;
                }

                int dstIdx = (_data.CurrentPage * ProgrammatorData.CELLS_PER_PAGE)
                             + (targetRow * ProgrammatorData.COLS) + targetCol;
                int srcIdx = (r * _clipboardWidth) + c;
                _data.Codes[dstIdx] = _clipboardCodes![srcIdx];
                _data.Labels[dstIdx] = _clipboardLabels![srcIdx];
                _data.Values[dstIdx] = _clipboardValues![srcIdx];
                _updateCell(targetRow, targetCol);
            }
        }

        _selection.SelectCell(anchorRow, anchorCol);
        _selection.SelEndRow = Mathf.Min(anchorRow + _clipboardHeight - 1, ProgrammatorData.ROWS - 1);
        _selection.SelEndCol = Mathf.Min(anchorCol + _clipboardWidth - 1, ProgrammatorData.COLS - 1);
        _selection.RefreshSelectionBorders();
    }

        public void ShiftSelection(int dx, int dy)
        {
            if (!_selection.HasAnySelection())
            {
                return;
            }

            int page = _data.CurrentPage;
            const int cols = ProgrammatorData.COLS;
            const int rows = ProgrammatorData.ROWS;
            const int cellsPerPage = ProgrammatorData.CELLS_PER_PAGE;

            if (_selection.SelectedCells.Count > 0)
            {
                var b = _selection.GetSetBounds();
                if (b.minRow + dy < 0 || b.maxRow + dy >= rows ||
                    b.minCol + dx < 0 || b.maxCol + dx >= cols)
                {
                    return;
                }

                var temp = new Dictionary<long, (int code, string? label, string? value)>();
                foreach (long key in _selection.SelectedCells)
                {
                    int idx = (page * cellsPerPage) + (int)key;
                    temp[key] = (_data.Codes[idx], _data.Labels[idx], _data.Values[idx]);
                }

                foreach (long key in _selection.SelectedCells)
                {
                    int r = (int)(key / cols);
                    int c = (int)(key % cols);
                    int idx = (page * cellsPerPage) + (int)key;
                    _data.Codes[idx] = 0;
                    _data.Labels[idx] = null;
                    _data.Values[idx] = null;
                    _updateCell(r, c);
                    _selection.SetSelectionBorder(r, c, false);
                }

                var ordered = new List<long>(_selection.SelectedCells);
                if (dx > 0)
                {
                    ordered.Sort((a, b) => (int)((b % cols) - (a % cols)));
                }
                else if (dx < 0)
                {
                    ordered.Sort((a, b) => (int)((a % cols) - (b % cols)));
                }
                else if (dy > 0)
                {
                    ordered.Sort((a, b) => (int)((b / cols) - (a / cols)));
                }
                else if (dy < 0)
                {
                    ordered.Sort((a, b) => (int)((a / cols) - (b / cols)));
                }

                _data.PushUndo();
                var newSet = new HashSet<long>();
                foreach (long key in ordered)
                {
                    int oldR = (int)(key / cols);
                    int oldC = (int)(key % cols);
                    int newR = oldR + dy;
                    int newC = oldC + dx;
                    if (newR < 0 || newR >= rows || newC < 0 || newC >= cols)
                    {
                        int origIdx = (page * cellsPerPage) + (int)key;
                        _data.Codes[origIdx] = temp[key].code;
                        _data.Labels[origIdx] = temp[key].label;
                        _data.Values[origIdx] = temp[key].value;
                        _updateCell(oldR, oldC);
                        _selection.SetSelectionBorder(oldR, oldC, true);
                        newSet.Add(key);
                        continue;
                    }

                    int destIdx = (page * cellsPerPage) + (newR * cols) + newC;
                    if (_data.Codes[destIdx] != 0)
                    {
                        if (TryFindEmptyCellAhead(page, newR + dy, newC + dx, dy, dx, out int pushR, out int pushC))
                        {
                            MoveCell(page, newR, newC, pushR, pushC);
                        }
                        else
                        {
                            int origIdx = (page * cellsPerPage) + (int)key;
                            _data.Codes[origIdx] = temp[key].code;
                            _data.Labels[origIdx] = temp[key].label;
                            _data.Values[origIdx] = temp[key].value;
                            _updateCell(oldR, oldC);
                            _selection.SetSelectionBorder(oldR, oldC, true);
                            newSet.Add(key);
                            continue;
                        }
                    }

                    _data.Codes[destIdx] = temp[key].code;
                    _data.Labels[destIdx] = temp[key].label;
                    _data.Values[destIdx] = temp[key].value;
                    _updateCell(newR, newC);
                    _selection.SetSelectionBorder(newR, newC, true);
                    newSet.Add(((long)newR * cols) + newC);
                }

                _selection.SelectedCells.Clear();
                foreach (long k in newSet)
                {
                    _selection.SelectedCells.Add(k);
                }

                _selection.HasSelection = false;
                return;
            }

            var (minRow, maxRow, minCol, maxCol) = GetEffectiveBounds();
            int newMinRow = minRow + dy;
            int newMaxRow = maxRow + dy;
            int newMinCol = minCol + dx;
            int newMaxCol = maxCol + dx;
            if (newMinRow < 0 || newMaxRow >= rows ||
                newMinCol < 0 || newMaxCol >= cols)
            {
                return;
            }

            if (!CanPushBlockObstacles(page, minRow, maxRow, minCol, maxCol, dx, dy))
            {
                return;
            }

            _data.PushUndo();
            int width = (maxCol - minCol) + 1;
            int height = (maxRow - minRow) + 1;
            int[] tmpCodes = new int[width * height];
            string?[] tmpLabels = new string?[width * height];
            string?[] tmpValues = new string?[width * height];
            for (int r = minRow; r <= maxRow; r++)
            {
                for (int c = minCol; c <= maxCol; c++)
                {
                    int srcIdx = (page * cellsPerPage) + (r * cols) + c;
                    int tmpIdx = ((r - minRow) * width) + (c - minCol);
                    tmpCodes[tmpIdx] = _data.Codes[srcIdx];
                    tmpLabels[tmpIdx] = _data.Labels[srcIdx];
                    tmpValues[tmpIdx] = _data.Values[srcIdx];
                    _data.Codes[srcIdx] = 0;
                    _data.Labels[srcIdx] = null;
                    _data.Values[srcIdx] = null;
                    _updateCell(r, c);
                    _selection.SetSelectionBorder(r, c, false);
                }
            }

            PushBlockObstacles(page, minRow, maxRow, minCol, maxCol, dx, dy);

            for (int r = newMinRow; r <= newMaxRow; r++)
            {
                for (int c = newMinCol; c <= newMaxCol; c++)
                {
                    int dstIdx = (page * cellsPerPage) + (r * cols) + c;
                    int tmpIdx = ((r - newMinRow) * width) + (c - newMinCol);
                    _data.Codes[dstIdx] = tmpCodes[tmpIdx];
                    _data.Labels[dstIdx] = tmpLabels[tmpIdx];
                    _data.Values[dstIdx] = tmpValues[tmpIdx];
                    _updateCell(r, c);
                    _selection.SetSelectionBorder(r, c, true);
                }
            }

            _selection.SelStartRow = newMinRow;
            _selection.SelStartCol = newMinCol;
            _selection.SelEndRow = newMaxRow;
            _selection.SelEndCol = newMaxCol;
        }

    private (int minRow, int maxRow, int minCol, int maxCol) GetEffectiveBounds()
    {
        if (_selection.SelectedCells.Count > 0)
        {
            var b = _selection.GetSetBounds();
            return (b.minRow, b.maxRow, b.minCol, b.maxCol);
        }

        return (
            Mathf.Min(_selection.SelStartRow, _selection.SelEndRow),
            Mathf.Max(_selection.SelStartRow, _selection.SelEndRow),
            Mathf.Min(_selection.SelStartCol, _selection.SelEndCol),
            Mathf.Max(_selection.SelStartCol, _selection.SelEndCol));
    }

    private bool CanPushBlockObstacles(int page, int minRow, int maxRow, int minCol, int maxCol, int dx, int dy)
    {
        const int cols = ProgrammatorData.COLS;
        const int cellsPerPage = ProgrammatorData.CELLS_PER_PAGE;
        int stepR = Math.Sign(dy);
        int stepC = Math.Sign(dx);

        if (dx != 0)
        {
            int colStart = dx > 0 ? maxCol + 1 : minCol + dx;
            int colEnd = dx > 0 ? maxCol + dx : minCol - 1;
            for (int r = minRow; r <= maxRow; r++)
            {
                for (int c = colStart; c <= colEnd; c++)
                {
                    if (_data.Codes[(page * cellsPerPage) + (r * cols) + c] != 0 &&
                        !TryFindEmptyCellAhead(page, r + dy, c + dx, stepR, stepC, out _, out _))
                    {
                        return false;
                    }
                }
            }
        }
        else if (dy != 0)
        {
            int rowStart = dy > 0 ? maxRow + 1 : minRow + dy;
            int rowEnd = dy > 0 ? maxRow + dy : minRow - 1;
            for (int c = minCol; c <= maxCol; c++)
            {
                for (int r = rowStart; r <= rowEnd; r++)
                {
                    if (_data.Codes[(page * cellsPerPage) + (r * cols) + c] != 0 &&
                        !TryFindEmptyCellAhead(page, r + dy, c + dx, stepR, stepC, out _, out _))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private void PushBlockObstacles(int page, int minRow, int maxRow, int minCol, int maxCol, int dx, int dy)
    {
        int stepR = Math.Sign(dy);
        int stepC = Math.Sign(dx);

        if (dx > 0)
        {
            for (int r = minRow; r <= maxRow; r++)
            {
                for (int c = maxCol + dx; c >= maxCol + 1; c--)
                {
                    TryPushObstacleAt(page, r, c, dx, dy, stepR, stepC);
                }
            }
        }
        else if (dx < 0)
        {
            for (int r = minRow; r <= maxRow; r++)
            {
                for (int c = minCol + dx; c <= minCol - 1; c++)
                {
                    TryPushObstacleAt(page, r, c, dx, dy, stepR, stepC);
                }
            }
        }
        else if (dy > 0)
        {
            for (int c = minCol; c <= maxCol; c++)
            {
                for (int r = maxRow + dy; r >= maxRow + 1; r--)
                {
                    TryPushObstacleAt(page, r, c, dx, dy, stepR, stepC);
                }
            }
        }
        else if (dy < 0)
        {
            for (int c = minCol; c <= maxCol; c++)
            {
                for (int r = minRow + dy; r <= minRow - 1; r++)
                {
                    TryPushObstacleAt(page, r, c, dx, dy, stepR, stepC);
                }
            }
        }
    }

    private void TryPushObstacleAt(int page, int r, int c, int dx, int dy, int stepR, int stepC)
    {
        int idx = (page * ProgrammatorData.CELLS_PER_PAGE) + (r * ProgrammatorData.COLS) + c;
        if (_data.Codes[idx] != 0 &&
            TryFindEmptyCellAhead(page, r + dy, c + dx, stepR, stepC, out int emptyR, out int emptyC))
        {
            MoveCell(page, r, c, emptyR, emptyC);
        }
    }

    private bool TryFindEmptyCellAhead(int page, int startR, int startC, int stepR, int stepC, out int emptyR, out int emptyC)
    {
        int r = startR;
        int c = startC;
        while (r >= 0 && r < ProgrammatorData.ROWS && c >= 0 && c < ProgrammatorData.COLS)
        {
            int idx = (page * ProgrammatorData.CELLS_PER_PAGE) + (r * ProgrammatorData.COLS) + c;
            if (_data.Codes[idx] == 0)
            {
                emptyR = r;
                emptyC = c;
                return true;
            }

            r += stepR;
            c += stepC;
        }

        emptyR = -1;
        emptyC = -1;
        return false;
    }

    private void MoveCell(int page, int srcR, int srcC, int dstR, int dstC)
    {
        const int cols = ProgrammatorData.COLS;
        const int cellsPerPage = ProgrammatorData.CELLS_PER_PAGE;
        int srcIdx = (page * cellsPerPage) + (srcR * cols) + srcC;
        int dstIdx = (page * cellsPerPage) + (dstR * cols) + dstC;
        _data.Codes[dstIdx] = _data.Codes[srcIdx];
        _data.Labels[dstIdx] = _data.Labels[srcIdx];
        _data.Values[dstIdx] = _data.Values[srcIdx];
        _data.Codes[srcIdx] = 0;
        _data.Labels[srcIdx] = null;
        _data.Values[srcIdx] = null;
        _updateCell(srcR, srcC);
        _updateCell(dstR, dstC);
    }
}

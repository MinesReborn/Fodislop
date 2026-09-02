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

            int minRow, maxRow, minCol, maxCol;
            if (_selection.SelectedCells.Count > 0)
            {
                var b = _selection.GetSetBounds();
                minRow = b.minRow;
                maxRow = b.maxRow;
                minCol = b.minCol;
                maxCol = b.maxCol;
            }
            else
            {
                minRow = Mathf.Min(_selection.SelStartRow, _selection.SelEndRow);
                maxRow = Mathf.Max(_selection.SelStartRow, _selection.SelEndRow);
                minCol = Mathf.Min(_selection.SelStartCol, _selection.SelEndCol);
                maxCol = Mathf.Max(_selection.SelStartCol, _selection.SelEndCol);
            }

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
            int minRow, maxRow, minCol, maxCol;
            if (_selection.SelectedCells.Count > 0)
            {
                var b = _selection.GetSetBounds();
                minRow = b.minRow;
                maxRow = b.maxRow;
                minCol = b.minCol;
                maxCol = b.maxCol;
            }
            else
            {
                minRow = Mathf.Min(_selection.SelStartRow, _selection.SelEndRow);
                maxRow = Mathf.Max(_selection.SelStartRow, _selection.SelEndRow);
                minCol = Mathf.Min(_selection.SelStartCol, _selection.SelEndCol);
                maxCol = Mathf.Max(_selection.SelStartCol, _selection.SelEndCol);
            }

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
            if (_selection.SelectedCells.Count > 0)
            {
                var b = _selection.GetSetBounds();
                anchorRow = b.minRow;
                anchorCol = b.minCol;
            }
            else if (_selection.HasSelection)
            {
                anchorRow = Mathf.Min(_selection.SelStartRow, _selection.SelEndRow);
                anchorCol = Mathf.Min(_selection.SelStartCol, _selection.SelEndCol);
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
                        int pushR = newR + dy;
                        int pushC = newC + dx;
                        bool pushed = false;
                        while (pushR >= 0 && pushR < rows && pushC >= 0 && pushC < cols)
                        {
                            int pushIdx = (page * cellsPerPage) + (pushR * cols) + pushC;
                            if (_data.Codes[pushIdx] == 0)
                            {
                                _data.Codes[pushIdx] = _data.Codes[destIdx];
                                _data.Labels[pushIdx] = _data.Labels[destIdx];
                                _data.Values[pushIdx] = _data.Values[destIdx];
                                _data.Codes[destIdx] = 0;
                                _data.Labels[destIdx] = null;
                                _data.Values[destIdx] = null;
                                _updateCell(newR, newC);
                                _updateCell(pushR, pushC);
                                pushed = true;
                                break;
                            }

                            pushR += dy;
                            pushC += dx;
                        }

                        if (!pushed)
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

            int minRow = Mathf.Min(_selection.SelStartRow, _selection.SelEndRow);
            int maxRow = Mathf.Max(_selection.SelStartRow, _selection.SelEndRow);
            int minCol = Mathf.Min(_selection.SelStartCol, _selection.SelEndCol);
            int maxCol = Mathf.Max(_selection.SelStartCol, _selection.SelEndCol);
            int newMinRow = minRow + dy;
            int newMaxRow = maxRow + dy;
            int newMinCol = minCol + dx;
            int newMaxCol = maxCol + dx;
            if (newMinRow < 0 || newMaxRow >= rows ||
                newMinCol < 0 || newMaxCol >= cols)
            {
                return;
            }

            if (dx > 0)
            {
                for (int r = minRow; r <= maxRow; r++)
                {
                    for (int c = maxCol + 1; c <= maxCol + dx; c++)
                    {
                        if (_data.Codes[(page * cellsPerPage) + (r * cols) + c] != 0)
                        {
                            bool found = false;
                            for (int e = c + dx; e < cols; e++)
                            {
                                if (_data.Codes[(page * cellsPerPage) + (r * cols) + e] == 0)
                                {
                                    found = true;
                                    break;
                                }
                            }

                            if (!found)
                            {
                                return;
                            }
                        }
                    }
                }
            }
            else if (dx < 0)
            {
                int absDx = -dx;
                for (int r = minRow; r <= maxRow; r++)
                {
                    for (int c = minCol + dx; c <= minCol - 1; c++)
                    {
                        if (_data.Codes[(page * cellsPerPage) + (r * cols) + c] != 0)
                        {
                            bool found = false;
                            for (int e = c - absDx; e >= 0; e--)
                            {
                                if (_data.Codes[(page * cellsPerPage) + (r * cols) + e] == 0)
                                {
                                    found = true;
                                    break;
                                }
                            }

                            if (!found)
                            {
                                return;
                            }
                        }
                    }
                }
            }
            else if (dy > 0)
            {
                for (int c = minCol; c <= maxCol; c++)
                {
                    for (int r = maxRow + 1; r <= maxRow + dy; r++)
                    {
                        if (_data.Codes[(page * cellsPerPage) + (r * cols) + c] != 0)
                        {
                            bool found = false;
                            for (int e = r + dy; e < rows; e++)
                            {
                                if (_data.Codes[(page * cellsPerPage) + (e * cols) + c] == 0)
                                {
                                    found = true;
                                    break;
                                }
                            }

                            if (!found)
                            {
                                return;
                            }
                        }
                    }
                }
            }
            else if (dy < 0)
            {
                int absDy = -dy;
                for (int c = minCol; c <= maxCol; c++)
                {
                    for (int r = minRow + dy; r <= minRow - 1; r++)
                    {
                        if (_data.Codes[(page * cellsPerPage) + (r * cols) + c] != 0)
                        {
                            bool found = false;
                            for (int e = r - absDy; e >= 0; e--)
                            {
                                if (_data.Codes[(page * cellsPerPage) + (e * cols) + c] == 0)
                                {
                                    found = true;
                                    break;
                                }
                            }

                            if (!found)
                            {
                                return;
                            }
                        }
                    }
                }
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

            if (dx > 0)
            {
                for (int r = minRow; r <= maxRow; r++)
                {
                    for (int c = maxCol + dx; c >= maxCol + 1; c--)
                    {
                        int idx = (page * cellsPerPage) + (r * cols) + c;
                        if (_data.Codes[idx] == 0)
                        {
                            continue;
                        }

                        int emptyCol = -1;
                        for (int e = c + dx; e < cols; e++)
                        {
                            if (_data.Codes[(page * cellsPerPage) + (r * cols) + e] == 0)
                            {
                                emptyCol = e;
                                break;
                            }
                        }

                        if (emptyCol < 0)
                        {
                            continue;
                        }

                        int dst = (page * cellsPerPage) + (r * cols) + emptyCol;
                        _data.Codes[dst] = _data.Codes[idx];
                        _data.Labels[dst] = _data.Labels[idx];
                        _data.Values[dst] = _data.Values[idx];
                        _data.Codes[idx] = 0;
                        _data.Labels[idx] = null;
                        _data.Values[idx] = null;
                        _updateCell(r, c);
                        _updateCell(r, emptyCol);
                    }
                }
            }
            else if (dx < 0)
            {
                int absDx = -dx;
                for (int r = minRow; r <= maxRow; r++)
                {
                    for (int c = minCol + dx; c <= minCol - 1; c++)
                    {
                        int idx = (page * cellsPerPage) + (r * cols) + c;
                        if (_data.Codes[idx] == 0)
                        {
                            continue;
                        }

                        int emptyCol = -1;
                        for (int e = c - absDx; e >= 0; e--)
                        {
                            if (_data.Codes[(page * cellsPerPage) + (r * cols) + e] == 0)
                            {
                                emptyCol = e;
                                break;
                            }
                        }

                        if (emptyCol < 0)
                        {
                            continue;
                        }

                        int dst = (page * cellsPerPage) + (r * cols) + emptyCol;
                        _data.Codes[dst] = _data.Codes[idx];
                        _data.Labels[dst] = _data.Labels[idx];
                        _data.Values[dst] = _data.Values[idx];
                        _data.Codes[idx] = 0;
                        _data.Labels[idx] = null;
                        _data.Values[idx] = null;
                        _updateCell(r, c);
                        _updateCell(r, emptyCol);
                    }
                }
            }
            else if (dy > 0)
            {
                for (int c = minCol; c <= maxCol; c++)
                {
                    for (int r = maxRow + dy; r >= maxRow + 1; r--)
                    {
                        int idx = (page * cellsPerPage) + (r * cols) + c;
                        if (_data.Codes[idx] == 0)
                        {
                            continue;
                        }

                        int emptyRow = -1;
                        for (int e = r + dy; e < rows; e++)
                        {
                            if (_data.Codes[(page * cellsPerPage) + (e * cols) + c] == 0)
                            {
                                emptyRow = e;
                                break;
                            }
                        }

                        if (emptyRow < 0)
                        {
                            continue;
                        }

                        int dst = (page * cellsPerPage) + (emptyRow * cols) + c;
                        _data.Codes[dst] = _data.Codes[idx];
                        _data.Labels[dst] = _data.Labels[idx];
                        _data.Values[dst] = _data.Values[idx];
                        _data.Codes[idx] = 0;
                        _data.Labels[idx] = null;
                        _data.Values[idx] = null;
                        _updateCell(r, c);
                        _updateCell(emptyRow, c);
                    }
                }
            }
            else if (dy < 0)
            {
                int absDy = -dy;
                for (int c = minCol; c <= maxCol; c++)
                {
                    for (int r = minRow + dy; r <= minRow - 1; r++)
                    {
                        int idx = (page * cellsPerPage) + (r * cols) + c;
                        if (_data.Codes[idx] == 0)
                        {
                            continue;
                        }

                        int emptyRow = -1;
                        for (int e = r - absDy; e >= 0; e--)
                        {
                            if (_data.Codes[(page * cellsPerPage) + (e * cols) + c] == 0)
                            {
                                emptyRow = e;
                                break;
                            }
                        }

                        if (emptyRow < 0)
                        {
                            continue;
                        }

                        int dst = (page * cellsPerPage) + (emptyRow * cols) + c;
                        _data.Codes[dst] = _data.Codes[idx];
                        _data.Labels[dst] = _data.Labels[idx];
                        _data.Values[dst] = _data.Values[idx];
                        _data.Codes[idx] = 0;
                        _data.Labels[idx] = null;
                        _data.Values[idx] = null;
                        _updateCell(r, c);
                        _updateCell(emptyRow, c);
                    }
                }
            }

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
}

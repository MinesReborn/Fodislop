#nullable enable

using System.Collections.Generic;
using System.Linq;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.UI.Programmator
{
    public sealed class ProgrammatorData
    {
        public const int COLS = 16;
        public const int ROWS = 12;
        public const int CELLS_PER_PAGE = COLS * ROWS;

        public List<int> Codes = new(new int[CELLS_PER_PAGE]);
        public List<string?> Values = new(new string?[CELLS_PER_PAGE]);
        public List<string?> Labels = new(new string?[CELLS_PER_PAGE]);
        public int PageCount => Codes.Count / CELLS_PER_PAGE;

        public int CurrentPage;
        public int HoveredCell = -1;

        public void AddPage()
        {
            if (PageCount >= 100)
            {
                return;
            }

            Codes.AddRange(new int[CELLS_PER_PAGE]);
            Values.AddRange(new string?[CELLS_PER_PAGE]);
            Labels.AddRange(new string?[CELLS_PER_PAGE]);
        }

        public bool RemoveLastPage()
        {
            if (PageCount <= 1)
            {
                return false;
            }

            PushUndo();
            int start = (PageCount - 1) * CELLS_PER_PAGE;
            Codes.RemoveRange(start, CELLS_PER_PAGE);
            Values.RemoveRange(start, CELLS_PER_PAGE);
            Labels.RemoveRange(start, CELLS_PER_PAGE);
            if (CurrentPage >= PageCount)
            {
                CurrentPage = PageCount - 1;
            }

            return true;
        }

        private struct UndoSnapshot
        {
            public int[] Codes;
            public string?[] Labels;
            public string?[] Values;
        }

        private readonly Stack<UndoSnapshot> _undoStack = new();
        private readonly Stack<UndoSnapshot> _redoStack = new();
        private const int MAX_UNDO_STEPS = 50;

        public void PushUndo()
        {
            _undoStack.Push(new UndoSnapshot
            {
                Codes = Codes.ToArray(),
                Labels = Labels.ToArray(),
                Values = Values.ToArray(),
            });
            _redoStack.Clear();

            if (_undoStack.Count > MAX_UNDO_STEPS)
            {
                var temp = _undoStack.ToArray();
                _undoStack.Clear();
                for (int i = temp.Length - MAX_UNDO_STEPS + 1; i < temp.Length; i++)
                {
                    _undoStack.Push(temp[i]);
                }
            }
        }

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public bool Undo()
        {
            if (_undoStack.Count == 0)
            {
                return false;
            }

            _redoStack.Push(new UndoSnapshot
            {
                Codes = Codes.ToArray(),
                Labels = Labels.ToArray(),
                Values = Values.ToArray(),
            });
            var snap = _undoStack.Pop();
            Codes = new List<int>(snap.Codes);
            Labels = new List<string?>(snap.Labels);
            Values = new List<string?>(snap.Values);
            return true;
        }

        public bool Redo()
        {
            if (_redoStack.Count == 0)
            {
                return false;
            }

            _undoStack.Push(new UndoSnapshot
            {
                Codes = Codes.ToArray(),
                Labels = Labels.ToArray(),
                Values = Values.ToArray(),
            });
            var snap = _redoStack.Pop();
            Codes = new List<int>(snap.Codes);
            Labels = new List<string?>(snap.Labels);
            Values = new List<string?>(snap.Values);
            return true;
        }

        public static readonly ProgAction[] WOPERATORS = ProgrammatorOperators.WOPERATORS;
        public static readonly ProgAction[] SHIFTWOPERATORS = ProgrammatorOperators.SHIFTWOPERATORS;

        // ─── Operator Categories ──────────────────────────────────────
        public const int CAT_CONTROL_FLOW = ProgrammatorOperators.CAT_CONTROL_FLOW;
        public const int CAT_ACTIONS = ProgrammatorOperators.CAT_ACTIONS;
        public const int CAT_OBSERVER = ProgrammatorOperators.CAT_OBSERVER;
        public const int CAT_CONDITIONS = ProgrammatorOperators.CAT_CONDITIONS;
        public const int CAT_MEMORY = ProgrammatorOperators.CAT_MEMORY;

        public static readonly int[] CATEGORIES = ProgrammatorOperators.CATEGORIES;

        public static readonly IReadOnlyDictionary<int, string> CATEGORY_NAMES = ProgrammatorOperators.CATEGORY_NAMES;

        public static readonly IReadOnlyDictionary<int, Color> CATEGORY_COLORS = ProgrammatorOperators.CATEGORY_COLORS;

        public static readonly IReadOnlyDictionary<int, ProgAction[]> CATEGORY_OPERATORS = ProgrammatorOperators.CATEGORY_OPERATORS;

        public static readonly IReadOnlyDictionary<ProgAction, string> OPERATOR_DESCRIPTIONS = ProgrammatorLocalization.OPERATOR_DESCRIPTIONS;

        public static readonly IReadOnlyDictionary<ProgAction, string> OPERATOR_NAMES = ProgrammatorLocalization.OPERATOR_NAMES;
    }
}

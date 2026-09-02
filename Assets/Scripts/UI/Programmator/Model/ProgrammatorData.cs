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

        public static readonly ProgAction[] WOPERATORS =
        {
            ProgAction.MoveForward, ProgAction.RotateUp, ProgAction.RotateDown,
            ProgAction.RotateLeft, ProgAction.RotateRight,
        };

        public static readonly ProgAction[] SHIFTWOPERATORS =
        {
            ProgAction.MoveForward, ProgAction.RotateUp, ProgAction.RotateDown,
            ProgAction.RotateLeft, ProgAction.RotateRight,
        };

        // ─── Operator Categories ──────────────────────────────────────
        public const int CAT_CONTROL_FLOW = -1;
        public const int CAT_ACTIONS = -2;
        public const int CAT_OBSERVER = -3;
        public const int CAT_CONDITIONS = -4;
        public const int CAT_MEMORY = -5;

        public static readonly int[] CATEGORIES = { CAT_CONTROL_FLOW, CAT_ACTIONS, CAT_OBSERVER, CAT_CONDITIONS, CAT_MEMORY };

        public static readonly IReadOnlyDictionary<int, string> CATEGORY_NAMES = new Dictionary<int, string>()
        {
            [CAT_CONTROL_FLOW] = "programmator.cat.CAT_CONTROL_FLOW",
            [CAT_ACTIONS] = "programmator.cat.CAT_ACTIONS",
            [CAT_OBSERVER] = "programmator.cat.CAT_OBSERVER",
            [CAT_CONDITIONS] = "programmator.cat.CAT_CONDITIONS",
            [CAT_MEMORY] = "programmator.cat.CAT_MEMORY",
        };

        public static readonly IReadOnlyDictionary<int, Color> CATEGORY_COLORS = new Dictionary<int, Color>()
        {
            [CAT_CONTROL_FLOW] = Color.white,
            [CAT_ACTIONS] = Color.yellow,
            [CAT_OBSERVER] = Color.cyan,
            [CAT_CONDITIONS] = Color.green,
            [CAT_MEMORY] = Color.magenta,
        };

        public static readonly IReadOnlyDictionary<int, ProgAction[]> CATEGORY_OPERATORS = new Dictionary<int, ProgAction[]>()
        {
            [CAT_CONTROL_FLOW] = new[]
            {
                ProgAction.NextLine, ProgAction.SetStart, ProgAction.Terminate,
                ProgAction.RepeatLastAction,
                ProgAction.Goto, ProgAction.Call, ProgAction.CallArg,
                ProgAction.Return, ProgAction.ReturnArg,
                ProgAction.Label, ProgAction.YesNoReturn, ProgAction.NoYesReturn,
                ProgAction.YesNoGoto, ProgAction.NoYesGoto,
                ProgAction.YesNoNextRow, ProgAction.NoYesNextRow,
                ProgAction.YesNoGotoStart, ProgAction.NoYesGotoStart,
                ProgAction.YesNoTerminate, ProgAction.NoYesTerminate,
                ProgAction.CallWhenDied,
                ProgAction.CallState, ProgAction.ReturnState,
                ProgAction.DebugPause, ProgAction.DebugShow,
                ProgAction.EnableAutoDig, ProgAction.DisableAutoDig,
                ProgAction.EnableAggression, ProgAction.DisableAggression,
                ProgAction.EnableHand, ProgAction.DisableHand,
                ProgAction.SetStartWhenDied, ProgAction.SetStartWhenHurt,
                ProgAction.SetStartWhenBotNearby,
            },
            [CAT_ACTIONS] = new[]
            {
                ProgAction.MoveUp, ProgAction.MoveLeft, ProgAction.MoveDown, ProgAction.MoveRight,
                ProgAction.MoveForward,
                ProgAction.RotateUp, ProgAction.RotateLeft, ProgAction.RotateDown, ProgAction.RotateRight,
                ProgAction.RotateLefthand, ProgAction.RotateRighthand,
                ProgAction.RotateRandom,
                ProgAction.Flip,
                ProgAction.Dig, ProgAction.STDDig,
                ProgAction.BuildBlock, ProgAction.UseGeo, ProgAction.BuildRoad,
                ProgAction.Heal, ProgAction.BuildQuadro, ProgAction.STDBlock, ProgAction.STDHeal,
                ProgAction.STDTunnel,
                ProgAction.PlaySound,
                ProgAction.UseBoom, ProgAction.UseRaz, ProgAction.UseProt,
                ProgAction.BuildWar,
                ProgAction.UseGeopack, ProgAction.UseZZ, ProgAction.UseC190, ProgAction.UsePoly,
                ProgAction.Upgrade, ProgAction.RefillCraft,
                ProgAction.UseNano, ProgAction.UseRem,
                ProgAction.ChargeGun,
                ProgAction.InventoryUp, ProgAction.InventoryLeft,
                ProgAction.InventoryDown, ProgAction.InventoryRight,
                ProgAction.BoxAll, ProgAction.BoxHalf,
                ProgAction.BoxWhite, ProgAction.BoxGreen, ProgAction.BoxRed,
                ProgAction.BoxBlue, ProgAction.BoxCyan, ProgAction.BoxViolet,
            },
            [CAT_OBSERVER] = new[]
            {
                ProgAction.CellUpLeft, ProgAction.CellDownRight,
                ProgAction.CellUp, ProgAction.CellUpRight,
                ProgAction.CellLeft, ProgAction.Cell, ProgAction.CellRight,
                ProgAction.CellDownLeft, ProgAction.CellDown,
                ProgAction.CellForward,
                ProgAction.CellLefthand, ProgAction.CellRighthand,
                ProgAction.ShiftLefthand, ProgAction.ShiftRighthand, ProgAction.ShiftBackwards,
                ProgAction.ShiftUp, ProgAction.ShiftLeft, ProgAction.ShiftDown, ProgAction.ShiftRight,
                ProgAction.ShiftForward,
            },
            [CAT_CONDITIONS] = new[]
            {
                ProgAction.BooleanOR, ProgAction.BooleanAND,
                ProgAction.IsNotEmpty, ProgAction.IsEmpty,
                ProgAction.IsFalling, ProgAction.IsCrystal, ProgAction.IsAliveCrystal,
                ProgAction.IsFallingLikeBoulder, ProgAction.IsFallingLikeLiquid,
                ProgAction.IsBreakable, ProgAction.IsUnbreakable,
                ProgAction.IsRedRock, ProgAction.IsBlackRock,
                ProgAction.IsAcid, ProgAction.IsAcidRock,
                ProgAction.IsSand, ProgAction.IsQuadro, ProgAction.IsRoad,
                ProgAction.IsRedBlock, ProgAction.IsYellowBlock,
                ProgAction.IsBoulder, ProgAction.IsLava,
                ProgAction.IsCyanAlive, ProgAction.IsWhiteAlive,
                ProgAction.IsRedAlive, ProgAction.IsVioletAlive,
                ProgAction.IsBlackAlive, ProgAction.IsBlueAlive,
                ProgAction.IsRainbowAlive,
                ProgAction.IsBox, ProgAction.IsStructure, ProgAction.IsGreenBlock,
                ProgAction.IsBasketFull, ProgAction.IsGeoFull,
                ProgAction.IsInsideGun,
                ProgAction.IsHealthNotFull, ProgAction.IsHealthLessThanHalf,
            },
            [CAT_MEMORY] = new[]
            {
                ProgAction.WriteStateToVar, ProgAction.ReadVarToState,
                ProgAction.SetNumberToVar,
                ProgAction.AddNumberToVar, ProgAction.MultNumberToVar,
                ProgAction.DivNumberToVar, ProgAction.SubNumberToVar,
                ProgAction.AddStateToVar, ProgAction.MultStateToVar,
                ProgAction.DivStateToVar, ProgAction.SubStateToVar,
                ProgAction.AddVarToVar, ProgAction.MultVarToVar,
                ProgAction.DivVarToVar, ProgAction.SubVarToVar,
                ProgAction.VarLessThanState, ProgAction.VarGreaterThanState,
                ProgAction.VarGreaterThanOrEqualsState,
                ProgAction.VarLessThanOrEqualState,
                ProgAction.VarEqualsState, ProgAction.VarNotEqualsState,
                ProgAction.VarGreaterThanNumber, ProgAction.VarLessThanNumber,
                ProgAction.VarGreaterThanOrEqualNumber,
                ProgAction.VarLessThanOrEqualNumber,
                ProgAction.VarEqualsNumber, ProgAction.VarNotEqualsNumber,
                ProgAction.VarRound, ProgAction.VarCeil, ProgAction.VarFloor,
            },
        };

        public static readonly IReadOnlyDictionary<ProgAction, string> OPERATOR_DESCRIPTIONS = new Dictionary<ProgAction, string>()
        {
            [ProgAction.None] = "programmator.opdesc.None",
            [ProgAction.NextLine] = "programmator.opdesc.NextLine",
            [ProgAction.SetStart] = "programmator.opdesc.SetStart",
            [ProgAction.Terminate] = "programmator.opdesc.Terminate",
            [ProgAction.MoveUp] = "programmator.opdesc.MoveUp",
            [ProgAction.MoveLeft] = "programmator.opdesc.MoveLeft",
            [ProgAction.MoveDown] = "programmator.opdesc.MoveDown",
            [ProgAction.MoveRight] = "programmator.opdesc.MoveRight",
            [ProgAction.Dig] = "programmator.opdesc.Dig",
            [ProgAction.RotateUp] = "programmator.opdesc.RotateUp",
            [ProgAction.RotateLeft] = "programmator.opdesc.RotateLeft",
            [ProgAction.RotateDown] = "programmator.opdesc.RotateDown",
            [ProgAction.RotateRight] = "programmator.opdesc.RotateRight",
            [ProgAction.RepeatLastAction] = "programmator.opdesc.RepeatLastAction",
            [ProgAction.MoveForward] = "programmator.opdesc.MoveForward",
            [ProgAction.RotateLefthand] = "programmator.opdesc.RotateLefthand",
            [ProgAction.RotateRighthand] = "programmator.opdesc.RotateRighthand",
            [ProgAction.BuildBlock] = "programmator.opdesc.BuildBlock",
            [ProgAction.UseGeo] = "programmator.opdesc.UseGeo",
            [ProgAction.BuildRoad] = "programmator.opdesc.BuildRoad",
            [ProgAction.Heal] = "programmator.opdesc.Heal",
            [ProgAction.BuildQuadro] = "programmator.opdesc.BuildQuadro",
            [ProgAction.RotateRandom] = "programmator.opdesc.RotateRandom",
            [ProgAction.PlaySound] = "programmator.opdesc.PlaySound",
            [ProgAction.Goto] = "programmator.opdesc.Goto",
            [ProgAction.Call] = "programmator.opdesc.Call",
            [ProgAction.CallArg] = "programmator.opdesc.CallArg",
            [ProgAction.Return] = "programmator.opdesc.Return",
            [ProgAction.ReturnArg] = "programmator.opdesc.ReturnArg",
            [ProgAction.CellUpLeft] = "programmator.opdesc.CellUpLeft",
            [ProgAction.CellDownRight] = "programmator.opdesc.CellDownRight",
            [ProgAction.CellUp] = "programmator.opdesc.CellUp",
            [ProgAction.CellUpRight] = "programmator.opdesc.CellUpRight",
            [ProgAction.CellLeft] = "programmator.opdesc.CellLeft",
            [ProgAction.Cell] = "programmator.opdesc.Cell",
            [ProgAction.CellRight] = "programmator.opdesc.CellRight",
            [ProgAction.CellDownLeft] = "programmator.opdesc.CellDownLeft",
            [ProgAction.CellDown] = "programmator.opdesc.CellDown",
            [ProgAction.BooleanOR] = "programmator.opdesc.BooleanOR",
            [ProgAction.BooleanAND] = "programmator.opdesc.BooleanAND",
            [ProgAction.Label] = "programmator.opdesc.Label",
            [ProgAction.YesNoReturn] = "programmator.opdesc.YesNoReturn",
            [ProgAction.NoYesReturn] = "programmator.opdesc.NoYesReturn",
            [ProgAction.IsNotEmpty] = "programmator.opdesc.IsNotEmpty",
            [ProgAction.IsEmpty] = "programmator.opdesc.IsEmpty",
            [ProgAction.IsFalling] = "programmator.opdesc.IsFalling",
            [ProgAction.IsCrystal] = "programmator.opdesc.IsCrystal",
            [ProgAction.IsAliveCrystal] = "programmator.opdesc.IsAliveCrystal",
            [ProgAction.IsFallingLikeBoulder] = "programmator.opdesc.IsFallingLikeBoulder",
            [ProgAction.IsFallingLikeLiquid] = "programmator.opdesc.IsFallingLikeLiquid",
            [ProgAction.IsBreakable] = "programmator.opdesc.IsBreakable",
            [ProgAction.IsUnbreakable] = "programmator.opdesc.IsUnbreakable",
            [ProgAction.IsRedRock] = "programmator.opdesc.IsRedRock",
            [ProgAction.IsBlackRock] = "programmator.opdesc.IsBlackRock",
            [ProgAction.IsAcid] = "programmator.opdesc.IsAcid",
            [ProgAction.IsSand] = "programmator.opdesc.IsSand",
            [ProgAction.IsQuadro] = "programmator.opdesc.IsQuadro",
            [ProgAction.IsRoad] = "programmator.opdesc.IsRoad",
            [ProgAction.IsRedBlock] = "programmator.opdesc.IsRedBlock",
            [ProgAction.IsYellowBlock] = "programmator.opdesc.IsYellowBlock",
            [ProgAction.IsAcidRock] = "programmator.opdesc.IsAcidRock",
            [ProgAction.IsBoulder] = "programmator.opdesc.IsBoulder",
            [ProgAction.IsLava] = "programmator.opdesc.IsLava",
            [ProgAction.IsCyanAlive] = "programmator.opdesc.IsCyanAlive",
            [ProgAction.IsWhiteAlive] = "programmator.opdesc.IsWhiteAlive",
            [ProgAction.IsRedAlive] = "programmator.opdesc.IsRedAlive",
            [ProgAction.IsVioletAlive] = "programmator.opdesc.IsVioletAlive",
            [ProgAction.IsBlackAlive] = "programmator.opdesc.IsBlackAlive",
            [ProgAction.IsBlueAlive] = "programmator.opdesc.IsBlueAlive",
            [ProgAction.IsRainbowAlive] = "programmator.opdesc.IsRainbowAlive",
            [ProgAction.IsBox] = "programmator.opdesc.IsBox",
            [ProgAction.IsStructure] = "programmator.opdesc.IsStructure",
            [ProgAction.IsGreenBlock] = "programmator.opdesc.IsGreenBlock",
            [ProgAction.IsBasketFull] = "programmator.opdesc.IsBasketFull",
            [ProgAction.IsGeoFull] = "programmator.opdesc.IsGeoFull",
            [ProgAction.SetStartWhenDied] = "programmator.opdesc.SetStartWhenDied",
            [ProgAction.SetStartWhenHurt] = "programmator.opdesc.SetStartWhenHurt",
            [ProgAction.SetStartWhenBotNearby] = "programmator.opdesc.SetStartWhenBotNearby",
            [ProgAction.ShiftLefthand] = "programmator.opdesc.ShiftLefthand",
            [ProgAction.ShiftRighthand] = "programmator.opdesc.ShiftRighthand",
            [ProgAction.ShiftBackwards] = "programmator.opdesc.ShiftBackwards",
            [ProgAction.BoxAll] = "programmator.opdesc.BoxAll",
            [ProgAction.BoxHalf] = "programmator.opdesc.BoxHalf",
            [ProgAction.BoxWhite] = "programmator.opdesc.BoxWhite",
            [ProgAction.BoxGreen] = "programmator.opdesc.BoxGreen",
            [ProgAction.BoxRed] = "programmator.opdesc.BoxRed",
            [ProgAction.BoxBlue] = "programmator.opdesc.BoxBlue",
            [ProgAction.BoxCyan] = "programmator.opdesc.BoxCyan",
            [ProgAction.BoxViolet] = "programmator.opdesc.BoxViolet",
            [ProgAction.WriteStateToVar] = "programmator.opdesc.WriteStateToVar",
            [ProgAction.ReadVarToState] = "programmator.opdesc.ReadVarToState",
            [ProgAction.SetNumberToVar] = "programmator.opdesc.SetNumberToVar",
            [ProgAction.AddNumberToVar] = "programmator.opdesc.AddNumberToVar",
            [ProgAction.MultNumberToVar] = "programmator.opdesc.MultNumberToVar",
            [ProgAction.DivNumberToVar] = "programmator.opdesc.DivNumberToVar",
            [ProgAction.SubNumberToVar] = "programmator.opdesc.SubNumberToVar",
            [ProgAction.AddStateToVar] = "programmator.opdesc.AddStateToVar",
            [ProgAction.MultStateToVar] = "programmator.opdesc.MultStateToVar",
            [ProgAction.DivStateToVar] = "programmator.opdesc.DivStateToVar",
            [ProgAction.SubStateToVar] = "programmator.opdesc.SubStateToVar",
            [ProgAction.AddVarToVar] = "programmator.opdesc.AddVarToVar",
            [ProgAction.MultVarToVar] = "programmator.opdesc.MultVarToVar",
            [ProgAction.DivVarToVar] = "programmator.opdesc.DivVarToVar",
            [ProgAction.SubVarToVar] = "programmator.opdesc.SubVarToVar",
            [ProgAction.VarLessThanState] = "programmator.opdesc.VarLessThanState",
            [ProgAction.VarGreaterThanState] = "programmator.opdesc.VarGreaterThanState",
            [ProgAction.VarGreaterThanOrEqualsState] = "programmator.opdesc.VarGreaterThanOrEqualsState",
            [ProgAction.VarLessThanOrEqualState] = "programmator.opdesc.VarLessThanOrEqualState",
            [ProgAction.VarEqualsState] = "programmator.opdesc.VarEqualsState",
            [ProgAction.VarNotEqualsState] = "programmator.opdesc.VarNotEqualsState",
            [ProgAction.VarGreaterThanNumber] = "programmator.opdesc.VarGreaterThanNumber",
            [ProgAction.VarLessThanNumber] = "programmator.opdesc.VarLessThanNumber",
            [ProgAction.VarGreaterThanOrEqualNumber] = "programmator.opdesc.VarGreaterThanOrEqualNumber",
            [ProgAction.VarLessThanOrEqualNumber] = "programmator.opdesc.VarLessThanOrEqualNumber",
            [ProgAction.VarEqualsNumber] = "programmator.opdesc.VarEqualsNumber",
            [ProgAction.VarNotEqualsNumber] = "programmator.opdesc.VarNotEqualsNumber",
            [ProgAction.VarRound] = "programmator.opdesc.VarRound",
            [ProgAction.VarCeil] = "programmator.opdesc.VarCeil",
            [ProgAction.VarFloor] = "programmator.opdesc.VarFloor",
            [ProgAction.ShiftUp] = "programmator.opdesc.ShiftUp",
            [ProgAction.ShiftLeft] = "programmator.opdesc.ShiftLeft",
            [ProgAction.ShiftDown] = "programmator.opdesc.ShiftDown",
            [ProgAction.ShiftRight] = "programmator.opdesc.ShiftRight",
            [ProgAction.CellForward] = "programmator.opdesc.CellForward",
            [ProgAction.ShiftForward] = "programmator.opdesc.ShiftForward",
            [ProgAction.CallState] = "programmator.opdesc.CallState",
            [ProgAction.ReturnState] = "programmator.opdesc.ReturnState",
            [ProgAction.YesNoGoto] = "programmator.opdesc.YesNoGoto",
            [ProgAction.NoYesGoto] = "programmator.opdesc.NoYesGoto",
            [ProgAction.STDDig] = "programmator.opdesc.STDDig",
            [ProgAction.STDBlock] = "programmator.opdesc.STDBlock",
            [ProgAction.STDHeal] = "programmator.opdesc.STDHeal",
            [ProgAction.Flip] = "programmator.opdesc.Flip",
            [ProgAction.STDTunnel] = "programmator.opdesc.STDTunnel",
            [ProgAction.IsInsideGun] = "programmator.opdesc.IsInsideGun",
            [ProgAction.ChargeGun] = "programmator.opdesc.ChargeGun",
            [ProgAction.IsHealthNotFull] = "programmator.opdesc.IsHealthNotFull",
            [ProgAction.IsHealthLessThanHalf] = "programmator.opdesc.IsHealthLessThanHalf",
            [ProgAction.YesNoNextRow] = "programmator.opdesc.YesNoNextRow",
            [ProgAction.NoYesNextRow] = "programmator.opdesc.NoYesNextRow",
            [ProgAction.YesNoGotoStart] = "programmator.opdesc.YesNoGotoStart",
            [ProgAction.NoYesGotoStart] = "programmator.opdesc.NoYesGotoStart",
            [ProgAction.YesNoTerminate] = "programmator.opdesc.YesNoTerminate",
            [ProgAction.NoYesTerminate] = "programmator.opdesc.NoYesTerminate",
            [ProgAction.CellLefthand] = "programmator.opdesc.CellLefthand",
            [ProgAction.CellRighthand] = "programmator.opdesc.CellRighthand",
            [ProgAction.EnableAutoDig] = "programmator.opdesc.EnableAutoDig",
            [ProgAction.DisableAutoDig] = "programmator.opdesc.DisableAutoDig",
            [ProgAction.EnableAggression] = "programmator.opdesc.EnableAggression",
            [ProgAction.DisableAggression] = "programmator.opdesc.DisableAggression",
            [ProgAction.UseBoom] = "programmator.opdesc.UseBoom",
            [ProgAction.UseRaz] = "programmator.opdesc.UseRaz",
            [ProgAction.UseProt] = "programmator.opdesc.UseProt",
            [ProgAction.BuildWar] = "programmator.opdesc.BuildWar",
            [ProgAction.CallWhenDied] = "programmator.opdesc.CallWhenDied",
            [ProgAction.UseGeopack] = "programmator.opdesc.UseGeopack",
            [ProgAction.UseZZ] = "programmator.opdesc.UseZZ",
            [ProgAction.UseC190] = "programmator.opdesc.UseC190",
            [ProgAction.UsePoly] = "programmator.opdesc.UsePoly",
            [ProgAction.Upgrade] = "programmator.opdesc.Upgrade",
            [ProgAction.RefillCraft] = "programmator.opdesc.RefillCraft",
            [ProgAction.UseNano] = "programmator.opdesc.UseNano",
            [ProgAction.UseRem] = "programmator.opdesc.UseRem",
            [ProgAction.InventoryUp] = "programmator.opdesc.InventoryUp",
            [ProgAction.InventoryLeft] = "programmator.opdesc.InventoryLeft",
            [ProgAction.InventoryDown] = "programmator.opdesc.InventoryDown",
            [ProgAction.InventoryRight] = "programmator.opdesc.InventoryRight",
            [ProgAction.EnableHand] = "programmator.opdesc.EnableHand",
            [ProgAction.DisableHand] = "programmator.opdesc.DisableHand",
            [ProgAction.DebugPause] = "programmator.opdesc.DebugPause",
            [ProgAction.DebugShow] = "programmator.opdesc.DebugShow",
        };

        public static readonly IReadOnlyDictionary<ProgAction, string> OPERATOR_NAMES = new Dictionary<ProgAction, string>()
        {
            [ProgAction.None] = "programmator.op.None",
            [ProgAction.NextLine] = "programmator.op.NextLine",
            [ProgAction.SetStart] = "programmator.op.SetStart",
            [ProgAction.Terminate] = "programmator.op.Terminate",
            [ProgAction.MoveUp] = "programmator.op.MoveUp",
            [ProgAction.MoveLeft] = "programmator.op.MoveLeft",
            [ProgAction.MoveDown] = "programmator.op.MoveDown",
            [ProgAction.MoveRight] = "programmator.op.MoveRight",
            [ProgAction.Dig] = "programmator.op.Dig",
            [ProgAction.RotateUp] = "programmator.op.RotateUp",
            [ProgAction.RotateLeft] = "programmator.op.RotateLeft",
            [ProgAction.RotateDown] = "programmator.op.RotateDown",
            [ProgAction.RotateRight] = "programmator.op.RotateRight",
            [ProgAction.RepeatLastAction] = "programmator.op.RepeatLastAction",
            [ProgAction.MoveForward] = "programmator.op.MoveForward",
            [ProgAction.RotateLefthand] = "programmator.op.RotateLefthand",
            [ProgAction.RotateRighthand] = "programmator.op.RotateRighthand",
            [ProgAction.BuildBlock] = "programmator.op.BuildBlock",
            [ProgAction.UseGeo] = "programmator.op.UseGeo",
            [ProgAction.BuildRoad] = "programmator.op.BuildRoad",
            [ProgAction.Heal] = "programmator.op.Heal",
            [ProgAction.BuildQuadro] = "programmator.op.BuildQuadro",
            [ProgAction.RotateRandom] = "programmator.op.RotateRandom",
            [ProgAction.PlaySound] = "programmator.op.PlaySound",
            [ProgAction.Goto] = "programmator.op.Goto",
            [ProgAction.Call] = "programmator.op.Call",
            [ProgAction.CallArg] = "programmator.op.CallArg",
            [ProgAction.Return] = "programmator.op.Return",
            [ProgAction.ReturnArg] = "programmator.op.ReturnArg",
            [ProgAction.CellUpLeft] = "programmator.op.CellUpLeft",
            [ProgAction.CellDownRight] = "programmator.op.CellDownRight",
            [ProgAction.CellUp] = "programmator.op.CellUp",
            [ProgAction.CellUpRight] = "programmator.op.CellUpRight",
            [ProgAction.CellLeft] = "programmator.op.CellLeft",
            [ProgAction.Cell] = "programmator.op.Cell",
            [ProgAction.CellRight] = "programmator.op.CellRight",
            [ProgAction.CellDownLeft] = "programmator.op.CellDownLeft",
            [ProgAction.CellDown] = "programmator.op.CellDown",
            [ProgAction.BooleanOR] = "programmator.op.BooleanOR",
            [ProgAction.BooleanAND] = "programmator.op.BooleanAND",
            [ProgAction.Label] = "programmator.op.Label",
            [ProgAction.YesNoReturn] = "programmator.op.YesNoReturn",
            [ProgAction.NoYesReturn] = "programmator.op.NoYesReturn",
            [ProgAction.IsNotEmpty] = "programmator.op.IsNotEmpty",
            [ProgAction.IsEmpty] = "programmator.op.IsEmpty",
            [ProgAction.IsFalling] = "programmator.op.IsFalling",
            [ProgAction.IsCrystal] = "programmator.op.IsCrystal",
            [ProgAction.IsAliveCrystal] = "programmator.op.IsAliveCrystal",
            [ProgAction.IsFallingLikeBoulder] = "programmator.op.IsFallingLikeBoulder",
            [ProgAction.IsFallingLikeLiquid] = "programmator.op.IsFallingLikeLiquid",
            [ProgAction.IsBreakable] = "programmator.op.IsBreakable",
            [ProgAction.IsUnbreakable] = "programmator.op.IsUnbreakable",
            [ProgAction.IsRedRock] = "programmator.op.IsRedRock",
            [ProgAction.IsBlackRock] = "programmator.op.IsBlackRock",
            [ProgAction.IsAcid] = "programmator.op.IsAcid",
            [ProgAction.UNKNOWN_CONDITION] = "programmator.op.UNKNOWN_CONDITION",
            [ProgAction.IsSand] = "programmator.op.IsSand",
            [ProgAction.IsQuadro] = "programmator.op.IsQuadro",
            [ProgAction.IsRoad] = "programmator.op.IsRoad",
            [ProgAction.IsRedBlock] = "programmator.op.IsRedBlock",
            [ProgAction.IsYellowBlock] = "programmator.op.IsYellowBlock",
            [ProgAction.UNKNOWN_MINUS_HEALTH] = "programmator.op.UNKNOWN_MINUS_HEALTH",
            [ProgAction.UNKNOWN_LESS_HEALTH] = "programmator.op.UNKNOWN_LESS_HEALTH",
            [ProgAction.IsAcidRock] = "programmator.op.IsAcidRock",
            [ProgAction.IsBoulder] = "programmator.op.IsBoulder",
            [ProgAction.IsLava] = "programmator.op.IsLava",
            [ProgAction.IsCyanAlive] = "programmator.op.IsCyanAlive",
            [ProgAction.IsWhiteAlive] = "programmator.op.IsWhiteAlive",
            [ProgAction.IsRedAlive] = "programmator.op.IsRedAlive",
            [ProgAction.IsVioletAlive] = "programmator.op.IsVioletAlive",
            [ProgAction.IsBlackAlive] = "programmator.op.IsBlackAlive",
            [ProgAction.IsBlueAlive] = "programmator.op.IsBlueAlive",
            [ProgAction.IsRainbowAlive] = "programmator.op.IsRainbowAlive",
            [ProgAction.UNKNOWN_73] = "programmator.op.UNKNOWN_73",
            [ProgAction.IsBox] = "programmator.op.IsBox",
            [ProgAction.UNKNOWN_75] = "programmator.op.UNKNOWN_75",
            [ProgAction.IsStructure] = "programmator.op.IsStructure",
            [ProgAction.IsGreenBlock] = "programmator.op.IsGreenBlock",
            [ProgAction.IsBasketFull] = "programmator.op.IsBasketFull",
            [ProgAction.IsGeoFull] = "programmator.op.IsGeoFull",
            [ProgAction.UNKNOWN_80] = "programmator.op.UNKNOWN_80",
            [ProgAction.UNKNOWN_84] = "programmator.op.UNKNOWN_84",
            [ProgAction.UNKNOWN_85] = "programmator.op.UNKNOWN_85",
            [ProgAction.ShiftLefthand] = "programmator.op.ShiftLefthand",
            [ProgAction.ShiftRighthand] = "programmator.op.ShiftRighthand",
            [ProgAction.ShiftBackwards] = "programmator.op.ShiftBackwards",
            [ProgAction.BoxAll] = "programmator.op.BoxAll",
            [ProgAction.BoxHalf] = "programmator.op.BoxHalf",
            [ProgAction.BoxWhite] = "programmator.op.BoxWhite",
            [ProgAction.BoxGreen] = "programmator.op.BoxGreen",
            [ProgAction.BoxRed] = "programmator.op.BoxRed",
            [ProgAction.BoxBlue] = "programmator.op.BoxBlue",
            [ProgAction.BoxCyan] = "programmator.op.BoxCyan",
            [ProgAction.BoxViolet] = "programmator.op.BoxViolet",
            [ProgAction.WriteStateToVar] = "programmator.op.WriteStateToVar",
            [ProgAction.ReadVarToState] = "programmator.op.ReadVarToState",
            [ProgAction.SetNumberToVar] = "programmator.op.SetNumberToVar",
            [ProgAction.AddNumberToVar] = "programmator.op.AddNumberToVar",
            [ProgAction.MultNumberToVar] = "programmator.op.MultNumberToVar",
            [ProgAction.DivNumberToVar] = "programmator.op.DivNumberToVar",
            [ProgAction.SubNumberToVar] = "programmator.op.SubNumberToVar",
            [ProgAction.AddStateToVar] = "programmator.op.AddStateToVar",
            [ProgAction.MultStateToVar] = "programmator.op.MultStateToVar",
            [ProgAction.DivStateToVar] = "programmator.op.DivStateToVar",
            [ProgAction.SubStateToVar] = "programmator.op.SubStateToVar",
            [ProgAction.AddVarToVar] = "programmator.op.AddVarToVar",
            [ProgAction.MultVarToVar] = "programmator.op.MultVarToVar",
            [ProgAction.DivVarToVar] = "programmator.op.DivVarToVar",
            [ProgAction.SubVarToVar] = "programmator.op.SubVarToVar",
            [ProgAction.VarLessThanState] = "programmator.op.VarLessThanState",
            [ProgAction.VarGreaterThanState] = "programmator.op.VarGreaterThanState",
            [ProgAction.VarGreaterThanOrEqualsState] = "programmator.op.VarGreaterThanOrEqualsState",
            [ProgAction.VarLessThanOrEqualState] = "programmator.op.VarLessThanOrEqualState",
            [ProgAction.VarEqualsState] = "programmator.op.VarEqualsState",
            [ProgAction.VarNotEqualsState] = "programmator.op.VarNotEqualsState",
            [ProgAction.UNKNOWN_118] = "programmator.op.UNKNOWN_118",
            [ProgAction.VarGreaterThanNumber] = "programmator.op.VarGreaterThanNumber",
            [ProgAction.VarLessThanNumber] = "programmator.op.VarLessThanNumber",
            [ProgAction.VarGreaterThanOrEqualNumber] = "programmator.op.VarGreaterThanOrEqualNumber",
            [ProgAction.VarLessThanOrEqualNumber] = "programmator.op.VarLessThanOrEqualNumber",
            [ProgAction.VarEqualsNumber] = "programmator.op.VarEqualsNumber",
            [ProgAction.VarNotEqualsNumber] = "programmator.op.VarNotEqualsNumber",
            [ProgAction.VarRound] = "programmator.op.VarRound",
            [ProgAction.VarCeil] = "programmator.op.VarCeil",
            [ProgAction.VarFloor] = "programmator.op.VarFloor",
            [ProgAction.Var_UNK_128] = "programmator.op.Var_UNK_128",
            [ProgAction.Var_UNK_129] = "programmator.op.Var_UNK_129",
            [ProgAction.Var_UNK_130] = "programmator.op.Var_UNK_130",
            [ProgAction.ShiftUp] = "programmator.op.ShiftUp",
            [ProgAction.ShiftLeft] = "programmator.op.ShiftLeft",
            [ProgAction.ShiftDown] = "programmator.op.ShiftDown",
            [ProgAction.ShiftRight] = "programmator.op.ShiftRight",
            [ProgAction.CellForward] = "programmator.op.CellForward",
            [ProgAction.ShiftForward] = "programmator.op.ShiftForward",
            [ProgAction.CallState] = "programmator.op.CallState",
            [ProgAction.ReturnState] = "programmator.op.ReturnState",
            [ProgAction.YesNoGoto] = "programmator.op.YesNoGoto",
            [ProgAction.NoYesGoto] = "programmator.op.NoYesGoto",
            [ProgAction.STDDig] = "programmator.op.STDDig",
            [ProgAction.STDBlock] = "programmator.op.STDBlock",
            [ProgAction.STDHeal] = "programmator.op.STDHeal",
            [ProgAction.Flip] = "programmator.op.Flip",
            [ProgAction.STDTunnel] = "programmator.op.STDTunnel",
            [ProgAction.IsInsideGun] = "programmator.op.IsInsideGun",
            [ProgAction.ChargeGun] = "programmator.op.ChargeGun",
            [ProgAction.IsHealthNotFull] = "programmator.op.IsHealthNotFull",
            [ProgAction.IsHealthLessThanHalf] = "programmator.op.IsHealthLessThanHalf",
            [ProgAction.YesNoNextRow] = "programmator.op.YesNoNextRow",
            [ProgAction.NoYesNextRow] = "programmator.op.NoYesNextRow",
            [ProgAction.YesNoGotoStart] = "programmator.op.YesNoGotoStart",
            [ProgAction.NoYesGotoStart] = "programmator.op.NoYesGotoStart",
            [ProgAction.YesNoTerminate] = "programmator.op.YesNoTerminate",
            [ProgAction.NoYesTerminate] = "programmator.op.NoYesTerminate",
            [ProgAction.CellLefthand] = "programmator.op.CellLefthand",
            [ProgAction.CellRighthand] = "programmator.op.CellRighthand",
            [ProgAction.EnableAutoDig] = "programmator.op.EnableAutoDig",
            [ProgAction.DisableAutoDig] = "programmator.op.DisableAutoDig",
            [ProgAction.EnableAggression] = "programmator.op.EnableAggression",
            [ProgAction.DisableAggression] = "programmator.op.DisableAggression",
            [ProgAction.UseBoom] = "programmator.op.UseBoom",
            [ProgAction.UseRaz] = "programmator.op.UseRaz",
            [ProgAction.UseProt] = "programmator.op.UseProt",
            [ProgAction.BuildWar] = "programmator.op.BuildWar",
            [ProgAction.CallWhenDied] = "programmator.op.CallWhenDied",
            [ProgAction.UseGeopack] = "programmator.op.UseGeopack",
            [ProgAction.UseZZ] = "programmator.op.UseZZ",
            [ProgAction.UseC190] = "programmator.op.UseC190",
            [ProgAction.UsePoly] = "programmator.op.UsePoly",
            [ProgAction.Upgrade] = "programmator.op.Upgrade",
            [ProgAction.RefillCraft] = "programmator.op.RefillCraft",
            [ProgAction.UseNano] = "programmator.op.UseNano",
            [ProgAction.UseRem] = "programmator.op.UseRem",
            [ProgAction.InventoryUp] = "programmator.op.InventoryUp",
            [ProgAction.InventoryLeft] = "programmator.op.InventoryLeft",
            [ProgAction.InventoryDown] = "programmator.op.InventoryDown",
            [ProgAction.InventoryRight] = "programmator.op.InventoryRight",
            [ProgAction.EnableHand] = "programmator.op.EnableHand",
            [ProgAction.DisableHand] = "programmator.op.DisableHand",
            [ProgAction.DebugPause] = "programmator.op.DebugPause",
            [ProgAction.DebugShow] = "programmator.op.DebugShow",
            [ProgAction.SetStartWhenDied] = "programmator.op.SetStartWhenDied",
            [ProgAction.SetStartWhenHurt] = "programmator.op.SetStartWhenHurt",
            [ProgAction.SetStartWhenBotNearby] = "programmator.op.SetStartWhenBotNearby",
        };

        // NOTE (from original author): OPERATOR_NAMES and OPERATOR_DESCRIPTIONS entries are
        // approximate/placeholder translations and may be inaccurate — must be rewritten by
        // someone who understands the semantics of each operator in the Mines game context.
    }
}

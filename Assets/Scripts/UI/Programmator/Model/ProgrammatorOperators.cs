#nullable enable

using System.Collections.Generic;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.UI.Programmator;

/// <summary>
/// Static definitions and categories for Programmator actions and operators.
/// </summary>
public static class ProgrammatorOperators
{
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
}

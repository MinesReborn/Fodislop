#nullable enable

using Kern.Core.Interfaces;
using MinesServer.Data;
using UnityEngine;

namespace Kern.Game.Player.Movement;

// Выбор одного кардинального шага из намерения движения. Сервер принимает
// только шаг в соседнюю по стороне клетку, поэтому:
//  - диагональ чередует оси (змейка), а у стены скользит по открытой оси;
//  - с помощью в движении (и выключенным автокопанием) непроходимая клетка
//    впереди обходится через ObstacleDetourPlanner.
// Возвращается направление в осях Unity. Если идти некуда, это направление
// на препятствие: исполнитель повернёт к нему или будет копать, как раньше.
public static class PlayerStepSelector
{
    // Сколько удерживать направление до первого шага, чтобы включился обход.
    // Короткое нажатие в сторону породы только поворачивает к ней робота
    // (под ручной бур), а не уводит его в сторону.
    public const float AssistHoldDelay = 0.2f;

    private const int FlagAutoDig = 1;
    private const int FlagAssistArmed = 2;
    private const int FlagIgnoreCollision = 4;

    private enum StepCell
    {
        Open,
        Unknown,
        Solid,
        OutOfWorld,
    }

    public static Vector2Int Select(
        PlayerStepSelectorState state,
        MovementIntent intent,
        Vector2Int position,
        IMapDataProvider mapDataProvider,
        IWorldDataStorage storage,
        bool autoDig,
        bool movementAssist,
        bool ignoreCollision,
        float time)
    {
        if (intent.IsNone)
        {
            Release(state);
            return Vector2Int.zero;
        }

        if (state.Intent.Primary != intent.Primary || state.Intent.Secondary != intent.Secondary)
        {
            state.Intent = intent;
            state.IntentSince = time;
            state.StepsInIntent = 0;
            state.LastDetourStep = Vector2Int.zero;
            state.HasCachedStep = false;
        }

        bool assistArmed = movementAssist && !autoDig && !ignoreCollision &&
            (state.StepsInIntent > 0 || time - state.IntentSince >= AssistHoldDelay);
        int flags = (autoDig ? FlagAutoDig : 0) |
            (assistArmed ? FlagAssistArmed : 0) |
            (ignoreCollision ? FlagIgnoreCollision : 0);
        long revision = storage.Revision;
        if (state.HasCachedStep &&
            state.CachedPosition == position &&
            state.CachedRevision == revision &&
            state.CachedFlags == flags &&
            state.CachedLastStep == state.LastStep)
        {
            return state.CachedStep;
        }

        Vector2Int step = intent.IsDiagonal
            ? SelectDiagonal(state, intent, position, mapDataProvider, storage, autoDig, assistArmed, ignoreCollision)
            : SelectStraight(state, intent.Primary, position, mapDataProvider, storage, assistArmed, ignoreCollision);

        state.HasCachedStep = true;
        state.CachedPosition = position;
        state.CachedRevision = revision;
        state.CachedFlags = flags;
        state.CachedLastStep = state.LastStep;
        state.CachedStep = step;
        return step;
    }

    // Шаг ушёл серверу: следующая диагональная ось берётся от него.
    public static void NoteStep(PlayerStepSelectorState state, Vector2Int direction)
    {
        state.LastStep = direction;
        state.StepsInIntent++;
    }

    // Ввод отпущен: следующее нажатие начинается с чистого намерения.
    public static void Release(PlayerStepSelectorState state)
    {
        state.Intent = MovementIntent.None;
        state.StepsInIntent = 0;
        state.LastDetourStep = Vector2Int.zero;
        state.HasCachedStep = false;
    }

    // Сервер переставил робота (телепорт, новый вход): прошлые шаги и обход
    // к новой клетке отношения не имеют.
    public static void Reset(PlayerStepSelectorState state)
    {
        Release(state);
        state.LastStep = Vector2Int.zero;
    }

    // Сервер поправил клетку на соседнюю (отказ шага, догоняющий снимок).
    // Обход сбрасываем, но фазу змейки сохраняем: иначе следующая диагональ
    // снова начинается с Primary и идёт ВВЕРХ-ВВЕРХ-ВЛЕВО вместо ВВЕРХ-ВЛЕВО.
    public static void Resync(PlayerStepSelectorState state) => Release(state);

    private static Vector2Int SelectStraight(
        PlayerStepSelectorState state,
        Vector2Int forward,
        Vector2Int position,
        IMapDataProvider mapDataProvider,
        IWorldDataStorage storage,
        bool assistArmed,
        bool ignoreCollision)
    {
        if (!assistArmed ||
            Classify(position, forward, mapDataProvider, storage, ignoreCollision) != StepCell.Solid)
        {
            state.LastDetourStep = Vector2Int.zero;
            return forward;
        }

        return PlanDetour(state, position, forward, Vector2Int.zero, mapDataProvider, storage) ?? forward;
    }

    private static Vector2Int SelectDiagonal(
        PlayerStepSelectorState state,
        MovementIntent intent,
        Vector2Int position,
        IMapDataProvider mapDataProvider,
        IWorldDataStorage storage,
        bool autoDig,
        bool assistArmed,
        bool ignoreCollision)
    {
        bool lastOnPrimaryAxis = state.LastStep != Vector2Int.zero &&
            (state.LastStep.x != 0) == (intent.Primary.x != 0);
        Vector2Int first = lastOnPrimaryAxis ? intent.Secondary : intent.Primary;
        Vector2Int second = lastOnPrimaryAxis ? intent.Primary : intent.Secondary;
        StepCell firstCell = Classify(position, first, mapDataProvider, storage, ignoreCollision);
        StepCell secondCell = Classify(position, second, mapDataProvider, storage, ignoreCollision);

        // Автокопание: змейка строгая, порода на очередной оси бурится.
        if (autoDig)
        {
            return firstCell != StepCell.OutOfWorld || secondCell == StepCell.OutOfWorld ? first : second;
        }

        if (firstCell is StepCell.Open or StepCell.Unknown)
        {
            state.LastDetourStep = Vector2Int.zero;
            return first;
        }

        if (secondCell is StepCell.Open or StepCell.Unknown)
        {
            state.LastDetourStep = Vector2Int.zero;
            return second;
        }

        Vector2Int blocked = firstCell == StepCell.Solid || secondCell != StepCell.Solid ? first : second;
        if (!assistArmed)
        {
            return blocked;
        }

        return PlanDetour(state, position, intent.Primary, intent.Secondary, mapDataProvider, storage) ?? blocked;
    }

    private static Vector2Int? PlanDetour(
        PlayerStepSelectorState state,
        Vector2Int position,
        Vector2Int forwardA,
        Vector2Int forwardB,
        IMapDataProvider mapDataProvider,
        IWorldDataStorage storage)
    {
        if (!ObstacleDetourPlanner.TryPlanFirstStep(
                position,
                PlayerDirectionMapping.MovementToDeltaServer(forwardA),
                PlayerDirectionMapping.MovementToDeltaServer(forwardB),
                PlayerDirectionMapping.MovementToDeltaServer(state.LastDetourStep),
                storage,
                mapDataProvider,
                out Vector2Int serverStep))
        {
            state.LastDetourStep = Vector2Int.zero;
            return null;
        }

        var step = new Vector2Int(serverStep.x, -serverStep.y);
        state.LastDetourStep = step;
        return step;
    }

    private static StepCell Classify(
        Vector2Int position,
        Vector2Int direction,
        IMapDataProvider mapDataProvider,
        IWorldDataStorage storage,
        bool ignoreCollision)
    {
        Vector2Int target = position + PlayerDirectionMapping.MovementToDeltaServer(direction);
        if (!PlayerMovementValidator.IsWithinWorldBounds(target, mapDataProvider.WorldWidth, mapDataProvider.WorldHeight))
        {
            return StepCell.OutOfWorld;
        }

        // Неполученную клетку решает сервер: шаг туда уходит как есть.
        if (storage.CellLayer == null ||
            !storage.TryGetCell(target.x, target.y, out CellType cell) ||
            cell is CellType.Unloaded or CellType.Pregener)
        {
            return StepCell.Unknown;
        }

        return ignoreCollision || PlayerMovementValidator.IsPassable(cell, mapDataProvider.GetCellConfig(cell))
            ? StepCell.Open
            : StepCell.Solid;
    }
}

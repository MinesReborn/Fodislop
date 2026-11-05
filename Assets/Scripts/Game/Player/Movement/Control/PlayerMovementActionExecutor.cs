#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Game.Entities.Robots;
using Kern.Game.Player.Actions;
using Kern.Game.Player.Pathfinding;
using Kern.Networking;
using Kern.Player.Interfaces;
using Kern.World;
using Kern.World.Terrain;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.Movement;
using UnityEngine;

namespace Kern.Game.Player.Movement;

internal interface IPlayerMovementActionContext
{
    Robot? Robot { get; }
    IPlayerInput? Input { get; }
    IInputBlocker InputBlocker { get; }
    IReadOnlyList<Vector2Int>? ClickPath { get; }
    int ClickPathIndex { get; set; }
    ClickPathRenderer? ClickPathRenderer { get; }
    IMapDataProvider MapDataProvider { get; }
    IWorldDataStorage Storage { get; }
    bool AwaitingMoveConfirmation { get; set; }
    PlayerActionDispatcher? ActionDispatcher { get; }
    bool AutoDig { get; }
    bool IgnoreCollision { get; }
    bool MovementAssist { get; }
    PlayerStepSelectorState StepState { get; }
    float NextMoveTime { get; set; }
    Direction? LastSentDirection { get; set; }
    Direction LastServerRotation { get; set; }
    float LastRotateTime { get; set; }
    INetworkService NetworkService { get; }
    Transform OwnerTransform { get; }
    Vector2Int Position { get; set; }
    bool HasServerPosition { get; set; }
    bool IsGameplayVisible { get; set; }
    SpriteRenderer[] PlayerRenderers { get; }
    IWorldPresentationState PresentationState { get; }
    void PublishPlayerTeleported();
    void CancelClickPath();
    void PublishPlayerMoved(Vector2Int oldPosition, Vector2Int newPosition);
}

internal sealed class PlayerMovementActionExecutor
{
        public void ApplyMovement(IPlayerMovementActionContext context)
        {
            if (context.Robot is null || context.Input is null)
            {
                return;
            }

            if (context.InputBlocker != null && context.InputBlocker.IsInputBlockedExcludingMapMode)
            {
                return;
            }

            Vector2 moveInput = context.Input.MoveInput;
            if (moveInput != Vector2.zero && context.ClickPath != null)
            {
                context.CancelClickPath();
            }

            // Намерение — до восьми направлений; на сервер уходит один
            // кардинальный шаг, который выбирает PlayerStepSelector (змейка
            // по диагонали, скольжение вдоль стен, обход препятствий).
            // Поворот на месте (Shift) смотрит по основной оси намерения.
            MovementIntent intent = PlayerDirectionMapping.InputToIntent(moveInput);
            Vector2Int direction = intent.IsNone || context.Input.IsShiftPressed ||
                context.MapDataProvider == null || context.Storage == null
                ? intent.Primary
                : PlayerStepSelector.Select(
                    context.StepState,
                    intent,
                    context.Position,
                    context.MapDataProvider,
                    context.Storage,
                    context.AutoDig,
                    context.MovementAssist,
                    context.IgnoreCollision,
                    Time.time);
            if (intent.IsNone)
            {
                PlayerStepSelector.Release(context.StepState);
            }

            // Поворот шлём только как явное действие на месте (Shift, грань
            // мира, сплошная клетка). Сам шаг направления не требует: клетка
            // в MovePacket задаёт его, и сервер выводит из неё ориентацию.
            // Отдельный RotatePacket в паре с шагом сервер подтверждает
            // снимком позиции ДО шага; клиент принимал его за коррекцию и
            // сбрасывал фазу диагональной змейки - получалось ВВЕРХ-ВВЕРХ-ВЛЕВО.
            if (direction != Vector2Int.zero && context.Input.IsShiftPressed && !context.AwaitingMoveConfirmation)
            {
                TryRotate(context, direction);
            }

            // Поворот к грани мира не должен ждать кулдауна шага или бура:
            // шага за границу всё равно не будет, но робот обязан смотреть
            // на видимую WorldEdge-клетку.
            if (direction != Vector2Int.zero && !context.AwaitingMoveConfirmation &&
                context.MapDataProvider != null &&
                !PlayerMovementValidator.IsWithinWorldBounds(
                    context.Position + PlayerDirectionMapping.MovementToDeltaServer(direction),
                    context.MapDataProvider.WorldWidth,
                    context.MapDataProvider.WorldHeight))
            {
                TryRotate(context, direction);
                if (context.AutoDig && !context.Input.IsShiftPressed && !context.AwaitingMoveConfirmation)
                {
                    context.ActionDispatcher?.HandleBoundaryDig(context.Position, PlayerDirectionMapping.ToPacketDirection(direction));
                }

                return;
            }

            if (context.AwaitingMoveConfirmation)
            {
                return;
            }

            if (moveInput == Vector2.zero)
            {
                // Ручного ввода нет - ведём робота по клик-маршруту (ЛКМ).
                if (context.ClickPath != null)
                {
                    StepClickPath(context);
                }

                return;
            }

            // The authoritative dig cooldown gates movement as well as
            // repeated digging. Without this check auto-dig used the
            // current terrain cell's movement delay and could send a
            // BzPacket every movement tick, ignoring ServerConfig.
            if (context.ActionDispatcher is { IsDigOnCooldown: true } or { IsDigAwaitingConfirmation: true })
            {
                return;
            }

            Direction packetDirection = PlayerDirectionMapping.ToPacketDirection(direction);

            ushort currentX = (ushort)Mathf.Clamp(context.Position.x, 0, ushort.MaxValue);
            ushort currentServerY = (ushort)Mathf.Clamp(context.Position.y, 0, ushort.MaxValue);

            var storage = context.Storage;
            if (storage == null || !storage.IsReady)
            {
                return;
            }

            // Клетка под игроком может быть ещё не получена: карту присылает
            // сервер, и он же решает, можно ли идти. Ранний выход здесь
            // оставлял клиент без MovePacket, пока не придёт регион, а
            // настоящий сервер регион без движения не шлёт.
            CellType currentCellType = storage.TryGetCell(
                currentX,
                currentServerY,
                out CellType residentCurrentCellType)
                ? residentCurrentCellType
                : CellType.Unloaded;

            var mapDataProvider = context.MapDataProvider ?? throw new InvalidOperationException(
                "[PlayerMovementController] IMapDataProvider is required for movement validation.");
            float cooldown = PlayerMovementValidator.CalculateMoveCooldown(
                mapDataProvider,
                currentCellType,
                context.Input.IsCtrlPressed,
                context.IgnoreCollision);

            if (Time.time < context.NextMoveTime)
            {
                return;
            }

            if (cooldown > 0)
            {
                context.Robot.MoveSpeed = 1f / cooldown;
            }

            if (context.Input.IsShiftPressed)
            {
                return;
            }

            if (!PlayerMovementValidator.TryEvaluateStep(
                context.Position,
                direction,
                mapDataProvider,
                storage,
                out Vector2Int targetPosition,
                out CellType targetCellType,
                out bool isPassable))
            {
                // Цель не загружена: предсказывать шаг не по чему, но запрос
                // уходит серверу как есть. Позицию он вернёт RobotPositionPacket,
                // и UpdateServerPosition переставит робота.
                if (storage.CellLayer != null &&
                    PlayerMovementValidator.IsWithinWorldBounds(
                        targetPosition,
                        mapDataProvider.WorldWidth,
                        mapDataProvider.WorldHeight) &&
                    (!storage.TryGetCell(
                        (ushort)targetPosition.x,
                        (ushort)targetPosition.y,
                        out CellType residentTargetCellType) ||
                        residentTargetCellType is CellType.Unloaded or CellType.Pregener))
                {
                    context.NextMoveTime = Time.time + cooldown;
                    context.LastSentDirection = packetDirection;
                    context.Robot.TargetAngle = PlayerDirectionMapping.DirectionToAngle(direction);
                    context.AwaitingMoveConfirmation = true;
                    PlayerStepSelector.NoteStep(context.StepState, direction);
                    context.NetworkService?.SendAction(new MovePacket((ushort)targetPosition.x, (ushort)targetPosition.y));
                }

                return;
            }

            ushort targetServerX = (ushort)targetPosition.x;
            ushort targetServerY = (ushort)targetPosition.y;

            if (isPassable || context.IgnoreCollision)
            {
                context.Robot.TargetAngle = PlayerDirectionMapping.DirectionToAngle(direction);
                context.Robot.TargetPosition = WorldCoordinateConverter.ServerToUnityPos(
                    targetServerX,
                    targetServerY,
                    mapDataProvider.WorldHeight,
                    context.OwnerTransform.position.z);
                Vector2Int oldPos = context.Position;
                context.Position = targetPosition;
                context.PublishPlayerMoved(oldPos, context.Position);
                context.NextMoveTime = Time.time + cooldown;
                context.LastSentDirection = packetDirection;
                context.AwaitingMoveConfirmation = true;
                PlayerStepSelector.NoteStep(context.StepState, direction);
                context.NetworkService?.SendAction(new MovePacket(targetServerX, targetServerY));
            }
            else if (context.AutoDig)
            {
                // Сервер доворачивает к буримой клетке сам (ClickCellHandler);
                // локальный коммит нужен визуалу и HandleDig, чтобы направление
                // соответствовало отправленному действию.
                context.LastSentDirection = packetDirection;
                context.Robot.TargetAngle = PlayerDirectionMapping.DirectionToAngle(direction);
                context.ActionDispatcher?.NotifyDug(targetPosition, packetDirection);
                context.NetworkService?.Send(new ActionClientPacket(targetServerX, targetServerY, new BzPacket()));
                context.NextMoveTime = Time.time + cooldown;
            }
            else
            {
                // Сплошная клетка по направлению при выключенном автокопании:
                // ни шага, ни бура не будет, но повернуться к породе нужно -
                // иначе робот упорно смотрит мимо неё, если изначально был
                // развёрнут в другую сторону, и ручной бур (HandleDig идёт по
                // направлению взгляда) уходит в старую клетку. Поворот на
                // месте - тот же троттлённый путь, что Shift+направление.
                TryRotate(context, direction);
            }
        }

        public void TryRotate(IPlayerMovementActionContext context, Vector2Int direction)
        {
            if (context.Robot is null)
            {
                return;
            }

            Direction packetDirection = PlayerDirectionMapping.ToPacketDirection(direction);
            if (context.LastServerRotation == packetDirection ||
                Time.time - context.LastRotateTime < MovementDefaults.MinRotateInterval)
            {
                return;
            }

            context.LastRotateTime = Time.time;
            context.LastServerRotation = packetDirection;
            context.LastSentDirection = packetDirection;
            // Предсказание угла, как при шаге движения: сервер подтвердит
            // поворот ответным снимком позиции, но визуал не должен ждать
            // его до следующего шага.
            context.Robot.TargetAngle = PlayerDirectionMapping.DirectionToAngle(direction);
            context.NetworkService?.SendAction(new RotatePacket(packetDirection));
        }

        private void StepClickPath(IPlayerMovementActionContext context)
        {
            if (context.Robot is null || context.ClickPath is null)
            {
                return;
            }

            if (context.ActionDispatcher is { IsDigOnCooldown: true })
            {
                return;
            }

            // Продвигаем индекс мимо уже пройденных клеток: предсказание
            // клиента и коррекции сервера сходятся здесь.
            int previousIndex = context.ClickPathIndex;
            while (context.ClickPathIndex < context.ClickPath.Count && context.ClickPath[context.ClickPathIndex] == context.Position)
            {
                context.ClickPathIndex++;
            }

            if (context.ClickPathIndex >= context.ClickPath.Count)
            {
                context.CancelClickPath();
                return;
            }

            // Пройденные клетки гаснут: перерисовываем линию по остатку.
            if (context.ClickPathIndex != previousIndex)
            {
                context.ClickPathRenderer?.Show(context.ClickPath, context.ClickPathIndex, context.MapDataProvider, context.OwnerTransform.position.z);
            }

            Vector2Int next = context.ClickPath[context.ClickPathIndex];
            Vector2Int serverDelta = next - context.Position;
            if (Mathf.Abs(serverDelta.x) + Mathf.Abs(serverDelta.y) != 1)
            {
                // Робот не рядом с ожидаемой клеткой (коррекция сервера) -
                // остаток маршрута недействителен.
                context.CancelClickPath();
                return;
            }

            var storage = context.Storage;
            var mapDataProvider = context.MapDataProvider;
            if (storage == null || !storage.IsReady || mapDataProvider == null)
            {
                return;
            }

            ushort currentX = (ushort)Mathf.Clamp(context.Position.x, 0, ushort.MaxValue);
            ushort currentServerY = (ushort)Mathf.Clamp(context.Position.y, 0, ushort.MaxValue);
            CellType currentCellType = storage.GetCell(currentX, currentServerY);

            float cooldown = PlayerMovementValidator.CalculateMoveCooldown(
                mapDataProvider,
                currentCellType,
                isCtrlPressed: context.Input != null && context.Input.IsCtrlPressed,
                context.IgnoreCollision);
            if (Time.time < context.NextMoveTime)
            {
                return;
            }

            if (cooldown > 0)
            {
                context.Robot.MoveSpeed = 1f / cooldown;
            }

            // Дельта маршрута в серверных координатах -> направление в Unity.
            Vector2Int direction = new Vector2Int(serverDelta.x, -serverDelta.y);
            Direction packetDirection = PlayerDirectionMapping.ToPacketDirection(direction);

            if (context.LastServerRotation != packetDirection)
            {
                TryRotate(context, direction);
                context.NextMoveTime = Time.time + cooldown;
                return;
            }

            context.Robot.TargetAngle = PlayerDirectionMapping.DirectionToAngle(direction);

            if (!PlayerMovementValidator.TryEvaluateStep(
                    context.Position,
                    direction,
                    mapDataProvider,
                    storage,
                    out Vector2Int targetPosition,
                    out CellType targetCellType,
                    out bool isPassable))
            {
                // Клетка маршрута ещё не загружена - ждём данные региона.
                return;
            }

            ushort targetServerX = (ushort)targetPosition.x;
            ushort targetServerY = (ushort)targetPosition.y;

            if (isPassable || context.IgnoreCollision)
            {
                context.Robot.TargetPosition = WorldCoordinateConverter.ServerToUnityPos(targetServerX, targetServerY, mapDataProvider.WorldHeight, context.OwnerTransform.position.z);
                Vector2Int oldPos = context.Position;
                context.Position = targetPosition;
                context.PublishPlayerMoved(oldPos, context.Position);
                context.NextMoveTime = Time.time + cooldown;
                context.NetworkService?.SendAction(new MovePacket(targetServerX, targetServerY));
            }
            else if (context.AutoDig)
            {
                // Сплошная клетка на маршруте: бурим, как WASD при включённом
                // автокопании.
                context.NetworkService?.Send(new ActionClientPacket(targetServerX, targetServerY, new BzPacket()));
                context.NextMoveTime = Time.time + cooldown;
                context.ActionDispatcher?.NotifyDug(targetPosition, packetDirection);
            }
            else
            {
                // Автокопание выключили посреди маршрута: вскрывать породу
                // нельзя - остаток маршрута недействителен, робот останавливается.
                context.CancelClickPath();
            }
        }
}

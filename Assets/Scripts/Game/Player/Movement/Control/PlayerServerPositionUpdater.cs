#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Game.Entities.Robots;
using Kern.World;
using UnityEngine;

namespace Kern.Game.Player.Movement;

internal sealed class PlayerServerPositionUpdater
{
        public void UpdateServerPosition(IPlayerMovementActionContext context, Vector2Int position, bool teleport)
        {
            if (context.MapDataProvider == null)
            {
                throw new InvalidOperationException(
                    "[PlayerMovementController] IMapDataProvider is required before applying server position.");
            }

            int worldHeight = context.MapDataProvider.WorldHeight;
            if (worldHeight <= 0)
            {
                throw new InvalidOperationException(
                    $"[PlayerMovementController] Cannot apply server position {position}: " +
                    $"world height is {worldHeight}.");
            }

            bool shouldSnap = !context.HasServerPosition ||
                Mathf.Abs(context.Position.x - position.x) > 1 ||
                Mathf.Abs(context.Position.y - position.y) > 1;
            Vector2Int oldPos = context.Position;
            if (shouldSnap && context.HasServerPosition)
            {
                // Рывок робота (а за ним и камеры): сервер прислал клетку
                // дальше соседней. Источник ищется по этой строке в логе.
                Debug.LogWarning(
                    $"[PlayerMovementController] Server moved the player {oldPos} -> {position} " +
                    $"(frame {Time.frameCount}).");
            }

            context.AwaitingMoveConfirmation = false;
            if (shouldSnap || teleport)
            {
                // Робота перенесло на другую клетку: прошлая змейка и обход
                // к новой позиции отношения не имеют.
                PlayerStepSelector.Reset(context.StepState);
            }
            else if (context.HasServerPosition && oldPos != position)
            {
                // Соседняя клетка вместо предсказанной: обход пересчитываем,
                // но фазу змейки сохраняем, чтобы коррекция не повторяла ось.
                PlayerStepSelector.Resync(context.StepState);
            }

            context.Position = position;
            context.HasServerPosition = true;
            Vector3 targetWorldPos = WorldCoordinateConverter.ServerToUnityPos(position.x, position.y, worldHeight, context.OwnerTransform.position.z);
            bool holdPresentation = false;
            if (teleport && context.PresentationState == null)
            {
                throw new InvalidOperationException(
                    "PlayerMovementController requires IWorldPresentationState before teleporting.");
            }

            if (teleport &&
                (oldPos != position || context.PresentationState.IsTeleportPreparing))
            {
                holdPresentation = context.PresentationState.IsTeleportAwaitingDestination
                    ? context.PresentationState.ConfirmTeleportRequest(
                        context.PresentationState.Current.TransitionId,
                        targetWorldPos)
                    : context.PresentationState.TryBeginTeleport(targetWorldPos, out _);
            }

            // Transform игрока — это transform Robot: им владеет Robot.Update
            // (сглаживание). Пакеты разбирает ConnectionManager.Update без
            // порядка относительно Robot.Update; запись целевой клетки сюда
            // на каждом подтверждении шага показывала тело на клетку впереди
            // сглаженной позиции в кадре, где пакет шёл после Robot.Update, и
            // следующий кадр возвращал его назад. Робот сам ставит transform
            // при снапе (SnapTo), поэтому здесь пишем только без робота.
            if (!holdPresentation && context.Robot is null)
            {
                context.OwnerTransform.position = targetWorldPos;
            }

            if (context.Robot is not null)
            {
                if (holdPresentation)
                {
                    context.Robot.TargetPosition = targetWorldPos;
                }
                else if (shouldSnap || teleport)
                {
                    context.Robot.SnapTo(targetWorldPos);
                    if (teleport)
                    {
                        // Телепорт: визуал робота (тело + сегменты-щупальца) снапится
                        // в точку мгновенно, иначе Robot.Update тянет transform назад
                        // к сглаженной позиции, и камера медленно "плывёт" за ним.
                        context.Robot.SnapVisualToTarget();
                    }
                }
                else
                {
                    context.Robot.TargetPosition = targetWorldPos;
                }
            }

            if (oldPos != context.Position)
            {
                context.PublishPlayerMoved(oldPos, context.Position);
            }

            if (teleport)
            {
                // Респаун/ТП: маршрут до старой цели теряет смысл.
                context.CancelClickPath();
                context.PublishPlayerTeleported();
            }
        }
}

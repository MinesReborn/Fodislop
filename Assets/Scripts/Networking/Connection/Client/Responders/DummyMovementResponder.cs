#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.Movement;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.World;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyMovementResponder(
    IAsyncOperationSupervisor operations,
    IDummyClock clock,
    DummyPlayerSimulationState playerState,
    DummyWorldSimulationState worldState,
    DummyTeleportManager teleportManager,
    DummyPathFinder pathFinder,
    Action<ServerPacket> sendPacket,
    Func<bool> ignoreCollision,
    ushort playerBotID) : IDisposable
{
    private CancellationTokenSource? _pathCancellation;
    private CancellationTokenSource? _pendingMoveLoadCancellation;
    private CancellationTokenSource? _positionSnapshotCancellation;
    private long _moveRequestVersion;

    public void HandleMove(MovePacket packet)
    {
        CancelPendingMoveLoad();
        CancelPositionSnapshot();
        if (teleportManager.WindowOpen)
        {
            return;
        }

        long requestVersion = ++_moveRequestVersion;
        int dx = Math.Abs(packet.X - playerState.X);
        int dy = Math.Abs(packet.Y - playerState.Y);
        bool isAdjacent = (dx == 1 && dy == 0) || (dx == 0 && dy == 1);
        if (!isAdjacent)
        {
            SendPositionSnapshot();
            return;
        }

        CellType cellType = CellType.Unloaded;
        if (worldState.HasLayer &&
            !ignoreCollision() &&
            !worldState.TryGetCell(packet.X, packet.Y, out cellType))
        {
            var pendingLoadCancellation = new CancellationTokenSource();
            _pendingMoveLoadCancellation = pendingLoadCancellation;
            operations.Run(
                "dummy_move_wait_for_cell",
                supervisorToken => WaitForCellAndMoveAsync(
                    packet,
                    requestVersion,
                    pendingLoadCancellation,
                    supervisorToken));
            return;
        }

        if (!CanEnter(cellType))
        {
            SendPositionSnapshot();
            return;
        }

        CancelPath();
        ScheduleMoveAfterPrefetch(packet.X, packet.Y, requestVersion);
    }

    public void HandleRotate(RotatePacket packet)
    {
        playerState.SetDirection(packet.Direction);
        SchedulePositionSnapshot();
    }

    public void HandleClick(ClickCellPacket packet)
    {
        CancelPendingMoveLoad();
        CancelPositionSnapshot();
        _moveRequestVersion++;
        CancelPath();
        List<(ushort X, ushort Y)> path = pathFinder.FindPath(
            playerState.X,
            playerState.Y,
            packet.X,
            packet.Y,
            worldState.GetCell);
        if (path.Count == 0)
        {
            return;
        }

        _pathCancellation = new CancellationTokenSource();
        CancellationToken pathToken = _pathCancellation.Token;
        operations.Run(
            "dummy_walk_path",
            supervisorToken => WalkPathAsync(path, pathToken, supervisorToken));
    }

    public void SendPositionSnapshot()
    {
        sendPacket(new ServerPacket(new HBPacket([
            new RobotPositionPacket(
                playerBotID,
                playerState.X,
                playerState.Y,
                (byte)playerState.Direction),
        ])));
    }

    public void CancelPath()
    {
        _pathCancellation?.Cancel();
        _pathCancellation?.Dispose();
        _pathCancellation = null;
    }

    public void Dispose()
    {
        CancelPendingMoveLoad();
        CancelPositionSnapshot();
        CancelPath();
    }

    private async UniTask WaitForCellAndMoveAsync(
        MovePacket packet,
        long requestVersion,
        CancellationTokenSource pendingLoadCancellation,
        CancellationToken supervisorToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            pendingLoadCancellation.Token,
            supervisorToken);
        try
        {
            if (!await worldState.EnsureCellAvailableAsync(
                    packet.X,
                    packet.Y,
                    linkedCancellation.Token) ||
                requestVersion != _moveRequestVersion ||
                teleportManager.WindowOpen ||
                !IsAdjacent(packet.X, packet.Y) ||
                !worldState.TryGetCell(packet.X, packet.Y, out CellType cellType) ||
                !CanEnter(cellType))
            {
                SendPositionSnapshot();
                return;
            }

            await worldState.SendChunksAroundAsync(
                packet.X,
                packet.Y,
                sendPacket,
                linkedCancellation.Token);
            if (requestVersion != _moveRequestVersion ||
                teleportManager.WindowOpen ||
                !IsAdjacent(packet.X, packet.Y))
            {
                SendPositionSnapshot();
                return;
            }

            playerState.SetPosition(packet.X, packet.Y);
            CancelPath();
            SchedulePositionSnapshot();
            teleportManager.CheckTeleportEntry(playerState.X, playerState.Y);
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
            // A newer move request owns the pending cell load.
        }
        finally
        {
            if (ReferenceEquals(_pendingMoveLoadCancellation, pendingLoadCancellation))
            {
                _pendingMoveLoadCancellation = null;
            }

            pendingLoadCancellation.Dispose();
        }
    }

    private void CancelPendingMoveLoad()
    {
        _pendingMoveLoadCancellation?.Cancel();
    }

    private void ScheduleMoveAfterPrefetch(
        ushort targetX,
        ushort targetY,
        long requestVersion)
    {
        var pendingMoveLoadCancellation = new CancellationTokenSource();
        _pendingMoveLoadCancellation = pendingMoveLoadCancellation;
        operations.Run(
            "dummy_move_prefetch",
            supervisorToken => PrefetchAndMoveAsync(
                targetX,
                targetY,
                requestVersion,
                pendingMoveLoadCancellation,
                supervisorToken));
    }

    private async UniTask PrefetchAndMoveAsync(
        ushort targetX,
        ushort targetY,
        long requestVersion,
        CancellationTokenSource pendingMoveLoadCancellation,
        CancellationToken supervisorToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            pendingMoveLoadCancellation.Token,
            supervisorToken);
        try
        {
            await worldState.SendChunksAroundAsync(
                targetX,
                targetY,
                sendPacket,
                linkedCancellation.Token);
            if (requestVersion != _moveRequestVersion ||
                teleportManager.WindowOpen ||
                !IsAdjacent(targetX, targetY))
            {
                SendPositionSnapshot();
                return;
            }

            playerState.SetPosition(targetX, targetY);
            CancelPath();
            SchedulePositionSnapshot();
            teleportManager.CheckTeleportEntry(playerState.X, playerState.Y);
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
            // A newer move request owns the prefetch.
        }
        finally
        {
            if (ReferenceEquals(_pendingMoveLoadCancellation, pendingMoveLoadCancellation))
            {
                _pendingMoveLoadCancellation = null;
            }

            pendingMoveLoadCancellation.Dispose();
        }
    }

    private void CancelPositionSnapshot()
    {
        _positionSnapshotCancellation?.Cancel();
    }

    private bool IsAdjacent(ushort x, ushort y)
    {
        int dx = Math.Abs(x - playerState.X);
        int dy = Math.Abs(y - playerState.Y);
        return (dx == 1 && dy == 0) || (dx == 0 && dy == 1);
    }

    private bool CanEnter(CellType cellType)
    {
        CellConfigurationPacket? cellConfig = worldState.GetCellConfig(cellType);
        if (!cellConfig.HasValue)
        {
            return true;
        }

        bool isPassable = cellType == CellType.Empty ||
            ((CellConfigProperties)cellConfig.Value.Properties)
                .HasFlag(CellConfigProperties.Passable);
        return isPassable || ignoreCollision();
    }

    private void SchedulePositionSnapshot()
    {
        CancelPositionSnapshot();
        var positionCancellation = new CancellationTokenSource();
        _positionSnapshotCancellation = positionCancellation;
        operations.Run(
            "dummy_position_snapshot",
            supervisorToken => UpdatePositionAsync(positionCancellation, supervisorToken));
    }

    private async UniTask UpdatePositionAsync(
        CancellationTokenSource positionCancellation,
        CancellationToken supervisorToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            positionCancellation.Token,
            supervisorToken);
        try
        {
            await clock.Delay(
                ignoreCollision() ? 20 : 200,
                linkedCancellation.Token);
            await worldState.SendChunksAroundAsync(
                playerState.X,
                playerState.Y,
                sendPacket,
                linkedCancellation.Token);
            SendPositionSnapshot();
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
            // A newer position snapshot owns the stream request.
        }
        catch (ObjectDisposedException) when (linkedCancellation.IsCancellationRequested)
        {
            // Teardown or responder reset in progress.
        }
        finally
        {
            if (ReferenceEquals(_positionSnapshotCancellation, positionCancellation))
            {
                _positionSnapshotCancellation = null;
            }

            positionCancellation.Dispose();
        }
    }

    private async UniTask WalkPathAsync(
        List<(ushort X, ushort Y)> path,
        CancellationToken pathToken,
        CancellationToken supervisorToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            pathToken,
            supervisorToken);
        CancellationToken cancellationToken = linkedCancellation.Token;
        try
        {
            ushort previousX = playerState.X;
            ushort previousY = playerState.Y;
            for (int index = 0; index < path.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (ushort nextX, ushort nextY) = path[index];
                Direction direction = nextY > previousY ? Direction.Down
                    : nextY < previousY ? Direction.Up
                    : nextX < previousX ? Direction.Left
                    : Direction.Right;

                await worldState.SendChunksAroundAsync(
                    nextX,
                    nextY,
                    sendPacket,
                    cancellationToken);
                playerState.SetPosition(nextX, nextY);
                previousX = nextX;
                previousY = nextY;
                sendPacket(new ServerPacket(new HBPacket([
                    new RobotPositionPacket(
                        playerBotID,
                        playerState.X,
                        playerState.Y,
                        (byte)direction),
                ])));
                await clock.Delay(100, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // A new move/click or teardown owns cancellation of the old path.
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
            // Teardown in progress.
        }
    }
}

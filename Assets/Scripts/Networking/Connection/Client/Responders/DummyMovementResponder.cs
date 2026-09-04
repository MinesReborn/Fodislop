#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.Movement;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.World;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyMovementResponder : IDisposable
{
    private readonly IAsyncOperationSupervisor _operations;
    private readonly DummyPlayerSimulationState _playerState;
    private readonly DummyWorldSimulationState _worldState;
    private readonly DummyTeleportManager _teleportManager;
    private readonly DummyPathFinder _pathFinder;
    private readonly Action<ServerPacket> _sendPacket;
    private readonly Func<bool> _ignoreCollision;
    private readonly ushort _playerBotId;
    private CancellationTokenSource? _pathCancellation;

    public DummyMovementResponder(
        IAsyncOperationSupervisor operations,
        DummyPlayerSimulationState playerState,
        DummyWorldSimulationState worldState,
        DummyTeleportManager teleportManager,
        DummyPathFinder pathFinder,
        Action<ServerPacket> sendPacket,
        Func<bool> ignoreCollision,
        ushort playerBotId)
    {
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _playerState = playerState ?? throw new ArgumentNullException(nameof(playerState));
        _worldState = worldState ?? throw new ArgumentNullException(nameof(worldState));
        _teleportManager = teleportManager ??
            throw new ArgumentNullException(nameof(teleportManager));
        _pathFinder = pathFinder ?? throw new ArgumentNullException(nameof(pathFinder));
        _sendPacket = sendPacket ?? throw new ArgumentNullException(nameof(sendPacket));
        _ignoreCollision = ignoreCollision ??
            throw new ArgumentNullException(nameof(ignoreCollision));
        _playerBotId = playerBotId;
    }

    public void HandleMove(MovePacket packet)
    {
        if (_teleportManager.WindowOpen)
        {
            return;
        }

        int dx = Math.Abs(packet.X - _playerState.X);
        int dy = Math.Abs(packet.Y - _playerState.Y);
        bool isAdjacent = (dx == 1 && dy == 0) || (dx == 0 && dy == 1);
        if (!isAdjacent || !CanEnter(packet.X, packet.Y))
        {
            SendPositionSnapshot();
            return;
        }

        _playerState.SetPosition(packet.X, packet.Y);
        CancelPath();
        _operations.Run("dummy_position_snapshot", _ => UpdatePositionAsync());
        _teleportManager.CheckTeleportEntry(_playerState.X, _playerState.Y);
    }

    public void HandleRotate(RotatePacket packet)
    {
        _playerState.SetDirection(packet.Direction);
        _operations.Run("dummy_position_snapshot", _ => UpdatePositionAsync());
    }

    public void HandleClick(ClickCellPacket packet)
    {
        CancelPath();
        List<(ushort X, ushort Y)> path = _pathFinder.FindPath(
            _playerState.X,
            _playerState.Y,
            packet.X,
            packet.Y,
            _worldState.GetCell);
        if (path.Count == 0)
        {
            return;
        }

        _pathCancellation = new CancellationTokenSource();
        CancellationToken pathToken = _pathCancellation.Token;
        _operations.Run(
            "dummy_walk_path",
            supervisorToken => WalkPathAsync(path, pathToken, supervisorToken));
    }

    public void SendPositionSnapshot()
    {
        _sendPacket(new ServerPacket(new HBPacket(new IHBPacket[]
        {
            new RobotPositionPacket(
                _playerBotId,
                _playerState.X,
                _playerState.Y,
                (byte)_playerState.Direction),
        })));
    }

    public void CancelPath()
    {
        _pathCancellation?.Cancel();
        _pathCancellation?.Dispose();
        _pathCancellation = null;
    }

    public void Dispose()
    {
        CancelPath();
    }

    private bool CanEnter(ushort x, ushort y)
    {
        if (!_worldState.HasLayer)
        {
            return true;
        }

        CellType cellType = _worldState.GetCell(x, y);
        CellConfigurationPacket? cellConfig = _worldState.GetCellConfig(cellType);
        if (!cellConfig.HasValue)
        {
            return true;
        }

        bool isPassable = cellType == CellType.Empty ||
            ((CellConfigProperties)cellConfig.Value.Properties)
                .HasFlag(CellConfigProperties.Passable);
        return isPassable || _ignoreCollision();
    }

    private async UniTask UpdatePositionAsync()
    {
        await UniTask.Delay(_ignoreCollision() ? 20 : 200);
        _worldState.SendChunksAround(_playerState.X, _playerState.Y, _sendPacket);
        SendPositionSnapshot();
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
            ushort previousX = _playerState.X;
            ushort previousY = _playerState.Y;
            for (int index = 0; index < path.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (ushort nextX, ushort nextY) = path[index];
                Direction direction = nextY > previousY ? Direction.Down
                    : nextY < previousY ? Direction.Up
                    : nextX < previousX ? Direction.Left
                    : Direction.Right;

                _playerState.SetPosition(nextX, nextY);
                previousX = nextX;
                previousY = nextY;
                _worldState.SendChunksAround(_playerState.X, _playerState.Y, _sendPacket);
                _sendPacket(new ServerPacket(new HBPacket(new IHBPacket[]
                {
                    new RobotPositionPacket(
                        _playerBotId,
                        _playerState.X,
                        _playerState.Y,
                        (byte)direction),
                })));
                await UniTask.Delay(100, cancellationToken: cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // A new move/click or teardown owns cancellation of the old path.
        }
    }
}

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.Movement;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.World;
using NUnit.Framework;

using Kern.Networking.Dummy;

namespace Kern.Tests.Networking;

public sealed class DummyMovementResponderTests
{
    [Test]
    public void NonAdjacentMove_IsRejectedWithAuthoritativePositionSnapshot()
    {
        var sent = new List<ServerPacket>();
        var supervisor = new RecordingSupervisor();
        var player = new DummyPlayerSimulationState();
        player.SetPosition(10, 20);
        using var world = new DummyWorldSimulationState(supervisor, new Kern.Tests.Networking.UnavailableDummyWorldMapSource());
        var teleports = new DummyTeleportManager(sent.Add, new List<(ushort X, ushort Y)>(), supervisor);
        var pathFinder = new DummyPathFinder(sent.Add, world.GetCellConfig);
        using var movement = new DummyMovementResponder(
            supervisor,
            new VirtualDummyClock(seed: 1),
            player,
            world,
            teleports,
            pathFinder,
            sent.Add,
            () => false,
            456);

        movement.HandleMove(new MovePacket(12, 20));

        Assert.That(player.X, Is.EqualTo(10));
        Assert.That(player.Y, Is.EqualTo(20));
        Assert.That(supervisor.OperationNames, Is.Empty);
        var heartbeat = (HBPacket)sent[0].Payload;
        var position = (RobotPositionPacket)heartbeat.Payload[0];
        Assert.That(position.X, Is.EqualTo(10));
        Assert.That(position.Y, Is.EqualTo(20));
    }

    [Test]
    public void AdjacentMove_WithoutWorldLayer_SchedulesChunkPrefetch()
    {
        var sent = new List<ServerPacket>();
        var supervisor = new RecordingSupervisor();
        var player = new DummyPlayerSimulationState();
        player.SetPosition(10, 20);
        using var world = new DummyWorldSimulationState(supervisor, new Kern.Tests.Networking.UnavailableDummyWorldMapSource());
        var teleports = new DummyTeleportManager(sent.Add, new List<(ushort X, ushort Y)>(), supervisor);
        var pathFinder = new DummyPathFinder(sent.Add, world.GetCellConfig);
        using var movement = new DummyMovementResponder(
            supervisor,
            new VirtualDummyClock(seed: 1),
            player,
            world,
            teleports,
            pathFinder,
            sent.Add,
            () => false,
            456);

        movement.HandleMove(new MovePacket(11, 20));

        Assert.That(player.X, Is.EqualTo(10));
        Assert.That(player.Y, Is.EqualTo(20));
        Assert.That(supervisor.OperationNames, Is.EqualTo(new[] { "dummy_move_prefetch" }));
    }

    [Test]
    public void AdjacentMove_WithIgnoreCollision_CompletesMoveAtPassableSpeed()
    {
        var sent = new List<ServerPacket>();
        var supervisor = new ExecutingSupervisor();
        var clock = new VirtualDummyClock(seed: 1);
        var player = new DummyPlayerSimulationState();
        player.SetPosition(10, 20);
        using var world = new DummyWorldSimulationState(supervisor, new Kern.Tests.Networking.UnavailableDummyWorldMapSource());
        var teleports = new DummyTeleportManager(sent.Add, new List<(ushort X, ushort Y)>(), supervisor);
        var pathFinder = new DummyPathFinder(sent.Add, world.GetCellConfig);
        using var movement = new DummyMovementResponder(
            supervisor,
            clock,
            player,
            world,
            teleports,
            pathFinder,
            sent.Add,
            () => true,
            456);

        movement.HandleMove(new MovePacket(11, 20));

        Assert.That(player.X, Is.EqualTo(11));
        Assert.That(player.Y, Is.EqualTo(20));

        Assert.That(clock.PendingCount, Is.EqualTo(1));
        clock.Advance(19);
        Assert.That(sent, Is.Empty);
        clock.Advance(1);
        Assert.That(sent, Has.Count.EqualTo(1));
        var heartbeat = (HBPacket)sent[0].Payload;
        var position = (RobotPositionPacket)heartbeat.Payload[0];
        Assert.That(position.X, Is.EqualTo(11));
        Assert.That(position.Y, Is.EqualTo(20));
    }

    [TestCase(10, 19, Direction.Up)]
    [TestCase(10, 21, Direction.Down)]
    [TestCase(9, 20, Direction.Left)]
    [TestCase(11, 20, Direction.Right)]
    public void AcceptedMove_UpdatesFacingBeforeDelayedSnapshot(int x, int y, Direction expected)
    {
        var sent = new List<ServerPacket>();
        var supervisor = new ExecutingSupervisor();
        var clock = new VirtualDummyClock(seed: 1);
        var player = new DummyPlayerSimulationState();
        player.SetPosition(10, 20);
        player.SetDirection(expected == Direction.Left ? Direction.Down : Direction.Left);
        using var world = new DummyWorldSimulationState(supervisor, new UnavailableDummyWorldMapSource());
        var teleports = new DummyTeleportManager(sent.Add, new List<(ushort X, ushort Y)>(), supervisor);
        var pathFinder = new DummyPathFinder(sent.Add, world.GetCellConfig);
        using var movement = new DummyMovementResponder(supervisor, clock, player, world,
            teleports, pathFinder, sent.Add, () => true, 456);

        movement.HandleMove(new MovePacket((ushort)x, (ushort)y));
        clock.Advance(20);

        Assert.That(player.Direction, Is.EqualTo(expected));
        var heartbeat = (HBPacket)sent[0].Payload;
        var position = (RobotPositionPacket)heartbeat.Payload[0];
        Assert.That(position.Rotation, Is.EqualTo((byte)expected));
        movement.SendPositionSnapshot();
        var idle = (RobotPositionPacket)((HBPacket)sent[1].Payload).Payload[0];
        Assert.That(idle.Rotation, Is.EqualTo((byte)expected));
    }

    private sealed class RecordingSupervisor : IAsyncOperationSupervisor
    {
        public List<string> OperationNames { get; } = new();

        public int ActiveCount => 0;

        public void Run(string operationName, Func<CancellationToken, UniTask> operation)
        {
            OperationNames.Add(operationName);
        }

        public UniTask StopAsync(CancellationToken cancellationToken = default)
        {
            return UniTask.CompletedTask;
        }
    }

    private sealed class ExecutingSupervisor : IAsyncOperationSupervisor
    {
        public List<string> OperationNames { get; } = new();

        public int ActiveCount => 0;

        public void Run(string operationName, Func<CancellationToken, UniTask> operation)
        {
            OperationNames.Add(operationName);
            operation(CancellationToken.None).Forget();
        }

        public UniTask StopAsync(CancellationToken cancellationToken = default)
        {
            return UniTask.CompletedTask;
        }
    }
}

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern;
using MinesServer.Networking.Client.Packets.Movement;
using MinesServer.Networking.Connection.Client;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.World;
using NUnit.Framework;

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
        var teleports = new DummyTeleportManager(sent.Add, [], supervisor);
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
    public void AdjacentMove_UpdatesStateAndSchedulesSnapshot()
    {
        var sent = new List<ServerPacket>();
        var supervisor = new RecordingSupervisor();
        var player = new DummyPlayerSimulationState();
        player.SetPosition(10, 20);
        using var world = new DummyWorldSimulationState(supervisor, new Kern.Tests.Networking.UnavailableDummyWorldMapSource());
        var teleports = new DummyTeleportManager(sent.Add, [], supervisor);
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

        Assert.That(player.X, Is.EqualTo(11));
        Assert.That(player.Y, Is.EqualTo(20));
        Assert.That(supervisor.OperationNames, Is.EqualTo(new[] { "dummy_position_snapshot" }));
    }

    private sealed class RecordingSupervisor : IAsyncOperationSupervisor
    {
        public List<string> OperationNames { get; } = [];

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
}

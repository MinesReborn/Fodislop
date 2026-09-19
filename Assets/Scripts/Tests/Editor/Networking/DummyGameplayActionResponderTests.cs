#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Connection.Client;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Information;
using NUnit.Framework;

namespace Kern.Tests.Networking;

public sealed class DummyGameplayActionResponderTests
{
    [Test]
    public void ToggleAndHeal_UpdatePlayerStateAndSendAuthoritativePackets()
    {
        var sent = new List<ServerPacket>();
        var operations = new RecordingSupervisor();
        var player = new DummyPlayerSimulationState();
        player.SetHealth(400);
        using var world = new DummyWorldSimulationState(operations, new Kern.Tests.Networking.UnavailableDummyWorldMapSource());
        var teleports = new DummyTeleportManager(sent.Add, [], operations);
        var pathFinder = new DummyPathFinder(sent.Add, world.GetCellConfig);
        var clock = new VirtualDummyClock(seed: 1);
        using var movement = new DummyMovementResponder(
            operations,
            clock,
            player,
            world,
            teleports,
            pathFinder,
            sent.Add,
            () => false,
            456);
        var inventory = new DummyInventoryResponder(
            sent.Add,
            (_, _, _, _) => { },
            [],
            player.SetHealth,
            world.GetCell,
            world.SetCell);
        var responder = new DummyGameplayActionResponder(
            player,
            world,
            movement,
            new DummyMissionRunner(sent.Add),
            inventory,
            new DummyChatSimulator(sent.Add, _ => false, operations, clock),
            clock,
            sent.Add,
            456);

        responder.Handle(new ActionClientPacket(0, 0, new ToggleAutoDigPacket()));
        responder.Handle(new ActionClientPacket(0, 0, new HealPacket()));

        Assert.That(player.AutoDig, Is.True);
        Assert.That(player.Health, Is.EqualTo(450));
        Assert.That(((AutoMineStatePacket)sent[0].Payload).Enabled, Is.True);
        Assert.That(((HealthPacket)sent[1].Payload).Current, Is.EqualTo(450));
    }

    private sealed class RecordingSupervisor : IAsyncOperationSupervisor
    {
        public int ActiveCount => 0;

        public void Run(string operationName, Func<CancellationToken, UniTask> operation)
        {
        }

        public UniTask StopAsync(CancellationToken cancellationToken = default)
        {
            return UniTask.CompletedTask;
        }
    }
}

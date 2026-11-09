#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Kern;
using Kern.Core;
using Kern.Persistence;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Information;
using NUnit.Framework;

using Kern.Networking.Dummy;

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
        var teleports = new DummyTeleportManager(sent.Add, new List<(ushort X, ushort Y)>(), operations);
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
            new List<(ushort X, ushort Y)>(),
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
            456,
            operations,
            () => 1,
            _ => true);

        responder.Handle(new ActionClientPacket(0, 0, new ToggleAutoDigPacket()));
        responder.Handle(new ActionClientPacket(0, 0, new HealPacket()));

        Assert.That(player.AutoDig, Is.True);
        Assert.That(player.Health, Is.EqualTo(450));
        Assert.That(((AutoMineStatePacket)sent[0].Payload).Enabled, Is.True);
        Assert.That(((HealthPacket)sent[1].Payload).Current, Is.EqualTo(450));
    }

    [Test]
    public async Task BuildCyan_PlacesFrameAndCompletesAfterFiveSeconds()
    {
        string testRoot = Path.Combine(Path.GetTempPath(), $"military_block_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testRoot);
        string mapPath = Path.Combine(testRoot, "world.mapb");
        using (var stream = new FileStream(mapPath, FileMode.CreateNew, FileAccess.Write))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(1);
            writer.Write(1);
            writer.Write(WorldGridDefaults.ChunkSize);
            writer.Write(WorldLayerFileHeader.CurrentFormatVersion);
            writer.Write(-1L);
        }

        try
        {
            var sent = new List<ServerPacket>();
            var operations = new RecordingSupervisor();
            var player = new DummyPlayerSimulationState();
            player.SetPosition(9, 10);
            player.SetDirection(Direction.Right);
            using var world = new DummyWorldSimulationState(operations, new FileMapSource(mapPath));
            await world.OpenAsync("fixture");
            world.SetCell(10, 10, CellType.Empty);

            var clock = new VirtualDummyClock(seed: 1);
            var teleports = new DummyTeleportManager(sent.Add, new List<(ushort X, ushort Y)>(), operations);
            var pathFinder = new DummyPathFinder(sent.Add, world.GetCellConfig);
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
                new List<(ushort X, ushort Y)>(),
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
                456,
                operations,
                () => 1,
                _ => true);

            responder.Handle(new ActionClientPacket(0, 0, new BuildCyanPacket()));
            Assert.That(world.GetCell(10, 10), Is.EqualTo(CellType.MilitaryBlockFrame));

            clock.Advance(4999);
            Assert.That(world.GetCell(10, 10), Is.EqualTo(CellType.MilitaryBlockFrame));

            clock.Advance(1);
            Assert.That(world.GetCell(10, 10), Is.EqualTo(CellType.MilitaryBlock));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private sealed class FileMapSource : IDummyWorldMapSource
    {
        private readonly string _mapPath;

        public FileMapSource(string mapPath) => _mapPath = mapPath;

        public UniTask<string> GetMapFileAsync(string worldCodeName, CancellationToken cancellationToken) =>
            UniTask.FromResult(_mapPath);
    }

    private sealed class RecordingSupervisor : IAsyncOperationSupervisor
    {
        public int ActiveCount => 0;

        public void Run(string operationName, Func<CancellationToken, UniTask> operation)
        {
            if (operationName == "dummy_military_block_assembly")
            {
                operation(CancellationToken.None).Forget();
            }
        }

        public UniTask StopAsync(CancellationToken cancellationToken = default)
        {
            return UniTask.CompletedTask;
        }
    }
}

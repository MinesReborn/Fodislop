#nullable enable

using System.Collections;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Game;
using Kern.Game.Managers;
using Kern.Networking.Processors;
using Kern.World;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

namespace Kern.Tests.PlayMode;

[TestFixture]
public sealed class MultiWorldSwitchPlayModeTests
{
    private const string TestDummyToken = "playmode-multiworld-token";
    private BootstrapLifetimeScope _bootstrap = null!;
    private DummyAuthenticationScope _authentication = null!;
    private IOfflineScenarioSettings _scenario = null!;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        _authentication = DummyAuthenticationScope.Seed(TestDummyToken);
        yield return PlayModeHarness.StartAtGateway();
        _bootstrap = PlayModeHarness.FindBootstrap()!;
        _scenario = _bootstrap.Container.Resolve<IOfflineScenarioSettings>();
        _scenario.Scenario = OfflineScenario.HappyPath;
        yield return PlayModeHarness.EnterMainGame(_bootstrap);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        _scenario.Scenario = OfflineScenario.HappyPath;
        yield return PlayModeHarness.Shutdown();
        _authentication.Restore();
    }

    [UnityTest]
    public IEnumerator SwitchWorldMidSession_CleansOldStateAndSnapsPlayerToNewWorld()
    {
        var game = PlayModeHarness.RequireInGame<GameManager>();
        var mapManager = PlayModeHarness.RequireInGame<IMapDataProvider>();
        var storage = PlayModeHarness.RequireInGame<IWorldDataStorage>();
        var robotService = PlayModeHarness.RequireInGame<IRobotService>();
        var buildingService = PlayModeHarness.RequireInGame<IBuildingService>();
        var localPlayerState = PlayModeHarness.RequireInGame<ILocalPlayerState>();
        var worldInitProcessor = PlayModeHarness.RequireInGame<WorldInitProcessor>();
        var playerInfoProcessor = PlayModeHarness.RequireInGame<PlayerInfoProcessor>();
        var mapRegionProcessor = PlayModeHarness.RequireInGame<MapRegionProcessor>();

        ILocalPlayer player = localPlayerState.Current ??
            throw new AssertionException("Local player was never spawned.");

        // 1. Add a dummy foreign robot and a building in World 1
        uint foreignBotId = 9999;
        robotService.UpdateRobotPosition(foreignBotId, 10, 10, (byte)0);
        buildingService.AddOrUpdateBuilding(12, 12, PackType.Market, 0, 0);

        yield return null;

        Assert.That(robotService.TryGetRobot(foreignBotId, out _), Is.True, "Foreign robot should exist before world switch.");

        // 2. Prepare new world packet (different code and size)
        var newWorldPacket = new WorldInitPacket(
            "playmode_multiverse_2",
            "Multiverse Alpha",
            64,
            64,
            MinesServer.Networking.Connection.Client.DummyCellConfigurationUtilities.CreateCellConfigurations(),
            [[37, 38, 106]]);

        // 3. Process WorldInit mid-session
        worldInitProcessor.Process(newWorldPacket);

        Assert.That(robotService.TryGetRobot(foreignBotId, out _), Is.False, "Foreign robot must be cleared on world switch.");
        Assert.That(player.HasServerPosition, Is.False, "Player server position must be reset immediately on WorldInit.");
        Assert.That(storage.GetWorldCodeName(), Is.EqualTo("playmode_multiverse_2"));
        Assert.That(mapManager.WorldWidth, Is.EqualTo(64));
        Assert.That(mapManager.WorldHeight, Is.EqualTo(64));

        // 4. Send new player teleport packet for the new world
        playerInfoProcessor.Process(new TeleportPacket(25, 30, false));

        Assert.That(player.HasServerPosition, Is.True, "Player must accept new world coordinates.");
        Assert.That(player.Position, Is.EqualTo(new Vector2Int(25, 30)));

        // 5. Verify robot visual snapped (no smoothing lag)
        if (player.TryGetComponent<Robot>(out var robot))
        {
            Vector3 expectedUnityPos = CoordinateUtils.ServerToUnityPos(25, 30, 64, player.transform.position.z);
            Assert.That(Vector2.Distance((Vector2)robot.TargetPosition, (Vector2)expectedUnityPos), Is.LessThan(0.01f));
        }

        // 6. Send map region around player so terrain can commit and world load can finish
        mapRegionProcessor.Process(new MapRegionPacket(0, 0, 63, 63, new CellType[64 * 64]));

        // 7. Wait for GameManager to publish world loaded for the new world
        yield return PlayModeHarness.WaitUntil(
            () => game.IsWorldLoaded,
            PlayModeHarness.WorldTimeoutSeconds,
            "GameManager did not finish loading the new world.");

        Assert.That(game.IsWorldLoaded, Is.True);
    }
}

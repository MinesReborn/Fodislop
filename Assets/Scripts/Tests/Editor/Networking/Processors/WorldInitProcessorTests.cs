#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Interfaces;
using Kern.Networking.Processors;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.World;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.Networking;

[TestFixture]
public class WorldInitProcessorTests
{
    private StubMapDataProvider _mapManager = null!;
    private StubWorldReadiness _gameManager = null!;
    private StubRobotService _robotManager = null!;
    private StubBuildingService _buildingManager = null!;
    private StubLocalPlayerState _localPlayer = null!;
    private WorldInitProcessor _processor = null!;

    [SetUp]
    public void SetUp()
    {
        _mapManager = new StubMapDataProvider();
        _gameManager = new StubWorldReadiness();
        _robotManager = new StubRobotService();
        _buildingManager = new StubBuildingService();
        _localPlayer = new StubLocalPlayerState();
        _processor = new WorldInitProcessor(
            _mapManager,
            _gameManager,
            _robotManager,
            _buildingManager,
            _localPlayer);
    }

    [TearDown]
    public void TearDown()
    {
        _processor.Dispose();
    }

    [Test]
    public void Process_ClearsRobotsBuildingsAndPlayerPositionBeforeLoadingWorld()
    {
        var stubPlayer = new StubLocalPlayer();
        stubPlayer.UpdateServerPosition(new Vector2Int(10, 20));
        Assert.That(stubPlayer.HasServerPosition, Is.True);
        _localPlayer.Current = stubPlayer;

        var packet = new WorldInitPacket(
            "world_test_2",
            "World 2",
            128,
            256,
            [],
            []);

        _processor.Process(packet);

        Assert.That(_robotManager.ClearAllRobotsCalled, Is.True, "Robots must be cleared on world init");
        Assert.That(_buildingManager.ClearAllBuildingsCalled, Is.True, "Buildings must be cleared on world init");
        Assert.That(stubPlayer.HasServerPosition, Is.False, "Player server position must be reset on world init");
        Assert.That(_mapManager.LoadedPacket, Is.SameAs(packet));
        Assert.That(_gameManager.WorldLoadedNotified, Is.True, "WorldLoaded must be notified via OnWorldInitialized");
    }

    [Test]
    public void Dispose_UnsubscribesFromWorldInitialized()
    {
        _processor.Dispose();
        _gameManager.WorldLoadedNotified = false;
        _mapManager.OnWorldInitialized?.Invoke();

        Assert.That(_gameManager.WorldLoadedNotified, Is.False, "Disposing WorldInitProcessor should unsubscribe NotifyWorldLoaded");
    }

    private sealed class StubMapDataProvider : IMapDataProvider
    {
        public WorldInitPacket? LoadedPacket { get; private set; }
        public Action? OnWorldInitialized { get; set; }
        public Action? OnWorldDataLoaded { get; set; }
        public ushort WorldWidth => 128;
        public ushort WorldHeight => 128;
        public string WorldCodeName => "test";
        public bool IsWorldInitialized => true;

        public void LoadWorldInit(WorldInitPacket packet)
        {
            LoadedPacket = packet;
            OnWorldInitialized?.Invoke();
        }

        public void UpdateMovementSpeeds(MovementSpeedPacket packet) { }
        public Camera MainCamera => null!;
        public bool IsStandaloneMode => false;
        public float GetMoveCooldown(CellType cellType) => 0f;
        public CellConfigurationPacket GetCellConfig(CellType type) => default;
        public bool TryGetTileGroup(CellType type, out int groupId)
        {
            groupId = 0;
            return false;
        }

        public Color GetCellMinimapColor(CellType type) => Color.white;
        public Color32 GetCellMinimapColor32(CellType type) => Color.white;
        public int GetAnimationFrameHeight(CellType cellType) => 0;
        public byte GetAnimationSpeed(CellType cellType) => 0;
        public bool HasAnimation(CellType cellType) => false;
        public void ResetWorldState() { }
    }

    private sealed class StubWorldReadiness : IWorldReadiness
    {
        public bool IsWorldLoaded => true;
        public bool WorldLoadedNotified { get; set; }

        public void NotifyWorldLoaded()
        {
            WorldLoadedNotified = true;
        }
    }

    private sealed class StubRobotService : IRobotService
    {
        public bool ClearAllRobotsCalled { get; private set; }
        public uint LocalPlayerBotId => 1;
        public int RobotCount => 0;

        public void ClearAllRobots()
        {
            ClearAllRobotsCalled = true;
        }

        public void SetLocalPlayerBotId(uint botId) { }
        public void RegisterRobot(IRobotView robot) { }
        public void UnregisterRobot(IRobotView robot) { }
        public void UnregisterRobot(uint botId) { }
        public IRobotView GetOrCreateRobot(uint botId) => null!;
        public bool TryGetRobot(uint botId, out IRobotView? robot)
        {
            robot = null;
            return false;
        }

        public void UpdateRobotMetadata(uint botId, RobotMetadata metadata) { }
        public void UpdateRobotPosition(uint botId, ushort x, ushort y, float rotation) { }
        public void UpdateRobotPosition(uint botId, ushort x, ushort y, byte rotation) { }
        public void PruneStaleRobots(float timeoutSeconds = 2.5f) { }
        public void ReplaceFactoryBotWithPlayer(uint botId, IRobotView playerRobot) { }
    }

    private sealed class StubBuildingService : IBuildingService
    {
        public bool ClearAllBuildingsCalled { get; private set; }

        public void ClearAllBuildings()
        {
            ClearAllBuildingsCalled = true;
        }

        public void AddOrUpdateBuilding(ushort x, ushort y, PackType buildingType, byte variant, byte linkedClan) { }
        public void RemoveBuilding(ushort x, ushort y) { }
    }

    private sealed class StubLocalPlayerState : ILocalPlayerState
    {
        public ILocalPlayer? Current { get; set; }
        public bool IsAuthenticated => true;
        public event Action<ILocalPlayer?>? Changed { add { } remove { } }
        public void Publish(ILocalPlayer player) => Current = player;
        public void Clear(ILocalPlayer player)
        {
            if (ReferenceEquals(Current, player))
            {
                Current = null;
            }
        }

        public void SetAuthenticated(bool value) { }
    }

    private sealed class StubLocalPlayer : ILocalPlayer
    {
        public GameObject gameObject => null!;
        public Transform transform => null!;
        public bool isActiveAndEnabled => true;
        public uint BotId => 1;
        public Vector2Int Position { get; private set; }
        public bool HasServerPosition { get; private set; }
        public bool IsGameplayVisible => true;
        public Direction LastDirection => Direction.Down;
        public bool IgnoreCollision { get; set; }
        public bool AutoDig { get; set; }
        public bool Aggression { get; set; }
        public event Action<Vector2Int, Vector2Int>? OnPlayerMoved { add { } remove { } }
        public event Action? OnPlayerTeleported { add { } remove { } }
        public event Action<bool>? OnAutoDigChanged { add { } remove { } }
        public event Action<bool>? OnAggressionChanged { add { } remove { } }

        public void UpdateServerPosition(Vector2Int position, bool teleport = false)
        {
            Position = position;
            HasServerPosition = true;
        }

        public void ResetServerPosition()
        {
            Position = default;
            HasServerPosition = false;
        }

        public void ConfirmDigAction(ushort x, ushort y) { }
        public bool TryGetDigDirection(ushort x, ushort y, out Direction direction)
        {
            direction = default;
            return false;
        }
        public void ResetDirection() { }
        public void Initialize(uint botId) { }
        public void SetGameplayVisible() { }
        public T GetComponent<T>() => default!;
        public bool TryGetComponent<T>(out T component)
        {
            component = default!;
            return false;
        }
    }
}

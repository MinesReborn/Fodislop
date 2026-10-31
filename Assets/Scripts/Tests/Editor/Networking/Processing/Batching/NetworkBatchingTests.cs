#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using Kern.Core.Interfaces;
using Kern.Core.Models;
using Kern.Networking;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Kern.Networking.Processing;

namespace Kern.Tests.Networking;

[TestFixture]
public class NetworkBatchingTests
{
    [Test]
    public void NetworkService_ReentrantBatch_FiresBatchEventsOnlyAtOuterBoundaries()
    {
        var go = new GameObject("TestNetworkService");
        var networkService = go.AddComponent<NetworkService>();

        int startedCount = 0;
        int completedCount = 0;
        networkService.PacketBatchStarted += () => startedCount++;
        networkService.PacketBatchCompleted += () => completedCount++;

        // Outer batch (depth 0 -> 1)
        networkService.BeginBatch();
        Assert.AreEqual(1, startedCount);
        Assert.AreEqual(0, completedCount);

        // Nested batch (depth 1 -> 2)
        networkService.BeginBatch();
        Assert.AreEqual(1, startedCount);
        Assert.AreEqual(0, completedCount);

        // Nested end (depth 2 -> 1)
        networkService.EndBatch();
        Assert.AreEqual(1, startedCount);
        Assert.AreEqual(0, completedCount);

        // Outer end (depth 1 -> 0)
        networkService.EndBatch();
        Assert.AreEqual(1, startedCount);
        Assert.AreEqual(1, completedCount);

        // Extra EndBatch when depth is 0 should not fire
        networkService.EndBatch();
        Assert.AreEqual(1, completedCount);

        UnityEngine.Object.DestroyImmediate(go);
    }

    [Test]
    public void NetworkService_WorldInitHandlerFailure_PropagatesToConnectionBoundary()
    {
        var go = new GameObject("TestNetworkServiceWorldInitFailure");
        try
        {
            NetworkService networkService = go.AddComponent<NetworkService>();
            networkService.Subscribe<WorldInitPacket>(
                _ => throw new InvalidOperationException("world storage failed"));

            LogAssert.Expect(
                LogType.Exception,
                new System.Text.RegularExpressions.Regex("InvalidOperationException: world storage failed"));
            MethodInfo onPacketReceived = typeof(NetworkService).GetMethod(
                "OnPacketReceived",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var packet = new MinesServer.Networking.Server.Packets.ServerPacket(
                new WorldInitPacket(
                    "testworld",
                    "Test",
                    1,
                    1,
                    Array.Empty<CellConfigurationPacket>(),
                    Array.Empty<byte[]>()));

            TargetInvocationException invocation = Assert.Throws<TargetInvocationException>(
                () => onPacketReceived.Invoke(networkService, new object[] { packet }))!;

            Assert.That(invocation.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(invocation.InnerException!.Message, Is.EqualTo("world storage failed"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void PlayerInfoProcessor_InBatch_DefersRobotPruningUntilBatchEnd()
    {
        var stubRobot = new StubRobotService();
        var stubStats = new StubPlayerStats();
        var stubMap = new StubMapDataProvider();
        var stubLocal = new StubLocalPlayerState();

        var processor = new PlayerInfoProcessor(stubRobot, stubStats, stubMap, stubLocal);

        // Outside batch: PruneStaleRobots is called on each position packet
        processor.Process(new RobotPositionPacket(1, 10, 20, 0));
        Assert.AreEqual(1, stubRobot.PruneCalls);

        // Inside batch: Pruning is deferred
        processor.BeginBatch();
        processor.Process(new RobotPositionPacket(1, 11, 20, 0));
        processor.Process(new RobotPositionPacket(2, 30, 40, 0));
        processor.Process(new RobotPositionPacket(3, 50, 60, 0));
        Assert.AreEqual(1, stubRobot.PruneCalls);

        processor.EndBatch();
        Assert.AreEqual(2, stubRobot.PruneCalls);
    }

    private sealed class StubRobotService : IRobotService
    {
        public int PruneCalls { get; private set; }
        public uint LocalPlayerBotId { get; set; } = 999;
        public int RobotCount => 0;

        public void RegisterRobot(IRobotView robot) { }
        public void UnregisterRobot(uint botId) { }
        public bool TryGetRobot(uint botId, out IRobotView? robot)
        {
            robot = null;
            return false;
        }

        public void UpdateRobotMetadata(uint botId, RobotMetadata metadata) { }
        public void UpdateRobotPosition(uint botId, ushort x, ushort y, byte rotation) { }
        public void SetLocalPlayerBotId(uint botId) => LocalPlayerBotId = botId;
        public void ClearAllRobots() { }
        public void PruneStaleRobots(float timeoutSeconds = 2.5f) => PruneCalls++;
    }

    private sealed class StubPlayerStats : IPlayerStats
    {
        public bool IsReady => true;
        public int Health => 100;
        public int MaxHealth => 100;
        public float HealthPercent => 1f;
        public string Nickname => "Test";
        public long Level => 1;
        public long Money => 0;
        public long Credits => 0;
        public int GeologyCurrent => 0;
        public int GeologyMax => 0;
        public string GeologyText => string.Empty;
        public uint BasketCapacity => 10;
        public long[] BasketContents => Array.Empty<long>();
        public int BasketMaxPercent => 0;
        public void SetBasket(uint capacity, long[] contents) { }
        public IReadOnlyDictionary<string, StatusLineEntry> StatusLines => new Dictionary<string, StatusLineEntry>();
        public int OnlinePlayers => 1;
        public int OnlineProgrammator => 0;
        public int ClanId => 0;
        public int MaxDepth => 0;
        public int CurrentDepth => 0;
        public bool IsMissionActive => false;
        public string MissionTitle => string.Empty;
        public string MissionDescription => string.Empty;
        public long MissionProgress => 0;
        public long MissionMaxProgress => 0;
        public string MissionImageUri => string.Empty;
        public ushort MissionImageWidth => 0;
        public ushort MissionImageHeight => 0;
        public bool DailyBonusAvailable => false;
        public Vector2Int? MissionArrowTarget => null;

        public void SetLevel(long level) { }
        public void SetHealth(int current, int max) { }
        public void SetCurrency(long money, long credits) { }
        public void SetGeology(int current, int max, CellType cell, string text) { }
        public void SetNickname(string nickname) { }
        public void SetClanId(int clanId) { }
        public void SetMaxDepth(int depth) { }
        public void SetDailyBonusAvailable(bool available) { }
        public void SetSkillProgress(SkillType skill, long current, long max) { }
        public void SetMission(string title, string description, long max, string imageUri = "", ushort imageWidth = 0, ushort imageHeight = 0) { }
        public void SetMissionArrow(ushort x, ushort y) { }
        public void SetMissionProgress(long current) { }
        public void SetMissionMaxProgress(long max) { }
        public void ClearMission() { }
        public void SetOnline(int players, int programmator) { }
        public void AddStatusLine(string tag, string[] text, Color color, byte blinkRate, long expiry) { }
        public void RemoveStatusLine(string tag) { }
        public void ClearStatusLines() { }

        public event Action? OnStatsChanged
        {
            add { }
            remove { }
        }

        public event Action? OnHealthChanged
        {
            add { }
            remove { }
        }

        public event Action<int, int>? OnHealthUpdated
        {
            add { }
            remove { }
        }

        public event Action? OnCurrencyChanged
        {
            add { }
            remove { }
        }

        public event Action<long, long>? OnCurrencyUpdated
        {
            add { }
            remove { }
        }

        public event Action? OnGeologyChanged
        {
            add { }
            remove { }
        }

        public event Action? OnLevelChanged
        {
            add { }
            remove { }
        }

        public event Action? OnNicknameChanged
        {
            add { }
            remove { }
        }

        public event Action? OnBasketChanged
        {
            add { }
            remove { }
        }

        public event Action<SkillType, long, long>? OnSkillProgress
        {
            add { }
            remove { }
        }

        public event Action? OnDailyBonusChanged
        {
            add { }
            remove { }
        }

        public event Action? OnMissionChanged
        {
            add { }
            remove { }
        }

        public event Action? OnMissionArrowChanged
        {
            add { }
            remove { }
        }

        public event Action? OnStatusLinesChanged
        {
            add { }
            remove { }
        }
    }

    private sealed class StubMapDataProvider : IMapDataProvider
    {
        public ushort WorldWidth => 100;
        public ushort WorldHeight => 100;
        public Camera MainCamera => null!;
        public bool IsStandaloneMode => false;

        public CellConfigurationPacket GetCellConfig(CellType type) => default;
        public float GetMoveCooldown(CellType cellType) => 1f;
        public float GetMinMoveCooldown() => 1f;
        public bool TryGetTileGroup(CellType type, out int groupId)
        {
            groupId = 0;
            return false;
        }

        public Color GetCellMinimapColor(CellType type) => Color.white;
        public Color32 GetCellMinimapColor32(CellType type) => new(255, 255, 255, 255);
        public void UpdateMovementSpeeds(MovementSpeedPacket packet) { }
        public void LoadWorldInit(WorldInitPacket packet) { }
        public Action? OnWorldInitialized { get; set; }
        public Action? OnWorldDataLoaded { get; set; }
        public void ResetWorldState() { }
    }

    private sealed class StubLocalPlayerState : ILocalPlayerState
    {
        public ILocalPlayer? Current => null;
        public event Action<ILocalPlayer?>? Changed;
        public void Publish(ILocalPlayer player) => Changed?.Invoke(player);
        public void Clear(ILocalPlayer player) => Changed?.Invoke(null);
        public bool IsAuthenticated => true;
        public void SetAuthenticated(bool authenticated) { }
    }
}

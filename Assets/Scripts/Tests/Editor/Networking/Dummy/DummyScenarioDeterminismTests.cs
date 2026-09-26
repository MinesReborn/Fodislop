#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Connection;
using MinesServer.Networking.Connection.Client;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Shared;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.Networking;

// Офлайн-сервер на виртуальных часах: один и тот же сценарий с одним и тем же
// зерном даёт один и тот же поток событий до миллисекунды и до пакета.
public sealed class DummyScenarioDeterminismTests
{
    private const int Seed = 20260916;

    private string _tokenDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        _tokenDirectory = Path.Combine(Path.GetTempPath(), $"kern_dummy_tokens_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tokenDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tokenDirectory))
        {
            Directory.Delete(_tokenDirectory, recursive: true);
        }
    }

    [TestCase(OfflineScenario.RejectAuthentication)]
    [TestCase(OfflineScenario.DisconnectDuringHandshake)]
    [TestCase(OfflineScenario.HandshakeTimeout)]
    [TestCase(OfflineScenario.WorldInitializationTimeout)]
    public void HandshakeScenario_RepeatsExactlyForSameSeed(OfflineScenario scenario)
    {
        List<string> first = RunHandshake(scenario, Seed, "first");
        List<string> second = RunHandshake(scenario, Seed, "second");

        Assert.That(first, Is.Not.Empty);
        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void WorldInitializationTimeout_IssuesTokenAndStaysConnected()
    {
        List<string> log = RunHandshake(OfflineScenario.WorldInitializationTimeout, Seed, "timeout");

        Assert.That(log, Is.EqualTo(new[]
        {
            "0 connecting",
            "0 connected",
            $"0 packet {nameof(AuthTokenPacket)}",
            "60000 disconnecting",
            "60100 disconnected",
        }));
    }

    [Test]
    public void DisconnectDuringHandshake_NeverReportsConnected()
    {
        List<string> log = RunHandshake(OfflineScenario.DisconnectDuringHandshake, Seed, "drop");

        Assert.That(log, Does.Not.Contain("0 connected"));
        Assert.That(log.Take(3), Is.EqualTo(new[] { "0 connecting", "0 disconnecting", "0 disconnected" }));
    }

    [Test]
    public void IssuedToken_DependsOnlyOnSeed()
    {
        string first = IssueToken(Seed, "a");
        string repeated = IssueToken(Seed, "b");
        string other = IssueToken(Seed + 1, "c");

        Assert.That(repeated, Is.EqualTo(first));
        Assert.That(other, Is.Not.EqualTo(first));
        Assert.That(first, Has.Length.EqualTo(32));
    }

    [Test]
    public void ChatLoop_RepeatsExactlyForSameSeedAndDiffersForAnother()
    {
        List<string> first = RunChat(Seed);
        List<string> repeated = RunChat(Seed);
        List<string> other = RunChat(Seed + 1);

        Assert.That(first, Has.Count.GreaterThanOrEqualTo(5));
        Assert.That(repeated, Is.EqualTo(first));
        Assert.That(other, Is.Not.EqualTo(first));
    }

    [Test]
    public void ChatLoop_OfPreviousSessionStopsWhenANewSessionStarts()
    {
        var clock = new VirtualDummyClock(Seed);
        using var operations = new AsyncOperationSupervisor();
        int currentSession = 1;
        var chat = new DummyChatSimulator(_ => { }, version => version == currentSession, operations, clock);

        chat.SendChatMock(lifecycleVersion: 1);
        currentSession = 2;
        chat.SendChatMock(lifecycleVersion: 2);
        clock.Advance(15000);

        Assert.That(operations.ActiveOperationNames, Is.EqualTo(new[] { "dummy_chat_loop" }), "The chat loop of the first session survived the second.");

        currentSession = 3;
        clock.Advance(15000);
        Assert.That(operations.ActiveCount, Is.Zero);
    }

    [Test]
    public void DailyBonus_BecomesAvailableAfterTenVirtualSeconds()
    {
        var clock = new VirtualDummyClock(Seed);
        var sent = new List<(long At, ServerPacket Packet)>();
        using var operations = new AsyncOperationSupervisor();
        bool alive = true;
        var buffs = new DummyBuffManager(packet => sent.Add((clock.NowMilliseconds, packet)), operations, clock, _ => alive);

        buffs.StartDailyBonusLoop(lifecycleVersion: 1);
        clock.Advance(9999);
        Assert.That(sent.Where(p => p.Packet.Payload is DailyBonusStatePacket), Is.Empty);

        clock.Advance(1);
        (long at, ServerPacket packet) = sent.Single(p => p.Packet.Payload is DailyBonusStatePacket);
        Assert.That(at, Is.EqualTo(10000));
        Assert.That(((DailyBonusStatePacket)packet.Payload).Enabled, Is.True);

        alive = false;
        clock.Advance(1000);
    }

    [Test]
    public void Buff_ExpiresOnTheFirstCheckAfterItsVirtualDuration()
    {
        var clock = new VirtualDummyClock(Seed);
        var cleared = new List<long>();
        using var operations = new AsyncOperationSupervisor();
        bool alive = true;
        var buffs = new DummyBuffManager(
            packet =>
            {
                if (packet.Payload is MinesServer.Networking.Server.Packets.Information.StatusPanel.ClearStatusLinePacket)
                {
                    cleared.Add(clock.NowMilliseconds);
                }
            },
            operations,
            clock,
            _ => alive);

        buffs.StartBuffLoop(lifecycleVersion: 1);
        buffs.ActivateBuff("xp3", durationSeconds: 5, System.Drawing.Color.Green, "xp3");
        clock.Advance(4999);
        Assert.That(cleared, Is.Empty);

        clock.Advance(1);
        Assert.That(cleared, Is.EqualTo(new long[] { 5000 }));

        alive = false;
        clock.Advance(1000);
    }

    private List<string> RunHandshake(OfflineScenario scenario, int seed, string runName)
    {
        var clock = new VirtualDummyClock(seed);
        var log = new List<string>();
        using var operations = new AsyncOperationSupervisor();
        var connection = CreateConnection(clock, operations, scenario, runName);
        connection.OnConnecting += () => log.Add($"{clock.NowMilliseconds} connecting");
        connection.OnConnected += () => log.Add($"{clock.NowMilliseconds} connected");
        connection.OnDisconnecting += () => log.Add($"{clock.NowMilliseconds} disconnecting");
        connection.OnDisconnected += () => log.Add($"{clock.NowMilliseconds} disconnected");
        connection.OnReceived += packet => log.Add($"{clock.NowMilliseconds} packet {packet.Payload.GetType().Name}");

        try
        {
            connection.Connect();
            clock.Advance(0);
            if (connection.ConnectionStatus == ConnectionStatus.Connected)
            {
                connection.SendAsync(Hello(string.Empty));
            }

            clock.Advance(60000);
            connection.Disconnect();
            clock.Advance(100);
        }
        finally
        {
            connection.Dispose();
        }

        return log;
    }

    private string IssueToken(int seed, string runName)
    {
        var clock = new VirtualDummyClock(seed);
        using var operations = new AsyncOperationSupervisor();
        var connection = CreateConnection(clock, operations, OfflineScenario.WorldInitializationTimeout, runName);
        string? token = null;
        connection.OnReceived += packet =>
        {
            if (packet.Payload is AuthTokenPacket issued)
            {
                token = issued.Token;
            }
        };

        try
        {
            connection.Connect();
            clock.Advance(0);
            connection.SendAsync(Hello(string.Empty));
        }
        finally
        {
            connection.Dispose();
        }

        return token ?? throw new AssertionException("The offline server issued no token.");
    }

    private static List<string> RunChat(int seed)
    {
        var clock = new VirtualDummyClock(seed);
        var log = new List<string>();
        using var operations = new AsyncOperationSupervisor();
        bool alive = true;
        var chat = new DummyChatSimulator(
            packet =>
            {
                if (packet.Payload is ChatMessageListPacket list)
                {
                    foreach (ChatMessagePacket message in list.Messages)
                    {
                        log.Add($"{clock.NowMilliseconds} {message}");
                    }
                }
            },
            _ => alive,
            operations,
            clock);

        chat.SendChatMock(lifecycleVersion: 1);
        clock.Advance(60000);
        alive = false;
        clock.Advance(12000);
        return log;
    }

    private DummyConnection CreateConnection(
        VirtualDummyClock clock,
        IAsyncOperationSupervisor operations,
        OfflineScenario scenario,
        string runName) =>
        new(
            new NoTextures(),
            new NoItems(),
            operations,
            new RuntimeDebugSettings(),
            new OfflineScenarioSettings { Scenario = scenario },
            new UnavailableDummyWorldMapSource(),
            new DummyTokenStore(Path.Combine(_tokenDirectory, runName + ".json")),
            clock);

    private static ClientPacket Hello(string token) =>
        new(0, new ClientHelloPacket(1, "test", 0, "fingerprint", token));

    private sealed class NoTextures : ITextureStorageService
    {
        public event Action<string> OnTextureLoaded
        {
            add { }
            remove { }
        }

        public bool HasTexture(string filename) => false;

        public UniTask<Texture2D?> GetTextureAsync(string filename, CancellationToken cancellationToken = default) =>
            UniTask.FromResult<Texture2D?>(null);

        public UniTask<byte[]?> GetTextureData(string filename, CancellationToken cancellationToken = default) =>
            UniTask.FromResult<byte[]?>(null);
    }

    private sealed class NoItems : IItemCatalog
    {
        public IEnumerable<ItemType> AllTypes => [];

        public string GetName(ItemType type) => type.ToString();

        public string GetDescription(ItemType type) => string.Empty;

        public Texture2D? GetIcon(ItemType type) => null;
    }
}

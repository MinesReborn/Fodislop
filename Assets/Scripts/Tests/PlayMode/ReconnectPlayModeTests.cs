#nullable enable

using System.Collections;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Game.Managers;
using Kern.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

namespace Kern.Tests.PlayMode;

// Переподключение в живой игре на офлайн-сервере: сессия восстанавливается на
// месте, без второй сцены, второго контейнера и лишних подписчиков пакетов.
[TestFixture]
public sealed class ReconnectPlayModeTests
{
    private const string TestDummyToken = "playmode-reconnect-token";
    private BootstrapLifetimeScope _bootstrap = null!;
    private DummyAuthenticationScope _authentication = null!;
    private IConnectionService _connection = null!;
    private IOfflineScenarioSettings _scenario = null!;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        _authentication = DummyAuthenticationScope.Seed(TestDummyToken);
        yield return PlayModeHarness.StartAtGateway();
        _bootstrap = PlayModeHarness.FindBootstrap()!;
        _connection = _bootstrap.Container.Resolve<IConnectionService>();
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
    public IEnumerator ServerReconnect_RestoresSessionAndReloadsWorldInPlace()
    {
        GameManager game = PlayModeHarness.RequireInGame<GameManager>();
        PacketHandler handler = PlayModeHarness.RequireInGame<PacketHandler>();
        int statusChanges = 0;
        int hidden = 0;
        _connection.OnReconnectStatusChanged += OnStatus;
        _connection.OnReconnectHidden += OnHidden;
        try
        {
            _connection.TriggerReconnect("playmode reconnect");
            yield return WaitForWorldReload(game, "The session did not come back after a server reconnect.");
        }
        finally
        {
            _connection.OnReconnectStatusChanged -= OnStatus;
            _connection.OnReconnectHidden -= OnHidden;
        }

        Assert.That(statusChanges, Is.GreaterThan(0), "The player saw no reconnect status.");
        Assert.That(hidden, Is.GreaterThan(0), "The reconnect overlay was never hidden.");
        Assert.That(_bootstrap.CurrentSceneName, Is.EqualTo(ProjectRuntimeContracts.SceneNames.MainGame));
        Assert.That(PlayModeHarness.CountScopes(PlayModeHarness.Scene(ProjectRuntimeContracts.SceneNames.MainGame)), Is.EqualTo(1));
        Assert.That(PlayModeHarness.RequireInGame<PacketHandler>(), Is.SameAs(handler));
        Assert.That(handler.IsSubscribed, Is.True);

        void OnStatus(string _) => statusChanges++;
        void OnHidden() => hidden++;
    }

    [UnityTest]
    public IEnumerator ServerDisconnect_ReportsReasonAndDoesNotReconnect()
    {
        string? reason = null;
        int statusChanges = 0;
        _connection.OnDisconnectReason += OnReason;
        _connection.OnReconnectStatusChanged += OnStatus;
        try
        {
            _connection.TriggerDisconnect("playmode maintenance");
            yield return PlayModeHarness.WaitUntil(
                () => reason != null,
                PlayModeHarness.UITimeoutSeconds,
                "The disconnect reason never reached the client.");

            // Первая попытка автопереподключения идёт через секунду; ждём
            // заметно дольше, чтобы убедиться, что её нет.
            yield return new WaitForSecondsRealtime(3f);
        }
        finally
        {
            _connection.OnDisconnectReason -= OnReason;
            _connection.OnReconnectStatusChanged -= OnStatus;
        }

        Assert.That(reason, Is.EqualTo("playmode maintenance"));
        Assert.That(_connection.IsConnected, Is.False);
        Assert.That(statusChanges, Is.Zero, "A server disconnect must not start auto-reconnect.");

        void OnReason(string value) => reason = value;
        void OnStatus(string _) => statusChanges++;
    }

    [UnityTest]
    public IEnumerator ReconnectAfterDroppedHandshake_ReusesTheOfflineServer()
    {
        GameManager game = PlayModeHarness.RequireInGame<GameManager>();
        _connection.Disconnect();

        // Офлайн-сервер закрывает соединение через 100 мс; раньше новое
        // подключение он не примет.
        yield return new WaitForSecondsRealtime(0.5f);
        _scenario.Scenario = OfflineScenario.DisconnectDuringHandshake;
        _connection.Connect();
        yield return PlayModeHarness.Frames(10);
        Assert.That(_connection.IsConnected, Is.False, "The dropped handshake unexpectedly connected.");

        // Повторный Connect поверх оборванного соединения раньше вызывал
        // Dispose у синглтона DummyConnection и ломал его мир навсегда.
        _scenario.Scenario = OfflineScenario.HappyPath;
        _connection.Connect();
        yield return WaitForWorldReload(game, "The offline server did not serve a world after a dropped handshake.");
    }

    [UnityTest]
    public IEnumerator RepeatedReconnects_LeaveOneSubscribedPacketHandler()
    {
        GameManager game = PlayModeHarness.RequireInGame<GameManager>();
        PacketHandler handler = PlayModeHarness.RequireInGame<PacketHandler>();
        int received = 0;
        _connection.OnPacketReceived += OnPacket;
        try
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                _connection.TriggerReconnect($"playmode reconnect {attempt}");
                yield return WaitForWorldReload(game, $"Reconnect {attempt} did not restore the session.");
            }

            received = 0;
            yield return new WaitForSecondsRealtime(6f);
        }
        finally
        {
            _connection.OnPacketReceived -= OnPacket;
        }

        Assert.That(handler.IsSubscribed, Is.True);
        Assert.That(PlayModeHarness.RequireInGame<PacketHandler>(), Is.SameAs(handler));

        // Пинг приходит раз в пять секунд. Больше двух за шесть секунд
        // значило бы, что петли старых сессий пережили переподключение.
        Assert.That(received, Is.InRange(1, 2), "Ping loops of previous sessions are still running.");

        void OnPacket(MinesServer.Networking.Server.Packets.ServerPacket packet)
        {
            if (packet.Payload is MinesServer.Networking.Server.Packets.Connection.PingPacket)
            {
                received++;
            }
        }
    }

    // Мир был загружен и до переподключения, поэтому одного IsWorldLoaded
    // мало: ждём новый WorldInit от сервера и готовность уже после него.
    private IEnumerator WaitForWorldReload(GameManager game, string failureMessage)
    {
        bool worldInitReceived = false;
        _connection.OnPacketReceived += OnPacket;
        try
        {
            yield return PlayModeHarness.WaitUntil(
                () => worldInitReceived && _connection.IsConnected && game.IsWorldLoaded,
                PlayModeHarness.WorldTimeoutSeconds,
                failureMessage);
        }
        finally
        {
            _connection.OnPacketReceived -= OnPacket;
        }

        void OnPacket(MinesServer.Networking.Server.Packets.ServerPacket packet)
        {
            worldInitReceived |= packet.Payload is MinesServer.Networking.Server.Packets.Connection.WorldInitPacket;
        }
    }
}

#nullable enable

using Fodinae.Core.Interfaces;
using MinesServer.Networking.Connection.Client;
using MinesServer.Networking.Shared;
using NUnit.Framework;

namespace Fodinae.Tests.Networking;

public sealed class DummyConnectionSessionTests
{
    [Test]
    public void OfflineScenarioSettings_DefaultToHappyPath()
    {
        var settings = new OfflineScenarioSettings();

        Assert.That(settings.Scenario, Is.EqualTo(OfflineScenario.HappyPath));

        settings.Scenario = OfflineScenario.RejectAuthentication;
        Assert.That(settings.Scenario, Is.EqualTo(OfflineScenario.RejectAuthentication));
    }

    [TestCase(OfflineScenario.HappyPath, false, false, false, false)]
    [TestCase(OfflineScenario.RejectAuthentication, false, false, true, false)]
    [TestCase(OfflineScenario.DisconnectDuringHandshake, false, true, false, false)]
    [TestCase(OfflineScenario.HandshakeTimeout, true, false, false, false)]
    [TestCase(OfflineScenario.WorldInitializationTimeout, false, false, false, true)]
    public void DummyScenarioController_MapsConfiguredScenarioDeterministically(
        OfflineScenario scenario,
        bool stallsConnection,
        bool disconnects,
        bool rejectsAuthentication,
        bool stallsWorld)
    {
        var settings = new OfflineScenarioSettings { Scenario = scenario };
        var controller = new DummyScenarioController(settings);

        controller.BeginLifecycle(7);

        Assert.That(controller.StallsConnection, Is.EqualTo(stallsConnection));
        Assert.That(controller.DisconnectsDuringHandshake, Is.EqualTo(disconnects));
        Assert.That(controller.RejectsAuthentication, Is.EqualTo(rejectsAuthentication));
        Assert.That(controller.StallsWorldInitialization, Is.EqualTo(stallsWorld));
        Assert.That(controller.TryBeginHello(7), Is.True);
        Assert.That(controller.TryBeginHello(7), Is.False);
        Assert.That(controller.TryBeginHello(6), Is.False);
    }

    [Test]
    public void DummyScenarioController_CapturesSettingsForWholeLifecycle()
    {
        var settings = new OfflineScenarioSettings
        {
            Scenario = OfflineScenario.RejectAuthentication,
        };
        var controller = new DummyScenarioController(settings);
        controller.BeginLifecycle(1);

        settings.Scenario = OfflineScenario.HandshakeTimeout;

        Assert.That(controller.RejectsAuthentication, Is.True);
        Assert.That(controller.StallsConnection, Is.False);
        Assert.That(controller.TryBeginHello(1), Is.True);
        Assert.That(controller.TryBeginHello(1), Is.False);

        controller.BeginLifecycle(2);

        Assert.That(controller.RejectsAuthentication, Is.False);
        Assert.That(controller.StallsConnection, Is.True);
        Assert.That(controller.TryBeginHello(1), Is.False);
        Assert.That(controller.TryBeginHello(2), Is.True);
    }

    [Test]
    public void Disconnect_InvalidatesPendingConnectCompletion()
    {
        var session = new DummyConnectionSession();
        Assert.That(session.TryBeginConnect(out int connectVersion), Is.True);
        Assert.That(session.TryBeginDisconnect(out int disconnectVersion), Is.True);

        bool staleConnectCompleted = session.TryCompleteConnect(connectVersion);

        Assert.That(staleConnectCompleted, Is.False);
        Assert.That(session.Status, Is.EqualTo(ConnectionStatus.Disconnecting));
        Assert.That(session.TryCompleteDisconnect(disconnectVersion), Is.True);
        Assert.That(session.Status, Is.EqualTo(ConnectionStatus.Disconnected));
    }

    [Test]
    public void Stop_InvalidatesAliveGeneration()
    {
        var session = new DummyConnectionSession();
        session.TryBeginConnect(out int lifecycleVersion);
        session.TryCompleteConnect(lifecycleVersion);
        Assert.That(session.IsAlive(lifecycleVersion), Is.True);

        session.Stop();

        Assert.That(session.IsAlive(lifecycleVersion), Is.False);
        Assert.That(session.Status, Is.EqualTo(ConnectionStatus.Disconnected));
    }

    [Test]
    public void StableUserId_IsDeterministicAndInsideOfflineRange()
    {
        long first = DummyAuthSession.StableUserId("device-a");
        long repeated = DummyAuthSession.StableUserId("device-a");
        long other = DummyAuthSession.StableUserId("device-b");

        Assert.That(repeated, Is.EqualTo(first));
        Assert.That(other, Is.Not.EqualTo(first));
        Assert.That(first, Is.InRange(10_000_000_000L, 11_999_999_999L));
    }
}

#nullable enable

using Kern.Core.Interfaces;
using MinesServer.Networking.Contracts;
using NUnit.Framework;

using Kern.Networking.Dummy;

namespace Kern.Tests.Networking;

public sealed class DummyConnectionSessionTests
{
    [Test]
    public void DummyScenarioSettings_DefaultToHappyPath()
    {
        var settings = new DummyScenarioSettings();

        Assert.That(settings.Scenario, Is.EqualTo(DummyScenario.HappyPath));

        settings.Scenario = DummyScenario.RejectAuthentication;
        Assert.That(settings.Scenario, Is.EqualTo(DummyScenario.RejectAuthentication));
    }

    [TestCase(DummyScenario.HappyPath, false, false, false, false)]
    [TestCase(DummyScenario.RejectAuthentication, false, false, true, false)]
    [TestCase(DummyScenario.DisconnectDuringHandshake, false, true, false, false)]
    [TestCase(DummyScenario.HandshakeTimeout, true, false, false, false)]
    [TestCase(DummyScenario.WorldInitializationTimeout, false, false, false, true)]
    public void DummyScenarioController_MapsConfiguredScenarioDeterministically(
        DummyScenario scenario,
        bool stallsConnection,
        bool disconnects,
        bool rejectsAuthentication,
        bool stallsWorld)
    {
        var settings = new DummyScenarioSettings { Scenario = scenario };
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
        var settings = new DummyScenarioSettings
        {
            Scenario = DummyScenario.RejectAuthentication,
        };
        var controller = new DummyScenarioController(settings);
        controller.BeginLifecycle(1);

        settings.Scenario = DummyScenario.HandshakeTimeout;

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
    public void ConnectDuringDisconnect_SupersedesTheLateDisconnectCompletion()
    {
        var session = new DummyConnectionSession();
        session.TryBeginConnect(out int firstVersion);
        session.TryCompleteConnect(firstVersion);
        Assert.That(session.TryBeginDisconnect(out int disconnectVersion), Is.True);

        Assert.That(session.TryBeginConnect(out int reconnectVersion), Is.True);
        Assert.That(session.TryCompleteDisconnect(disconnectVersion), Is.False);
        Assert.That(session.TryCompleteConnect(reconnectVersion), Is.True);
        Assert.That(session.Status, Is.EqualTo(ConnectionStatus.Connected));
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
    public void StableUserId_IsDeterministicAndInsideDummyRange()
    {
        long first = DummyAuthSession.StableUserId("device-a");
        long repeated = DummyAuthSession.StableUserId("device-a");
        long other = DummyAuthSession.StableUserId("device-b");

        Assert.That(repeated, Is.EqualTo(first));
        Assert.That(other, Is.Not.EqualTo(first));
        Assert.That(first, Is.InRange(10_000_000_000L, 11_999_999_999L));
    }
}

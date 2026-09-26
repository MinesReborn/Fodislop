#nullable enable

using MinesServer.Networking.Connection.Client;
using MinesServer.Networking.Shared;
using NUnit.Framework;

namespace Kern.Tests.Networking;

[TestFixture]
public class DummyConnectionSessionFuzzTests
{
    [Test]
    public void RandomLifecycle_NeverEntersInvalidState()
    {
        var random = new System.Random(42);
        var session = new DummyConnectionSession();
        int lastVersion = 0;

        for (int i = 0; i < 500; i++)
        {
            int action = random.Next(5);
            switch (action)
            {
                case 0:
                    if (session.TryBeginConnect(out int v)) lastVersion = v;
                    break;
                case 1:
                    session.TryCompleteConnect(lastVersion);
                    break;
                case 2:
                    if (session.TryBeginDisconnect(out int v2)) lastVersion = v2;
                    break;
                case 3:
                    session.TryCompleteDisconnect(lastVersion);
                    break;
                case 4:
                    session.Stop();
                    lastVersion++;
                    break;
            }
            bool valid = session.Status == ConnectionStatus.Disconnected ||
                session.Status == ConnectionStatus.Connecting ||
                session.Status == ConnectionStatus.Connected ||
                session.Status == ConnectionStatus.Disconnecting;
            Assert.That(valid, Is.True, $"i={i}, status={session.Status}");
        }
    }

    [Test]
    public void StaleVersion_AlwaysRejected()
    {
        var random = new System.Random(42);
        for (int i = 0; i < 50; i++)
        {
            var session = new DummyConnectionSession();
            session.TryBeginConnect(out int v);
            session.TryCompleteConnect(v);
            int stale = v - 1 - random.Next(0, 10);
            Assert.IsFalse(session.IsAlive(stale), $"i={i}");
        }
    }

    [Test]
    public void DoubleBeginConnect_SecondRejected()
    {
        var random = new System.Random(42);
        for (int i = 0; i < 50; i++)
        {
            var session = new DummyConnectionSession();
            session.TryBeginConnect(out _);
            Assert.IsFalse(session.TryBeginConnect(out _), $"i={i}");
        }
    }

    [Test]
    public void Stop_ResetsToDisconnected()
    {
        var random = new System.Random(42);
        for (int i = 0; i < 50; i++)
        {
            var session = new DummyConnectionSession();
            session.TryBeginConnect(out _);
            session.Stop();
            Assert.That(session.Status, Is.EqualTo(ConnectionStatus.Disconnected));
            Assert.IsFalse(session.TryBeginDisconnect(out _));
        }
    }
}

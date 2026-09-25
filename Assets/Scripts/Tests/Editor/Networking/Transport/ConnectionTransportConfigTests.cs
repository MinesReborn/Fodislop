#nullable enable

using System.Net;
using Kern.Networking.Connection;
using NUnit.Framework;

namespace Kern.Tests.Networking;

[TestFixture]
public class ConnectionTransportConfigTests
{
    [Test]
    public void SelectTransport_DummyFlag_ReturnsDummy()
    {
        Assert.AreEqual(
            ConnectionTransportKind.Dummy,
            ConnectionTransportConfig.SelectTransport(useDummyConnection: true));
    }

    [Test]
    public void SelectTransport_RealNetworking_ReturnsTcp()
    {
        Assert.AreEqual(
            ConnectionTransportKind.Tcp,
            ConnectionTransportConfig.SelectTransport(useDummyConnection: false));
    }

    [Test]
    public void TryResolveEndpoint_LocalhostIp_ReturnsLoopbackAndPort()
    {
        bool ok = ConnectionTransportConfig.TryResolveEndpoint(
            "127.0.0.1",
            7777,
            out IPAddress address,
            out int port);

        Assert.IsTrue(ok);
        Assert.AreEqual(IPAddress.Loopback, address);
        Assert.AreEqual(7777, port);
    }

    [Test]
    public void TryResolveEndpoint_EmptyHost_UsesDefaultHost()
    {
        bool ok = ConnectionTransportConfig.TryResolveEndpoint(
            string.Empty,
            7777,
            out IPAddress address,
            out _);

        Assert.IsTrue(ok);
        Assert.AreEqual(IPAddress.Parse(ConnectionTransportConfig.DefaultServerHost), address);
    }

    [Test]
    public void TryResolveEndpoint_NullHost_UsesDefaultHost()
    {
        bool ok = ConnectionTransportConfig.TryResolveEndpoint(
            null,
            7777,
            out IPAddress address,
            out _);

        Assert.IsTrue(ok);
        Assert.AreEqual(IPAddress.Parse(ConnectionTransportConfig.DefaultServerHost), address);
    }

    [Test]
    public void TryResolveEndpoint_WhitespaceHost_UsesDefaultHost()
    {
        bool ok = ConnectionTransportConfig.TryResolveEndpoint(
            "   ",
            7777,
            out IPAddress address,
            out _);

        Assert.IsTrue(ok);
        Assert.AreEqual(IPAddress.Parse(ConnectionTransportConfig.DefaultServerHost), address);
    }

    [Test]
    public void TryResolveEndpoint_InvalidPorts_ReturnFalse()
    {
        Assert.IsFalse(ConnectionTransportConfig.TryResolveEndpoint("127.0.0.1", 0, out _, out _));
        Assert.IsFalse(ConnectionTransportConfig.TryResolveEndpoint("127.0.0.1", -1, out _, out _));
        Assert.IsFalse(ConnectionTransportConfig.TryResolveEndpoint("127.0.0.1", 65536, out _, out _));
    }

    [Test]
    public void TryResolveEndpoint_InvalidHost_ReturnsFalse()
    {
        // "invalid host!" is neither an IP literal nor a resolvable hostname.
        Assert.IsFalse(ConnectionTransportConfig.TryResolveEndpoint("invalid host!", 7777, out _, out _));
    }
}

#nullable enable

using System;
using System.Net;
using System.Net.Sockets;
using Kern.Core;

namespace Kern.Networking.Connection;
public enum ConnectionTransportKind
{
    Dummy,
    Tcp,
}

public static class ConnectionTransportConfig
{
    public const string DefaultServerHost = ProjectRuntimeContracts.ClientConfiguration.DefaultServerHost;
    public const int DefaultServerPort = ProjectRuntimeContracts.ClientConfiguration.DefaultServerPort;

    public static ConnectionTransportKind SelectTransport(bool useDummyConnection)
    {
        return useDummyConnection
            ? ConnectionTransportKind.Dummy
            : ConnectionTransportKind.Tcp;
    }

    public static bool TryParseEndpoint(string? value, out string host, out int port)
    {
        host = DefaultServerHost;
        port = DefaultServerPort;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        string candidate = value.Trim();
        if (!candidate.Contains(':'))
        {
            host = candidate;
            return true;
        }

        if (!Uri.TryCreate($"tcp://{candidate}", UriKind.Absolute, out Uri? endpoint) ||
            string.IsNullOrWhiteSpace(endpoint.Host) ||
            endpoint.Port <= 0 ||
            endpoint.Port > 65535)
        {
            return false;
        }

        host = endpoint.Host;
        port = endpoint.Port;
        return true;
    }

    public static bool TryResolveEndpoint(
        string? host,
        int port,
        out IPAddress address,
        out int validatedPort)
    {
        address = null!;
        validatedPort = 0;
        if (port <= 0 || port > 65535)
        {
            return false;
        }

        string resolvedHost = string.IsNullOrWhiteSpace(host)
            ? DefaultServerHost
            : host.Trim();
        if (!IPAddress.TryParse(resolvedHost, out address) &&
            !TryResolveHostname(resolvedHost, out address))
        {
            return false;
        }

        validatedPort = port;
        return true;
    }

    private static bool TryResolveHostname(string host, out IPAddress address)
    {
        address = null!;
        try
        {
            IPAddress[] addresses = Dns.GetHostAddresses(host);
            if (addresses.Length == 0)
            {
                return false;
            }

            address = addresses[0];
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            // Не-хостнейм строка (пробелы, спецсимволы) — не endpoint.
            return false;
        }
    }
}

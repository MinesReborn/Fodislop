#nullable enable

using System;
using System.Net;
using Kern.Core;
using Kern.Core.Interfaces;
using MinesServer.Networking.Connection;
using UnityEngine;

namespace Kern.Networking.Connection;

public sealed class ConnectionTransportFactory
{
    private readonly IClientConfigManager _clientConfigManager;
    private readonly IDummyConnection _dummyConnection;
    public ConnectionTransportFactory(
    IClientConfigManager clientConfigManager,
    IDummyConnection dummyConnection)
    {
        _clientConfigManager = clientConfigManager;
        _dummyConnection = dummyConnection;
    }

    public IServerConnection Create()
    {
        ClientConfig? config = _clientConfigManager.Config;
        if (config == null)
        {
            Debug.LogWarning(
                "[Connection] Client config is not initialized yet; using the Bootstrap-registered DummyConnection.");
            return _dummyConnection;
        }

        ConnectionSettings settings = config.Connection;
        if (ConnectionTransportConfig.SelectTransport(settings.UseDummyConnection) ==
            ConnectionTransportKind.Dummy)
        {
            Debug.Log(
                "[Connection] Transport: DummyConnection (dummy stub). Set UseDummyConnection=false in client config for the real server.");
            return _dummyConnection;
        }

        if (!ConnectionTransportConfig.TryResolveEndpoint(
                settings.ServerHost,
                settings.ServerPort,
                out IPAddress address,
                out int port))
        {
            throw new InvalidOperationException(
                $"[Connection] Invalid server endpoint '{settings.ServerHost}:{settings.ServerPort}' in client config. " +
                "Expected a valid host/IP and a port in [1, 65535].");
        }

        Debug.Log(
            $"[Connection] Transport: ManagedTcpConnection {address}:{port} (fallback transport, NetCoreServer receive-loop is broken).");
        return new ManagedTcpConnection(address, port);
    }
}

#nullable enable

using System;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Connection;
using MinesServer.Networking.Server.Packets;

namespace Kern.Core.Interfaces;

public sealed class ConnectionStartupInterruptedException : InvalidOperationException
{
    public ConnectionStartupInterruptedException()
        : base("Server connection was lost during world startup.")
    {
    }
}

// Транспорт, который считает принятые байты: по ним строится плавный прогресс скачивания внутри крупного пакета
public interface ITransportTraffic
{
    long ReceivedBytes { get; }
}

public interface IConnectionService
{
    // Принятые текущим транспортом байты; у нового транспорта счёт идёт с нуля. 0 — транспорт счёт не ведёт
    long ReceivedBytes { get; }
    bool IsConnected { get; }
    bool IsAutoReconnecting { get; }
    bool IsWorldStartupPending { get; }
    bool IsDummy { get; }
    void Connect();
    // Пока пакеты удержаны, транспорт подключается и принимает их в очередь, но обработчикам они не отдаются:
    // соединение стартует параллельно инициализации презентации и открывается, когда потребители готовы
    void HoldInboundPackets();
    void ReleaseInboundPackets();
    void Disconnect();
    void MarkWorldReady();
    void TriggerDisconnect(string reason);
    void TriggerReconnect(string reason);
    void HandleServerDisconnect(string reason);
    void HandleServerReconnect();
    void Send(ClientPacket packet);
    event Action<ServerPacket>? OnPacketReceived;
    event Action? OnPacketBatchStarted;
    event Action? OnPacketBatchCompleted;
    event Action? OnTransportDisconnected;
    event Action<string>? OnReconnectStatusChanged;
    event Action<string>? OnDisconnectReason;
    event Action? OnReconnectHidden;
}

public interface IDummyConnection : IServerConnection
{
    void TriggerDisconnect(string reason);
    void TriggerReconnect(string reason);
}

// Optional transport capability; coordinates are server cells, Y down.
public interface IWorldRegionRequester
{
    void RequestWorldRegion(string worldCodeName, UnityEngine.RectInt serverRegion);
}

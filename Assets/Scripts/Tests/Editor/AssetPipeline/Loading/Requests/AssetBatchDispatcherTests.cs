#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern;
using Kern.Core.Interfaces;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Server.Packets;
using NUnit.Framework;

using Kern.AssetPipeline.Loading;

namespace Kern.Tests.AssetPipeline;

[TestFixture]
public sealed class AssetBatchDispatcherTests
{
    private readonly ConnectedConnection _connection = new();

    [Test]
    public void RequestAssetBytesAsync_ConnectionProviderThrows_RemovesFailedRequest()
    {
        using var dispatcher = new AssetBatchDispatcher();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            dispatcher.RequestAssetBytesAsync(
                    "cells/1",
                    string.Empty,
                    CancellationToken.None,
                    () => throw new InvalidOperationException("connection provider failed"),
                    () => null)
                .GetAwaiter()
                .GetResult());

        Assert.That(exception.Message, Is.EqualTo("connection provider failed"));
        Assert.That(dispatcher.PendingCount, Is.Zero);
    }

    [Test]
    public void FailPendingOnDisconnect_FailsWaitingRequestsAndClearsQueue()
    {
        // Активное соединение обязано ответить: запрос встал в очередь и ждёт
        // приход байтов, никакого таймаута у него нет.
        using var dispatcher = new AssetBatchDispatcher();
        UniTask<byte[]> request = dispatcher.RequestAssetBytesAsync(
            "cells/1",
            string.Empty,
            CancellationToken.None,
            () => _connection,
            () => null);

        Assert.That(dispatcher.PendingCount, Is.EqualTo(1));

        // Разрыв транспорта — единственный честный признак того, что ответа
        // не будет: по нему ожидания фейлятся сразу.
        dispatcher.FailPendingOnDisconnect();

        Assert.That(dispatcher.PendingCount, Is.Zero);
        Assert.That(dispatcher.QueuedCount, Is.Zero);
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => request.GetAwaiter().GetResult());
        Assert.That(failure.Message, Does.Contain("Connection lost"));
    }

    /// Минимальное живое подключение: запрос должен встать в очередь на
    /// отправку, а не отвалиться на проверке связи.
    private sealed class ConnectedConnection : IConnectionService
    {
        public long ReceivedBytes => 0;

        public bool IsConnected => true;

        public bool IsAutoReconnecting => false;

        public bool IsWorldStartupPending => false;

        public bool IsDummy => false;

        public void Connect()
        {
        }

        public void HoldInboundPackets()
        {
        }

        public void ReleaseInboundPackets()
        {
        }

        public void Disconnect()
        {
        }

        public void MarkWorldReady()
        {
        }

        public void TriggerDisconnect(string reason)
        {
        }

        public void TriggerReconnect(string reason)
        {
        }

        public void HandleServerDisconnect(string reason)
        {
        }

        public void HandleServerReconnect()
        {
        }

        public void Send(ClientPacket packet)
        {
        }

        public event Action<ServerPacket>? OnPacketReceived;

        public event Action? OnPacketBatchStarted;

        public event Action? OnPacketBatchCompleted;

        public event Action? OnTransportDisconnected;

        public event Action<string>? OnReconnectStatusChanged;

        public event Action<string>? OnDisconnectReason;

        public event Action? OnReconnectHidden;
    }
}

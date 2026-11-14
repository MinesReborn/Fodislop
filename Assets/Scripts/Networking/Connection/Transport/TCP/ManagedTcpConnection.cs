#nullable enable

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Connection;
using MinesServer.Networking.Server;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Utilities;
using MinesServer.Networking.Contracts;
using UnityEngine;

namespace Kern.Networking.Connection
{
    /// <summary>
    /// Резервный TCP-транспорт с собственным блокирующим receive-loop'ом.
    /// Заменяет NetCoreServer-<c>TcpConnection</c> из пакета, у которого на
    /// текущей сборке цикл приёма не стартует (send работает, OnReceived не
    /// вызывается никогда — подтверждено netdiag-диагностикой от 11.09).
    /// Фрейминг идентичен пакетному: [int32 len][uint16 code][payload].
    /// События приходят с фонового потока — потребитель обязан маршалить сам
    /// (ConnectionManager уже ставит пакеты в ConcurrentQueue).
    /// </summary>
    public sealed class ManagedTcpConnection : IServerConnection, Kern.Core.Interfaces.ITransportTraffic
    {
        private const int ConnectTimeoutMs = 5000;
        private const int ReadBufferSize = 65536;

        private readonly PacketBuffer _buffer = new();
        private readonly object _sendLock = new();
        private readonly object _statusLock = new();
        private readonly IPAddress _address;
        private readonly int _port;
        private TcpClient? _client;
        private NetworkStream? _stream;
        private Thread? _worker;
        private volatile bool _disposing;
        private int _decodeErrorCount;
        private ConnectionStatus _status = ConnectionStatus.Disconnected;
        private SynchronizationContext? _mainContext;

        public ConnectionStatus ConnectionStatus
        {
            get { lock (_statusLock) return _status; }
        }

        public event Action<ServerPacket>? OnReceived;
        public event Action? OnConnected;
        public event Action? OnDisconnected;
        public event Action? OnDisconnecting;
        public event Action? OnConnecting;
        public event Action<Exception>? OnError;

        public int DecodeErrorCount => Volatile.Read(ref _decodeErrorCount);

        private long _receivedBytes;

        public long ReceivedBytes => Interlocked.Read(ref _receivedBytes);

        public ManagedTcpConnection(IPAddress address, int port)
        {
            _address = address;
            _port = port;
        }

        public void Connect()
        {
            if (ConnectionStatus != ConnectionStatus.Disconnected)
            {
                return;
            }

            SetStatus(ConnectionStatus.Connecting);
            _mainContext = SynchronizationContext.Current;
            PostToMain(() => OnConnecting?.Invoke());

            _worker = new Thread(RunWorker)
            {
                IsBackground = true,
                Name = "ManagedTcpConnection.Worker"
            };
            _worker.Start();
        }

        private void RunWorker()
        {
            try
            {
                var client = new TcpClient { NoDelay = true };
                // Ожидание синхронное намеренно: RunWorker идёт в отдельном
                // потоке (см. Connect), поэтому ждёт рабочий поток, а не кадр.
                // Переводить на await здесь нечего — выигрыша нет, а цикл чтения
                // ниже всё равно блокирующий.
                if (!client.ConnectAsync(_address, _port).Wait(ConnectTimeoutMs))
                {
                    client.Close();
                    throw new IOException($"Connect to {_address}:{_port} timed out after {ConnectTimeoutMs} ms.");
                }

                if (_disposing)
                {
                    client.Close();
                    return;
                }

                _client = client;
                _stream = client.GetStream();
                SetStatus(ConnectionStatus.Connected);
                PostToMain(() => OnConnected?.Invoke());

                RunReadLoop(client, _stream);
            }
            catch (Exception) when (_disposing)
            {
                // Явное закрытие: событие уже отправил Disconnect().
            }
            catch (SocketException exception) when (
                exception.SocketErrorCode == SocketError.ConnectionRefused)
            {
                // Сервер может быть выключен при запуске клиента. Это
                // обычный отказ подключения; состояние и OnDisconnected
                // выставятся в finally.
                Debug.Log(
                    $"[ManagedTcpConnection] Connection refused by {_address}:{_port}; connection remains disconnected.");
            }
            catch (AggregateException exception) when (IsConnectionRefused(exception))
            {
                // Task.Wait оборачивает отказ ConnectAsync в AggregateException.
                // Оставляем сообщение о недоступном сервере без исключения и
                // stack trace.
                Debug.Log(
                    $"[ManagedTcpConnection] Connection refused by {_address}:{_port}; connection remains disconnected.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ManagedTcpConnection] Connection lost:\n{ex}");
            }
            finally
            {
                CloseSocket();
                SetStatus(ConnectionStatus.Disconnected);
                PostToMain(() => OnDisconnected?.Invoke());
            }
        }

        private static bool IsConnectionRefused(AggregateException exception)
        {
            AggregateException flattened = exception.Flatten();
            if (flattened.InnerExceptions.Count == 0)
            {
                return false;
            }

            foreach (Exception innerException in flattened.InnerExceptions)
            {
                if (innerException is not SocketException socketException ||
                    socketException.SocketErrorCode != SocketError.ConnectionRefused)
                {
                    return false;
                }
            }

            return true;
        }

        private void RunReadLoop(TcpClient client, NetworkStream stream)
        {
            var chunk = new byte[ReadBufferSize];
            while (!_disposing && client.Connected)
            {
                int read = stream.Read(chunk, 0, chunk.Length);
                if (read <= 0)
                {
                    break; // сервер закрыл соединение
                }

                Interlocked.Add(ref _receivedBytes, read);
                _buffer.Put(chunk, read);
                while (_buffer.TryTake(out var frame))
                {
                    TrackAssetFrame(frame);
                    ServerPacket packet;
                    try
                    {
                        packet = ServerPacket.Decode(frame);
                    }
                    catch (Exception ex)
                    {
                        // Порченый кадр: логируем полностью (включая внутреннее
                        // исключение и hex-голову) и продолжаем. Кадр отброшен,
                        // но соединение цело — событие OnError даёт хосту
                        // основание решить «N ошибок — дисконнект».
                        int head = Math.Min(48, frame.Length);
                        var hex = new System.Text.StringBuilder(head * 3);
                        for (int i = 0; i < head; i++)
                            hex.Append(frame[i].ToString("X2")).Append(' ');
                        Debug.LogWarning(
                            $"[ManagedTcpConnection] Frame decode failed ({frame.Length}B): {ex}\nHEAD: {hex}" +
                            $"\n[NETDIAG] client sizes: RobotPosition={default(MinesServer.Networking.Server.Packets.World.RobotPositionPacket).Size}" +
                            $" Building={default(MinesServer.Networking.Server.Packets.World.BuildingPacket).Size}" +
                            $" RemoveBuilding={default(MinesServer.Networking.Server.Packets.World.RemoveBuildingPacket).Size}");
                        Interlocked.Increment(ref _decodeErrorCount);
                        OnError?.Invoke(ex);
                        continue;
                    }

                    OnReceived?.Invoke(packet);
                }
            }
        }

        public void SendAsync(ClientPacket packet)
        {
            var stream = _stream;
            if (stream == null || ConnectionStatus != ConnectionStatus.Connected)
            {
                // Раньше пакет отбрасывался молча, и отправитель не узнавал о
                // потере (страдало прежде всего рукопожатие ClientHello). Диагностика:
                // какой пакет, при каком статусе и с каким стримом.
                Debug.LogWarning(
                    $"[ManagedTcpConnection] Send dropped before write: " +
                    $"status={ConnectionStatus}, stream={(stream != null ? "open" : "null")}, " +
                    $"packet={packet.GetType().Name}.");
                return;
            }

            var buffer = new byte[packet.Size];
            packet.Encode(buffer.AsSpan());
            lock (_sendLock)
            {
                try
                {
                    stream.Write(buffer, 0, buffer.Length);
                    stream.Flush();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ManagedTcpConnection] Send failed: {ex.Message}");
                }
            }
        }

        public void Disconnect()
        {
            if (ConnectionStatus == ConnectionStatus.Disconnected)
            {
                return;
            }

            SetStatus(ConnectionStatus.Disconnecting);
            PostToMain(() => OnDisconnecting?.Invoke());
            _disposing = true;
            CloseSocket();
        }

        private void CloseSocket()
        {
            try { _stream?.Close(); } catch { }
            try { _client?.Close(); } catch { }
            _stream = null;
            _client = null;
        }

        private void SetStatus(ConnectionStatus status)
        {
            lock (_statusLock)
            {
                _status = status;
            }
        }

        /// <summary>
        /// События жизненного цикта (connected/disconnected) доставляются в
        /// главный поток: обработчики (ConnectionManager) трогают Unity-API
        /// (например, destroyCancellationToken в CompleteConnectionAsync).
        /// OnReceived остаётся в потоке чтения — потребитель кладёт пакеты
        /// в ConcurrentQueue и дренит их в Update.
        /// </summary>
        private void PostToMain(Action action)
        {
            var ctx = _mainContext;
            if (ctx != null && !ReferenceEquals(ctx, SynchronizationContext.Current))
            {
                ctx.Post(_ => action(), null);
            }
            else
            {
                action();
            }
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}

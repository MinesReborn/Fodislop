#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Kern.Core.Interfaces;
using Kern.Networking.Connection;
using Kern.Networking.Diagnostics;
using MinesServer.Networking.Client;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Compression;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;
using VContainer;

namespace Kern.Networking
{
    public class NetworkService : MonoBehaviour, INetworkService
    {
        [Inject]
        private IConnectionService _connectionService = null!;
        [Inject]
        private ILocalPlayerState _localPlayer = null!;
        private IConnectionService? _subscribedConnection;

        private readonly Dictionary<Type, List<Subscription>> _subscribers = new();
        private readonly Dictionary<Type, Subscription[]> _subscriberSnapshots = new();
        private readonly HashSet<Type> _reportedDispatchFailures = [];
        private readonly object _subscribersLock = new();
        private bool _connectionSubscribed;

        public bool IsConnectionSubscriptionEstablished => _connectionSubscribed;

        protected void Awake()
        {
        }

        protected void OnEnable()
        {
            EnsureConnectionSubscription();
        }

        protected void OnDisable()
        {
            UnsubscribeFromConnection();
        }

        protected void OnDestroy()
        {
            UnsubscribeFromConnection();
        }

        public void EnsureConnectionSubscription()
        {
            if (_subscribedConnection != null)
            {
                _subscribedConnection.OnPacketReceived -= OnPacketReceived;
                _subscribedConnection = null;
            }

            if (_connectionService == null)
            {
                _connectionSubscribed = false;
                return;
            }

            // Rebind even when the local flag says "subscribed". During an
            // editor/domain reload the ConnectionManager instance can be
            // replaced while this component survives; the old boolean then
            // describes a dead connection event source.
            _connectionService.OnPacketReceived -= OnPacketReceived;
            _connectionService.OnPacketReceived += OnPacketReceived;
            _subscribedConnection = _connectionService;
            _connectionSubscribed = true;
            lock (_subscribersLock)
            {
                _reportedDispatchFailures.Clear();
            }
        }

        private void UnsubscribeFromConnection()
        {
            if (_subscribedConnection == null)
            {
                _connectionSubscribed = false;
                return;
            }

            _subscribedConnection.OnPacketReceived -= OnPacketReceived;
            _subscribedConnection = null;
            _connectionSubscribed = false;
        }

        public bool IsConnected
        {
            get
            {
                return _connectionService != null && _connectionService.IsConnected;
            }
        }

        private ILocalPlayer? _cachedPlayerController;
        private bool _missingPlayerActionWarningLogged;

        public void SendAction(IActionClientPacket action)
        {
            if (!IsConnected)
            {
                return;
            }

            if (_cachedPlayerController == null)
            {
                _cachedPlayerController = _localPlayer.Current;
            }

            if (_cachedPlayerController == null)
            {
                if (!_missingPlayerActionWarningLogged)
                {
                    Debug.LogWarning("[NetworkService] Action dropped: local player is not ready.");
                    _missingPlayerActionWarningLogged = true;
                }

                return;
            }

            _missingPlayerActionWarningLogged = false;

            Vector2Int pos = _cachedPlayerController.Position;
            ushort serverX = (ushort)pos.x;
            ushort serverY = (ushort)pos.y;

            Send(new ActionClientPacket(serverX, serverY, action));
        }

        public void Send(IRootClientPacket packet)
        {
            var connectionService = _connectionService ??
                throw new InvalidOperationException(
                    "NetworkService requires IConnectionService before sending.");
            var timestamp = (uint)DateTimeOffset.UtcNow.Ticks;
            PacketTelemetry.RecordOutgoing(packet.GetType(), Time.unscaledTimeAsDouble);
            connectionService.Send(new ClientPacket(timestamp, packet));
        }

        void INetworkService.Send(IRootClientPacket packet) => Send(packet);

        public void Subscribe<T>(Action<T> handler)
        {
            var type = typeof(T);
            lock (_subscribersLock)
            {
                if (!_subscribers.TryGetValue(type, out var handlers))
                {
                    handlers = new List<Subscription>();
                    _subscribers[type] = handlers;
                }

                bool alreadySubscribed = false;
                for (int i = 0; i < handlers.Count; i++)
                {
                    if (handlers[i].OriginalHandler == (Delegate)handler)
                    {
                        alreadySubscribed = true;
                        break;
                    }
                }

                if (alreadySubscribed)
                {
                    return;
                }

                handlers.Add(new Subscription
                {
                    OriginalHandler = handler,
                    Wrapper = obj => handler((T)obj),
                });
                _subscriberSnapshots[type] = handlers.ToArray();
            }
        }

        public void Unsubscribe<T>(Action<T> handler)
        {
            var type = typeof(T);
            lock (_subscribersLock)
            {
                if (_subscribers.TryGetValue(type, out var handlers))
                {
                    handlers.RemoveAll(s => s.OriginalHandler == (Delegate)handler);
                    _subscriberSnapshots[type] = handlers.ToArray();
                }
            }
        }

        private void OnPacketReceived(ServerPacket packet)
        {
            var payload = packet.Payload;
            if (payload == null)
            {
                Debug.LogWarning("[NetworkService] Received ServerPacket with null Payload");
                return;
            }

            while (payload is LzmaPacket lzma)
            {
                payload = lzma.Payload;
                PacketTelemetry.RecordCompressed();
            }

            while (payload is LZ4Packet lz4)
            {
                payload = lz4.Payload;
                PacketTelemetry.RecordCompressed();
            }

            if (payload is HBPacket hbPacket && hbPacket.Payload != null)
            {
                PacketBatchStarted?.Invoke();
                // Размер пачки считается перебором, а не свойством длины:
                // тип полезной нагрузки задан протоколом, и обращаться к его
                // внутреннему устройству ради одного числа значит привязать
                // учёт к тому, что клиенту не принадлежит.
                int batched = 0;
                try
                {
                    foreach (var innerPacket in hbPacket.Payload)
                    {
                        batched++;
                        Dispatch(innerPacket);
                    }
                }
                finally
                {
                    PacketBatchCompleted?.Invoke();
                }

                PacketTelemetry.RecordBatch(batched);
            }
            else
            {
                Dispatch(payload);
            }
        }

        public event Action? PacketBatchStarted;
        public event Action? PacketBatchCompleted;

        private void Dispatch(object packet)
        {
            if (packet == null)
            {
                return;
            }

            var packetType = packet.GetType();

            Subscription[]? handlers;
            lock (_subscribersLock)
            {
                _subscriberSnapshots.TryGetValue(packetType, out handlers);
            }

            // Учёт ведётся за пределами замка. Внутри он не нужен — телеметрия
            // однопоточная и своих замков не берёт, — а вызов чужого кода
            // из-под замка это привычка, которая однажды приводит к тому, что
            // сеть встаёт из-за отладочного окна.
            //
            // Пакет без подписчика — не ошибка и не повод для лога: клиент
            // слушает не весь протокол. Но «сервер не прислал» и «прислал, а
            // никто не слушает» обязаны различаться при разборе, поэтому такой
            // пакет всё равно считается.
            PacketTelemetry.RecordIncoming(
                packetType,
                handled: handlers is { Length: > 0 },
                Time.unscaledTimeAsDouble);

            if (handlers == null)
            {
                return;
            }

            // Время всех обработчиков пакета вместе: без него пик разбора
            // очереди не привязать к типу пакета.
            long handlerStarted = PacketTelemetry.Enabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            try
            {
                using var allocationScope = Kern.Core.Interfaces.Diagnostics.AllocationLedger.Enabled
                    ? Kern.Core.Interfaces.Diagnostics.AllocationLedger.Measure(PacketAllocationEntry(packetType))
                    : default;
                InvokeHandlers(packet, packetType, handlers);
            }
            finally
            {
                if (handlerStarted != 0L)
                {
                    PacketTelemetry.RecordHandlerTime(
                        packetType,
                        (System.Diagnostics.Stopwatch.GetTimestamp() - handlerStarted) * 1000.0 /
                        System.Diagnostics.Stopwatch.Frequency);
                }
            }
        }

        // Одна запись учёта аллокаций на тип пакета; строка имени строится один
        // раз на тип, а не на пакет.
        private static readonly Dictionary<Type, Kern.Core.Interfaces.Diagnostics.AllocationLedger.Entry> _PacketAllocationEntries = new();

        private static Kern.Core.Interfaces.Diagnostics.AllocationLedger.Entry PacketAllocationEntry(Type packetType)
        {
            if (!_PacketAllocationEntries.TryGetValue(packetType, out var entry))
            {
                entry = Kern.Core.Interfaces.Diagnostics.AllocationLedger.Register("Пакет · " + packetType.Name);
                _PacketAllocationEntries[packetType] = entry;
            }

            return entry;
        }

        private void InvokeHandlers(object packet, Type packetType, Subscription[] handlers)
        {
            for (int i = handlers.Length - 1; i >= 0; i--)
            {
                try
                {
                    handlers[i].Wrapper(packet);
                }
                catch (Exception ex)
                {
                    bool firstFailure;
                    lock (_subscribersLock)
                    {
                        firstFailure = _reportedDispatchFailures.Add(packetType);
                    }

                    if (firstFailure)
                    {
                        Debug.LogException(new InvalidOperationException(
                            $"[NetworkService] Error dispatching packet {packetType.Name} to subscriber.",
                            ex));
                    }
                }
            }
        }


        private class Subscription
        {
            public Delegate OriginalHandler { get; set; } = null!;
            public Action<object> Wrapper { get; set; } = null!;
        }
    }
}

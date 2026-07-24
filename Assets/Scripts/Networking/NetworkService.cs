using System;
using System.Collections.Generic;
using System.Linq;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Networking.Connection;
using Fodinae.Scripts.Player;
using Fodinae.Scripts.Player.Logic;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.World;
using MinesServer.Networking.Client;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;

namespace Fodinae.Scripts.Networking
{
    #pragma warning disable CS0649
    public class NetworkService : MonoBehaviour, INetworkService
    {
        private static NetworkService _instance;
        public static NetworkService Instance => _instance;
        public static NetworkService InstanceIfExists => _instance;

        private readonly Dictionary<Type, List<Subscription>> _subscribers = new();

        protected void Awake()
        {
            _instance = this;
        }

        protected void OnEnable()
        {
            if (Instance != this)
            {
                return;
            }

            if (ConnectionManager.Instance != null)
            {
                ConnectionManager.Instance.OnPacketReceived -= OnPacketReceived;
                ConnectionManager.Instance.OnPacketReceived += OnPacketReceived;
            }
        }

        protected void OnDisable()
        {
            if (Instance != this)
            {
                return;
            }

            var cm = ConnectionManager.InstanceIfExists;
            if (cm != null)
            {
                cm.OnPacketReceived -= OnPacketReceived;
            }
        }

        public static bool IsConnected
        {
            get
            {
                var cm = ConnectionManager.InstanceIfExists;
                return cm != null && cm.Connection != null && cm.Connection.ConnectionStatus == MinesServer.Networking.Shared.ConnectionStatus.Connected;
            }
        }

        private PlayerMovementController _cachedPlayerController;

        public void SendAction(IActionClientPacket action)
        {
            if (!IsConnected)
            {
                return;
            }

            if (_cachedPlayerController == null)
            {
                _cachedPlayerController = PlayerMovementController.LocalPlayer;
            }

            if (_cachedPlayerController == null)
            {
                Debug.LogError("[NetworkService] Cannot send action: PlayerMovementController not found.");
                return;
            }

            Vector2Int pos = _cachedPlayerController.Position;
            ushort serverX = (ushort)pos.x;
            ushort serverY = (ushort)pos.y;

            Send(new ActionClientPacket(serverX, serverY, action));
        }

        void INetworkService.Send(IRootClientPacket packet) => Send(packet);

        public static void Send(IRootClientPacket packet)
        {
            if (ConnectionManager.Instance == null)
            {
                return;
            }

            var connection = ConnectionManager.Instance.Connection;
            if (connection == null || connection.ConnectionStatus != MinesServer.Networking.Shared.ConnectionStatus.Connected)
            {
                return;
            }

            var timestamp = (uint)DateTimeOffset.UtcNow.Ticks;
            connection.SendAsync(new ClientPacket(timestamp, packet));
        }

        public void Subscribe<T>(Action<T> handler)
        {
            var type = typeof(T);
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
        }

        public void Unsubscribe<T>(Action<T> handler)
        {
            var type = typeof(T);
            if (_subscribers.TryGetValue(type, out var handlers))
            {
                handlers.RemoveAll(s => s.OriginalHandler == (Delegate)handler);
            }
        }

        private void OnPacketReceived(ServerPacket packet)
        {
            var payload = packet.Payload;
            if (payload == null)
            {
                return;
            }

            if (payload is HBPacket hbPacket && hbPacket.Payload != null)
            {
                foreach (var innerPacket in hbPacket.Payload)
                {
                    Dispatch(innerPacket);
                }
            }

            Dispatch(payload);
        }

        private void Dispatch(object packet)
        {
            if (packet == null)
            {
                return;
            }

            var packetType = packet.GetType();

            if (_subscribers.TryGetValue(packetType, out var handlers))
            {
                for (int i = handlers.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        handlers[i].Wrapper(packet);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[NetworkService] Error dispatching packet {packetType.Name} to subscriber: {ex.Message}\n{ex.StackTrace}");
                    }
                }
            }
        }


        private class Subscription
        {
            public Delegate OriginalHandler { get; set; }
            public Action<object> Wrapper { get; set; }
        }
    }
}

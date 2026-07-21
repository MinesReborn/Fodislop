using System;
using System.Collections.Generic;
using System.Linq;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Networking.Connection;
using Fodinae.Scripts.Player;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.World;
using MinesServer.Networking.Client;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;

namespace Fodinae.Scripts.Networking
{
    public class NetworkService : SingletonMonoBehaviour<NetworkService>
    {
        private readonly Dictionary<Type, List<Subscription>> _subscribers = new();

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

            var cm = FindAnyObjectByType<ConnectionManager>();
            if (cm != null)
            {
                cm.OnPacketReceived -= OnPacketReceived;
            }
        }

        public void SendAction(IActionClientPacket action)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                Debug.LogError("[NetworkService] Cannot send action: Player object not found.");
                return;
            }

            if (!player.TryGetComponent<PlayerMovementController>(out var controller))
            {
                Debug.LogError("[NetworkService] Cannot send action: PlayerMovementController not found on player.");
                return;
            }

            Vector2Int pos = controller.Position;
            ushort serverX = (ushort)pos.x;
            ushort serverY = (ushort)pos.y;

            Send(new ActionClientPacket(serverX, serverY, action));
        }

        public static void Send(IRootClientPacket packet)
        {
            if (ConnectionManager.Instance == null)
            {
                Debug.LogError("[NetworkService] Cannot send packet: ConnectionManager.Instance is null!");
                return;
            }

            var connection = ConnectionManager.Instance.Connection;
            if (connection == null || connection.ConnectionStatus != MinesServer.Networking.Shared.ConnectionStatus.Connected)
            {
                Debug.LogWarning($"[NetworkService] Cannot send packet {packet.GetType().Name}: Not connected.");
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

            if (handlers.Any(s => s.OriginalHandler == (Delegate)handler))
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
                var handlersCopy = handlers.ToList();
                foreach (var sub in handlersCopy)
                {
                    try
                    {
                        sub.Wrapper(packet);
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

using System;
using System.Collections.Generic;
using System.Linq;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Networking.Connection;
using Fodinae.Scripts.Player;
using MinesServer.Networking.Client;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;

namespace Fodinae.Scripts.Networking
{
    /// <summary>
    /// High-level service for server communication and packet routing.
    /// </summary>
    public class NetworkService : MonoBehaviour
    {
        private static NetworkService _instance;
        private static bool _isQuitting = false;

        private readonly Dictionary<Type, List<Subscription>> _subscribers = new();

        public static NetworkService InstanceIfExists => _instance;

        public static NetworkService Instance
        {
            get
            {
                if (_isQuitting)
                {
                    return null;
                }

                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<NetworkService>();
                    if (_instance == null && !_isQuitting)
                    {
                        var go = new GameObject("[NetworkService]");
                        _instance = go.AddComponent<NetworkService>();

                        // System Grouping
                        if (Application.isPlaying)
                        {
                            var parent = GameObject.Find("[Systems]") ?? new GameObject("[Systems]");
                            UnityEngine.Object.DontDestroyOnLoad(parent);
                            go.transform.SetParent(parent.transform);
                        }
                    }
                }

                return _instance;
            }
        }

        protected virtual void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);

                // Ensure parented if created in scene
                var parent = GameObject.Find("[Systems]") ?? new GameObject("[Systems]");
                UnityEngine.Object.DontDestroyOnLoad(parent);
                transform.SetParent(parent.transform);
            }

            _isQuitting = false;
        }

        protected virtual void OnEnable()
        {
            if (_instance != this)
            {
                return; // Prevent duplicates from overriding the main singleton
            }

            if (ConnectionManager.Instance != null)
            {
                ConnectionManager.Instance.OnPacketReceived -= OnPacketReceived;
                ConnectionManager.Instance.OnPacketReceived += OnPacketReceived;
            }
        }

        protected virtual void OnDisable()
        {
            if (_instance != this)
            {
                return;
            }

            // Use FindFirstObjectByType instead of .Instance to avoid instantiating singletons during app teardown
            var cm = FindFirstObjectByType<ConnectionManager>();
            if (cm != null)
            {
                cm.OnPacketReceived -= OnPacketReceived;
            }
        }

        protected virtual void OnApplicationQuit()
        {
            _isQuitting = true;
        }

        /// <summary>
        /// Wraps an action packet in an ActionClientPacket with the player's current logical position and sends it.
        /// </summary>
        /// <param name="action">The action to send.</param>
        public void SendAction(IActionClientPacket action)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                Debug.LogError("[NetworkService] Cannot send action: Player object not found.");
                return;
            }

            var controller = player.GetComponent<PlayerMovementController>();
            if (controller == null)
            {
                Debug.LogError("[NetworkService] Cannot send action: PlayerMovementController not found on player.");
                return;
            }

            Vector2Int clientPos = controller.ClientPosition;
            ushort serverX = (ushort)clientPos.x;
            ushort serverY = (ushort)(MapManager.Instance.WorldHeight - 1 - clientPos.y);

            Send(new ActionClientPacket(serverX, serverY, action));
        }

        /// <summary>
        /// Wraps a root client packet in a ClientPacket with the current timestamp and sends it.
        /// </summary>
        /// <param name="packet">The root client packet to send.</param>
        public void Send(IRootClientPacket packet)
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

        /// <summary>
        /// Subscribes a handler to a specific packet type.
        /// Works for both IRootServerPacket and IHBPacket types.
        /// </summary>
        /// <typeparam name="T">The type of packet to subscribe to.</typeparam>
        /// <param name="handler">The action to execute when a packet of this type is received.</param>
        public void Subscribe<T>(Action<T> handler)
        {
            var type = typeof(T);
            if (!_subscribers.TryGetValue(type, out var handlers))
            {
                handlers = new List<Subscription>();
                _subscribers[type] = handlers;
            }

            // Check if already subscribed to prevent duplicates
            if (handlers.Any(s => s.OriginalHandler == (Delegate)handler))
            {
                return;
            }

            handlers.Add(new Subscription
            {
                OriginalHandler = handler,
                Wrapper = obj => handler((T)obj)
            });
        }

        /// <summary>
        /// Unsubscribes a handler from a specific packet type.
        /// </summary>
        /// <typeparam name="T">The type of packet to unsubscribe from.</typeparam>
        /// <param name="handler">The handler to remove.</param>
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

            // If it's an HBPacket, dispatch individual inner packets FIRST
            // This ensures that systems reacting to the HBPacket as a whole (like PacketHandler triggering OnWorldDataLoaded)
            // see the results of the individual inner packets (like MapRegionPackets) already processed.
            if (payload is HBPacket hbPacket && hbPacket.Payload != null)
            {
                foreach (var innerPacket in hbPacket.Payload)
                {
                    Dispatch(innerPacket);
                }
            }

            // Dispatch the root packet
            Dispatch(payload);
        }

        private void Dispatch(object packet)
        {
            if (packet == null)
            {
                return;
            }

            var packetType = packet.GetType();

            // We iterate through all keys to support interface/base class subscriptions
            foreach (var pair in _subscribers)
            {
                if (pair.Key.IsAssignableFrom(packetType))
                {
                    // Copy list to avoid issues if a subscriber unsubscribes during dispatch
                    var handlersCopy = pair.Value.ToList();
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
        }

        private class Subscription
        {
            public Delegate OriginalHandler { get; set; }

            public Action<object> Wrapper { get; set; }
        }
    }
}

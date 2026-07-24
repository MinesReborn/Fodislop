using System;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.World;
using MinesServer.Networking.Client;
using MinesServer.Networking.Client.Packets.Connection;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Connection;
using MinesServer.Networking.Connection.Client;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Shared;
using Fodinae.Scripts.Networking.Auth;
using UnityEngine;

namespace Fodinae.Scripts.Networking.Connection
{
    public class ConnectionManager : MonoBehaviour
    {
        private static ConnectionManager _instance;
        public static ConnectionManager Instance => _instance;
        public static ConnectionManager InstanceIfExists => _instance;

        public IServerConnection Connection { get; private set; }
        private bool _useOldClient;
        public event Action<ServerPacket> OnPacketReceived;

        private readonly System.Collections.Concurrent.ConcurrentQueue<ServerPacket> _packetQueue = new();

        protected void Awake()
        {
            Debug.Log("[ConnectionManager] Awake START");
            _instance = this;
            gameObject.AddComponent<PacketHandler>();
            Debug.Log("[ConnectionManager] Awake END");
        }

        protected void OnDestroy()
        {
            if (_instance != this)
            {
                return;
            }

            Disconnect();
        }

        protected void Update()
        {
            float startTime = Time.realtimeSinceStartup;
            while (_packetQueue.TryDequeue(out var packet))
            {
                try
                {
                    OnPacketReceived?.Invoke(packet);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ConnectionManager] Error processing packet: {ex.Message}\n{ex.StackTrace}");
                }

                // If processing takes more than 10ms, yield to next frame to prevent stutter
                if ((Time.realtimeSinceStartup - startTime) * 1000f > 10f)
                {
                    break;
                }
            }
        }

        public void Connect(bool oldClient = false)
        {
            if (Connection != null && Connection.ConnectionStatus != ConnectionStatus.Disconnected)
            {
                return;
            }

            _useOldClient = oldClient;
            Game.Managers.GameManager.InstanceIfExists?.SetState(Game.Managers.GameState.Connecting);

            Connection = new DummyConnection();
            Connection.OnReceived += OnReceived;
            Connection.OnConnected += OnConnected;
            Connection.OnDisconnected += OnDisconnected;
            Connection.Connect();
        }

        private void OnConnected()
        {
            int version = _useOldClient ? 0 : 1;
            string token = AuthTokenManager.LoadToken();
            Debug.Log($"[Auth] Sending ClientHello with token: {(string.IsNullOrEmpty(token) ? "EMPTY" : "PRESENT")}");
            NetworkService.Send(new ClientHelloPacket(version, "Windows", 10, "fingerprint", token));

            NetworkService.Send(new OpenHelpClickPacket());
        }

        private void OnDisconnected()
        {
            Game.Managers.GameManager.InstanceIfExists?.DeauthorizeUI();
        }

        private void OnReceived(ServerPacket obj)
        {
            if (obj != null)
            {
                _packetQueue.Enqueue(obj);
            }
        }

        public void Disconnect()
        {
            if (Connection == null)
            {
                return;
            }

            Connection.OnReceived -= OnReceived;
            Connection.OnConnected -= OnConnected;
            Connection.OnDisconnected -= OnDisconnected;
            Connection.Disconnect();
            Connection = null;
        }
    }
}

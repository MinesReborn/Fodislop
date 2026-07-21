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
using UnityEngine;

namespace Fodinae.Scripts.Networking.Connection
{
    public class ConnectionManager : SingletonMonoBehaviour<ConnectionManager>
    {
        public IServerConnection Connection { get; private set; }
        private bool _useOldClient;
        public event Action<ServerPacket> OnPacketReceived;

        private readonly System.Collections.Concurrent.ConcurrentQueue<ServerPacket> _packetQueue = new();

        protected override void OnAwake()
        {
            gameObject.AddComponent<PacketHandler>();
        }

        protected override void OnDestroyed()
        {
            Disconnect();
        }

        protected void Update()
        {
            int processedCount = 0;
            while (processedCount < 50 && _packetQueue.TryDequeue(out var packet))
            {
                try
                {
                    OnPacketReceived?.Invoke(packet);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ConnectionManager] Error processing packet: {ex.Message}\n{ex.StackTrace}");
                }

                processedCount++;
            }
        }

        public void Connect(bool oldClient = false)
        {
            if (Connection != null && Connection.ConnectionStatus != ConnectionStatus.Disconnected)
            {
                return;
            }

            _useOldClient = oldClient;
            Connection = new DummyConnection();
            Connection.OnReceived += OnReceived;
            Connection.OnConnected += OnConnected;
            Connection.Connect();
        }

        private void OnConnected()
        {
            int version = _useOldClient ? 0 : 1;
            NetworkService.Send(new ClientHelloPacket(version, "Windows", 10, "fingerprint", "token"));

            NetworkService.Send(new OpenHelpClickPacket());
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

            Connection.Disconnect();
            Connection = null;
        }
    }
}

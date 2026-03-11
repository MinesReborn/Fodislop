
using Cysharp.Threading.Tasks;
using MinesServer.Networking.Client;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Connection;
using MinesServer.Networking.Connection;
using MinesServer.Networking.Connection.Client;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Shared;
using System;
using System.Net;
using UnityEngine;

namespace Fodinae.Assets.Scripts.Networking.Connection
{
    public class ConnectionManager : MonoBehaviour
    {
        private static ConnectionManager _instance;
        public static ConnectionManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<ConnectionManager>();
                    if (_instance == null)
                    {
                        var go = new GameObject("[ConnectionManager]");
                        _instance = go.AddComponent<ConnectionManager>();
                    }
                }
                return _instance;
            }
        }

        public IServerConnection Connection { get; private set; }

        public event Action<ServerPacket> OnPacketReceived;

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            gameObject.AddComponent<PacketHandler>();
        }

        public void Connect()
        {
            if (Connection != null && Connection.ConnectionStatus != ConnectionStatus.Disconnected)
            {
                return;
            }

            Connection = new DummyConnection();
            Connection.OnReceived += OnReceived;
            Connection.OnConnected += OnConnected;
            Connection.Connect();
        }

        private void OnConnected()
        {
            // Send ClientHelloPacket
            SendAsync(new ClientHelloPacket(0, "Windows", 10, "fingerprint", "token"));
        }

        public void SendPacket(IRootClientPacket packet)
        {
            SendAsync(packet);
        }

        private void SendAsync(IRootClientPacket packet)
        {
            Connection.SendAsync(new ClientPacket((uint)DateTimeOffset.UtcNow.Ticks, packet));
        }

        private void OnReceived(ServerPacket obj)
        {
            OnPacketReceived?.Invoke(obj);
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

        void OnDestroy()
        {
            Disconnect();
        }
    }
}


using System;
using System.Net;
using Cysharp.Threading.Tasks;
using MinesServer.Networking.Client;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Connection;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Connection;
using MinesServer.Networking.Connection.Client;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Shared;
using Unity.VisualScripting.Antlr3.Runtime;
using UnityEngine;

namespace Fodinae.Scripts.Networking.Connection
{
    public class ConnectionManager : MonoBehaviour
    {
        private static ConnectionManager _instance;
        private static bool _isQuitting = false;

        public static ConnectionManager InstanceIfExists => _instance;

        public static ConnectionManager Instance
        {
            get
            {
                if (_isQuitting) return null;
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<ConnectionManager>();
                    if (_instance == null && !_isQuitting)
                    {
                        var go = new GameObject("[ConnectionManager]");
                        _instance = go.AddComponent<ConnectionManager>();

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

        public IServerConnection Connection { get; private set; }
        private bool _useOldClient;
        public event Action<ServerPacket> OnPacketReceived;

        private readonly System.Collections.Concurrent.ConcurrentQueue<ServerPacket> _packetQueue = new();

        void Awake()
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

            gameObject.AddComponent<PacketHandler>();
            _isQuitting = false;
        }

        void Update()
        {
            // Process up to 50 packets per frame to avoid freezing the main thread
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

        void OnApplicationQuit()
        {
            _isQuitting = true;
        }

        public void Connect(bool oldClient = false)
        {
            if (Connection != null && Connection.ConnectionStatus != ConnectionStatus.Disconnected)
                return;
            _useOldClient = oldClient;
            Connection = new DummyConnection();
            Connection.OnReceived += OnReceived;
            Connection.OnConnected += OnConnected;
            Connection.Connect();
        }

        private void OnConnected()
        {
            // Send ClientHelloPacket
            int version = _useOldClient ? 0 : 1;
            NetworkService.Instance.Send(new ClientHelloPacket(version, "Windows", 10, "fingerprint", "token"));

            NetworkService.Instance.Send(new OpenHelpClickPacket());
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

        void OnDestroy()
        {
            Disconnect();
        }
    }
}

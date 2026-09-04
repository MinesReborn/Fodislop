#nullable enable

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Localization;
using Fodinae.Networking.Auth;
using MinesServer.Networking.Client;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Connection;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Connection;
using MinesServer.Networking.Connection.Client;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Shared;
using Unity.Profiling;
using UnityEngine;
using VContainer;

namespace Fodinae.Networking.Connection
{
    public class ConnectionManager : MonoBehaviour, IConnectionService
    {
        private static readonly ProfilerMarker PacketDrainMarker =
            new("Fodinae.Net.DrainPacketQueue");

        // Бюджет на обработку входящих пакетов — доля времени КАДРА, а не стены часов.
        // Пропорция к deltaTime масштабирует пропускную способность с частотой кадров
        // (независимость от 30 vs 144 FPS) и ограничивает долю CPU, но при этом
        // всплеск пакетов (мировые текстуры при входе в мир) дренится за несколько
        // кадров, а не тянется секунду, как с накоплением 2% реального времени.
        private const float PacketDrainBudgetFractionOfFrame = 0.33f;
        private const float PacketDrainBudgetMaximumSeconds = 0.01f;

        public IServerConnection? Connection { get; private set; }
        public bool IsConnected => Connection != null && Connection.ConnectionStatus != ConnectionStatus.Disconnected;
        public bool IsOffline => Connection is IOfflineConnection;
        private bool _useOldClient;
        public event Action<ServerPacket>? OnPacketReceived;
        public event Action<string>? OnReconnectStatusChanged;
        public event Action<string>? OnDisconnectReason;
        public event Action? OnReconnectHidden;

        private readonly ConcurrentQueue<ServerPacket> _packetQueue = new();
        private readonly ReconnectBackoff _reconnectBackoff = new();

        [Inject]
        private IClientConfigManager _clientConfigManager = null!;
        [Inject]
        private ISceneNavigator _sceneNavigator = null!;
        [Inject]
        private ILocalizationService _loc = null!;
        [Inject]
        private IAsyncOperationSupervisor _operations = null!;
        [Inject]
        private IGameTokenStore _tokens = null!;

        [Inject]
        private DummyConnection _dummyConnection = null!;

        private bool _shouldAutoReconnect;
        private float _reconnectCountdown;
        private string _reconnectStatus = string.Empty;
        private bool _tearingDown;
        private bool _restartWorldOnConnect;

        // НУЖЕН: сохраняет причину серверного дисконнекта — используется при реконнекте
        // и для диагностики в ReconnectUI. НЕ УДАЛЯТЬ (см. HandleServerDisconnect).
        [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0052", Justification = "Хранит причину дисконнекта для реконнект-статуса")]
        private string _disconnectReason = string.Empty;

        protected void OnDestroy()
        {
            Disconnect();
        }

        protected void Update()
        {
            DrainPacketQueue();
            UpdateReconnect();
        }

        /// <summary>
        /// Разбирает очередь входящих пакетов в рамках бюджета на кадр — доля
        /// <see cref="PacketDrainBudgetFractionOfFrame"/> от времени кадра, но не более
        /// <see cref="PacketDrainBudgetMaximumSeconds"/>. Батч за кадр дополнительно
        /// ограничен <see cref="ProjectRuntimeContracts.RuntimeLimits.MaximumPacketBatchPerFrame"/>,
        /// чтобы единичный всплеск не вешал кадр.
        /// </summary>
        private void DrainPacketQueue()
        {
            using var marker = PacketDrainMarker.Auto();
            float budgetSeconds = Mathf.Min(
                Time.unscaledDeltaTime * PacketDrainBudgetFractionOfFrame,
                PacketDrainBudgetMaximumSeconds);
            long startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            int processedCount = 0;
            while (_packetQueue.TryDequeue(out ServerPacket packet))
            {
                processedCount++;
                try
                {
                    OnPacketReceived?.Invoke(packet);
                }
                catch (Exception ex)
                {
                    Debug.LogException(
                        new InvalidOperationException(
                            "A server packet could not be processed. Disconnecting to avoid continuing with corrupted state.",
                            ex));
                    TriggerDisconnect("Client packet processing failed.");
                    break;
                }

                if (processedCount >= ProjectRuntimeContracts.RuntimeLimits.MaximumPacketBatchPerFrame)
                {
                    break;
                }

                float elapsedMs = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                if (elapsedMs >= budgetSeconds * 1000f)
                {
                    break;
                }
            }
        }

        private void UpdateReconnect()
        {
            if (!_shouldAutoReconnect || Connection != null)
            {
                return;
            }

            _reconnectCountdown -= Time.deltaTime;
            int secsRemaining = Mathf.CeilToInt(_reconnectCountdown);
            string status = secsRemaining > 0
                ? _loc.Get("network.reconnect.retry", secsRemaining)
                : _loc.Get("network.connecting");
            if (status != _reconnectStatus)
            {
                _reconnectStatus = status;
                OnReconnectStatusChanged?.Invoke(status);
            }

            if (_reconnectCountdown <= 0f)
            {
                _reconnectCountdown = _reconnectBackoff.CurrentDelay;
                Connect();
            }
        }

        public void Connect(bool oldClient = false)
        {
            if (Connection != null && Connection.ConnectionStatus != ConnectionStatus.Disconnected)
            {
                return;
            }

            if (Connection != null)
            {
                Connection.OnReceived -= OnReceived;
                Connection.OnConnected -= OnConnected;
                Connection.OnDisconnected -= OnDisconnected;
                (Connection as IDisposable)?.Dispose();
                Connection = null;
            }

            _useOldClient = oldClient;
            Connection = CreateConnection();
            Connection.OnReceived += OnReceived;
            Connection.OnConnected += OnConnected;
            Connection.OnDisconnected += OnDisconnected;
            Connection.Connect();

            _reconnectStatus = _loc.Get("network.connecting");
            OnReconnectStatusChanged?.Invoke(_reconnectStatus);
        }

        /// <summary>
        /// Выбирает транспорт: реальный Darkar25 <see cref="TcpConnection"/> из
        /// конфига, либо офлайн-заглушку <see cref="DummyConnection"/> для
        /// локального теста без сервера.
        /// </summary>
        private IServerConnection CreateConnection()
        {
            // Config может быть ещё не загружен (ClientConfigManager грузит его в Start).
            ClientConfig? config = _clientConfigManager.Config;
            if (config == null)
            {
                Debug.LogWarning(
                    "[Connection] Client config is not initialized yet; using the Bootstrap-registered DummyConnection.");
                return _dummyConnection;
            }

            ConnectionSettings connection = config.Connection;
            if (ConnectionTransportConfig.SelectTransport(connection.UseDummyConnection) == ConnectionTransportKind.Dummy)
            {
                Debug.Log(
                    "[Connection] Transport: DummyConnection (offline stub). Set UseDummyConnection=false in client config for the real server.");
                return _dummyConnection;
            }

            if (!ConnectionTransportConfig.TryResolveEndpoint(
                    connection.ServerHost,
                    connection.ServerPort,
                    out IPAddress address,
                    out int port))
            {
                throw new InvalidOperationException(
                    $"[Connection] Invalid server endpoint '{connection.ServerHost}:{connection.ServerPort}' in client config. " +
                    "Expected a valid host/IP and a port in [1, 65535].");
            }

            Debug.Log($"[Connection] Transport: TcpConnection {address}:{port} (Darkar25 MinesServerNetworking).");
            return new TcpConnection(address, port);
        }

        public void Disconnect()
        {
            if (Connection == null)
            {
                return;
            }

            _tearingDown = true;
            try
            {
                Connection.OnReceived -= OnReceived;
                Connection.OnConnected -= OnConnected;
                Connection.OnDisconnected -= OnDisconnected;
                Connection.Disconnect();
                Connection = null;

                ClearPendingPackets();

            }
            finally
            {
                _tearingDown = false;
            }
        }

        public void TriggerDisconnect(string reason)
        {
            if (Connection is IOfflineConnection offline)
            {
                offline.TriggerDisconnect(reason);
                return;
            }

            Disconnect();
        }

        public void TriggerReconnect(string reason)
        {
            if (Connection is IOfflineConnection offline)
            {
                offline.TriggerReconnect(reason);
                return;
            }

            Disconnect();
        }

        public void Send(ClientPacket packet)
        {
            Connection?.SendAsync(packet);
        }

        public void HandleServerDisconnect(string reason)
        {
            _shouldAutoReconnect = false;
            _disconnectReason = reason;
            Disconnect();
            OnDisconnectReason?.Invoke(reason);
        }

        public void HandleServerReconnect()
        {
            _restartWorldOnConnect = true;
            _shouldAutoReconnect = true;
            _reconnectBackoff.Reset();
            _reconnectCountdown = _reconnectBackoff.CurrentDelay;
            _reconnectStatus = _loc.Get("network.reconnect.retry", Mathf.CeilToInt(_reconnectCountdown));
            Disconnect();
            OnReconnectStatusChanged?.Invoke(_reconnectStatus);
        }

        public void StartManualReconnect()
        {
            _restartWorldOnConnect = true;
            _shouldAutoReconnect = true;
            _reconnectBackoff.Reset();
            _reconnectCountdown = _reconnectBackoff.CurrentDelay;
            OnReconnectStatusChanged?.Invoke(_reconnectStatus);
        }

        private void OnConnected()
        {
            _operations.Run("complete_connection", CompleteConnectionAsync);
        }

        private async UniTask CompleteConnectionAsync(CancellationToken supervisorToken)
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                supervisorToken,
                destroyCancellationToken);
            CancellationToken cancellationToken = linkedCancellation.Token;

            if (_restartWorldOnConnect)
            {
                bool alreadyInTargetScene = string.Equals(
                    _sceneNavigator.CurrentSceneName,
                    ProjectRuntimeContracts.SceneNames.MainGame,
                    StringComparison.Ordinal);
                // Always clear the flag here so a missing reload (e.g. re-entry)
                // doesn't keep us pinned in restart mode for the next connect.
                _restartWorldOnConnect = false;
                if (!alreadyInTargetScene)
                {
                    await _sceneNavigator.TransitionAsync(
                        ProjectRuntimeContracts.SceneNames.MainGame,
                        cancellationToken);
                }
                else
                {
                    Debug.Log(
                        "[Connection] Restart-on-connect suppressed: already inside MainGame; skipping redundant transition.");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            _shouldAutoReconnect = false;
            _reconnectBackoff.Reset();
            _reconnectStatus = string.Empty;
            OnReconnectHidden?.Invoke();

            int version = _useOldClient ? 0 : 1;
            string token = _tokens.Load();
            Debug.Log($"[Auth] Sending ClientHello with token: {(string.IsNullOrEmpty(token) ? "EMPTY" : "PRESENT")}");
            Connection?.SendAsync(new ClientPacket(
                (uint)DateTimeOffset.UtcNow.Ticks,
                new ClientHelloPacket(version, "Windows", 10, "fingerprint", token)));
            Connection?.SendAsync(new ClientPacket(
                (uint)DateTimeOffset.UtcNow.Ticks,
                new OpenHelpClickPacket()));
        }

        private void OnDisconnected()
        {
            if (_tearingDown)
            {
                // Явный teardown (Disconnect/HandleServer*) уже выполнил очистку.
                return;
            }

            ClearPendingPackets();

            if (_shouldAutoReconnect)
            {
                // Сокетный транспорт может оборваться в любой момент. Забываем
                // мёртвое соединение, чтобы UpdateReconnect создал новое.
                Connection = null;
                _reconnectBackoff.RecordFailure();
                _reconnectCountdown = _reconnectBackoff.CurrentDelay;
                _reconnectStatus = _loc.Get("network.reconnect.retry", Mathf.CeilToInt(_reconnectCountdown));
                OnReconnectStatusChanged?.Invoke(_reconnectStatus);
            }
        }

        private void ClearPendingPackets()
        {
            int discardedCount = 0;
            while (_packetQueue.TryDequeue(out _))
            {
                discardedCount++;
            }

            if (discardedCount > 0)
            {
                Debug.LogWarning(
                    $"[ConnectionManager] Discarded {discardedCount} stale packet(s) after disconnect.");
            }
        }

        private void OnReceived(ServerPacket obj)
        {
            if (_tearingDown)
            {
                return;
            }

            if (obj != null)
            {
                _packetQueue.Enqueue(obj);
            }
        }

        /// <summary>
        /// Экспоненциальный backoff реконнекта с капом:
        /// 1s → 2s → 4s → 8s → 16s → 30s → 30s ...
        /// </summary>
        private sealed class ReconnectBackoff
        {
            private static readonly float[] Steps = [1f, 2f, 4f, 8f, 16f, 30f];

            private int _attempt;

            public int AttemptCount => _attempt;

            public float CurrentDelay => Steps[Math.Min(_attempt, Steps.Length - 1)];

            public void RecordFailure()
            {
                _attempt++;
            }

            public void Reset()
            {
                _attempt = 0;
            }
        }
    }
}

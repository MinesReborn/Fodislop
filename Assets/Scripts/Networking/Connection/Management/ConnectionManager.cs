#nullable enable

using Kern.Core.Interfaces.Diagnostics;
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Networking.Diagnostics;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using MinesServer.Networking.Client;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Connection;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Connection;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Contracts;
using Unity.Profiling;
using UnityEngine;
using VContainer;

using Kern.Diagnostics.Memory;
using Kern.Networking.Authentication;

namespace Kern.Networking.Connection
{
    public class ConnectionManager : MonoBehaviour, IConnectionService, IWorldRegionRequester
    {

        public IServerConnection? Connection { get; private set; }
        public bool IsConnected => Connection != null && Connection.ConnectionStatus == ConnectionStatus.Connected;
        public bool IsAutoReconnecting => _shouldAutoReconnect;
        public bool IsWorldStartupPending => _isWorldStartupPending;
        public bool IsDummy => Connection is IDummyConnection;

        public void RequestWorldRegion(string worldCodeName, RectInt serverRegion)
        {
            if (IsConnected && Connection is IWorldRegionRequester requester)
            {
                requester.RequestWorldRegion(worldCodeName, serverRegion);
            }
        }

        public event Action<ServerPacket>? OnPacketReceived;
        public event Action? OnPacketBatchStarted;
        public event Action? OnPacketBatchCompleted;
        public event Action? OnTransportDisconnected;
        public event Action<string>? OnReconnectStatusChanged;
        public event Action<string>? OnDisconnectReason;
        public event Action? OnReconnectHidden;

        private readonly InboundPacketBuffer _inboundPackets = new();
        private InboundPacketDrain _packetDrain = null!;
        // Сессионные пакеты, пришедшие под удержанием: буфер читающего потока
        // -> главный тред (Update), как и основной буфер, но с приоритетом.
        private readonly System.Collections.Concurrent.ConcurrentQueue<ServerPacket> _heldSessionPackets = new();
        private readonly ReconnectBackoff _reconnectBackoff = new();

        [Inject]
        private ISceneNavigator _sceneNavigator = null!;
        [Inject]
        private ILocalizationService _loc = null!;
        [Inject]
        private IAsyncOperationSupervisor _operations = null!;
        [Inject]
        private IGameTokenStore _tokens = null!;

        [Inject]
        private IDummyConnection _dummyConnection = null!;
        [Inject]
        private ConnectionTransportFactory _transportFactory = null!;

        private bool _shouldAutoReconnect;
        private bool _isConnectionAttemptPending;
        private bool _isWorldStartupPending;
        private bool _clientHelloSent;
        private float _reconnectCountdown;
        private string _reconnectStatus = string.Empty;
        private bool _restartWorldOnConnect;
        private bool _inboundHeld;

        // НУЖЕН: сохраняет причину серверного дисконнекта — используется при реконнекте
        // и для диагностики в ReconnectUI. НЕ УДАЛЯТЬ (см. HandleServerDisconnect).
        private string _disconnectReason = string.Empty;

        protected void Awake()
        {
            _inboundPackets.CaptureMainThread();
            _packetDrain = new InboundPacketDrain(_inboundPackets,
                () => OnPacketBatchStarted?.Invoke(), () => OnPacketBatchCompleted?.Invoke(),
                packet => OnPacketReceived?.Invoke(packet), HandleServerDisconnect);
        }

        protected void OnDestroy()
        {
            Disconnect();
        }

        protected void Update()
        {
            // Сессионные пакеты — всегда вперёд: и под удержанием (окно
            // авторизации живёт до релиза), и сразу после снятия (токен и
            // закрытие окна оседают до первого удержанного пакета мира).
            DispatchHeldSessionPackets();
            if (!_inboundHeld)
            {
                _packetDrain.Drain();
            }

            UpdateReconnect();
        }

        private void UpdateReconnect()
        {
            if (!_shouldAutoReconnect || Connection != null)
            {
                return;
            }

            _reconnectCountdown -= Time.unscaledDeltaTime;
            int secsRemaining = Mathf.CeilToInt(_reconnectCountdown);
            // Без сети отсчёт до повтора ничего не объясняет: игроку важно знать,
            // что дело в интернете, а не в сервере.
            string status = Application.internetReachability == NetworkReachability.NotReachable
                ? _loc.Get("network.no_internet")
                : secsRemaining > 0
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
                ConnectTransport();
            }
        }

        public void Connect()
        {
            // GameStartupPipeline already owns the scene transition and has
            // installed packet consumers. This attempt must not queue another scene.
            _restartWorldOnConnect = false;
            ConnectTransport();
        }

        private void ConnectTransport()
        {
            if (Connection != null && Connection.ConnectionStatus != ConnectionStatus.Disconnected)
            {
                if (Connection.ConnectionStatus == ConnectionStatus.Connected)
                {
                    SendClientHelloIfNeeded();
                }

                return;
            }

            if (Connection != null)
            {
                Connection.OnReceived -= OnReceived;
                Connection.OnConnected -= OnConnected;
                Connection.OnDisconnected -= OnDisconnected;

                // DummyConnection — синглтон Bootstrap и переиспользуется на
                // следующем подключении; Dispose закрыл бы его состояние мира
                // навсегда. Освобождается только одноразовый сокетный транспорт.
                if (!ReferenceEquals(Connection, _dummyConnection))
                {
                    (Connection as IDisposable)?.Dispose();
                }

                Connection = null;
            }

            // A retry is now an active transport attempt, not a pending retry.
            // A genuine disconnect below arms the retry state again.
            _shouldAutoReconnect = false;
            _isConnectionAttemptPending = true;
            _isWorldStartupPending = true;
            _clientHelloSent = false;
            Connection = _transportFactory.Create();
            Connection.OnReceived += OnReceived;
            Connection.OnConnected += OnConnected;
            Connection.OnDisconnected += OnDisconnected;
            Connection.Connect();

            _reconnectStatus = _loc.Get("network.connecting");
            OnReconnectStatusChanged?.Invoke(_reconnectStatus);
        }

        public long ReceivedBytes => Connection is ITransportTraffic traffic ? traffic.ReceivedBytes : 0;

        public void HoldInboundPackets()
        {
            _inboundHeld = true;
        }

        public void ReleaseInboundPackets()
        {
            _inboundHeld = false;
        }

        public void Disconnect()
        {
            _inboundHeld = false;
            _isConnectionAttemptPending = false;
            _isWorldStartupPending = false;
            _shouldAutoReconnect = false;
            _restartWorldOnConnect = false;
            _clientHelloSent = false;
            if (Connection == null)
            {
                return;
            }

            _inboundPackets.BeginTeardown();
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
                _inboundPackets.EndTeardown();
            }
        }

        public void TriggerDisconnect(string reason)
        {
            if (Connection is IDummyConnection dummy)
            {
                dummy.TriggerDisconnect(reason);
                return;
            }

            HandleServerDisconnect(reason);
        }

        public void TriggerReconnect(string reason)
        {
            if (Connection is IDummyConnection dummy)
            {
                dummy.TriggerReconnect(reason);
                return;
            }

            Disconnect();
        }

        public void Send(ClientPacket packet)
        {
            Connection?.SendAsync(packet);
        }

        public void MarkWorldReady()
        {
            if (!IsConnected)
            {
                throw new ConnectionStartupInterruptedException();
            }

            _isWorldStartupPending = false;
            _shouldAutoReconnect = false;
            _restartWorldOnConnect = false;
            _reconnectBackoff.Reset();
            _reconnectCountdown = 0f;
            _reconnectStatus = string.Empty;
            OnReconnectHidden?.Invoke();
        }

        public void HandleServerDisconnect(string reason)
        {
            _shouldAutoReconnect = false;
            _disconnectReason = reason;
            Disconnect();
            Debug.LogWarning($"[ConnectionManager] Server disconnected the client: {reason}");
            OnDisconnectReason?.Invoke(reason);
            OnTransportDisconnected?.Invoke();
        }

        public void HandleServerReconnect()
        {
            Disconnect();
            _restartWorldOnConnect = true;
            _shouldAutoReconnect = true;
            _reconnectBackoff.Reset();
            _reconnectCountdown = _reconnectBackoff.CurrentDelay;
            _reconnectStatus = _loc.Get("network.reconnect.retry", Mathf.CeilToInt(_reconnectCountdown));
            OnReconnectStatusChanged?.Invoke(_reconnectStatus);
            OnTransportDisconnected?.Invoke();
        }
        private void OnConnected()
        {
            _isConnectionAttemptPending = false;
            // Hello обязан быть первым пакетом на проводе: PreAuth-фильтр сервера
            // рвёт соединение за любой не-hello пакет до авторизации, а UI (чат)
            // начинает слать запросы сразу при загрузке сцены — раньше, чем
            // отложенная отправка из пайплайна.
            SendClientHelloIfNeeded();
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
                    SceneNames.MainGame,
                    StringComparison.Ordinal);
                // Always clear the flag here so a missing reload (e.g. re-entry)
                // doesn't keep us pinned in restart mode for the next connect.
                _restartWorldOnConnect = false;
                if (!alreadyInTargetScene)
                {
                    try
                    {
                        await _sceneNavigator.TransitionAsync(
                            SceneNames.MainGame,
                            cancellationToken);
                    }
                    catch (ConnectionStartupInterruptedException) when (_shouldAutoReconnect)
                    {
                        return;
                    }
                    catch (TimeoutException) when (_shouldAutoReconnect)
                    {
                        return;
                    }
                }
                else
                {
                    Debug.Log(
                        "[Connection] Restart-on-connect suppressed: already inside MainGame; skipping redundant transition.");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            SendClientHelloIfNeeded();

            // On a reconnect, startup calls Connect() after packet consumers
            // are initialized. That call sends ClientHello while this
            // transition is waiting for WorldInit; the guard makes this
            // completion path safe to call it again after readiness.
        }

        private void SendClientHelloIfNeeded()
        {
            if (_clientHelloSent || Connection == null || !IsConnected)
            {
                return;
            }

            _clientHelloSent = true;
            int version = ClientProtocolVersion.ClientVersion;
            string token = _tokens.Load();
            Debug.Log($"[Auth] Sending ClientHello with token: {(string.IsNullOrEmpty(token) ? "EMPTY" : "PRESENT")}");

            // ClientHello несёт только метаданные наблюдаемости
            // (protocol-trust-audit.md §1.4); сервер ветвится на них не вправе,
            // доверие — исключительно AuthToken. Осознанные хардкоды:
            // - ClientVersion — константа клиента, синхронная с серверным
            //   гейтом актуальности (иначе OutdatedClientPacket (0));
            // - OS — зашитые строки «Windows»/«macOS» (GetClientOperatingSystem);
            // - OSVersion — только Major (Windows 10/11 неразличимы);
            // - HardwareFingerprint — SystemInfo.deviceUniqueIdentifier.
            Connection?.SendAsync(new ClientPacket(
                (uint)DateTimeOffset.UtcNow.Ticks,
                new ClientHelloPacket(
                    version,
                    GetClientOperatingSystem(),
                    Environment.OSVersion.Version.Major,
                    SystemInfo.deviceUniqueIdentifier,
                    token)));
        }

        private static string GetClientOperatingSystem() =>
            Application.platform switch
            {
                RuntimePlatform.OSXPlayer or RuntimePlatform.OSXEditor => "macOS",
                RuntimePlatform.WindowsPlayer or RuntimePlatform.WindowsEditor => "Windows",
                _ => Application.platform.ToString(),
            };

        private void OnDisconnected()
        {
            if (_inboundPackets.IsTearingDown)
            {
                // Явный teardown (Disconnect/HandleServer*) уже выполнил очистку.
                return;
            }

            ClearPendingPackets();

            // Detach before forgetting the transport. DummyConnection is reused
            // on the next attempt; retaining these handlers delivers every
            // packet and lifecycle event multiple times after reconnecting.
            if (Connection != null)
            {
                Connection.OnReceived -= OnReceived;
                Connection.OnConnected -= OnConnected;
                Connection.OnDisconnected -= OnDisconnected;
                Connection = null;
            }

            if (_isConnectionAttemptPending || _isWorldStartupPending)
            {
                _isConnectionAttemptPending = false;
                _shouldAutoReconnect = true;
                _restartWorldOnConnect = true;
            }

            if (_shouldAutoReconnect)
            {
                // Сокетный транспорт может оборваться в любой момент. Забываем
                // мёртвое соединение, чтобы UpdateReconnect создал новое.
                Connection = null;
                _restartWorldOnConnect = true;
                _reconnectBackoff.RecordFailure();
                _reconnectCountdown = _reconnectBackoff.CurrentDelay;
                _reconnectStatus = _loc.Get("network.reconnect.retry", Mathf.CeilToInt(_reconnectCountdown));
                OnReconnectStatusChanged?.Invoke(_reconnectStatus);
                OnTransportDisconnected?.Invoke();
                return;
            }

            Connection = null;
            _disconnectReason = _loc.Get("network.error.connection_lost");
            OnDisconnectReason?.Invoke(_disconnectReason);
            OnTransportDisconnected?.Invoke();
        }

        private void ClearPendingPackets()
        {
            int discardedCount = _inboundPackets.Clear();
            while (_heldSessionPackets.TryDequeue(out _))
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
            if (_inboundPackets.IsTearingDown || obj.Payload is null)
            {
                return;
            }

            // Пока удержание активно (спуск и старт игровой сцены), мир и
            // геймплей копятся в очереди до релиза, но пакеты сессии обязаны
            // диспетчиться сразу: окно авторизации, присланное до игровой
            // сцены, обязано показаться, токен — сохраниться, разрыв — быть
            // замеченным. Сессионная очередь дренируется в Update на главном
            // треде — обработчикам Unity API нужен именно он.
            if (_inboundHeld && !IsSessionPacket(obj.Payload))
            {
                _inboundPackets.Enqueue(obj);
                return;
            }
            if (_inboundHeld)
            {
                _heldSessionPackets.Enqueue(obj);
                return;
            }

            _inboundPackets.Enqueue(obj);
        }

        /// <summary>
        /// Диспетчеризация сессионных пакетов, пришедших под удержанием.
        /// Вызывается из Update (главный тред) и раньше обычного дрена при
        /// снятии удержания — состояние сессии (окно, токен) оседает раньше,
        /// чем в мир начинают стрелять удержанные пакеты.
        /// </summary>
        private void DispatchHeldSessionPackets()
        {
            while (_heldSessionPackets.TryDequeue(out var packet))
            {
                try
                {
                    OnPacketReceived?.Invoke(packet);
                }
                catch (Exception ex)
                {
                    Debug.LogException(
                        new InvalidOperationException(
                            "A session packet could not be processed. Disconnecting to avoid continuing with corrupted state.",
                            ex));
                    HandleServerDisconnect("Client session packet processing failed.");
                    return;
                }
            }
        }

        // Пакеты сессии: они имеют смысл до входа в мир и после него, их
        // потребители (окна, токен, пинг, разрывы, картинки окна) живут в
        // каждой фазе.
        private static bool IsSessionPacket(MinesServer.Networking.Server.IRootServerPacket? payload) => payload is
            MinesServer.Networking.Server.Packets.GUI.OpenWindowPacket
            or MinesServer.Networking.Server.Packets.GUI.CloseWindowPacket
            or MinesServer.Networking.Server.Packets.GUI.ModalWindowPacket
            or MinesServer.Networking.Server.Packets.Utilities.OpenURLPacket
            or MinesServer.Networking.Server.Packets.Utilities.RuntimeAssetPacket
            or MinesServer.Networking.Server.Packets.Connection.AuthTokenPacket
            or MinesServer.Networking.Server.Packets.Connection.PingPacket
            or MinesServer.Networking.Server.Packets.Connection.DisconnectPacket
            or MinesServer.Networking.Server.Packets.Connection.ReconnectPacket
            or MinesServer.Networking.Server.Packets.Connection.OutdatedClientPacket;

    }
}

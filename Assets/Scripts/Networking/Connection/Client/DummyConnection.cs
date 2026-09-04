#nullable enable

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fodinae;
using Fodinae.Audio;
using Fodinae.Core.Interfaces;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.Chat;
using MinesServer.Networking.Client.Packets.Connection;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Client.Packets.Movement;
using MinesServer.Networking.Client.Packets.Utilities;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using MinesServer.Networking.Server.Packets.GUI.Components.Input;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Information.StatusPanel;
using MinesServer.Networking.Server.Packets.Inventory;
using MinesServer.Networking.Server.Packets.Mission;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.Utilities;
using MinesServer.Networking.Server.Packets.World;
using MinesServer.Networking.Shared;
using MinesServer.Networking.Shared.Packets;
using UnityEngine;

namespace MinesServer.Networking.Connection.Client
{
    public class DummyConnection : IServerConnection, IOfflineConnection
    {
        private readonly ITextureStorageService _textureStorage;
        private readonly IAsyncOperationSupervisor _operations;
        private readonly IRuntimeDebugSettings _debugSettings;
        private readonly DummyConnectionSession _session = new();
        private readonly DummyScenarioController _scenario;

        public DummyConnection(
            ITextureStorageService textureStorage,
            IItemCatalog itemCatalog,
            IAsyncOperationSupervisor operations,
            IRuntimeDebugSettings debugSettings,
            IOfflineScenarioSettings scenarioSettings)
        {
            _textureStorage = textureStorage;
            _operations = operations;
            _debugSettings = debugSettings;
            _scenario = new DummyScenarioController(scenarioSettings);
            _worldState = new DummyWorldSimulationState(operations);
            _authSession = new DummyAuthSession();
            _missionRunner = new DummyMissionRunner(SendPacket);
            _buffManager = new DummyBuffManager(
                SendPacket,
                operations,
                LoopAlive);
            _inventoryResponder = new DummyInventoryResponder(
                SendPacket,
                _buffManager.ActivateBuff,
                _teleportPositions,
                _playerState.SetHealth,
                _worldState.GetCell,
                _worldState.SetCell);
            _teleportManager = new DummyTeleportManager(SendPacket, _teleportPositions);
            // LoopAlive привязан к жизненному циклу соединения: чат-петля
            // умирает вместе с коннектом (раньше она жила вечно и текла
            // через реконнекты — фиксированный источник мусора).
            _chatSimulator = new DummyChatSimulator(
                SendPacket,
                () => LoopAlive(_session.LifecycleVersion),
                operations);
            _chatResponder = new DummyChatResponder(SendPacket);
            _clanManager = new DummyClanManager(SendPacket);
            _pathFinder = new DummyPathFinder(SendPacket, _worldState.GetCellConfig);
            _movementResponder = new DummyMovementResponder(
                operations,
                _playerState,
                _worldState,
                _teleportManager,
                _pathFinder,
                SendPacket,
                () => _debugSettings.IgnoreCollision,
                _mockBotId);
            _actionResponder = new DummyGameplayActionResponder(
                _playerState,
                _worldState,
                _movementResponder,
                _missionRunner,
                _inventoryResponder,
                _chatSimulator,
                SendPacket,
                _mockBotId);
            _windowResponder = new DummyWindowResponder(
                SendPacket,
                _buffManager,
                _inventoryResponder,
                _teleportManager,
                _clanManager,
                _missionRunner);
            _worldStartup = new DummyWorldStartupResponder(
                operations,
                itemCatalog,
                _worldState,
                _playerState,
                _buffManager,
                _chatSimulator,
                _inventoryResponder,
                _teleportPositions,
                SendPacket,
                LoopAlive);
        }

        /// <summary>
        /// Stable local identity used by the emulated game server.
        /// </summary>
        private string PlayerName => _authSession.PlayerName;

        /// <summary>Офлайн-статистика (уровень/валюта) для мира.</summary>
        private long Level => 12345;

        private long Currency => 123456;

        public ConnectionStatus ConnectionStatus => _session.Status;

        public event Action<ServerPacket>? OnReceived;
        public event Action? OnConnected;
        public event Action? OnDisconnected;
        public event Action? OnDisconnecting;
        public event Action? OnConnecting;

        private readonly DummyAuthSession _authSession;
        private readonly DummyMissionRunner _missionRunner;
        private readonly DummyBuffManager _buffManager;
        private readonly DummyInventoryResponder _inventoryResponder;
        private readonly DummyTeleportManager _teleportManager;
        private readonly DummyChatSimulator _chatSimulator;
        private readonly DummyChatResponder _chatResponder;
        private readonly DummyClanManager _clanManager;
        private readonly DummyPathFinder _pathFinder;
        private readonly DummyMovementResponder _movementResponder;
        private readonly DummyGameplayActionResponder _actionResponder;
        private readonly DummyWindowResponder _windowResponder;
        private readonly DummyPlayerSimulationState _playerState = new();
        private readonly DummyWorldSimulationState _worldState;
        private readonly DummyWorldStartupResponder _worldStartup;

        private const ushort _mockBotId = 456;
        private readonly List<(ushort X, ushort Y)> _teleportPositions = new();

        // Depth warning/damage feature disabled in DummyConnection
        // private const int _maxDepth = 200;
        // private bool _depthWarningActive;

        // Сериализует InitWorldAsync: повторный ClientHello (ретрансмит,
        // двойной вход) больше не запускает второй конкурентный init, который
        // рвал мир и гонял распаковку карты одновременно с чтением.

        public bool UsePrebakedMap = true;
        public string PrebakedWorldCodeName = "pallada";

        public void Connect()
        {
            if (!_session.TryBeginConnect(out int lifecycleVersion))
            {
                return;
            }

            _scenario.BeginLifecycle(lifecycleVersion);
            OnConnecting?.Invoke();

            // Run asynchronously, but stay on the Unity Main Thread
            _operations.Run(
                "dummy_connect",
                _ => ConnectAsync(lifecycleVersion));
        }

        private async UniTask ConnectAsync(int lifecycleVersion)
        {
            await UniTask.Yield();

            if (_scenario.StallsConnection)
            {
                return;
            }

            if (_scenario.DisconnectsDuringHandshake)
            {
                _session.Stop();
                OnDisconnecting?.Invoke();
                OnDisconnected?.Invoke();
                return;
            }

            if (!_session.TryCompleteConnect(lifecycleVersion))
            {
                return;
            }

            OnConnected?.Invoke();
        }

        public void Disconnect()
        {
            if (!_session.TryBeginDisconnect(out int lifecycleVersion))
            {
                _worldState.Reset();
                _movementResponder.CancelPath();
                return;
            }

            _worldState.Reset();
            _movementResponder.CancelPath();

            // Cleared so the buff loop can start again on the next connection.
            // It was never reset, so after one disconnect StartBuffLoop's guard
            // stayed latched and the loop never came back - the mirror image of
            // the other four loops, which had no guard at all and duplicated.
            _buffManager.ResetLoopGuard();

            OnDisconnecting?.Invoke();
            _operations.Run(
                "dummy_disconnect",
                _ => DisconnectAsync(lifecycleVersion));
        }

        private async UniTask DisconnectAsync(int lifecycleVersion)
        {
            await UniTask.Delay(100);

            if (!_session.TryCompleteDisconnect(lifecycleVersion))
            {
                return;
            }

            OnDisconnected?.Invoke();
        }

        /// <summary>
        /// Whether a background mock loop started at
        /// <paramref name="lifecycleVersion"/> should still be running.
        /// </summary>
        /// <remarks>
        /// The loops used to test <c>_status == Connected</c> and nothing else,
        /// which made them immortal. Dispose did not touch _status, so every
        /// loop on a disposed instance kept running forever - and since a new
        /// DummyConnection is built for each connection, a menu-game-menu-game
        /// cycle left a full set of them behind each time. RunCircularBots
        /// alone allocates a List, an array and six position packets every
        /// 100ms, so each leaked set is a permanent fixed-rate garbage source
        /// that nothing can ever stop.
        ///
        /// Comparing the captured lifecycle version as well ties every loop to
        /// the connection that started it: one bump retires all of them at
        /// once, whether the trigger was a disconnect, a reconnect or a
        /// dispose.
        /// </remarks>
        private bool LoopAlive(int lifecycleVersion)
        {
            return _session.IsAlive(lifecycleVersion);
        }

        public void Dispose()
        {
            // Retires every background loop belonging to this instance. Without
            // this the loops outlive the object that owns them.
            _session.Stop();
            _buffManager.ResetLoopGuard();

            _worldState.Dispose();
            _movementResponder.Dispose();
        }

        public void TriggerDisconnect(string reason)
        {
            OnReceived?.Invoke(new ServerPacket(new MinesServer.Networking.Server.Packets.Connection.DisconnectPacket(reason)));
        }

        public void TriggerReconnect(string reason)
        {
            OnReceived?.Invoke(new ServerPacket(new MinesServer.Networking.Server.Packets.Connection.ReconnectPacket(reason)));
        }

        private void SendPacket(ServerPacket packet)
        {
            OnReceived?.Invoke(packet);
        }

        public void SendAsync(ClientPacket packet)
        {
            if (packet.Data is ActionClientPacket actionPacket)
            {
                _actionResponder.Handle(actionPacket);
                return;
            }

            switch (packet.Data)
            {
                case ClientHelloPacket clientHello:
                    if (!_scenario.TryBeginHello(_session.LifecycleVersion))
                    {
                        return;
                    }

                    if (_scenario.RejectsAuthentication)
                    {
                        OnReceived?.Invoke(DummyWindowBuilder.BuildAuthWindow());
                        return;
                    }

                    string receivedToken = clientHello.AuthToken;
                    // Офлайн-сервер пермиссивный и окна авторизации не вызывает:
                    // Знакомые токены принимаются как есть, для пустого или
                    // незнакомого dummy-сервер сам выдаёт новый
                    // и запоминает его, чтобы авто-вход работал и дальше.
                    string resolvedToken = _authSession.ResolveToken(receivedToken);

                    if (clientHello.ClientVersion < 1)
                    {
                        // Причина передаётся ключом словаря: StatusProcessor и
                        // ReconnectUI резолвят его через HasKey, если он попадёт
                        // на экран.
                        OnReceived?.Invoke(new ServerPacket(new OutdatedClientPacket(
                            2, "Mines 3", "network.error.old_client",
                            "https://minesgame.ru/download", string.Empty)));
                        return;
                    }

                    OnReceived?.Invoke(new ServerPacket(new AuthTokenPacket(resolvedToken)));

                    if (_scenario.StallsWorldInitialization)
                    {
                        return;
                    }

                    _operations.Run("dummy_world_init", _ => InitWorldAsync());
                    break;
                case RuntimeAssetRequestPacket runtimeAssets:
                    _operations.Run(
                        "dummy_asset_request",
                        _ => DummyAssetResponder.HandleRequestAsync(
                            runtimeAssets,
                            _textureStorage,
                            SendPacket));
                    break;
                case OpenHelpClickPacket:
                    break;
                case OpenSettingsClickPacket:
                    break;
                case ChangeChatColorPacket colorChange:
                    _chatResponder.ChangeColor(colorChange);
                    break;
                case OpenClanClickPacket:
                    _clanManager.HandleOpenClanClick();
                    break;
                case QueryChatHistoryPacket qh:
                    _chatResponder.SendHistory(qh);
                    break;
                case SendLocalChatMessagePacket localMsg:
                    _chatResponder.SendLocal(
                        localMsg,
                        _mockBotId,
                        _playerState.X,
                        _playerState.Y);
                    break;

                case SendChatMessagePacket globalMsg:
                    _chatResponder.SendGlobal(globalMsg);
                    break;
                case MinesServer.Networking.Client.Packets.Inventory.SelectItemPacket selectItem:
                    _inventoryResponder.Select(selectItem.Item);
                    break;
                case MinesServer.Networking.Client.Packets.Inventory.DeselectItemPacket:
                    _inventoryResponder.Deselect();
                    break;
                case MinesServer.Networking.Client.Packets.Inventory.UseItemPacket:
                    _inventoryResponder.Use(
                        _playerState.X,
                        _playerState.Y,
                        _playerState.Direction);
                    break;
                case ElementClickPacket elementClick:
                    _windowResponder.Handle(
                        elementClick,
                        _playerState.X,
                        _playerState.Y);
                    break;
                default:
                    break;
            }
        }
        private UniTask InitWorldAsync()
        {
            return _worldState.EnsureInitializedAsync(InitWorldCoreAsync);
        }

        private UniTask InitWorldCoreAsync()
        {
            return _worldStartup.InitializeAsync(
                PrebakedWorldCodeName,
                _session.LifecycleVersion,
                PlayerName,
                Level,
                Currency,
                _mockBotId);
        }

    }
}

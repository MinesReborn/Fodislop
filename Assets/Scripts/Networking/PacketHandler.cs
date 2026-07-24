using System.Collections.Generic;
using System.Linq;
using Fodinae.Scripts.Networking.Processors;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.Game;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Player;
using Fodinae.Scripts.UI;
using Fodinae.Scripts.UI.Programmator;
using Fodinae.UI;
using Fodinae.UI.Binding;
using VContainer;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.Connection;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Server;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Information.StatusPanel;
using MinesServer.Networking.Server.Packets.Inventory;
using MinesServer.Networking.Server.Packets.Mission;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.Networking
{
    #pragma warning disable CS0649
    public partial class PacketHandler : MonoBehaviour
    {
        public static PacketHandler Instance { get; private set; }

        public static bool IsInputBlocked => Instance != null && (Instance._windowProcessor.HasOpenWindows || Instance._windowProcessor.IsModalShowing || PauseMenu.IsMenuOpen || ProgrammatorGrid.IsOpen);
        public static string TopWindowTag => Instance?._windowProcessor.TopWindowTag;

        private static readonly WorldInitProcessor WorldInit = new();
        private static readonly RobotInfoProcessor RobotInfo = new();
        private static readonly MapRegionProcessor MapRegion = new();
        private static readonly AudioPacketProcessor Audio = new();
        private static readonly PlayerInfoProcessor PlayerInfo = new();
        private static readonly PlayerStatsProcessor PlayerStats = new();
        private static readonly PlayerStateProcessor PlayerState = new();
        private static readonly RobotPositionProcessor RobotPosition = new();
        private static readonly ChatProcessor Chat = new();
        private static readonly StatusProcessor Status = new();
        private static readonly InventoryProcessor Inventory = new();
        private static readonly ClanProcessor Clan = new();
        private static readonly MissionProcessor Mission = new();
        private static readonly PackProcessor Pack = new();
        private readonly WindowPacketProcessor _windowProcessor = new();
        private bool _isInitialized;

        private INetworkService _networkService;

        protected virtual void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            Debug.Log("[PacketHandler] Starting initialization...");

            // Verify Dependencies
            if (MapManager.Instance is null)
            {
                Debug.LogError("[PacketHandler] MapManager not found - cannot process world initialization");
                return;
            }

            if (MapStorage.Instance == null)
            {
                Debug.LogError("[PacketHandler] MapStorage not found - cannot process map data");
                return;
            }

            var uiDocument = FindAnyObjectByType<UIDocument>();
            if (uiDocument != null)
            {
                var mwh = new ModalWindowHandler(uiDocument);
                _windowProcessor.Initialize(uiDocument, mwh);
            }

            // Subscribe to events via NetworkService
            TrySubscribeToNetworkService();

            var mm = MapManager.Instance;
            if (mm != null)
            {
                mm.OnWorldInitialized += OnWorldInitialized;
            }

            Debug.Log("[PacketHandler] Initialization complete - ready to receive packets");
            _isInitialized = true;
        }

        protected void Start()
        {
            TrySubscribeToNetworkService();
        }

        private void TrySubscribeToNetworkService()
        {
            if (_networkService != null)
            {
                return;
            }

            _networkService = NetworkService.Instance;
            if (_networkService == null)
            {
                return;
            }

            _networkService.Subscribe<WorldInitPacket>(WorldInit.Process);
            _networkService.Subscribe<RobotInfoPacket>(RobotInfo.Process);
            _networkService.Subscribe<PlayerInfoPacket>(PlayerInfo.Process);
            _networkService.Subscribe<MovementSpeedPacket>(PlayerInfo.Process);
            _networkService.Subscribe<OpenWindowPacket>(_windowProcessor.Process);
            _networkService.Subscribe<CloseWindowPacket>(_windowProcessor.Process);
            _networkService.Subscribe<RobotPositionPacket>(RobotPosition.Process);
            _networkService.Subscribe<MapRegionPacket>(MapRegion.Process);
            _networkService.Subscribe<PackPacket>(Pack.Process);
            _networkService.Subscribe<RemovePackPacket>(Pack.Process);

            _networkService.Subscribe<LevelPacket>(PlayerStats.Process);
            _networkService.Subscribe<HealthPacket>(PlayerStats.Process);
            _networkService.Subscribe<CurrencyPacket>(PlayerStats.Process);
            _networkService.Subscribe<GeologyPacket>(PlayerStats.Process);
            _networkService.Subscribe<BasketPacket>(PlayerStats.Process);
            _networkService.Subscribe<MaxDepthPacket>(PlayerStats.Process);

            _networkService.Subscribe<AutoMineStatePacket>(PlayerState.Process);
            _networkService.Subscribe<AggressionStatePacket>(PlayerState.Process);
            _networkService.Subscribe<SkillProgressPacket>(PlayerStats.Process);
            _networkService.Subscribe<DailyBonusStatePacket>(PlayerStats.Process);
            _networkService.Subscribe<TeleportPacket>(PlayerInfo.Process);
            _networkService.Subscribe<ChatMessageListPacket>(Chat.Process);
            _networkService.Subscribe<LocalChatMessagePacket>(Chat.Process);
            _networkService.Subscribe<ChatMutePacket>(Chat.Process);

            _networkService.Subscribe<OnlinePacket>(Status.Process);
            _networkService.Subscribe<PingPacket>(Status.Process);
            _networkService.Subscribe<OutdatedClientPacket>(Status.Process);
            _networkService.Subscribe<AudioPacket>(Audio.Process);
            _networkService.Subscribe<InventoryPacket>(Inventory.Process);
            _networkService.Subscribe<MinesServer.Networking.Server.Packets.Inventory.SelectItemPacket>(Inventory.Process);
            _networkService.Subscribe<MinesServer.Networking.Server.Packets.Inventory.DeselectItemPacket>(Inventory.Process);
            _networkService.Subscribe<AddStatusLinePacket>(Status.Process);
            _networkService.Subscribe<ClearStatusLinePacket>(Status.Process);
            _networkService.Subscribe<ClearStatusPacket>(Status.Process);
            _networkService.Subscribe<ModalWindowPacket>(_windowProcessor.HandleModalWindow);
            _networkService.Subscribe<ShowClanPacket>(Clan.Process);
            _networkService.Subscribe<HideClanPacket>(Clan.Process);
            _networkService.Subscribe<MissionInitPacket>(Mission.Process);
            _networkService.Subscribe<MissionProgressPacket>(Mission.Process);
            _networkService.Subscribe<AuthTokenPacket>(HandleAuthTokenPacket);
        }

        protected virtual void OnDestroy()
        {
            if (!_isInitialized)
            {
                return;
            }

            if (_networkService != null)
            {
                _networkService.Unsubscribe<WorldInitPacket>(WorldInit.Process);
                _networkService.Unsubscribe<RobotInfoPacket>(RobotInfo.Process);
                _networkService.Unsubscribe<PlayerInfoPacket>(PlayerInfo.Process);
                _networkService.Unsubscribe<MovementSpeedPacket>(PlayerInfo.Process);
                _networkService.Unsubscribe<OpenWindowPacket>(_windowProcessor.Process);
                _networkService.Unsubscribe<CloseWindowPacket>(_windowProcessor.Process);
                _networkService.Unsubscribe<RobotPositionPacket>(RobotPosition.Process);
                _networkService.Unsubscribe<MapRegionPacket>(MapRegion.Process);
                _networkService.Unsubscribe<PackPacket>(Pack.Process);
                _networkService.Unsubscribe<RemovePackPacket>(Pack.Process);
                _networkService.Unsubscribe<SkillProgressPacket>(PlayerStats.Process);
                _networkService.Unsubscribe<AutoMineStatePacket>(PlayerState.Process);
                _networkService.Unsubscribe<AggressionStatePacket>(PlayerState.Process);
                _networkService.Unsubscribe<ChatMessageListPacket>(Chat.Process);
                _networkService.Unsubscribe<LocalChatMessagePacket>(Chat.Process);
                _networkService.Unsubscribe<ChatMutePacket>(Chat.Process);

                _networkService.Unsubscribe<LevelPacket>(PlayerStats.Process);
                _networkService.Unsubscribe<HealthPacket>(PlayerStats.Process);
                _networkService.Unsubscribe<CurrencyPacket>(PlayerStats.Process);
                _networkService.Unsubscribe<GeologyPacket>(PlayerStats.Process);
                _networkService.Unsubscribe<BasketPacket>(PlayerStats.Process);

                _networkService.Unsubscribe<OnlinePacket>(Status.Process);
                _networkService.Unsubscribe<PingPacket>(Status.Process);

                _networkService.Unsubscribe<OutdatedClientPacket>(Status.Process);
                _networkService.Unsubscribe<AudioPacket>(Audio.Process);
                _networkService.Unsubscribe<InventoryPacket>(Inventory.Process);
                _networkService.Unsubscribe<MinesServer.Networking.Server.Packets.Inventory.SelectItemPacket>(Inventory.Process);
                _networkService.Unsubscribe<MinesServer.Networking.Server.Packets.Inventory.DeselectItemPacket>(Inventory.Process);
                _networkService.Unsubscribe<DailyBonusStatePacket>(PlayerStats.Process);
                _networkService.Unsubscribe<TeleportPacket>(PlayerInfo.Process);
                _networkService.Unsubscribe<AddStatusLinePacket>(Status.Process);
                _networkService.Unsubscribe<ClearStatusLinePacket>(Status.Process);
                _networkService.Unsubscribe<ClearStatusPacket>(Status.Process);
                _networkService.Unsubscribe<ModalWindowPacket>(_windowProcessor.HandleModalWindow);
                _networkService.Unsubscribe<ShowClanPacket>(Clan.Process);
                _networkService.Unsubscribe<HideClanPacket>(Clan.Process);
                _networkService.Unsubscribe<MaxDepthPacket>(PlayerStats.Process);
                _networkService.Unsubscribe<MissionInitPacket>(Mission.Process);
                _networkService.Unsubscribe<MissionProgressPacket>(Mission.Process);
                _networkService.Unsubscribe<AuthTokenPacket>(HandleAuthTokenPacket);
            }

            // Close modal and any open windows
            _windowProcessor.Dispose();

            var mm = MapManager.InstanceIfExists;
            if (mm != null)
            {
                mm.OnWorldInitialized -= OnWorldInitialized;
            }

            Debug.Log("[PacketHandler] Destroyed");
        }

        private void OnWorldInitialized()
        {
            Debug.Log("[PacketHandler] World initialized event received from MapManager");
            if (GameManager.Instance != null)
            {
                GameManager.Instance.SetState(GameState.InGame);
                GameManager.Instance.NotifyWorldLoaded();
            }
        }

        private void HandleAuthTokenPacket(AuthTokenPacket packet)
        {
            string newToken = packet.Token;
            if (string.IsNullOrEmpty(newToken))
            {
                Debug.LogError("[Auth] Received empty token from server");
                return;
            }

            Auth.AuthTokenManager.SaveToken(newToken);

            var gm = GameManager.Instance;
            if (gm != null)
            {
                gm.AuthorizeUI();
            }

            Debug.Log($"[Auth] Token received and saved, length={newToken.Length}");
        }
    }
}

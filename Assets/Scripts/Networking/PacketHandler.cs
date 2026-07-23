using System.Collections.Generic;
using System.Linq;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.Networking.Processors;
using Fodinae.Scripts.Game;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Player;
using Fodinae.Scripts.UI;
using Fodinae.UI;
using Fodinae.UI.Binding;
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
    public partial class PacketHandler : MonoBehaviour
    {
        public static PacketHandler Instance { get; private set; }

        public static bool IsInputBlocked => Instance != null && (Instance._windowProcessor.HasOpenWindows || Instance._windowProcessor.IsModalShowing || PauseMenu.IsMenuOpen);
        public static string TopWindowTag => Instance?._windowProcessor.TopWindowTag;

        private static readonly WorldInitProcessor WorldInit = new();
        private static readonly RobotInfoProcessor RobotInfo = new();
        private static readonly MapRegionProcessor MapRegion = new();
        private static readonly AudioPacketProcessor SFXProcessor = new();
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
            if (MapManager.Instance == null)
            {
                Debug.LogError("[PacketHandler] MapManager not found - cannot process world initialization");
                return;
            }

            if (ServiceLocator.Resolve<IWorldDataStorage>() == null)
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
            var ns = NetworkService.Instance;
            if (ns != null)
            {
                ns.Subscribe<WorldInitPacket>(WorldInit.Process);
                ns.Subscribe<RobotInfoPacket>(RobotInfo.Process);
                ns.Subscribe<PlayerInfoPacket>(PlayerInfo.Process);
                ns.Subscribe<MovementSpeedPacket>(PlayerInfo.Process);
                ns.Subscribe<OpenWindowPacket>(_windowProcessor.Process);
                ns.Subscribe<CloseWindowPacket>(_windowProcessor.Process);
                ns.Subscribe<RobotPositionPacket>(RobotPosition.Process);
                ns.Subscribe<MapRegionPacket>(MapRegion.Process);
                ns.Subscribe<PackPacket>(Pack.Process);
                ns.Subscribe<RemovePackPacket>(Pack.Process);

                // Player stats
                ns.Subscribe<LevelPacket>(PlayerStats.Process);
                ns.Subscribe<HealthPacket>(PlayerStats.Process);
                ns.Subscribe<CurrencyPacket>(PlayerStats.Process);
                ns.Subscribe<GeologyPacket>(PlayerStats.Process);
                ns.Subscribe<BasketPacket>(PlayerStats.Process);
                ns.Subscribe<MaxDepthPacket>(PlayerStats.Process);

                ns.Subscribe<AutoMineStatePacket>(PlayerState.Process);
                ns.Subscribe<AggressionStatePacket>(PlayerState.Process);
                ns.Subscribe<SkillProgressPacket>(PlayerStats.Process);
                ns.Subscribe<DailyBonusStatePacket>(PlayerStats.Process);
                ns.Subscribe<TeleportPacket>(PlayerInfo.Process);
                ns.Subscribe<ChatMessageListPacket>(Chat.Process);
                ns.Subscribe<LocalChatMessagePacket>(Chat.Process);
                ns.Subscribe<ChatMutePacket>(Chat.Process);

                ns.Subscribe<OnlinePacket>(Status.Process);
                ns.Subscribe<PingPacket>(Status.Process);
                ns.Subscribe<OutdatedClientPacket>(Status.Process);
                ns.Subscribe<SFXPacket>(SFXProcessor.Process);
                ns.Subscribe<InventoryPacket>(Inventory.Process);
                ns.Subscribe<MinesServer.Networking.Server.Packets.Inventory.SelectItemPacket>(Inventory.Process);
                ns.Subscribe<MinesServer.Networking.Server.Packets.Inventory.DeselectItemPacket>(Inventory.Process);
                ns.Subscribe<AddStatusLinePacket>(Status.Process);
                ns.Subscribe<ClearStatusLinePacket>(Status.Process);
                ns.Subscribe<ClearStatusPacket>(Status.Process);
                ns.Subscribe<ModalWindowPacket>(_windowProcessor.HandleModalWindow);
                ns.Subscribe<ShowClanPacket>(Clan.Process);
                ns.Subscribe<HideClanPacket>(Clan.Process);
                ns.Subscribe<MissionInitPacket>(Mission.Process);
                ns.Subscribe<MissionProgressPacket>(Mission.Process);
            }

            var mm = MapManager.Instance;
            if (mm != null)
            {
                mm.OnWorldInitialized += OnWorldInitialized;
            }

            Debug.Log("[PacketHandler] Initialization complete - ready to receive packets");
            _isInitialized = true;
        }

        protected virtual void OnDestroy()
        {
            if (!_isInitialized)
            {
                return;
            }

            var ns = NetworkService.InstanceIfExists;
            if (ns != null)
            {
                ns.Unsubscribe<WorldInitPacket>(WorldInit.Process);
                ns.Unsubscribe<RobotInfoPacket>(RobotInfo.Process);
                ns.Unsubscribe<PlayerInfoPacket>(PlayerInfo.Process);
                ns.Unsubscribe<MovementSpeedPacket>(PlayerInfo.Process);
                ns.Unsubscribe<OpenWindowPacket>(_windowProcessor.Process);
                ns.Unsubscribe<CloseWindowPacket>(_windowProcessor.Process);
                ns.Unsubscribe<RobotPositionPacket>(RobotPosition.Process);
                ns.Unsubscribe<MapRegionPacket>(MapRegion.Process);
                ns.Unsubscribe<PackPacket>(Pack.Process);
                ns.Unsubscribe<RemovePackPacket>(Pack.Process);
                ns.Unsubscribe<SkillProgressPacket>(PlayerStats.Process);
                ns.Unsubscribe<AutoMineStatePacket>(PlayerState.Process);
                ns.Unsubscribe<AggressionStatePacket>(PlayerState.Process);
                ns.Unsubscribe<ChatMessageListPacket>(Chat.Process);
                ns.Unsubscribe<LocalChatMessagePacket>(Chat.Process);
                ns.Unsubscribe<ChatMutePacket>(Chat.Process);

                ns.Unsubscribe<LevelPacket>(PlayerStats.Process);
                ns.Unsubscribe<HealthPacket>(PlayerStats.Process);
                ns.Unsubscribe<CurrencyPacket>(PlayerStats.Process);
                ns.Unsubscribe<GeologyPacket>(PlayerStats.Process);
                ns.Unsubscribe<BasketPacket>(PlayerStats.Process);

                ns.Unsubscribe<OnlinePacket>(Status.Process);
                ns.Unsubscribe<PingPacket>(Status.Process);

                ns.Unsubscribe<OutdatedClientPacket>(Status.Process);
                ns.Unsubscribe<SFXPacket>(SFXProcessor.Process);
                ns.Unsubscribe<InventoryPacket>(Inventory.Process);
                ns.Unsubscribe<MinesServer.Networking.Server.Packets.Inventory.SelectItemPacket>(Inventory.Process);
                ns.Unsubscribe<MinesServer.Networking.Server.Packets.Inventory.DeselectItemPacket>(Inventory.Process);
                ns.Unsubscribe<DailyBonusStatePacket>(PlayerStats.Process);
                ns.Unsubscribe<TeleportPacket>(PlayerInfo.Process);
                ns.Unsubscribe<AddStatusLinePacket>(Status.Process);
                ns.Unsubscribe<ClearStatusLinePacket>(Status.Process);
                ns.Unsubscribe<ClearStatusPacket>(Status.Process);
                ns.Unsubscribe<ModalWindowPacket>(_windowProcessor.HandleModalWindow);
                ns.Unsubscribe<ShowClanPacket>(Clan.Process);
                ns.Unsubscribe<HideClanPacket>(Clan.Process);
                ns.Unsubscribe<MaxDepthPacket>(PlayerStats.Process);
                ns.Unsubscribe<MissionInitPacket>(Mission.Process);
                ns.Unsubscribe<MissionProgressPacket>(Mission.Process);
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
    }
}

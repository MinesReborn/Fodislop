using System.Collections.Generic;
using System.Linq;
using MinesServer.Data;
using Fodinae.Scripts.Audio;
using Fodinae.Scripts.Game;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Player;
using Fodinae.Scripts.UI;
using Fodinae.UI;
using Fodinae.UI.Binding;
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
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.Networking
{
    public class PacketHandler : MonoBehaviour
    {
        public static PacketHandler Instance { get; private set; }

        public static bool IsInputBlocked => Instance != null && (Instance._openWindows.Count > 0 || Instance._modalWindowHandler?.IsShowing == true);
        public static string TopWindowTag => Instance?._openWindows.Count > 0 ? Instance._openWindows[^1].tag : null;

        private bool _isInitialized = false;
        private int _packetCount = 0;
        private int _worldInitPacketsReceived = 0;
        private int _mapRegionPacketsReceived = 0;
        private UIDocument _uiDocument;
        private ModalWindowHandler _modalWindowHandler;
        private readonly List<(string tag, VisualElement root, WindowBinding binding, List<VisualElement> clickableElements)> _openWindows = new();

        public void HandleWorldInitPacket(WorldInitPacket worldInitPacket)
        {
            _packetCount++;
            _worldInitPacketsReceived++;
            Debug.Log($"[PacketHandler] Processing WorldInitPacket #{_worldInitPacketsReceived}");
            Debug.Log($"[PacketHandler] World: {worldInitPacket.DisplayName} ({worldInitPacket.CodeName}) [{worldInitPacket.Width}x{worldInitPacket.Height}]");

            try
            {
                // Call MapManager.LoadWorldInit immediately
                MapManager.Instance?.LoadWorldInit(worldInitPacket);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[PacketHandler] Error processing WorldInitPacket: {ex.Message}");
            }
        }

        public string GetStatistics()
        {
            return $"[PacketHandler Stats] Total: {_packetCount}, WorldInit: {_worldInitPacketsReceived}, MapRegion: {_mapRegionPacketsReceived}, Initialized: {_isInitialized}";
        }

        protected virtual void Awake()
        {
            Instance = this;
            Debug.Log("[PacketHandler] Starting initialization...");

            // Verify Dependencies
            if (MapManager.Instance == null)
            {
                Debug.LogError("[PacketHandler] MapManager not found - cannot process world initialization");
                return;
            }

            if (MapStorage.Instance == null)
            {
                Debug.LogError("[PacketHandler] MapStorage not found - cannot process map data");
                return;
            }

            _uiDocument = FindFirstObjectByType<UIDocument>();
            if (_uiDocument == null)
            {
                Debug.LogWarning("[PacketHandler] UIDocument not found - window packets will not be displayed");
            }
            else
            {
                _modalWindowHandler = new ModalWindowHandler(_uiDocument);
            }

            // Subscribe to events via NetworkService
            var ns = NetworkService.Instance;
            if (ns != null)
            {
                ns.Subscribe<WorldInitPacket>(HandleWorldInitPacket);
                ns.Subscribe<HBPacket>(HandleHBPacket);
                ns.Subscribe<RobotInfoPacket>(HandleRobotInfoPacket);
                ns.Subscribe<PlayerInfoPacket>(HandlePlayerInfoPacket);
                ns.Subscribe<MovementSpeedPacket>(HandleMovementSpeedPacket);
                ns.Subscribe<OpenWindowPacket>(HandleOpenWindowPacket);
                ns.Subscribe<CloseWindowPacket>(HandleCloseWindowPacket);
                ns.Subscribe<RobotPositionPacket>(HandleRobotPositionPacket);
                ns.Subscribe<MapRegionPacket>(HandleMapRegionPacket);
                ns.Subscribe<PackPacket>(HandlePackPacket);
                ns.Subscribe<RemovePackPacket>(HandleRemovePackPacket);
                
                // Player stats
                ns.Subscribe<LevelPacket>(HandleLevelPacket);
                ns.Subscribe<HealthPacket>(HandleHealthPacket);
                ns.Subscribe<CurrencyPacket>(HandleCurrencyPacket);
                ns.Subscribe<GeologyPacket>(HandleGeologyPacket);
                ns.Subscribe<BasketPacket>(HandleBasketPacket);

                ns.Subscribe<AutoMineStatePacket>(HandleAutoMineStatePacket);
                ns.Subscribe<AggressionStatePacket>(HandleAggressionStatePacket);
                ns.Subscribe<SkillProgressPacket>(HandleSkillProgressPacket);
                ns.Subscribe<ChatMessageListPacket>(HandleChatMessageList);
                ns.Subscribe<ChatListPacket>(HandleChatList);
                ns.Subscribe<LocalChatMessagePacket>(HandleLocalChatMessage);
                ns.Subscribe<ChatMutePacket>(HandleChatMute);

                ns.Subscribe<OnlinePacket>(HandleOnlinePacket);
                ns.Subscribe<PingPacket>(HandlePingPacket);

                ns.Subscribe<OutdatedClientPacket>(HandleOutdatedClient);
                ns.Subscribe<SFXPacket>(HandleSFXPacket);
                ns.Subscribe<InventoryPacket>(HandleInventoryPacket);
                ns.Subscribe<MinesServer.Networking.Server.Packets.Inventory.SelectItemPacket>(HandleServerSelectItem);
                ns.Subscribe<MinesServer.Networking.Server.Packets.Inventory.DeselectItemPacket>(HandleServerDeselect);
                ns.Subscribe<DailyBonusStatePacket>(HandleDailyBonusStatePacket);
                ns.Subscribe<TeleportPacket>(HandleTeleportPacket);
                ns.Subscribe<AddStatusLinePacket>(HandleAddStatusLine);
                ns.Subscribe<ClearStatusLinePacket>(HandleClearStatusLine);
                ns.Subscribe<ClearStatusPacket>(HandleClearStatus);
                ns.Subscribe<ModalWindowPacket>(HandleModalWindowPacket);
                ns.Subscribe<ShowClanPacket>(HandleShowClanPacket);
                ns.Subscribe<HideClanPacket>(HandleHideClanPacket);
                ns.Subscribe<MaxDepthPacket>(HandleMaxDepthPacket);
                ns.Subscribe<MissionInitPacket>(HandleMissionInitPacket);
                ns.Subscribe<MissionProgressPacket>(HandleMissionProgressPacket);
            }

            var mm = MapManager.Instance;
            if (mm != null)
            {
                mm.OnWorldInitialized += OnWorldInitialized;
                mm.OnWorldDataLoaded += OnWorldDataLoaded;
            }

            _isInitialized = true;
            Debug.Log("[PacketHandler] Initialization complete - ready to receive packets");
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
                ns.Unsubscribe<WorldInitPacket>(HandleWorldInitPacket);
                ns.Unsubscribe<HBPacket>(HandleHBPacket);
                ns.Unsubscribe<RobotInfoPacket>(HandleRobotInfoPacket);
                ns.Unsubscribe<PlayerInfoPacket>(HandlePlayerInfoPacket);
                ns.Unsubscribe<MovementSpeedPacket>(HandleMovementSpeedPacket);
                ns.Unsubscribe<OpenWindowPacket>(HandleOpenWindowPacket);
                ns.Unsubscribe<CloseWindowPacket>(HandleCloseWindowPacket);
                ns.Unsubscribe<RobotPositionPacket>(HandleRobotPositionPacket);
                ns.Unsubscribe<MapRegionPacket>(HandleMapRegionPacket);
                ns.Unsubscribe<PackPacket>(HandlePackPacket);
                ns.Unsubscribe<RemovePackPacket>(HandleRemovePackPacket);
                ns.Unsubscribe<SkillProgressPacket>(HandleSkillProgressPacket);
                ns.Unsubscribe<AutoMineStatePacket>(HandleAutoMineStatePacket);
                ns.Unsubscribe<AggressionStatePacket>(HandleAggressionStatePacket);
                ns.Unsubscribe<ChatMessageListPacket>(HandleChatMessageList);
                ns.Unsubscribe<ChatListPacket>(HandleChatList);
                ns.Unsubscribe<LocalChatMessagePacket>(HandleLocalChatMessage);
                ns.Unsubscribe<ChatMutePacket>(HandleChatMute);

                ns.Unsubscribe<LevelPacket>(HandleLevelPacket);
                ns.Unsubscribe<HealthPacket>(HandleHealthPacket);
                ns.Unsubscribe<CurrencyPacket>(HandleCurrencyPacket);
                ns.Unsubscribe<GeologyPacket>(HandleGeologyPacket);
                ns.Unsubscribe<BasketPacket>(HandleBasketPacket);

                ns.Unsubscribe<OnlinePacket>(HandleOnlinePacket);
                ns.Unsubscribe<PingPacket>(HandlePingPacket);

                ns.Unsubscribe<OutdatedClientPacket>(HandleOutdatedClient);
                ns.Unsubscribe<SFXPacket>(HandleSFXPacket);
                ns.Unsubscribe<InventoryPacket>(HandleInventoryPacket);
                ns.Unsubscribe<MinesServer.Networking.Server.Packets.Inventory.SelectItemPacket>(HandleServerSelectItem);
                ns.Unsubscribe<MinesServer.Networking.Server.Packets.Inventory.DeselectItemPacket>(HandleServerDeselect);
                ns.Unsubscribe<DailyBonusStatePacket>(HandleDailyBonusStatePacket);
                ns.Unsubscribe<TeleportPacket>(HandleTeleportPacket);
                ns.Unsubscribe<AddStatusLinePacket>(HandleAddStatusLine);
                ns.Unsubscribe<ClearStatusLinePacket>(HandleClearStatusLine);
                ns.Unsubscribe<ClearStatusPacket>(HandleClearStatus);
                ns.Unsubscribe<ModalWindowPacket>(HandleModalWindowPacket);
                ns.Unsubscribe<ShowClanPacket>(HandleShowClanPacket);
                ns.Unsubscribe<HideClanPacket>(HandleHideClanPacket);
                ns.Unsubscribe<MaxDepthPacket>(HandleMaxDepthPacket);
                ns.Unsubscribe<MissionInitPacket>(HandleMissionInitPacket);
                ns.Unsubscribe<MissionProgressPacket>(HandleMissionProgressPacket);
            }

            // Close modal and any open windows
            _modalWindowHandler?.Hide();
            foreach (var (_, root, binding, _) in _openWindows)
            {
                binding.Dispose();
                if (_uiDocument != null)
                    _uiDocument.rootVisualElement.Remove(root);
            }
            _openWindows.Clear();

            var mm = MapManager.InstanceIfExists;
            if (mm != null)
            {
                mm.OnWorldInitialized -= OnWorldInitialized;
                mm.OnWorldDataLoaded -= OnWorldDataLoaded;
            }

            Debug.Log($"[PacketHandler] Destroyed - processed {_packetCount} total packets ({_worldInitPacketsReceived} WorldInit, {_mapRegionPacketsReceived} MapRegion)");
        }

        private void HandleOpenWindowPacket(OpenWindowPacket packet)
        {
            _packetCount++;
            Debug.Log($"[PacketHandler] Handling OpenWindowPacket: {packet.WindowTag}");

            if (_uiDocument == null)
            {
                _uiDocument = FindFirstObjectByType<UIDocument>();
                if (_uiDocument == null)
                {
                    Debug.LogError("[PacketHandler] Cannot open window: UIDocument not found");
                    return;
                }
            }

            var builder = new PacketUIBuilder();
            var element = builder.Build(packet.Content);

            element.style.width = packet.Width;
            element.style.height = packet.Height;
            element.style.position = Position.Absolute;
            element.style.left = new Length(50, LengthUnit.Percent);
            element.style.top = new Length(50, LengthUnit.Percent);
            element.style.translate = new Translate(new Length(-50, LengthUnit.Percent), new Length(-50, LengthUnit.Percent));

            _uiDocument.rootVisualElement.Add(element);

            // Set up SmartFormat binding for this window
            var binding = new WindowBinding();
            binding.Bind(element);

            // Register clickable elements via DFS traversal
            var clickableElements = RegisterClickableElements(element, packet.WindowTag);

            var windowIndex = _openWindows.Count;
            _openWindows.Add((packet.WindowTag, element, binding, clickableElements));
            Debug.Log($"[PacketHandler] Window '{packet.WindowTag}' opened with {clickableElements.Count} clickable elements (window index {windowIndex})");
        }

        /// <summary>
        /// DFS-traverses the window VisualElement tree and registers all clickable elements.
        /// A clickable element is one whose userData contains an IGUIComponentPacket with non-empty OnClickContext.
        /// </summary>
        private List<VisualElement> RegisterClickableElements(VisualElement windowRoot, string windowTag)
        {
            var clickableElements = new List<VisualElement>();
            WalkForClickable(windowRoot, clickableElements, windowTag);
            return clickableElements;
        }

        private void WalkForClickable(VisualElement element, List<VisualElement> clickableElements, string windowTag)
        {
            // Check if this element is clickable
            if (element.userData is IGUIComponentPacket componentPacket &&
                !string.IsNullOrEmpty(componentPacket.OnClickContext))
            {
                var elementIndex = clickableElements.Count;
                clickableElements.Add(element);
                WireClickHandler(element, elementIndex, windowTag);
            }

            foreach (var child in element.Children())
                WalkForClickable(child, clickableElements, windowTag);
        }

        private void WireClickHandler(VisualElement element, int elementIndex, string windowTag)
        {
            element.RegisterCallback<ClickEvent>(_ => HandleElementClick(element, elementIndex, windowTag));
        }

        private void HandleElementClick(VisualElement clickedElement, int elementIndex, string windowTag)
        {
            // Find the window data for this window tag
            var windowEntry = _openWindows.FirstOrDefault(w => w.tag == windowTag);
            if (windowEntry == default)
            {
                Debug.LogWarning($"[PacketHandler] Cannot handle element click: window '{windowTag}' not found");
                return;
            }

            var (_, windowRoot, _, _) = windowEntry;

            // Get the click context from the element's packet data
            if (clickedElement.userData is not IGUIComponentPacket componentPacket)
            {
                Debug.LogWarning("[PacketHandler] Clicked element has no IGUIComponentPacket userData");
                return;
            }

            var clickContext = componentPacket.OnClickContext;
            Debug.Log($"[PacketHandler] Element click: index={elementIndex}, context='{clickContext}', window='{windowTag}'");

            // Resolve the root element for input traversal
            var inputRoot = ClickContextResolver.ResolveRoot(clickedElement, windowRoot, clickContext);

            // Collect all input control values from the resolved root
            var inputValues = ClickContextResolver.CollectInputValues(inputRoot);

            // Send the ElementClickPacket
            var elementClickPacket = new ElementClickPacket(windowTag, elementIndex, inputValues);
            NetworkService.Instance.Send(elementClickPacket);

            Debug.Log($"[PacketHandler] Sent ElementClickPacket: index={elementIndex}, contextValues={inputValues.Length}");
        }

        private void HandleCloseWindowPacket(CloseWindowPacket packet)
        {
            _packetCount++;
            Debug.Log("[PacketHandler] Handling CloseWindowPacket");

            _modalWindowHandler?.Hide();

            if (_openWindows.Count == 0) return;

            var (_, root, binding, _) = _openWindows[^1];
            binding.Dispose();
            _uiDocument.rootVisualElement.Remove(root);
            _openWindows.RemoveAt(_openWindows.Count - 1);
        }

        private void HandleRobotInfoPacket(RobotInfoPacket packet)
        {
            _packetCount++;
            //Debug.Log($"[PacketHandler] Handling RobotInfoPacket for BotId: {packet.BotId}, Name: {packet.Name}");
            RobotManager.Instance?.UpdateRobotMetadata(packet.BotId, packet.PlayerId, packet.ClanId, packet.Name, packet.Skin, packet.Tail);
        }

        private void HandlePlayerInfoPacket(PlayerInfoPacket packet)
        {
            _packetCount++;
            //Debug.Log($"[PacketHandler] Handling PlayerInfoPacket for BotId: {packet.BotId}, PlayerId: {packet.PlayerId}, Name: {packet.Nickname}");
            var rm = RobotManager.Instance;
            if (rm != null)
            {
                rm.LocalPlayerBotId = packet.BotId;
            }
            PlayerStatsModel.Instance.SetNickname(packet.Nickname);

            var playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                var robot = playerObj.GetComponent<Robot>();
                if (robot != null)
                {
                    robot.Initialize(packet.BotId);
                }

                var controller = playerObj.GetComponent<PlayerMovementController>();
                if (controller != null)
                {
                    controller.Initialize(packet.BotId);
                }
            }
        }

        private void HandleMovementSpeedPacket(MovementSpeedPacket packet)
        {
            _packetCount++;
            Debug.Log($"[PacketHandler] Handling MovementSpeedPacket with {packet.CooldownMap.Count} entries");
            MapManager.Instance?.UpdateMovementSpeeds(packet);
        }

        private void HandleRobotPositionPacket(RobotPositionPacket robotPositionPacket)
        {
            _packetCount++;
            var rm = RobotManager.Instance;
            if (rm != null)
            {
                rm.UpdateRobotPosition(robotPositionPacket.BotId, robotPositionPacket.X, robotPositionPacket.Y, robotPositionPacket.Rotation);

                // If this is the local player, update the server position in the movement controller
                if (robotPositionPacket.BotId != 0 && robotPositionPacket.BotId == rm.LocalPlayerBotId)
                {
                    var player = GameObject.FindGameObjectWithTag("Player");
                    if (player != null)
                    {
                        var controller = player.GetComponent<PlayerMovementController>();
                        if (controller != null)
                        {
                            controller.UpdateServerPosition(new Vector2Int(robotPositionPacket.X, robotPositionPacket.Y));
                        }
                    }
                }
            }
        }

        private void HandleMapRegionPacket(MapRegionPacket mapRegionPacket)
        {
            _packetCount++;
            _mapRegionPacketsReceived++;
            //Debug.Log($"[PacketHandler] Processing MapRegionPacket #{_mapRegionPacketsReceived}: X={mapRegionPacket.X}, Y={mapRegionPacket.Y}, Size={mapRegionPacket.Width + 1}x{mapRegionPacket.Height + 1}");

            if (MapStorage.Instance == null || MapStorage.Instance.CellLayer == null)
            {
                Debug.LogError("[PacketHandler] MapStorage or CellLayer not available for MapRegion processing");
                return;
            }

            int expectedPayloadLength = (mapRegionPacket.Width + 1) * (mapRegionPacket.Height + 1);
            if (mapRegionPacket.Payload == null || mapRegionPacket.Payload.Length != expectedPayloadLength)
            {
                Debug.LogError($"[PacketHandler] MapRegionPacket has malformed payload: expected {expectedPayloadLength} cells, got {mapRegionPacket.Payload?.Length.ToString() ?? "null"}. Discarding.");
                return;
            }

            try
            {
                int index = 0;
                for (int y = 0; y <= mapRegionPacket.Height; y++)
                {
                    for (int x = 0; x <= mapRegionPacket.Width; x++)
                    {
                        if (index < mapRegionPacket.Payload.Length)
                        {
                            MapStorage.Instance.SetCell(mapRegionPacket.X + x, mapRegionPacket.Y + y, mapRegionPacket.Payload[index++]);
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[PacketHandler] Error processing MapRegionPacket: {ex.Message}");
            }
        }

        private void HandlePackPacket(PackPacket packPacket)
        {
            _packetCount++;
            Debug.Log($"[PacketHandler] Processing PackPacket: X={packPacket.X}, Y={packPacket.Y}, Type={packPacket.PackCode}");
            PackManager.Instance?.AddOrUpdatePack(packPacket.X, packPacket.Y, packPacket.PackCode, packPacket.Variant, packPacket.LinkedClan);
        }

        private void HandleRemovePackPacket(RemovePackPacket removePackPacket)
        {
            _packetCount++;
            Debug.Log($"[PacketHandler] Processing RemovePackPacket: X={removePackPacket.X}, Y={removePackPacket.Y}");
            PackManager.Instance?.RemovePack(removePackPacket.X, removePackPacket.Y);
        }

        private void HandleHBPacket(HBPacket hbPacket)
        {
            _packetCount++;
            if (hbPacket.Payload == null)
            {
                return;
            }

            bool hasMapData = hbPacket.Payload.Any(p => p is MapRegionPacket);

            // Only trigger world data loaded event if we received at least one MapRegion packet in this heartbeat
            if (hasMapData)
            {
                //Debug.Log("[PacketHandler] Map data received in HBPacket, triggering OnWorldDataLoaded event");
                MapManager.Instance?.OnWorldDataLoaded?.Invoke();
            }
        }

        private void HandleLevelPacket(LevelPacket packet)
        {
            _packetCount++;
            PlayerStatsModel.Instance.SetLevel(packet.Level);
        }

        private void HandleHealthPacket(HealthPacket packet)
        {
            _packetCount++;
            PlayerStatsModel.Instance.SetHealth(packet.Current, packet.Max);
        }

        private void HandleCurrencyPacket(CurrencyPacket packet)
        {
            _packetCount++;
            PlayerStatsModel.Instance.SetCurrency(packet.Money, packet.Creds);
        }

        private void HandleGeologyPacket(GeologyPacket packet)
        {
            _packetCount++;
            PlayerStatsModel.Instance.SetGeology(packet.Current, packet.Max, packet.Cell, packet.Text);
        }

        private void HandleBasketPacket(BasketPacket packet)
        {
            _packetCount++;
            PlayerStatsModel.Instance.SetBasket(packet.Capacity, packet.Contents);
        }

        private void HandleAutoMineStatePacket(AutoMineStatePacket packet)
        {
            var player = FindObjectOfType<PlayerMovementController>();
            if (player != null)
                player.AutoDig = packet.Enabled;
        }

        private void HandleAggressionStatePacket(AggressionStatePacket packet)
        {
            Debug.Log($"[PacketHandler] AggressionStatePacket: {packet.Enabled}");
            var player = FindObjectOfType<PlayerMovementController>();
            if (player != null)
                player.Aggression = packet.Enabled;
        }

        private void HandleDailyBonusStatePacket(DailyBonusStatePacket packet)
        {
            Debug.Log($"[PacketHandler] DailyBonusStatePacket: {packet.Enabled}");
            PlayerStatsModel.Instance.SetDailyBonusAvailable(packet.Enabled);
        }

        private void HandleTeleportPacket(TeleportPacket packet)
        {
            _packetCount++;
            Debug.Log($"[PacketHandler] TeleportPacket: X={packet.X}, Y={packet.Y}, Smooth={packet.SmoothTransition}");

            var player = FindObjectOfType<PlayerMovementController>();
            if (player == null) return;

            var mm = MapManager.Instance;
            if (mm == null) return;

            int unityX = packet.X;
            int unityY = mm.WorldHeight - 1 - packet.Y;

            player.transform.position = new Vector3(unityX + 0.5f, unityY + 0.5f, 0);
            player.UpdateServerPosition(new Vector2Int(packet.X, packet.Y));
        }

        private void HandleSkillProgressPacket(SkillProgressPacket packet)
        {
            PlayerStatsModel.Instance.SetSkillProgress(packet.Skill, packet.Current, packet.Max);
        }

        private void HandleChatMessageList(ChatMessageListPacket packet)
        {
            _packetCount++;
            foreach (var msg in packet.Messages)
            {
                if (GlobalChatUI.Instance != null)
                    GlobalChatUI.Instance.AddMessage(msg);
            }
        }

        private void HandleChatList(ChatListPacket packet)
        {
            _packetCount++;
            Debug.Log($"[PacketHandler] Received {packet.Chats.Count} chat channels");
        }

        private void HandleLocalChatMessage(LocalChatMessagePacket packet)
        {
            _packetCount++;
            Debug.Log($"[PacketHandler] Local chat from BotId={packet.BotId}: {packet.Text}");
            if (FloatingChatManager.Instance != null)
                FloatingChatManager.Instance.ShowLocalChat(packet);
        }

        private void HandleChatMute(ChatMutePacket packet)
        {
            _packetCount++;
            Debug.Log($"[PacketHandler] Chat mute until {packet.EndsAt}: {packet.Reason}");
        }

        private void OnWorldInitialized()
        {
            Debug.Log("[PacketHandler] World initialized event received from MapManager");
        }

        private void HandleOnlinePacket(OnlinePacket packet)
        {
            _packetCount++;
            var fps = FindObjectOfType<FPSCounter>();
            if (fps != null)
                fps.SetOnline((int)packet.Players, (int)packet.Programmator);
        }

        private void HandlePingPacket(PingPacket packet)
        {
            _packetCount++;
            var fps = FindObjectOfType<FPSCounter>();
            if (fps != null)
                fps.SetPing(packet.PreviousPing);
            NetworkService.Instance.Send(new PongPacket(packet.SentAt));
        }

        private void HandleOutdatedClient(OutdatedClientPacket packet)
        {
            _packetCount++;
            Debug.LogError($"[PacketHandler] Клиент устарел: {packet.Name}");
            Debug.LogError($"[PacketHandler] {packet.Description}");
            Debug.LogError($"[PacketHandler] Скачать: {packet.UpdateURL}");
            Application.OpenURL(packet.UpdateURL);
        }

        private void HandleInventoryPacket(InventoryPacket packet)
        {
            var model = InventoryModel.Instance;
            var remaining = new Dictionary<ItemType, long>(packet.Changes);

            for (int i = 0; i < InventoryModel.TOTAL_SLOTS; i++)
            {
                var existing = model.GetSlot(i);
                if (existing == null) continue;

                if (remaining.TryGetValue(existing.ItemType, out long qty))
                {
                    if (qty <= 0)
                        model.SetSlot(i, null);
                    else
                    {
                        existing.Quantity = (int)qty;
                        model.SetSlot(i, existing);
                    }

                    remaining.Remove(existing.ItemType);
                }
            }

            foreach (var kvp in remaining)
            {
                if (kvp.Value <= 0) continue;

                for (int i = 0; i < InventoryModel.TOTAL_SLOTS; i++)
                {
                    if (model.GetSlot(i) != null) continue;

                    var item = new ItemData(
                        kvp.Key.ToString(),
                        Color.gray,
                        (int)kvp.Value);
                    item.ItemType = kvp.Key;
                    item.Icon = ItemRegistry.GetIcon(kvp.Key);
                    model.SetSlot(i, item);
                    break;
                }
            }
        }

        private void HandleServerSelectItem(MinesServer.Networking.Server.Packets.Inventory.SelectItemPacket packet)
        {
            _packetCount++;
            var model = InventoryModel.Instance;
            int slot = model.SelectedSlot;
            if (slot < 0) return;

            var item = model.GetSlot(slot);
            if (item == null) return;

            item.Name = packet.Name;
            item.Description = packet.Description;
            model.SetSlot(slot, item);
        }

        private void HandleServerDeselect(MinesServer.Networking.Server.Packets.Inventory.DeselectItemPacket packet)
        {
            _packetCount++;
            Debug.Log("[PacketHandler] Server deselected item");
            InventoryModel.Instance.ClearSelection();
        }

        private void HandleSFXPacket(SFXPacket packet)
        {
            _packetCount++;
            SFXEffectManager.Instance?.PlayEffect(packet);
        }

        private void HandleAddStatusLine(AddStatusLinePacket packet)
        {
            _packetCount++;
            var sysColor = packet.Color;
            var unityColor = new Color(sysColor.R / 255f, sysColor.G / 255f, sysColor.B / 255f, sysColor.A / 255f);
            long expiry = 0;
            if (packet.Text.Count > 1)
                long.TryParse(packet.Text[1], out expiry);
            PlayerStatsModel.Instance.AddStatusLine(packet.Tag, packet.Text.ToArray(), unityColor, packet.BlinkRate, expiry);
        }

        private void HandleModalWindowPacket(ModalWindowPacket packet)
        {
            _packetCount++;
            Debug.Log("[PacketHandler] Handling ModalWindowPacket");

            if (_modalWindowHandler == null)
            {
                Debug.LogError("[PacketHandler] ModalWindowHandler not initialized");
                return;
            }

            _modalWindowHandler.Show(packet);
        }

        private void HandleClearStatusLine(ClearStatusLinePacket packet)
        {
            _packetCount++;
            PlayerStatsModel.Instance.RemoveStatusLine(packet.Tag);
        }

        private void HandleClearStatus(ClearStatusPacket packet)
        {
            _packetCount++;
            PlayerStatsModel.Instance.ClearStatusLines();
        }

        private void HandleShowClanPacket(ShowClanPacket packet)
        {
            _packetCount++;
            Debug.Log($"[PacketHandler] ShowClanPacket: ClanId={packet.ClanId}");
            PlayerStatsModel.Instance.SetClanId(packet.ClanId);
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                var robot = player.GetComponent<Robot>();
                if (robot != null)
                {
                    robot.SetClanBadge(packet.ClanId);
                }
            }
        }

        private void HandleHideClanPacket(HideClanPacket packet)
        {
            _packetCount++;
            Debug.Log("[PacketHandler] HideClanPacket");
            PlayerStatsModel.Instance.SetClanId(0);
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                var robot = player.GetComponent<Robot>();
                if (robot != null)
                {
                    robot.ClearClanBadge();
                }
            }
        }

        private void HandleMaxDepthPacket(MaxDepthPacket packet)
        {
            _packetCount++;
            Debug.Log($"[PacketHandler] MaxDepthPacket: Depth={packet.Depth}");
            PlayerStatsModel.Instance.SetMaxDepth(packet.Depth);
        }

        private void HandleMissionInitPacket(MissionInitPacket packet)
        {
            _packetCount++;
            Debug.Log($"[PacketHandler] MissionInitPacket: {packet.Title}");
            if (string.IsNullOrEmpty(packet.Title))
            {
                PlayerStatsModel.Instance.ClearMission();
                return;
            }
            PlayerStatsModel.Instance.SetMission(packet.Title, packet.Description, 0);
        }

        private void HandleMissionProgressPacket(MissionProgressPacket packet)
        {
            _packetCount++;
            Debug.Log($"[PacketHandler] MissionProgressPacket: {packet.Current}/{packet.Max}");
            var stats = PlayerStatsModel.Instance;
            stats.SetMissionProgress(packet.Current);
            if (packet.Max > 0)
                stats.SetMissionMaxProgress(packet.Max);
        }

        private void OnWorldDataLoaded()
        {
            //Debug.Log("[PacketHandler] World data loaded event received from MapManager");
        }
    }
}

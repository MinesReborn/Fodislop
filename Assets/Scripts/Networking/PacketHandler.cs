using Fodinae.Assets.Scripts.Game.Managers;
using Fodinae.Assets.Scripts.Game;
using Fodinae.Assets.Scripts.Networking.Connection;
using Fodinae.Assets.Scripts.Player;
using MinesServer.Networking.Server;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using System.Linq;

namespace Fodinae.Assets.Scripts.Networking
{
    public class PacketHandler : MonoBehaviour
    {
        private bool _isInitialized = false;
        private int _packetCount = 0;
        private int _worldInitPacketsReceived = 0;
        private int _mapRegionPacketsReceived = 0;
        private UIDocument _uiDocument;

        void Start()
        {
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

            _uiDocument = FindObjectOfType<UIDocument>();
            if (_uiDocument == null)
            {
                Debug.LogWarning("[PacketHandler] UIDocument not found - window packets will not be displayed");
            }

            // Subscribe to events via NetworkService
            var ns = NetworkService.Instance;
            ns.Subscribe<WorldInitPacket>(HandleWorldInitPacket);
            ns.Subscribe<HBPacket>(HandleHBPacket);
            ns.Subscribe<RobotInfoPacket>(HandleRobotInfoPacket);
            ns.Subscribe<PlayerInfoPacket>(HandlePlayerInfoPacket);
            ns.Subscribe<MovementSpeedPacket>(HandleMovementSpeedPacket);
            ns.Subscribe<OpenWindowPacket>(HandleOpenWindowPacket);
            ns.Subscribe<RobotPositionPacket>(HandleRobotPositionPacket);
            ns.Subscribe<MapRegionPacket>(HandleMapRegionPacket);
            ns.Subscribe<PackPacket>(HandlePackPacket);
            ns.Subscribe<RemovePackPacket>(HandleRemovePackPacket);

            MapManager.Instance.OnWorldInitialized += OnWorldInitialized;
            MapManager.Instance.OnWorldDataLoaded += OnWorldDataLoaded;
            
            _isInitialized = true;
            Debug.Log("[PacketHandler] Initialization complete - ready to receive packets");
        }

        void OnDestroy()
        {
            if (!_isInitialized) return;

            if (NetworkService.Instance != null)
            {
                var ns = NetworkService.Instance;
                ns.Unsubscribe<WorldInitPacket>(HandleWorldInitPacket);
                ns.Unsubscribe<HBPacket>(HandleHBPacket);
                ns.Unsubscribe<RobotInfoPacket>(HandleRobotInfoPacket);
                ns.Unsubscribe<PlayerInfoPacket>(HandlePlayerInfoPacket);
                ns.Unsubscribe<MovementSpeedPacket>(HandleMovementSpeedPacket);
                ns.Unsubscribe<OpenWindowPacket>(HandleOpenWindowPacket);
                ns.Unsubscribe<RobotPositionPacket>(HandleRobotPositionPacket);
                ns.Unsubscribe<MapRegionPacket>(HandleMapRegionPacket);
                ns.Unsubscribe<PackPacket>(HandlePackPacket);
                ns.Unsubscribe<RemovePackPacket>(HandleRemovePackPacket);
            }
            
            if (MapManager.Instance != null)
            {
                MapManager.Instance.OnWorldInitialized -= OnWorldInitialized;
                MapManager.Instance.OnWorldDataLoaded -= OnWorldDataLoaded;
            }
            
            Debug.Log($"[PacketHandler] Destroyed - processed {_packetCount} total packets ({_worldInitPacketsReceived} WorldInit, {_mapRegionPacketsReceived} MapRegion)");
        }

        private void HandleOpenWindowPacket(OpenWindowPacket packet)
        {
            _packetCount++;
            Debug.Log($"[PacketHandler] Handling OpenWindowPacket: {packet.WindowTag}");
            
            if (_uiDocument == null)
            {
                _uiDocument = FindObjectOfType<UIDocument>();
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
        }

        public void HandleWorldInitPacket(WorldInitPacket worldInitPacket)
        {
            _packetCount++;
            _worldInitPacketsReceived++;
            Debug.Log($"[PacketHandler] Processing WorldInitPacket #{_worldInitPacketsReceived}");
            Debug.Log($"[PacketHandler] World: {worldInitPacket.DisplayName} ({worldInitPacket.CodeName}) [{worldInitPacket.Width}x{worldInitPacket.Height}]");

            try
            {
                // Call MapManager.LoadWorldInit immediately
                MapManager.Instance.LoadWorldInit(worldInitPacket);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[PacketHandler] Error processing WorldInitPacket: {ex.Message}");
            }
        }

        private void HandleRobotInfoPacket(RobotInfoPacket packet)
        {
            _packetCount++;
            Debug.Log($"[PacketHandler] Handling RobotInfoPacket for BotId: {packet.BotId}, Name: {packet.Name}");
            RobotManager.Instance.UpdateRobotMetadata(packet.BotId, packet.PlayerId, packet.ClanId, packet.Name, packet.Skin, packet.Tail);
        }

        private void HandlePlayerInfoPacket(PlayerInfoPacket packet)
        {
            _packetCount++;
            Debug.Log($"[PacketHandler] Handling PlayerInfoPacket for BotId: {packet.BotId}, PlayerId: {packet.PlayerId}, Name: {packet.Nickname}");
            RobotManager.Instance.LocalPlayerBotId = packet.BotId;

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
            MapManager.Instance.UpdateMovementSpeeds(packet);
        }

        private void HandleRobotPositionPacket(RobotPositionPacket robotPositionPacket)
        {
            _packetCount++;
            Debug.Log($"[PacketHandler] Processing RobotPositionPacket for BotId: {robotPositionPacket.BotId}");
            RobotManager.Instance.UpdateRobotPosition(robotPositionPacket.BotId, robotPositionPacket.X, robotPositionPacket.Y, robotPositionPacket.Rotation);

            // If this is the local player, update the server position in the movement controller
            if (robotPositionPacket.BotId != 0 && robotPositionPacket.BotId == RobotManager.Instance.LocalPlayerBotId)
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

        private void HandleMapRegionPacket(MapRegionPacket mapRegionPacket)
        {
            _packetCount++;
            _mapRegionPacketsReceived++;
            Debug.Log($"[PacketHandler] Processing MapRegionPacket #{_mapRegionPacketsReceived}: X={mapRegionPacket.X}, Y={mapRegionPacket.Y}, Size={mapRegionPacket.Width+1}x{mapRegionPacket.Height+1}");

            if (MapStorage.Instance == null || MapStorage.Instance.cellLayer == null)
            {
                Debug.LogError("[PacketHandler] MapStorage or cellLayer not available for MapRegion processing");
                return;
            }

            try
            {
                var layer = MapStorage.Instance.cellLayer;
                int index = 0;
                for (int y = 0; y <= mapRegionPacket.Height; y++)
                {
                    for (int x = 0; x <= mapRegionPacket.Width; x++)
                    {
                        if (index < mapRegionPacket.Payload.Length)
                        {
                            layer[mapRegionPacket.X + x, mapRegionPacket.Y + y] = mapRegionPacket.Payload[index++];
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
            PackManager.Instance.AddOrUpdatePack(packPacket.X, packPacket.Y, packPacket.PackCode, packPacket.Variant, packPacket.LinkedClan);
        }

        private void HandleRemovePackPacket(RemovePackPacket removePackPacket)
        {
            _packetCount++;
            Debug.Log($"[PacketHandler] Processing RemovePackPacket: X={removePackPacket.X}, Y={removePackPacket.Y}");
            PackManager.Instance.RemovePack(removePackPacket.X, removePackPacket.Y);
        }

        private void HandleHBPacket(HBPacket hbPacket)
        {
            _packetCount++;
            bool hasMapData = hbPacket.Payload.Any(p => p is MapRegionPacket);

            // Only trigger world data loaded event if we received at least one MapRegion packet in this heartbeat
            if (hasMapData)
            {
                Debug.Log("[PacketHandler] Map data received in HBPacket, triggering OnWorldDataLoaded event");
                MapManager.Instance.OnWorldDataLoaded?.Invoke();
            }
        }

        private void OnWorldInitialized()
        {
            Debug.Log("[PacketHandler] World initialized event received from MapManager");
        }

        private void OnWorldDataLoaded()
        {
            Debug.Log("[PacketHandler] World data loaded event received from MapManager");
        }

        public string GetStatistics()
        {
            return $"[PacketHandler Stats] Total: {_packetCount}, WorldInit: {_worldInitPacketsReceived}, MapRegion: {_mapRegionPacketsReceived}, Initialized: {_isInitialized}";
        }
    }
}

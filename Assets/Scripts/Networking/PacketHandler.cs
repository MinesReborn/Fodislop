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
            
            // Verify ConnectionManager exists
            if (ConnectionManager.Instance == null)
            {
                Debug.LogError("[PacketHandler] ConnectionManager not found - cannot receive packets");
                return;
            }

            // Verify MapManager exists
            if (MapManager.Instance == null)
            {
                Debug.LogError("[PacketHandler] MapManager not found - cannot process world initialization");
                return;
            }

            // Verify MapStorage exists
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

            // Subscribe to events
            ConnectionManager.Instance.OnPacketReceived += OnPacketReceived;
            MapManager.Instance.OnWorldInitialized += OnWorldInitialized;
            MapManager.Instance.OnWorldDataLoaded += OnWorldDataLoaded;
            
            _isInitialized = true;
            Debug.Log("[PacketHandler] Initialization complete - ready to receive packets");
        }

        void OnDestroy()
        {
            if (!_isInitialized) return;

            if (ConnectionManager.Instance != null)
            {
                ConnectionManager.Instance.OnPacketReceived -= OnPacketReceived;
            }
            
            if (MapManager.Instance != null)
            {
                MapManager.Instance.OnWorldInitialized -= OnWorldInitialized;
                MapManager.Instance.OnWorldDataLoaded -= OnWorldDataLoaded;
            }
            
            Debug.Log($"[PacketHandler] Destroyed - processed {_packetCount} total packets ({_worldInitPacketsReceived} WorldInit, {_mapRegionPacketsReceived} MapRegion)");
        }

        public void OnPacketReceived(ServerPacket packet)
        {
            if (!_isInitialized)
            {
                Debug.LogWarning("[PacketHandler] Received packet but not properly initialized");
                return;
            }

            _packetCount++;
            
            // Log packet type for debugging
            string packetType = packet.Payload?.GetType().Name ?? "Unknown";
            Debug.Log($"[PacketHandler] Received packet #{_packetCount}: {packetType}");

            if (packet.Payload is WorldInitPacket worldInitPacket)
            {
                HandleWorldInitPacket(worldInitPacket);
            }
            else if (packet.Payload is HBPacket hbPacket)
            {
                HandleHBPacket(hbPacket);
            }
            else if (packet.Payload is RobotInfoPacket robotInfoPacket)
            {
                HandleRobotInfoPacket(robotInfoPacket);
            }
            else if (packet.Payload is PlayerInfoPacket playerInfoPacket)
            {
                HandlePlayerInfoPacket(playerInfoPacket);
            }
            else if (packet.Payload is MovementSpeedPacket movementSpeedPacket)
            {
                HandleMovementSpeedPacket(movementSpeedPacket);
            }
            else if (packet.Payload is OpenWindowPacket openWindowPacket)
            {
                HandleOpenWindowPacket(openWindowPacket);
            }
            else
            {
                // Log other packet types for debugging
                Debug.Log($"[PacketHandler] Packet type {packetType} not handled by this handler");
            }
        }

        private void HandleOpenWindowPacket(OpenWindowPacket packet)
        {
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
            _worldInitPacketsReceived++;
            Debug.Log($"[PacketHandler] Processing WorldInitPacket #{_worldInitPacketsReceived}");
            Debug.Log($"[PacketHandler] World: {worldInitPacket.DisplayName} ({worldInitPacket.CodeName}) [{worldInitPacket.Width}x{worldInitPacket.Height}]");

            try
            {
                // Verify MapManager is available
                if (MapManager.Instance == null)
                {
                    Debug.LogError("[PacketHandler] MapManager not available for WorldInit processing");
                    return;
                }

                // Call MapManager.LoadWorldInit immediately
                Debug.Log("[PacketHandler] Calling MapManager.LoadWorldInit...");
                MapManager.Instance.LoadWorldInit(worldInitPacket);
                Debug.Log("[PacketHandler] MapManager.LoadWorldInit completed successfully");
                
                // CRITICAL: DO NOT trigger OnWorldDataLoaded here yet
                // We need to wait for MapStorage to be properly initialized
                // The MapManager should trigger this event when MapStorage is ready
                
                Debug.Log("[PacketHandler] WorldInit processing complete - waiting for MapStorage to be ready");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[PacketHandler] Error processing WorldInitPacket: {ex.Message}");
                Debug.LogError($"[PacketHandler] Exception details: {ex.StackTrace}");
            }
        }

        private void HandleRobotInfoPacket(RobotInfoPacket packet)
        {
            Debug.Log($"[PacketHandler] Handling RobotInfoPacket for BotId: {packet.BotId}, Name: {packet.Name}");
            RobotManager.Instance.UpdateRobotMetadata(packet.BotId, packet.PlayerId, packet.ClanId, packet.Name, packet.Skin, packet.Tail);
        }

        private void HandlePlayerInfoPacket(PlayerInfoPacket packet)
        {
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
            Debug.Log($"[PacketHandler] Handling MovementSpeedPacket with {packet.CooldownMap.Count} entries");
            MapManager.Instance.UpdateMovementSpeeds(packet);
        }

        private void HandleHBPacket(HBPacket hbPacket)
        {
            bool hasMapData = false;
            bool allMapDataProcessed = true;
            
            foreach (var p in hbPacket.Payload)
            {
                if (p is RobotPositionPacket robotPositionPacket)
                {
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
                else if (p is MapRegionPacket mapRegionPacket)
                {
                    _mapRegionPacketsReceived++;
                    hasMapData = true;
                    
                    Debug.Log($"[PacketHandler] Processing MapRegionPacket #{_mapRegionPacketsReceived}: X={mapRegionPacket.X}, Y={mapRegionPacket.Y}, Size={mapRegionPacket.Width+1}x{mapRegionPacket.Height+1}");
                    
                    // Ensure MapStorage is properly initialized before accessing cellLayer
                    if (MapStorage.Instance == null)
                    {
                        Debug.LogError("[PacketHandler] MapStorage not available for MapRegion processing");
                        allMapDataProcessed = false;
                        continue;
                    }

                    if (MapStorage.Instance.cellLayer == null)
                    {
                        Debug.LogError("[PacketHandler] MapStorage.cellLayer is null, cannot process map region data");
                        Debug.LogError("[PacketHandler] This suggests MapStorage.InitWorld() was never called");
                        Debug.LogError("[PacketHandler] Check if WorldInitPacket was properly processed");
                        allMapDataProcessed = false;
                        continue;
                    }

                    try
                    {
                        var layer = MapStorage.Instance.cellLayer;
                        int index = 0;
                        int totalCells = (mapRegionPacket.Width + 1) * (mapRegionPacket.Height + 1);
                        
                        Debug.Log($"[PacketHandler] Writing {totalCells} cells to MapStorage starting at ({mapRegionPacket.X}, {mapRegionPacket.Y})");
                        
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
                        
                        Debug.Log($"[PacketHandler] Successfully processed MapRegionPacket #{_mapRegionPacketsReceived}");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[PacketHandler] Error processing MapRegionPacket: {ex.Message}");
                        Debug.LogError($"[PacketHandler] Exception details: {ex.StackTrace}");
                        allMapDataProcessed = false;
                    }
                }
            }
            
            // Only trigger world data loaded event if we successfully processed all map data
            // AND we have received at least one MapRegion packet
            if (hasMapData && allMapDataProcessed)
            {
                Debug.Log("[PacketHandler] All map data processed successfully, triggering OnWorldDataLoaded event");
                MapManager.Instance.OnWorldDataLoaded?.Invoke();
            }
            else if (hasMapData && !allMapDataProcessed)
            {
                Debug.LogWarning("[PacketHandler] Map data received but processing failed, not triggering OnWorldDataLoaded");
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

        /// <summary>
        /// Get current packet processing statistics
        /// </summary>
        public string GetStatistics()
        {
            return $"[PacketHandler Stats] Total: {_packetCount}, WorldInit: {_worldInitPacketsReceived}, MapRegion: {_mapRegionPacketsReceived}, Initialized: {_isInitialized}";
        }

        /// <summary>
        /// Force re-initialization of the packet handler
        /// </summary>
        public void ForceReinitialize()
        {
            Debug.Log("[PacketHandler] Force reinitialization requested");
            
            // Clean up existing subscriptions
            if (ConnectionManager.Instance != null)
            {
                ConnectionManager.Instance.OnPacketReceived -= OnPacketReceived;
            }
            
            if (MapManager.Instance != null)
            {
                MapManager.Instance.OnWorldInitialized -= OnWorldInitialized;
                MapManager.Instance.OnWorldDataLoaded -= OnWorldDataLoaded;
            }
            
            // Reset state
            _isInitialized = false;
            _packetCount = 0;
            _worldInitPacketsReceived = 0;
            _mapRegionPacketsReceived = 0;
            
            // Re-initialize
            Start();
        }
    }
}

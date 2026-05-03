using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Information;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fodinae.Assets.Scripts.Game.Managers
{
    public class MapManager : MonoBehaviour
    {
        private static MapManager _instance;
        public static MapManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<MapManager>();
                    if (_instance == null)
                    {
                        var go = new GameObject("[MapManager]");
                        _instance = go.AddComponent<MapManager>();
                    }
                }
                return _instance;
            }
        }

    public Action OnWorldInitialized;
    public Action OnWorldDataLoaded;

    private CellConfigurationPacket[] cellConfigurations;
    private Dictionary<CellType, ushort> _cellMoveSpeeds = new();
    private string worldCodeName;
    private string worldDisplayName;
    private ushort width;
    private ushort height;
    public bool _isWorldInitialized = false;
    
    // Add public property for standalone mode support
    public bool IsStandaloneMode { get; set; } = false;

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

    public void LoadWorldInit(WorldInitPacket packet)
    {
        Debug.Log($"[MapManager] LoadWorldInit called: {packet.DisplayName} ({packet.CodeName}) [{packet.Width}x{packet.Height}]");
        
        // Validate packet data
        if (packet == null)
        {
            Debug.LogError("[MapManager] LoadWorldInit called with null packet");
            return;
        }
        
        if (string.IsNullOrEmpty(packet.CodeName))
        {
            Debug.LogError("[MapManager] LoadWorldInit called with null or empty world code name");
            return;
        }
        
        if (packet.Width <= 0 || packet.Height <= 0)
        {
            Debug.LogError($"[MapManager] LoadWorldInit called with invalid dimensions: {packet.Width}x{packet.Height}");
            return;
        }
        
        // Store world information
        worldCodeName = packet.CodeName;
        worldDisplayName = packet.DisplayName;
        width = packet.Width;
        height = packet.Height;
        cellConfigurations = packet.Cells;
        Debug.Log($"[MapManager] World initialized: {packet.DisplayName} ({packet.CodeName}) [{width}x{height}]");
        
        // CRITICAL: IMMEDIATE MapStorage initialization - this is essential for terrain rendering
        Debug.Log($"[MapManager] IMMEDIATELY initializing MapStorage with world '{packet.CodeName}' dimensions {width}x{height}");
        
        try
        {
            // Ensure MapStorage is properly initialized
            MapStorage.Instance.InitWorld(packet.CodeName, width, height);
            
            // CRITICAL: Verify that MapStorage was properly initialized
            if (!MapStorage.Instance.IsReady)
            {
                Debug.LogError($"[MapManager] CRITICAL: MapStorage failed to initialize for world {packet.CodeName}");
                Debug.LogError($"[MapManager] MapStorage state: IsReady={MapStorage.Instance.IsReady}, IsInitialized={MapStorage.Instance.IsInitialized()}");
                Debug.LogError($"[MapManager] MapStorage cellLayer: {(MapStorage.Instance.cellLayer != null ? "not null" : "NULL - this is the problem!")}");
                Debug.LogError($"[MapManager] MapStorage world name: {MapStorage.Instance.GetWorldCodeName()}");
                
                // Try emergency initialization with more detailed error handling
                Debug.LogWarning("[MapManager] Attempting emergency MapStorage initialization...");
                try
                {
                    MapStorage.Instance.Dispose();
                    MapStorage.Instance.InitWorld(packet.CodeName, width, height);
                    
                    if (MapStorage.Instance.IsReady)
                    {
                        Debug.Log("[MapManager] Emergency MapStorage initialization successful!");
                    }
                    else
                    {
                        Debug.LogError("[MapManager] Emergency MapStorage initialization FAILED - terrain rendering will not work");
                        Debug.LogError("[MapManager] This is a CRITICAL failure - terrain rendering system cannot function");
                        
                        // Try creating a test world as last resort
                        Debug.LogWarning("[MapManager] Creating test world as fallback...");
                        MapStorage.Instance.Dispose();
                        MapStorage.Instance.InitWorld("fallback_test_world", 64, 64);
                        
                        if (MapStorage.Instance.IsReady)
                        {
                            Debug.Log("[MapManager] Test world created successfully as fallback");
                            worldCodeName = "fallback_test_world";
                            width = 64;
                            height = 64;
                        }
                        else
                        {
                            Debug.LogError("[MapManager] Even test world creation failed - terrain rendering system is broken");
                        }
                    }
                }
                catch (System.Exception emergencyEx)
                {
                    Debug.LogError($"[MapManager] Emergency MapStorage initialization threw exception: {emergencyEx.Message}");
                    Debug.LogError($"[MapManager] Exception details: {emergencyEx.GetType().Name}");
                }
            }
            else
            {
                Debug.Log($"[MapManager] MapStorage initialized successfully for world '{packet.CodeName}'");
                Debug.Log($"[MapManager] WorldLayer created: {MapStorage.Instance.cellLayer.WidthChunks}x{MapStorage.Instance.cellLayer.HeightChunks} chunks");
                Debug.Log($"[MapManager] Chunk size: {MapStorage.Instance.cellLayer.ChunkSize}");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[MapManager] CRITICAL ERROR during MapStorage initialization: {ex.Message}");
            Debug.LogError($"[MapManager] Exception type: {ex.GetType().Name}");
            Debug.LogError($"[MapManager] Stack trace: {ex.StackTrace}");
            
            // Provide specific guidance based on exception type
            if (ex is System.IO.IOException)
            {
                Debug.LogError("[MapManager] This is likely a file I/O issue. Check disk space and file permissions.");
            }
            else if (ex is System.ArgumentException)
            {
                Debug.LogError("[MapManager] This is likely an invalid parameter issue. Check world dimensions.");
            }
            else if (ex is System.OutOfMemoryException)
            {
                Debug.LogError("[MapManager] This is a memory issue. The world may be too large for available memory.");
            }
        }
        
        _isWorldInitialized = true;
        Debug.Log($"[MapManager] Triggering OnWorldInitialized event");
        OnWorldInitialized?.Invoke();
        
        // CRITICAL: Only trigger OnWorldDataLoaded if MapStorage is actually ready
        // This is the key fix - terrain rendering depends on this event being triggered correctly
        if (MapStorage.Instance.IsReady)
        {
            Debug.Log($"[MapManager] MapStorage is ready, triggering OnWorldDataLoaded event");
            OnWorldDataLoaded?.Invoke();
            Debug.Log("[MapManager] World data loaded event triggered successfully");
        }
        else
        {
            Debug.LogError("[MapManager] CRITICAL: MapStorage not ready, skipping OnWorldDataLoaded event");
            Debug.LogError($"[MapManager] This means terrain rendering will fail - MapStorage must be ready!");
            Debug.LogError($"[MapManager] MapStorage details: IsReady={MapStorage.Instance.IsReady}, IsInitialized={MapStorage.Instance.IsInitialized()}, cellLayer={(MapStorage.Instance.cellLayer != null ? "not null" : "NULL")}");
            Debug.LogError("[MapManager] Terrain rendering will remain in 'WaitingForWorldInit' state until this is resolved");
            
            // Try to force the event anyway after a delay to see if MapStorage becomes ready
            Debug.LogWarning("[MapManager] Scheduling delayed OnWorldDataLoaded trigger attempt...");
            StartCoroutine(DelayedWorldDataLoadedTrigger());
        }
    }

    /// <summary>
    /// Delayed attempt to trigger OnWorldDataLoaded event
    /// </summary>
    private System.Collections.IEnumerator DelayedWorldDataLoadedTrigger()
    {
        yield return new WaitForSeconds(2.0f);
        
        if (MapStorage.Instance.IsReady)
        {
            Debug.Log("[MapManager] MapStorage became ready after delay, triggering OnWorldDataLoaded event");
            OnWorldDataLoaded?.Invoke();
        }
        else
        {
            Debug.LogError("[MapManager] MapStorage still not ready after delay - terrain rendering will remain broken");
        }
    }

        public void UpdateMovementSpeeds(MovementSpeedPacket packet)
        {
            foreach (var entry in packet.CooldownMap)
            {
                _cellMoveSpeeds[entry.Key] = entry.Value;
            }
        }

        public float GetMoveCooldown(CellType cellType)
        {
            if (_cellMoveSpeeds.TryGetValue(cellType, out ushort speed) && speed > 0)
            {
                return speed / 1000f;
            }
            return 0f;
        }

        public CellConfigurationPacket GetCellConfig(CellType cellType)
        {
            if (cellConfigurations == null || (int)cellType >= cellConfigurations.Length)
            {
                Debug.LogError($"Cell configuration for type {cellType} not found.");
                return default;
            }
            return cellConfigurations[(int)cellType];
        }

        /// <summary>
        /// Get the minimap color for a cell type from server configuration
        /// </summary>
        /// <param name="cellType">The cell type</param>
        /// <returns>Unity Color converted from ARGB int, or transparent if not found</returns>
        public Color GetCellMinimapColor(CellType cellType)
        {
            var config = GetCellConfig(cellType);
            if (config.Color == 0)
            {
                // Return transparent color if no configuration found
                return new Color(0, 0, 0, 0);
            }

            // Convert ARGB int to Unity Color
            // ARGB format: AARRGGBB
            int argb = config.Color;
            byte a = (byte)((argb >> 24) & 0xFF);
            byte r = (byte)((argb >> 16) & 0xFF);
            byte g = (byte)((argb >> 8) & 0xFF);
            byte b = (byte)(argb & 0xFF);

            return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
        }

        /// <summary>
        /// Get the animation frame height in pixels for a cell type
        /// </summary>
        /// <param name="cellType">The cell type</param>
        /// <returns>Frame height in pixels (tile height * 16), or 0 if not animated</returns>
        public int GetAnimationFrameHeight(CellType cellType)
        {
            var config = GetCellConfig(cellType);

            // Frame height is defined in tiles, each tile is 16x16 pixels
            // FrameOffset > 0 indicates it's an animated or multi-frame texture
            return (int)config.FrameOffset * 16;
        }

        /// <summary>
        /// Get the animation speed for a cell type
        /// </summary>
        /// <param name="cellType">The cell type</param>
        /// <returns>Animation speed in frames per second</returns>
        public byte GetAnimationSpeed(CellType cellType)
        {
            var config = GetCellConfig(cellType);
            return config.AnimationSpeed;
        }

        /// <summary>
        /// Get the frame offset for a cell type
        /// </summary>
        /// <param name="cellType">The cell type</param>
        /// <returns>Frame offset</returns>
        public byte GetFrameOffset(CellType cellType)
        {
            var config = GetCellConfig(cellType);
            return config.FrameOffset;
        }

        /// <summary>
        /// Check if a cell type has animation
        /// </summary>
        /// <param name="cellType">The cell type</param>
        /// <returns>True if animated, false otherwise</returns>
        public bool HasAnimation(CellType cellType)
        {
            var config = GetCellConfig(cellType);
            return config.Animation != CellAnimationType.None;
        }

        /// <summary>
        /// Get world information
        /// </summary>
        public string WorldCodeName => worldCodeName;
        public string WorldDisplayName => worldDisplayName;
        public ushort WorldWidth => width;
        public ushort WorldHeight => height;
    }
}

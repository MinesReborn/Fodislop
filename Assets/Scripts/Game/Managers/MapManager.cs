using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Information;
using Fodinae.Scripts.World;
using Fodinae.Scripts.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fodinae.Scripts.Game.Managers
{
    public class MapManager : MonoBehaviour
    {
        private static MapManager _instance;
        private static bool _isQuitting = false;
        private Camera _mainCamera;

        public static MapManager InstanceIfExists => _instance;
        public static MapManager Instance
        {
            get
            {
                if (_isQuitting) return null;
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<MapManager>();
                    if (_instance == null && !_isQuitting)
                    {
                        var go = new GameObject("[MapManager]");
                        _instance = go.AddComponent<MapManager>();
                        
                        // System Grouping
                        var parent = GameObject.Find("[Systems]") ?? new GameObject("[Systems]");
                        DontDestroyOnLoad(parent);
                        go.transform.SetParent(parent.transform);
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Cached reference to the Main Camera.
        /// Faster than using Camera.main which performs a lookup.
        /// </summary>
        public Camera MainCamera
        {
            get
            {
                if (_mainCamera == null)
                {
                    _mainCamera = Camera.main;
                }
                return _mainCamera;
            }
        }

        public Action OnWorldInitialized;
        public Action OnWorldDataLoaded;

        private CellConfigurationPacket[] _cellConfigurations;
        private Dictionary<CellType, int> _cellToTileGroup = new();
        private Dictionary<CellType, ushort> _cellMoveSpeeds = new();
        private string _worldCodeName;
        private string _worldDisplayName;
        private ushort _width;
        private ushort _height;
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

            // Ensure parented if created in scene
            var parent = GameObject.Find("[Systems]") ?? new GameObject("[Systems]");
            DontDestroyOnLoad(parent);
            transform.SetParent(parent.transform);

            _isQuitting = false;
        }

        void OnDestroy()
        {
            if (_instance == this)
            {
                MapStorage.Instance?.Dispose();
            }
        }

        void OnApplicationQuit()
        {
            _isQuitting = true;
            MapStorage.Instance?.Dispose();
        }

        public void LoadWorldInit(WorldInitPacket packet)
        {
            Debug.Log($"[MapManager] LoadWorldInit called: {packet.DisplayName} ({packet.CodeName}) [{packet.Width}x{packet.Height}]");
            
            // Clear all packs when a new world is initialized
            PackManager.Instance.ClearAllPacks();

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
            _worldCodeName = packet.CodeName;
            _worldDisplayName = packet.DisplayName;
            _width = packet.Width;
            _height = packet.Height;
            _cellConfigurations = packet.Cells;

            _cellToTileGroup.Clear();
            if (packet.TileGroups != null)
            {
                for (int i = 0; i < packet.TileGroups.Length; i++)
                {
                    if (packet.TileGroups[i] == null) continue;
                    foreach (byte cellId in packet.TileGroups[i])
                    {
                        _cellToTileGroup[(CellType)cellId] = i;
                    }
                }
            }

            Debug.Log($"[MapManager] World initialized: {packet.DisplayName} ({packet.CodeName}) [{_width}x{_height}]");
            
            // CRITICAL: IMMEDIATE MapStorage initialization - this is essential for terrain rendering
            Debug.Log($"[MapManager] IMMEDIATELY initializing MapStorage with world '{packet.CodeName}' dimensions {_width}x{_height}");
            
            try
            {
                // Ensure MapStorage is properly initialized
                MapStorage.Instance.InitWorld(packet.CodeName, _width, _height);
                
                // CRITICAL: Verify that MapStorage was properly initialized
                if (!MapStorage.Instance.IsReady)
                {
                    Debug.LogError($"[MapManager] CRITICAL: MapStorage failed to initialize for world {packet.CodeName}");
                    Debug.LogError($"[MapManager] MapStorage state: IsReady={MapStorage.Instance.IsReady}, IsInitialized={MapStorage.Instance.IsInitialized()}");
                    Debug.LogError($"[MapManager] MapStorage CellLayer: {(MapStorage.Instance.CellLayer != null ? "not null" : "NULL - this is the problem!")}");
                    Debug.LogError($"[MapManager] MapStorage world name: {MapStorage.Instance.GetWorldCodeName()}");
                    
                    // Try emergency initialization with more detailed error handling
                    Debug.LogWarning("[MapManager] Attempting emergency MapStorage initialization...");
                    try
                    {
                        MapStorage.Instance.Dispose();
                        MapStorage.Instance.InitWorld(packet.CodeName, _width, _height);
                        
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
                                _worldCodeName = "fallback_test_world";
                                _width = 64;
                                _height = 64;
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
                    Debug.Log($"[MapManager] WorldLayer created: {MapStorage.Instance.CellLayer.WidthChunks}x{MapStorage.Instance.CellLayer.HeightChunks} chunks");
                    Debug.Log($"[MapManager] Chunk size: {MapStorage.Instance.CellLayer.ChunkSize}");
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
            if (MapStorage.Instance.IsReady)
            {
                Debug.Log($"[MapManager] MapStorage is ready, triggering OnWorldDataLoaded event");
                OnWorldDataLoaded?.Invoke();
                Debug.Log("[MapManager] World data loaded event triggered successfully");
            }
            else
            {
                Debug.LogError("[MapManager] CRITICAL: MapStorage not ready, skipping OnWorldDataLoaded event");
                StartCoroutine(DelayedWorldDataLoadedTrigger());
            }
        }

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
            if (_cellConfigurations == null || (int)cellType < 0 || (int)cellType >= _cellConfigurations.Length)
            {
                return default;
            }
            return _cellConfigurations[(int)cellType];
        }

        public bool TryGetTileGroup(CellType cellType, out int groupId)
        {
            return _cellToTileGroup.TryGetValue(cellType, out groupId);
        }

        public Color GetCellMinimapColor(CellType cellType)
        {
            var config = GetCellConfig(cellType);
            if (config.Color == 0)
            {
                return new Color(0, 0, 0, 0);
            }

            int argb = config.Color;
            byte a = (byte)((argb >> 24) & 0xFF);
            byte r = (byte)((argb >> 16) & 0xFF);
            byte g = (byte)((argb >> 8) & 0xFF);
            byte b = (byte)(argb & 0xFF);

            return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
        }

        public int GetAnimationFrameHeight(CellType cellType)
        {
            var config = GetCellConfig(cellType);
            return (int)config.FrameOffset * RenderingConstants.CELL_SIZE;
        }

        public byte GetAnimationSpeed(CellType cellType)
        {
            var config = GetCellConfig(cellType);
            return config.AnimationSpeed;
        }

        public byte GetFrameOffset(CellType cellType)
        {
            var config = GetCellConfig(cellType);
            return config.FrameOffset;
        }

        public bool HasAnimation(CellType cellType)
        {
            var config = GetCellConfig(cellType);
            return config.Animation != CellAnimationType.None;
        }

        public string WorldCodeName => _worldCodeName;
        public string WorldDisplayName => _worldDisplayName;
        public ushort WorldWidth => _width;
        public ushort WorldHeight => _height;

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!_isWorldInitialized || _width == 0 || _height == 0) return;

            Gizmos.color = new Color(1, 1, 1, 0.5f);
            Vector3 worldCenter = new Vector3(_width * 0.5f, _height * 0.5f, 0);
            Vector3 worldSize = new Vector3(_width, _height, 0.1f);
            Gizmos.DrawWireCube(worldCenter, worldSize);
        }

        private void OnDrawGizmosSelected()
        {
            if (!_isWorldInitialized || _width == 0 || _height == 0) return;

            Vector3 worldCenter = new Vector3(_width * 0.5f, _height * 0.5f, 0);

            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(Vector3.zero, 0.5f);
            Utils.FodislopGizmos.DrawLabel(Vector3.zero, "World Origin (0,0)", Color.magenta);

            if (MapStorage.Instance.IsReady && MapStorage.Instance.CellLayer != null)
            {
                var layer = MapStorage.Instance.CellLayer;
                int chunkSize = layer.ChunkSize;
                int hChunks = layer.HeightChunks;
                var loaded = layer.GetLoadedChunkIndices();

                foreach (int index in loaded)
                {
                    int cx = index / hChunks;
                    int cy = index % hChunks;
                    
                    float unityY = CoordinateUtils.ServerToUnityY((ushort)((cy + 1) * chunkSize), _height) - chunkSize * 0.5f;
                    Vector3 chunkPos = new Vector3(cx * chunkSize + chunkSize * 0.5f, unityY + chunkSize * 0.5f, 0);
                    
                    Utils.FodislopGizmos.DrawSolidRect(chunkPos, new Vector2(chunkSize - 0.2f, chunkSize - 0.2f), 
                        new Color(0, 1, 0, 0.02f), new Color(0, 1, 0, 0.1f));
                }

                Vector3 labelPos = worldCenter + Vector3.down * (_height * 0.5f + 2f);
                string stats = $"Chunks: {layer.GetLoadedCount()}/{layer.MaxChunksInMemory} loaded | {layer.GetDirtyCount()} dirty";
                Utils.FodislopGizmos.DrawLabel(labelPos, stats, Color.green);

                Camera cam = MainCamera;
                if (cam != null && Application.isPlaying)
                {
                    Vector3 camPos = cam.transform.position;
                    int range = 10;
                    int startX = Mathf.FloorToInt(camPos.x) - range;
                    int startY = Mathf.FloorToInt(camPos.y) - range;

                    for (int x = startX; x < startX + range * 2; x++)
                    {
                        for (int y = startY; y < startY + range * 2; y++)
                        {
                            if (x < 0 || x >= _width || y < 0 || y >= _height) continue;
                            
                            ushort serverX = (ushort)x;
                            ushort serverY = CoordinateUtils.UnityToServerY(y, _height);
                            var cellType = MapStorage.Instance.GetCell(serverX, serverY);
                            var config = GetCellConfig(cellType);
                            
                            if (config.Properties != 0)
                            {
                                bool isPassable = ((CellConfigProperties)config.Properties).HasFlag(CellConfigProperties.Passable);
                                if (!isPassable)
                                {
                                    Gizmos.color = new Color(1, 0, 0, 0.15f);
                                    Gizmos.DrawCube(new Vector3(x + 0.5f, y + 0.5f, 0), new Vector3(0.9f, 0.9f, 0.1f));
                                }
                            }
                        }
                    }
                }
            }
        }
#endif
    }
}

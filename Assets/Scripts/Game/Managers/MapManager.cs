using System;
using System.Collections.Generic;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.World;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Information;
using UnityEngine;

namespace Fodinae.Scripts.Game.Managers
{
    [ExecuteAlways]
    public class MapManager : SingletonMonoBehaviour<MapManager>, IMapDataProvider
    {
        private Camera _mainCamera;

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

        public Action OnWorldInitialized { get; set; }
        public Action OnWorldDataLoaded { get; set; }

        private static readonly CellConfigurationPacket _fallbackConfig = new CellConfigurationPacket
        {
            Animation = CellAnimationType.None,
            AnimationSpeed = 0,
            Color = 0,
            FrameOffset = 0,
            Properties = CellConfigProperties.None,
            Distortion = (CellDistortionType)0,
            ReliefGroup = 0,
        };

        private CellConfigurationPacket[] _cellConfigurations;
        private Dictionary<CellType, int> _cellToTileGroup = new();
        private Dictionary<CellType, ushort> _cellMoveSpeeds = new();
        private string _worldCodeName;
        private string _worldDisplayName;
        private ushort _width;
        private ushort _height;

        public bool IsWorldInitialized { get; private set; } = false;

        public bool IsStandaloneMode { get; set; } = false;

        protected override void OnAwake()
        {
            ServiceLocator.Register<IMapDataProvider>(this);
#if UNITY_EDITOR
            if (!Application.isPlaying && !IsWorldInitialized)
            {
                _width = 128;
                _height = 128;
                _worldCodeName = "EditorPreview";
                _worldDisplayName = "Editor Preview";
            }
#endif
        }

        protected override void OnDestroyed()
        {
            MapStorage.InstanceIfExists?.Dispose();
        }

        protected override void OnApplicationQuitting()
        {
            ServiceLocator.Resolve<IWorldDataStorage>()?.Dispose();
        }

        public void LoadWorldInit(WorldInitPacket packet)
        {
            Debug.Log($"[MapManager] LoadWorldInit called: {packet.DisplayName} ({packet.CodeName}) [{packet.Width}x{packet.Height}]");

            PackManager.Instance?.ClearAllPacks();
            RobotManager.InstanceIfExists?.ClearAllRobots();
            ServerAudioEventManager.InstanceIfExists?.ClearAllEffects();

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
                    if (packet.TileGroups[i] == null)
                    {
                        continue;
                    }

                    foreach (byte cellId in packet.TileGroups[i])
                    {
                        _cellToTileGroup[(CellType)cellId] = i;
                    }
                }
            }

            Debug.Log($"[MapManager] World initialized: {packet.DisplayName} ({packet.CodeName}) [{_width}x{_height}]");
            Debug.Log($"[MapManager] IMMEDIATELY initializing MapStorage with world '{packet.CodeName}' dimensions {_width}x{_height}");

            try
            {
                ServiceLocator.Resolve<IWorldDataStorage>().InitWorld(packet.CodeName, _width, _height);

                if (!ServiceLocator.Resolve<IWorldDataStorage>().IsReady)
                {
                    Debug.LogError($"[MapManager] CRITICAL: MapStorage failed to initialize for world {packet.CodeName}");
                    Debug.LogError($"[MapManager] MapStorage state: IsReady={ServiceLocator.Resolve<IWorldDataStorage>().IsReady}, IsInitialized={ServiceLocator.Resolve<IWorldDataStorage>().IsInitialized()}");
                    Debug.LogError($"[MapManager] MapStorage CellLayer: {(ServiceLocator.Resolve<IWorldDataStorage>().CellLayer != null ? "not null" : "NULL - this is the problem!")}");
                    Debug.LogError($"[MapManager] MapStorage world name: {ServiceLocator.Resolve<IWorldDataStorage>().GetWorldCodeName()}");

                    Debug.LogWarning("[MapManager] Attempting emergency MapStorage initialization...");
                    try
                    {
                        ServiceLocator.Resolve<IWorldDataStorage>().Dispose();
                        ServiceLocator.Resolve<IWorldDataStorage>().InitWorld(packet.CodeName, _width, _height);

                        if (ServiceLocator.Resolve<IWorldDataStorage>().IsReady)
                        {
                            Debug.Log("[MapManager] Emergency MapStorage initialization successful!");
                        }
                        else
                        {
                            Debug.LogError("[MapManager] Emergency MapStorage initialization FAILED - terrain rendering will not work");
                            Debug.LogError("[MapManager] This is a CRITICAL failure - terrain rendering system cannot function");

                            Debug.LogWarning("[MapManager] Creating test world as fallback...");
                            ServiceLocator.Resolve<IWorldDataStorage>().Dispose();
                            ServiceLocator.Resolve<IWorldDataStorage>().InitWorld("fallback_test_world", 64, 64);

                            if (ServiceLocator.Resolve<IWorldDataStorage>().IsReady)
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
                    Debug.Log($"[MapManager] WorldLayer created: {ServiceLocator.Resolve<IWorldDataStorage>().CellLayer.WidthChunks}x{ServiceLocator.Resolve<IWorldDataStorage>().CellLayer.HeightChunks} chunks");
                    Debug.Log($"[MapManager] Chunk size: {ServiceLocator.Resolve<IWorldDataStorage>().CellLayer.ChunkSize}");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MapManager] CRITICAL ERROR during MapStorage initialization: {ex.Message}");
                Debug.LogError($"[MapManager] Exception type: {ex.GetType().Name}");
                Debug.LogError($"[MapManager] Stack trace: {ex.StackTrace}");

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

            IsWorldInitialized = true;
            Debug.Log($"[MapManager] Triggering OnWorldInitialized event");
            OnWorldInitialized?.Invoke();

            if (ServiceLocator.Resolve<IWorldDataStorage>().IsReady)
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

            if (ServiceLocator.Resolve<IWorldDataStorage>().IsReady)
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

        public CellConfigurationPacket GetCellConfig(CellType type)
        {
            if (_cellConfigurations == null || (int)type < 0 || (int)type >= _cellConfigurations.Length)
            {
                return _fallbackConfig;
            }

            return _cellConfigurations[(int)type];
        }

        private static readonly System.Collections.Generic.HashSet<CellType> LooseRockTypes = new()
        {
            CellType.BlackBoulder1, CellType.BlackBoulder2, CellType.BlackBoulder3,
            CellType.MetalBoulder1, CellType.MetalBoulder2, CellType.MetalBoulder3,
            CellType.WhiteSand, CellType.DarkWhiteSand,
            CellType.RustySand, CellType.DarkRustySand,
            CellType.BlackSand, CellType.DarkBlackSand,
            CellType.BlueSand, CellType.DarkBlueSand,
            CellType.YellowSand, CellType.DarkYellowSand,
            CellType.DeepMagmaBoulder, CellType.MilitaryBlockSand,
            CellType.Lava, CellType.Boulder1, CellType.Boulder2, CellType.Boulder3,
            CellType.GrayAcid, CellType.PurpleAcid,
        };

        private static readonly System.Collections.Generic.HashSet<CellType> RoundableLooseTypes = new()
        {
            CellType.WhiteSand, CellType.DarkWhiteSand,
            CellType.RustySand, CellType.DarkRustySand,
            CellType.BlackSand, CellType.DarkBlackSand,
            CellType.BlueSand, CellType.DarkBlueSand,
            CellType.YellowSand, CellType.DarkYellowSand,
            CellType.MilitaryBlockSand,
            CellType.Lava,
            CellType.GrayAcid, CellType.PurpleAcid,
        };

        public static bool IsLooseRockType(CellType type) => LooseRockTypes.Contains(type);

        public static bool IsRoundableLoose(CellType type) => RoundableLooseTypes.Contains(type);

        public bool TryGetTileGroup(CellType type, out int groupId)
        {
            return _cellToTileGroup.TryGetValue(type, out groupId);
        }

        public Color GetCellMinimapColor(CellType type)
        {
            var config = GetCellConfig(type);
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
        public ushort WorldWidth => (_width > 0) ? _width : (ushort)128;
        public ushort WorldHeight => (_height > 0) ? _height : (ushort)128;

#if UNITY_EDITOR
        protected virtual void OnDrawGizmos()
        {
            if (_width == 0 || _height == 0)
            {
                return;
            }

            Gizmos.color = new Color(1, 1, 1, 0.3f);
            Vector3 worldCenter = new Vector3(_width * 0.5f, _height * 0.5f, 0);
            Vector3 worldSize = new Vector3(_width, _height, 0.1f);
            Gizmos.DrawWireCube(worldCenter, worldSize);
        }

        protected virtual void OnDrawGizmosSelected()
        {
            if (_width == 0 || _height == 0)
            {
                return;
            }

            Vector3 worldCenter = new Vector3(_width * 0.5f, _height * 0.5f, 0);

            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(Vector3.zero, 0.5f);
            Fodinae.Scripts.World.FodinaeGizmos.DrawLabel(Vector3.zero, "World Origin (0,0)", Color.magenta);

            if (ServiceLocator.Resolve<IWorldDataStorage>().IsReady && ServiceLocator.Resolve<IWorldDataStorage>().CellLayer != null)
            {
                var layer = ServiceLocator.Resolve<IWorldDataStorage>().CellLayer;
                int CHUNK_SIZE = layer.ChunkSize;
                var loaded = layer.GetLoadedChunkIndices();

                foreach (int index in loaded)
                {
                    int cy = index % layer.HeightChunks;
                    int cx = index / layer.HeightChunks;

                    float unityY = (cy * CHUNK_SIZE) + (CHUNK_SIZE * 0.5f);
                    Vector3 chunkPos = new Vector3((cx * CHUNK_SIZE) + (CHUNK_SIZE * 0.5f), unityY, 0);

                    Fodinae.Scripts.World.FodinaeGizmos.DrawSolidRect(chunkPos, new Vector2(CHUNK_SIZE - 0.2f, CHUNK_SIZE - 0.2f),
                        new Color(0, 1, 0, 0.02f), new Color(0, 1, 0, 0.1f));
                }

                Vector3 labelPos = worldCenter + (Vector3.down * ((WorldHeight * 0.5f) + 2f));
                string stats = $"Chunks: {layer.GetLoadedCount()}/{layer.MaxChunksInMemory} loaded | {layer.GetDirtyCount()} dirty";
                Fodinae.Scripts.World.FodinaeGizmos.DrawLabel(labelPos, stats, Color.green);

                Camera cam = MainCamera;
                if (cam != null && Application.isPlaying)
                {
                    Vector3 camPos = cam.transform.position;
                    const int range = GameConstants.Debug.COLLISION_DEBUG_RANGE;
                    int startX = Mathf.FloorToInt(camPos.x) - range;
                    int startY = Mathf.FloorToInt(camPos.y) - range;

                    for (int x = startX; x < startX + (range * 2); x++)
                    {
                        for (int y = startY; y < startY + (range * 2); y++)
                        {
                            int worldX = x;
                            int worldY = CoordinateUtils.UnityToServerY(y, WorldHeight);

                            var cellType = ServiceLocator.Resolve<IWorldDataStorage>().GetCell(worldX, worldY);
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

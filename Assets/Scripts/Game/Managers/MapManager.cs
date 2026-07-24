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
    [DefaultExecutionOrder(-10000)]
    public class MapManager : MonoBehaviour, IMapDataProvider
    {
        private static MapManager _instance;
        public static MapManager Instance => _instance;
        public static MapManager InstanceIfExists => _instance;

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

        protected void Awake()
        {
            _instance = this;
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

        private static IWorldDataStorage WorldStorage => MapStorage.Instance;

        protected void OnDestroy()
        {
            MapStorage.InstanceIfExists?.Dispose();
        }

        public void LoadWorldInit(WorldInitPacket packet)
        {
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
                Debug.LogError($"[MapManager] Invalid dimensions: {packet.Width}x{packet.Height}");
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

            Debug.Log($"[MapManager] World: {packet.DisplayName} ({packet.CodeName}) [{_width}x{_height}]");

            var storage = WorldStorage;
            if (storage == null)
            {
                Debug.LogError("[MapManager] WorldStorage is null — MapStorage.Instance is null");
                return;
            }

            storage.InitWorld(packet.CodeName, _width, _height);

            if (!storage.IsReady)
            {
                Debug.LogError($"[MapManager] MapStorage.InitWorld failed: IsReady=false, IsInitialized={storage.IsInitialized()}, CellLayer={(storage.CellLayer != null ? "ok" : "NULL")}");
                return;
            }

            Debug.Log($"[MapManager] WorldLayer: {storage.CellLayer.WidthChunks}x{storage.CellLayer.HeightChunks} chunks, size={storage.CellLayer.ChunkSize}");

            IsWorldInitialized = true;
            OnWorldInitialized?.Invoke();
            OnWorldDataLoaded?.Invoke();
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

        public int GetConfigLength()
        {
            if (_cellConfigurations == null)
            {
                Debug.LogWarning("[MapManager] GetConfigLength called but _cellConfigurations is null");
                return -1;
            }

            return _cellConfigurations.Length;
        }

        private static readonly HashSet<CellType> LooseRockTypes = new()
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

        private static readonly HashSet<CellType> RoundableLooseTypes = new()
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
                return Color.clear;
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

            var storage = WorldStorage;
            if (storage != null && storage.IsReady && storage.CellLayer != null)
            {
                var layer = storage.CellLayer;
                int chunkSize = layer.ChunkSize;
                var loaded = layer.GetLoadedChunkIndices();

                foreach (int index in loaded)
                {
                    int cy = index % layer.HeightChunks;
                    int cx = index / layer.HeightChunks;

                    float unityY = (cy * chunkSize) + (chunkSize * 0.5f);
                    Vector3 chunkPos = new Vector3((cx * chunkSize) + (chunkSize * 0.5f), unityY, 0);

                    Fodinae.Scripts.World.FodinaeGizmos.DrawSolidRect(chunkPos, new Vector2(chunkSize - 0.2f, chunkSize - 0.2f),
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

                            var cellType = storage.GetCell(worldX, worldY);
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

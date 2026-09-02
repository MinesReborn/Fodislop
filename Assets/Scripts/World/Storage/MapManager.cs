#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.World;
using Fodinae.World.Terrain;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Information;
using UnityEngine;
using VContainer;

namespace Fodinae.World
{
    [DefaultExecutionOrder(-10000)]
    public class MapManager : MonoBehaviour, IMapDataProvider
    {
        private Camera? _mainCamera;
        private IWorldDataStorage _worldStorage = null!;
        private IWorldPersistence _worldPersistence = null!;
        private bool _hasWorldStorage;

        [Inject]
        public void Construct(IWorldDataStorage worldStorage, IWorldPersistence worldPersistence)
        {
            _worldStorage = worldStorage;
            _worldPersistence = worldPersistence;
            _hasWorldStorage = true;
        }

        [Inject]
        private IGameplayCamera _gameplayCamera = null!;

        public Camera MainCamera
        {
            get
            {
                if (_mainCamera == null)
                {
                    _mainCamera = _gameplayCamera?.Camera;
                }

                return _mainCamera!;
            }
        }

        public Action? OnWorldInitialized { get; set; }
        public Action? OnWorldDataLoaded { get; set; }

        private CellConfigurationPacket[]? _cellConfigurations;
        private Dictionary<CellType, int> _cellToTileGroup = new();
        private Dictionary<CellType, ushort> _cellMoveSpeeds = new();
        private string _worldCodeName = string.Empty;
        private string _worldDisplayName = string.Empty;
        private ushort _width;
        private ushort _height;

        private float _nextMapFlushTime;
        private const float DurableMapFlushInterval = 5f;
        public bool IsWorldInitialized { get; private set; }

        public bool IsStandaloneMode { get; set; }

        public void ResetWorldState()
        {
            IsWorldInitialized = false;
            _cellConfigurations = null;
            _cellToTileGroup.Clear();
            _cellMoveSpeeds.Clear();
            _worldCodeName = string.Empty;
            _worldDisplayName = string.Empty;
            _width = 0;
            _height = 0;
        }

        public void InitializeEditorPreview(MapStorage storage)
        {
            if (Application.isPlaying)
            {
                throw new InvalidOperationException(
                    "[MapManager] Editor preview initialization is forbidden in Play Mode.");
            }

            _worldStorage = storage ?? throw new ArgumentNullException(nameof(storage));
            _worldPersistence = storage;
            _hasWorldStorage = true;
            IsStandaloneMode = true;
        }

        public IWorldDataStorage WorldStorage => _worldStorage;

        public async UniTask FlushForUnloadAsync()
        {
            if (!_hasWorldStorage || _worldStorage == null || !_worldStorage.IsInitialized())
            {
                return;
            }

            // Once the durable write begins it must run to completion even when
            // the scene transition is cancelled. Storage owns its I/O gate and
            // returns to the main thread before scene teardown continues.
            await _worldStorage.FlushAsync(durable: true);
        }

        protected void OnDestroy()
        {
            IsWorldInitialized = false;
            if (_hasWorldStorage && _worldStorage != null)
            {
                _worldStorage.Dispose();
            }

            _hasWorldStorage = false;
        }

        protected void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus && _hasWorldStorage && _worldStorage != null)
            {
                _worldStorage.Flush();
            }
        }

        protected void OnApplicationQuit()
        {
            if (_hasWorldStorage && _worldStorage != null)
            {
                _worldStorage.Flush();
            }
        }

        protected void OnLowMemory()
        {
            if (_hasWorldStorage && _worldStorage != null)
            {
                _worldStorage.Flush();
            }
        }

        protected void Update()
        {
            if (!IsWorldInitialized || Time.unscaledTime < _nextMapFlushTime)
            {
                return;
            }

            _nextMapFlushTime = Time.unscaledTime + DurableMapFlushInterval;
            if (_worldPersistence.HasDirtyChunks)
            {
                // Not durable: see MapStorage.Flush. The fsync belongs to
                // OnApplicationQuit/Pause/LowMemory above, which still pass the
                // default. Here it only bought a periodic main-thread stall.
                _worldPersistence.Flush(durable: false);
            }
        }

        public void LoadWorldInit(WorldInitPacket packet)
        {
            UnityEngine.Debug.Log($"[Probe] WorldInit {UnityEngine.Time.realtimeSinceStartup:F3}");
            IsWorldInitialized = false;
            if (packet == null)
            {
                throw new ArgumentNullException(nameof(packet), "WorldInitPacket is required.");
            }

            if (string.IsNullOrEmpty(packet.CodeName))
            {
                throw new InvalidDataException("WorldInitPacket.CodeName is required.");
            }

            if (packet.Width <= 0 || packet.Height <= 0)
            {
                throw new InvalidDataException(
                    $"WorldInitPacket dimensions are invalid: {packet.Width}x{packet.Height}.");
            }

            ValidateCellConfigurations(packet.Cells);

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
                throw new InvalidOperationException(
                    "WorldStorage is not registered; cannot initialize the world.");
            }


            try
            {
                storage.InitWorld(packet.CodeName, _width, _height);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"[MapManager] Failed to initialize world '{packet.CodeName}' " +
                    $"({_width}x{_height}) in storage.",
                    ex);
            }

            if (!storage.IsReady)
            {
                throw new InvalidDataException(
                    $"World storage initialization completed without readiness: " +
                    $"IsInitialized={storage.IsInitialized()}, CellLayer={(storage.CellLayer != null ? "ok" : "NULL")}.");
            }

            IsWorldInitialized = true;
            OnWorldInitialized?.Invoke();
            OnWorldDataLoaded?.Invoke();
            Debug.Assert(IsWorldInitialized, "[MapManager] IsWorldInitialized must be true at the end of LoadWorldInit");
        }

        private static void ValidateCellConfigurations(CellConfigurationPacket[]? configurations)
        {
            if (configurations == null || configurations.Length == 0)
            {
                throw new InvalidDataException(
                    "WorldInitPacket.Cells is missing or empty; terrain cannot be initialized.");
            }

            for (int index = 0; index < configurations.Length; index++)
            {
                CellConfigurationPacket configuration = configurations[index];
                if (configuration.Animation == CellAnimationType.None)
                {
                    continue;
                }

                if (configuration.AnimationSpeed == 0)
                {
                    throw new InvalidDataException(
                        $"WorldInitPacket.Cells[{index}] ({(CellType)index}) declares " +
                        "an animated texture with AnimationSpeed=0.");
                }
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
            if (!_cellMoveSpeeds.TryGetValue(cellType, out ushort speed))
            {
                throw new InvalidOperationException(
                    $"Movement cooldown for cell type '{cellType}' was not received from the server.");
            }

            if (speed == 0)
            {
                throw new InvalidDataException(
                    $"Movement cooldown for cell type '{cellType}' must be greater than zero.");
            }

            return speed / 1000f;
        }

        public CellConfigurationPacket GetCellConfig(CellType type)
        {
            if (_cellConfigurations == null)
            {
                throw new InvalidOperationException(
                    $"Cell configuration requested for '{type}' before WorldInitPacket was loaded.");
            }

            if ((int)type < 0 || (int)type >= _cellConfigurations.Length)
            {
                throw new InvalidOperationException(
                    $"Cell type '{type}' has no server configuration. Config count: {_cellConfigurations.Length}.");
            }

            return _cellConfigurations[(int)type];
        }

        public int GetConfigLength()
        {
            if (_cellConfigurations == null)
            {
                throw new InvalidOperationException(
                    "Cell configuration count requested before WorldInitPacket was loaded.");
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
            if (config.Color != 0)
            {
                int argb = config.Color;
                byte a = (byte)((argb >> 24) & 0xFF);
                if (a == 0)
                {
                    a = 255;
                }

                byte r = (byte)((argb >> 16) & 0xFF);
                byte g = (byte)((argb >> 8) & 0xFF);
                byte b = (byte)(argb & 0xFF);

                return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
            }

            return MapBlockColors.GetColor(type);
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
            Fodinae.World.FodinaeGizmos.DrawLabel(Vector3.zero, "World Origin (0,0)", Color.magenta);

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

                    Fodinae.World.FodinaeGizmos.DrawSolidRect(chunkPos, new Vector2(chunkSize - 0.2f, chunkSize - 0.2f),
                        new Color(0, 1, 0, 0.02f), new Color(0, 1, 0, 0.1f));
                }

                Vector3 labelPos = worldCenter + (Vector3.down * ((WorldHeight * 0.5f) + 2f));
                string stats = $"Chunks: {layer.GetLoadedCount()}/{layer.MaxChunksInMemory} loaded | {layer.GetDirtyCount()} dirty";
                Fodinae.World.FodinaeGizmos.DrawLabel(labelPos, stats, Color.green);

                Camera cam = MainCamera;
                if (cam != null && Application.isPlaying)
                {
                    Vector3 camPos = cam.transform.position;
                    const int range = ProjectRuntimeContracts.Debug.CollisionDebugRange;
                    int startX = Mathf.FloorToInt(camPos.x) - range;
                    int startY = Mathf.FloorToInt(camPos.y) - range;

                    for (int x = startX; x < startX + (range * 2); x++)
                    {
                        for (int y = startY; y < startY + (range * 2); y++)
                        {
                            if (y < 0 || y >= WorldHeight)
                            {
                                continue;
                            }

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

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.World;
using Kern.World.Terrain;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Information;
using UnityEngine;
using VContainer;

namespace Kern.World
{
    [DefaultExecutionOrder(-10000)]
    public class MapManager : MonoBehaviour, IMapDataProvider
    {
        private Camera? _mainCamera;
        private IWorldDataStorage _worldStorage = null!;
        private IWorldPersistence _worldPersistence = null!;
        private IAsyncOperationSupervisor? _operations;

        // NonSerialized: перекомпиляция во время Play сохраняет приватные поля
        // компонента, а внедрённые зависимости — нет. Флаги переживали
        // перезагрузку домена при пустом _worldPersistence, и Update бросал
        // NullReferenceException каждые пять секунд.
        [NonSerialized]
        private bool _hasWorldStorage;

        [Inject]
        public void Construct(
            IWorldDataStorage worldStorage,
            IWorldPersistence worldPersistence,
            IAsyncOperationSupervisor? operations = null)
        {
            _worldStorage = worldStorage;
            _worldPersistence = worldPersistence;
            _operations = operations;
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

        private readonly MapCellConfigCatalog _cellCatalog = new();
        private string _worldCodeName = string.Empty;
        private string _worldDisplayName = string.Empty;
        private ushort _width;
        private ushort _height;

        [NonSerialized]
        private float _nextMapFlushTime;
        private const float DurableMapFlushInterval = 5f;
        [field: NonSerialized]
        public bool IsWorldInitialized { get; private set; }

        public bool IsStandaloneMode { get; set; }

        public void ResetWorldState()
        {
            IsWorldInitialized = false;
            _cellCatalog.Reset();
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

        [NonSerialized]
        private bool _isFlushing;

        protected void Update()
        {
            if (!IsWorldInitialized || Time.unscaledTime < _nextMapFlushTime)
            {
                return;
            }

            _nextMapFlushTime = Time.unscaledTime + DurableMapFlushInterval;
            if (_worldPersistence.HasDirtyChunks && !_isFlushing)
            {
                if (_operations != null)
                {
                    _isFlushing = true;
                    _operations.Run("flush_dirty_chunks", async ct =>
                    {
                        try
                        {
                            await _worldPersistence.FlushAsync(durable: false, ct);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            Debug.LogError($"[MapManager] Async flush failed: {ex.Message}");
                        }
                        finally
                        {
                            _isFlushing = false;
                        }
                    });
                }
                else
                {
                    _worldPersistence.Flush(durable: false);
                }
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

            _cellCatalog.LoadConfigurations(packet.Cells, packet.TileGroups);

            _worldCodeName = packet.CodeName;
            _worldDisplayName = packet.DisplayName;
            _width = packet.Width;
            _height = packet.Height;

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

        public void UpdateMovementSpeeds(MovementSpeedPacket packet) => _cellCatalog.UpdateMovementSpeeds(packet);

        public float GetMoveCooldown(CellType cellType) => _cellCatalog.GetMoveCooldown(cellType);

        public CellConfigurationPacket GetCellConfig(CellType type) => _cellCatalog.GetCellConfig(type);

        public static bool IsRoundableLoose(CellType type) => MapCellConfigCatalog.IsRoundableLoose(type);

        public bool TryGetTileGroup(CellType type, out int groupID) => _cellCatalog.TryGetTileGroup(type, out groupID);

        public Color GetCellMinimapColor(CellType type) => _cellCatalog.GetCellMinimapColor(type);

        public int GetAnimationFrameHeight(CellType cellType) => _cellCatalog.GetAnimationFrameHeight(cellType);

        public byte GetAnimationSpeed(CellType cellType) => _cellCatalog.GetAnimationSpeed(cellType);

        public bool HasAnimation(CellType cellType) => _cellCatalog.HasAnimation(cellType);

        public string WorldCodeName => _worldCodeName;
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
            Kern.World.KernGizmos.DrawLabel(Vector3.zero, "World Origin (0,0)", Color.magenta);

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

                    Kern.World.KernGizmos.DrawSolidRect(chunkPos, new Vector2(chunkSize - 0.2f, chunkSize - 0.2f),
                        new Color(0, 1, 0, 0.02f), new Color(0, 1, 0, 0.1f));
                }

                Vector3 labelPos = worldCenter + (Vector3.down * ((WorldHeight * 0.5f) + 2f));
                string stats = $"Chunks: {layer.GetLoadedCount()}/{layer.MaxChunksInMemory} loaded | {layer.GetDirtyCount()} dirty";
                Kern.World.KernGizmos.DrawLabel(labelPos, stats, Color.green);

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
                            if (cellType == CellType.Unloaded)
                            {
                                continue;
                            }

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

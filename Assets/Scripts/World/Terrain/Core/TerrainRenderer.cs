#nullable enable

using Kern.Core.Interfaces.Diagnostics;
using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.World.Lighting;
using Kern.World.Lighting.Quality;
using Kern.World.Streaming;
using Kern.World.Terrain.Background;
using MinesServer.Data;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using VContainer;

namespace Kern.World.Terrain
{
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [DefaultExecutionOrder(100)]
    public class TerrainRenderer : MonoBehaviour, ICachedCellDataProvider
    {
        private const int PresentationMarginCells = 4;

        [Header("Configuration")]
        [SerializeField]
        private float _cellSize = ProjectRuntimeContracts.World.CellSize;
        [SerializeField]
        private Shader? _terrainShader;
        [SerializeField]
        private string _sortingLayerName = "Default";
        [SerializeField]
        private int _sortingOrder = ProjectRuntimeContracts.RequiredLayers.TerrainSortingOrder;
        [SerializeField]
        private int _doorOverlaySortingOrder = 500;
        [SerializeField]
        private int _viewportPadding = 2;

        private MeshFilter? _meshFilter;
        private MeshRenderer? _meshRenderer;

        [Inject]
        private IWorldDataStorage _storage = null!;

        [Inject]
        private IConnectionService _connectionService = null!;

        [Inject]
        private MapManager _mapManager = null!;

        [Inject]
        private ITextureService _textureService = null!;

        [Inject]
        private IClientConfigManager _clientConfigManager = null!;
        [Inject]
        private IFrameTelemetry _telemetry = null!;
        [Inject]
        private IRuntimeDebugSettings _debugSettings = null!;
        [Inject]
        private LightingEngine _lightingEngine = null!;
        [Inject]
        private ILocalPlayerState _localPlayer = null!;
        [Inject]
        private IGameplayCamera _gameplayCamera = null!;
        [Inject]
        private ISceneObjectFactory _sceneObjects = null!;

        private Camera? _mainCamera;

        private readonly TerrainCellCache _cellCache = new();
        private readonly TerrainPrecalculator _precalc = new();
        private readonly TerrainCellBuilder _cellBuilder = new();
        private readonly BackgroundFloodFill _backgroundFloodFill = new();
        private readonly TerrainViewportCalculator _viewportCalculator = new();
        private readonly TerrainMeshManager _meshManager = new();
        private readonly TerrainMaterialManager _materialManager = new();
        private readonly TerrainDoorOverlayRenderer _doorOverlayRenderer = new();
        private readonly TerrainCellIDMesh _cellIDMesh = new();

        // Экран рисует только видимое окно с полем: запас сетки нужен
        // освещению и сдвигу, но не кадру.
        private readonly TerrainCellIDMesh _visibleIDMesh = new();
        private Vector4 _viewOffset;
        private int _visibleWidth;
        private int _visibleHeight;
        private int _visibleGridWidth;
        private int _visibleGridHeight;
        private readonly List<TerrainVertex> _doorOverlayVertices = [];
        private bool _cellTexturesDirty = true;
        private bool _incrementalGridOffsetsPending;
        private int _gridOffsetDeltaX;
        private int _gridOffsetDeltaY;
        private bool _cellsCommitted;
        private RectInt _lightingViewport;
        private List<int>[] _doorOverlaySubMeshIndices = Array.Empty<List<int>>();

        private Vector2Int _lastGridPos = new Vector2Int(int.MinValue, int.MinValue);
        private int _meshWidth;
        private int _meshHeight;
        private bool _isInitialized = false;
        private bool _hasMissingTextures;
        private bool _needsRefresh = false;
        private bool _wasCpuMeshRebuildBypassed;
        private readonly HashSet<CellType> _pendingTextureCellTypes = [];
        private readonly DirtyRectSet _dirtyRects = new();
        private bool _useColorLod = false;
        private bool _fatalBuildError;
        private IWorldLayer<CellType>? _subscribedCellLayer;
        private ITextureService? _subscribedTextureService;
        private MapManager? _subscribedMapManager;
        private IWorldDataStorage? _subscribedStorage;

        private static readonly ProfilerMarker _CacheMarker = new("Kern.Terrain.Cache");
        private static readonly ProfilerMarker _PrecalculateMarker = new("Kern.Terrain.Precalculate");
        private static readonly ProfilerMarker _FloodFillMarker = new("Kern.World.Terrain.BackgroundFloodFill");
        private static readonly ProfilerMarker _MeshBuildMarker = new("Kern.Terrain.MeshBuild");
        private static readonly ProfilerMarker _MeshUploadMarker = new("Kern.Terrain.MeshUpload");
        private static readonly ProfilerMarker _TerrainLateUpdateMarker =
            new("Kern.Terrain.LateUpdate.CPU");

        private static readonly AllocationLedger.Entry _AllocationEntry =
            AllocationLedger.Register("Террейн — LateUpdate");
        private ulong _terrainContentRevision = 1;

        // Причины пересборки меша — чтобы пик «сборка меша 125 мс» можно было
        // приписать событию, а не гадать.
        private static readonly RebuildLedger.Entry _RebuildResize = RebuildLedger.Register("Террейн · полная: смена размера сетки");
        private static readonly RebuildLedger.Entry _RebuildGridMove = RebuildLedger.Register("Террейн · полная: сдвиг сетки");
        private static readonly RebuildLedger.Entry _RebuildRefresh = RebuildLedger.Register("Террейн · полная: флаг обновления");
        private static readonly RebuildLedger.Entry _RebuildPatch = RebuildLedger.Register("Террейн · частичная: изменённые клетки");


        public CachedCellInfo GetCell(int x, int y)
        {
            var c = _cellCache.GetCellData(x, y);
            return new CachedCellInfo { Type = c.Type, Properties = c.Properties };
        }

        public bool BypassCpuMeshRebuild
        {
            get => _debugSettings.BypassCpuMeshRebuild;
            set => _debugSettings.BypassCpuMeshRebuild = value;
        }

        public bool BypassTerrainDraw
        {
            get => _debugSettings.BypassTerrainDraw;
            set => _debugSettings.BypassTerrainDraw = value;
        }

        public ulong TerrainContentRevision => _terrainContentRevision;

        public bool IsReadyForGameplay =>
            _isInitialized &&
            _cellIDMesh.Mesh != null &&
            _cellsCommitted &&
            _materialManager.Materials.Length > 0 &&
            _pendingTextureCellTypes.Count == 0;

        public void ApplyClientConfig()
        {
            IClientConfigManager clientConfigManager = _clientConfigManager ??
                throw new InvalidOperationException(
                    "TerrainRenderer requires IClientConfigManager injection.");
            ClientConfig config = clientConfigManager.Config ??
                throw new InvalidOperationException(
                    "TerrainRenderer requires an initialized ClientConfig.");

            bool enableDistortion = config.Terrain.EnableDistortion;
            if (_precalc.EnableDistortion != enableDistortion)
            {
                _precalc.EnableDistortion = enableDistortion;
                _needsRefresh = true;
            }

            _materialManager.ApplyClientConfig(config);
            _terrainContentRevision++;
            Debug.Log($"[TerrainRenderer] ApplyClientConfig: distortion={enableDistortion}");
        }

        private void HandleCellChanged(int serverX, int serverY)
        {
            HandleRegionChanged(serverX, serverY, 1, 1);
        }

        private void HandleRegionChanged(
            int serverX,
            int serverY,
            int width,
            int height)
        {
            if (_mapManager == null || _lastGridPos.x == int.MinValue)
            {
                _needsRefresh = true;
                return;
            }

            int lastServerY = serverY + Mathf.Max(0, height - 1);
            int firstUnityY = Mathf.FloorToInt(
                CoordinateUtils.ServerToUnityY(serverY, _mapManager.WorldHeight));
            int lastUnityY = Mathf.FloorToInt(
                CoordinateUtils.ServerToUnityY(lastServerY, _mapManager.WorldHeight));
            int minimumUnityY = Mathf.Min(firstUnityY, lastUnityY);
            int maximumUnityY = Mathf.Max(firstUnityY, lastUnityY);
            bool affectsCachedTerrain =
                serverX + width - 1 >= _lastGridPos.x - 1 &&
                serverX <= _lastGridPos.x + _meshWidth &&
                maximumUnityY >= _lastGridPos.y - 1 &&
                minimumUnityY <= _lastGridPos.y + _meshHeight;
            if (!affectsCachedTerrain)
            {
                return;
            }

            RectInt changedRegion = new(
                serverX,
                minimumUnityY,
                width,
                maximumUnityY + 1 - minimumUnityY);
            _lightingEngine?.InvalidateRegion(
                changedRegion.x,
                changedRegion.y,
                changedRegion.width,
                changedRegion.height);

            _dirtyRects.Add(
                changedRegion,
                new RectInt(_lastGridPos.x, _lastGridPos.y, _meshWidth, _meshHeight));
        }

        protected void Awake()
        {
            _materialManager.TerrainShader = _terrainShader;
            _materialManager.InitializeShader();

            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();
            _mainCamera = _gameplayCamera?.Camera;

            if (_meshRenderer != null)
            {
                _meshRenderer.enabled = true;
                _meshRenderer.sortingLayerName = _sortingLayerName;
                _meshRenderer.sortingOrder = _sortingOrder;
            }
        }

        protected void Start()
        {
            _mainCamera = _gameplayCamera?.Camera;
        }

        public void InitializeEditorPreview(IWorldDataStorage storage, MapManager mapManager, ITextureService textureService)
        {
            _storage = storage;
            _mapManager = mapManager;
            _textureService = textureService;
            _meshFilter ??= GetComponent<MeshFilter>();
            _meshRenderer ??= GetComponent<MeshRenderer>();
            if (_mainCamera == null)
            {
                _mainCamera = _gameplayCamera?.Camera;
            }

            _materialManager.TerrainShader = _terrainShader;
            _materialManager.InitializeShader();
            if (_meshRenderer != null)
            {
                _meshRenderer.enabled = true;
                _meshRenderer.sortingLayerName = _sortingLayerName;
                _meshRenderer.sortingOrder = _sortingOrder;
            }

            EnsureSubscriptions();
            _needsRefresh = true;
        }

        public void EnsureSubscriptions()
        {
            SubscribeToCellLayer();
            if (_subscribedStorage != null)
            {
                _subscribedStorage.CellChanged -= HandleCellChanged;
                _subscribedStorage.RegionChanged -= HandleRegionChanged;
            }

            _subscribedStorage = _storage;
            if (_subscribedStorage != null)
            {
                _subscribedStorage.CellChanged += HandleCellChanged;
                _subscribedStorage.RegionChanged += HandleRegionChanged;
            }

            if (_subscribedTextureService != null)
            {
                _subscribedTextureService.OnTextureLoaded -= OnTextureLoaded;
            }

            _subscribedTextureService = _textureService;
            if (_subscribedTextureService != null)
            {
                _subscribedTextureService.OnTextureLoaded += OnTextureLoaded;
            }

            if (_subscribedMapManager != null)
            {
                _subscribedMapManager.OnWorldDataLoaded -= OnWorldDataLoaded;
            }

            _subscribedMapManager = _mapManager;
            if (_subscribedMapManager != null)
            {
                _subscribedMapManager.OnWorldDataLoaded += OnWorldDataLoaded;
            }
        }

        protected void OnDestroy()
        {

            if (_subscribedStorage != null)
            {
                _subscribedStorage.CellChanged -= HandleCellChanged;
                _subscribedStorage.RegionChanged -= HandleRegionChanged;
                _subscribedStorage = null;
            }

            if (_subscribedTextureService != null)
            {
                _subscribedTextureService.OnTextureLoaded -= OnTextureLoaded;
                _subscribedTextureService = null;
            }

            if (_subscribedMapManager != null)
            {
                _subscribedMapManager.OnWorldDataLoaded -= OnWorldDataLoaded;
                _subscribedMapManager = null;
            }

            if (_subscribedCellLayer != null)
            {
                _subscribedCellLayer.ChunkLoaded -= OnCellLayerChunkLoaded;
                _subscribedCellLayer = null;
            }

            _cellIDMesh.Dispose();
            _visibleIDMesh.Dispose();
            _cellBuilder.Dispose();
            _doorOverlayRenderer.Dispose();
            _materialManager.CleanupMaterials();
        }

        private int _diagLogged;

        [System.Diagnostics.Conditional("KERN_TERRAIN_DIAG")]
        private void LogDiag(int bit, string message)
        {
            if ((_diagLogged & bit) != 0)
            {
                return;
            }

            _diagLogged |= bit;
            Debug.Log(message);
        }

        private void OnTextureLoaded(string filename, Texture2D texture)
        {
            if ((_diagLogged & (1 << 9)) == 0)
            {
                LogDiag(1 << 9, $"[TerrainDiag] first texture arrived: {filename}");
            }

            if (filename.StartsWith("Cells/", StringComparison.OrdinalIgnoreCase))
            {
                _materialManager.TerrainShader = _terrainShader;
                _materialManager.InitializeShader();
                int extensionIndex = filename.LastIndexOf('.');
                ReadOnlySpan<char> id = filename.AsSpan(
                    "Cells/".Length,
                    (extensionIndex >= 0 ? extensionIndex : filename.Length) - "Cells/".Length);
                if (int.TryParse(id, out int cellTypeID) &&
                    (uint)cellTypeID <= ushort.MaxValue)
                {
                    _pendingTextureCellTypes.Add((CellType)cellTypeID);
                }
            }
        }

        private void OnWorldDataLoaded()
        {
            SubscribeToCellLayer();
            _needsRefresh = true;
            _terrainContentRevision++;
            _lightingEngine?.InvalidateStaticCache();
        }

        private void SubscribeToCellLayer()
        {
            IWorldLayer<CellType>? cellLayer = _storage?.CellLayer;
            if (ReferenceEquals(_subscribedCellLayer, cellLayer))
            {
                return;
            }

            if (_subscribedCellLayer != null)
            {
                _subscribedCellLayer.ChunkLoaded -= OnCellLayerChunkLoaded;
            }

            _subscribedCellLayer = cellLayer;
            if (_subscribedCellLayer != null)
            {
                _subscribedCellLayer.ChunkLoaded += OnCellLayerChunkLoaded;
            }
        }

        private void OnCellLayerChunkLoaded(int serverX, int serverY, int width, int height)
        {
            _telemetry.TerrainChunkLoadCount++;
            HandleRegionChanged(serverX, serverY, width, height);
        }

        protected void LateUpdate()
        {
            if (_fatalBuildError)
            {
                return;
            }

            using var terrainLateUpdateMarker = _TerrainLateUpdateMarker.Auto();

            using var allocationScope = AllocationLedger.Measure(_AllocationEntry);
            if (_mapManager == null || _storage == null || !_storage.IsReady)
            {
                return;
            }

            if (_localPlayer is not { Current: { HasServerPosition: true } })
            {
                return;
            }

            if ((_diagLogged & (1 << 1)) == 0)
            {
                LogDiag(1 << 1, "[TerrainDiag] gate passed: storage ready");
            }

            if (!TryResolveCamera())
            {
                return;
            }

            LightingEngine? lightingEngine = ResolveLightingEngine();
            if (lightingEngine == null)
            {
                return;
            }

            UpdateViewportDimensions(
                lightingEngine,
                out int targetWidth,
                out int targetHeight,
                out int requestedWidth,
                out int requestedHeight,
                out int effectiveViewportPadding,
                out bool dimensionsChanged);

            ResolveGridPosition(
                targetWidth,
                targetHeight,
                requestedWidth,
                requestedHeight,
                effectiveViewportPadding,
                dimensionsChanged,
                out Vector2Int currentGridPos,
                out int viewportMinX,
                out int viewportMinY,
                out int viewportWidth,
                out int viewportHeight);
            StreamingPlan streamingPlan = _viewportCalculator.LastPlan;
            _telemetry.StreamingPlanKind = (int)streamingPlan.Kind;
            _telemetry.StreamingWindowOriginX = streamingPlan.Target.Origin.x;
            _telemetry.StreamingWindowOriginY = streamingPlan.Target.Origin.y;
            _telemetry.StreamingWindowWidth = streamingPlan.Target.Size.x;
            _telemetry.StreamingWindowHeight = streamingPlan.Target.Size.y;
            _telemetry.StreamingDeltaX = streamingPlan.Delta.x;
            _telemetry.StreamingDeltaY = streamingPlan.Delta.y;

            _telemetry.ResetFrameTimers();

            StreamingWindow requestedTerrainWindow = new(
                currentGridPos,
                new Vector2Int(targetWidth, targetHeight));
            StreamingWindow committedTerrainWindow = new(
                _lastGridPos,
                new Vector2Int(_meshWidth, _meshHeight));
            RectInt cameraViewport = new(
                viewportMinX,
                viewportMinY,
                viewportWidth,
                viewportHeight);

            bool isRequestedResident = IsTerrainWindowResident(
                requestedTerrainWindow.Origin,
                requestedTerrainWindow.Size.x,
                requestedTerrainWindow.Size.y);

            TerrainFramePlan framePlan = _viewportCalculator.SelectFramePlan(
                requestedTerrainWindow,
                committedTerrainWindow,
                cameraViewport,
                _lightingViewport,
                isRequestedResident,
                dimensionsChanged,
                _cellsCommitted);

            if (!framePlan.ShouldProcess)
            {
                return;
            }

            if (_meshRenderer != null)
            {
                _meshRenderer.enabled = !BypassTerrainDraw;
            }

            if (_pendingTextureCellTypes.Count > 0 && !BypassCpuMeshRebuild && _cellsCommitted)
            {
                UpdateTextureCells(_lastGridPos.x, _lastGridPos.y);
                _pendingTextureCellTypes.Clear();
                _cellTexturesDirty = true;
            }

            ApplyTerrainDimensions(
                framePlan.ActiveWindow.Size.x,
                framePlan.ActiveWindow.Size.y,
                framePlan.DimensionsChanged);
            CoalesceOversizedDirtyRects();

            if (!RebuildOrPatchTerrain(framePlan.ActiveWindow.Origin, framePlan.DimensionsChanged))
            {
                return;
            }

            SyncCellTextures();
            UpdateVisibleWindow(
                framePlan.CameraViewport.x,
                framePlan.CameraViewport.y,
                framePlan.CameraViewport.width,
                framePlan.CameraViewport.height);

            // Terrain cache и lighting cache имеют разные окна жизни. Terrain
            // может сдвинуться на выровненную границу, пока камера всё ещё
            // находится внутри стабильного lighting region.
            PublishLightingUpdate(
                lightingEngine,
                framePlan.LightingViewport.x,
                framePlan.LightingViewport.y,
                framePlan.LightingViewport.width,
                framePlan.LightingViewport.height);
            _lightingViewport = framePlan.LightingViewport;
            lightingEngine.CaptureBudgetViolationIfNeeded();
        }

        private bool TryResolveCamera()
        {
            Camera? resolvedCam = _gameplayCamera?.Camera;
            if (resolvedCam != null)
            {
                _mainCamera = resolvedCam;
            }

            if (_mainCamera == null)
            {
                LogDiag(1 << 2, "[TerrainDiag] camera NULL");
                return false;
            }

            if ((_diagLogged & (1 << 3)) == 0)
            {
                LogDiag(1 << 3, $"[TerrainDiag] camera ok: {_mainCamera.name} at {_mainCamera.transform.position}");
            }

            return true;
        }

        private LightingEngine? ResolveLightingEngine()
        {
            LightingEngine? lightingEngine = _lightingEngine;
            if (lightingEngine == null)
            {
                if (!Application.isPlaying)
                {
                    return null;
                }

                throw new InvalidOperationException(
                    "LightingEngine was not initialized by GameLifetimeScope.");
            }

            return lightingEngine;
        }

        private void UpdateViewportDimensions(
            LightingEngine lightingEngine,
            out int targetWidth,
            out int targetHeight,
            out int requestedWidth,
            out int requestedHeight,
            out int effectiveViewportPadding,
            out bool dimensionsChanged)
        {
            _viewportCalculator.CalculateDimensions(
                _mainCamera!,
                _cellSize,
                _viewportPadding,
                lightingEngine.RequiredTerrainPadding,
                lightingEngine.StableRegionPaddingCells,
                _meshWidth,
                _meshHeight,
                _isInitialized,
                out targetWidth,
                out targetHeight,
                out effectiveViewportPadding,
                out requestedWidth,
                out requestedHeight,
                out dimensionsChanged);
        }

        private void ApplyTerrainDimensions(int targetWidth, int targetHeight, bool dimensionsChanged)
        {
            if (dimensionsChanged || !_isInitialized)
            {
                _meshWidth = targetWidth;
                _meshHeight = targetHeight;
                _isInitialized = true;
                _lastGridPos = new Vector2Int(int.MinValue, int.MinValue);
                _cellCache.EnsureCapacity(_meshWidth, _meshHeight);
                _precalc.EnsureCapacity(_meshWidth, _meshHeight);
                _cellBuilder.EnsureCapacity(_meshWidth, _meshHeight, _cellSize);
                _cellIDMesh.EnsureSize(_meshWidth, _meshHeight, _cellSize);
                _backgroundFloodFill.Allocate(_meshWidth, _meshHeight);

                _needsRefresh = true;
            }
        }

        private void ResolveGridPosition(
            int targetWidth,
            int targetHeight,
            int requestedWidth,
            int requestedHeight,
            int effectiveViewportPadding,
            bool dimensionsChanged,
            out Vector2Int currentGridPos,
            out int viewportMinX,
            out int viewportMinY,
            out int viewportWidth,
            out int viewportHeight)
        {
            currentGridPos = _viewportCalculator.ResolveGridPosition(
                _mainCamera!,
                _cellSize,
                targetWidth,
                targetHeight,
                requestedWidth,
                requestedHeight,
                effectiveViewportPadding,
                dimensionsChanged,
                _lastGridPos,
                out viewportMinX,
                out viewportMinY,
                out viewportWidth,
                out viewportHeight);
        }

        private bool IsTerrainWindowResident(Vector2Int gridPosition, int width, int height)
        {
            if (_storage?.CellLayer is not { } layer || _mapManager == null)
            {
                return false;
            }

            int worldWidth = _mapManager.WorldWidth;
            int worldHeight = _mapManager.WorldHeight;
            int minX = Mathf.Max(0, gridPosition.x - 1);
            int maxX = Mathf.Min(worldWidth - 1, gridPosition.x + width);
            int unityMinY = Mathf.Max(0, gridPosition.y - 1);
            int unityMaxY = Mathf.Min(worldHeight - 1, gridPosition.y + height);
            if (minX > maxX || unityMinY > unityMaxY)
            {
                return true;
            }

            int serverMinY = CoordinateUtils.UnityToServerY(unityMaxY, worldHeight);
            int serverMaxY = CoordinateUtils.UnityToServerY(unityMinY, worldHeight);

            int chunkSize = layer.ChunkSize;
            int firstChunkX = minX / chunkSize;
            int lastChunkX = maxX / chunkSize;
            int firstChunkY = serverMinY / chunkSize;
            int lastChunkY = serverMaxY / chunkSize;
            bool resident = true;
            bool missing = false;
            for (int chunkX = firstChunkX; chunkX <= lastChunkX; chunkX++)
            {
                for (int chunkY = firstChunkY; chunkY <= lastChunkY; chunkY++)
                {
                    // Request cold disk chunks as well: TryGetCell only probes
                    // RAM and can wait forever without initiating a load.
                    ChunkReadResult<CellType> result = layer.ReadChunk(
                        chunkY + chunkX * layer.HeightChunks, touchLru: true);
                    resident &= result.Status == ChunkReadStatus.Available;
                    missing |= result.Status == ChunkReadStatus.Missing;
                }
            }

            if (missing && _connectionService is IWorldRegionRequester requester)
            {
                requester.RequestWorldRegion(_storage.GetWorldCodeName(),
                    new RectInt(minX, serverMinY, maxX - minX + 1, serverMaxY - serverMinY + 1));
            }

            return resident;
        }

        private void CoalesceOversizedDirtyRects()
        {
            if (!_dirtyRects.IsEmpty && _meshWidth > 0 && _meshHeight > 0)
            {
                if (EstimateDirtyPatchWork() >= EstimateFullTerrainWork())
                {
                    _needsRefresh = true;
                    _dirtyRects.Clear();
                }
            }
        }

        private long EstimateFullTerrainWork()
        {
            long cells = (long)_meshWidth * _meshHeight;
            long cacheCells = (long)(_meshWidth + 2) * (_meshHeight + 2);
            long gridNodes = (long)(_meshWidth + 1) * (_meshHeight + 1);
            return cacheCells + gridNodes + cells + cells + cells;
        }

        private long EstimateDirtyPatchWork()
        {
            long work = 0;
            RectInt terrainBounds = new(_lastGridPos.x, _lastGridPos.y, _meshWidth, _meshHeight);
            for (int index = 0; index < _dirtyRects.Count; index++)
            {
                RectInt dirty = _dirtyRects[index];
                int startX = Mathf.Clamp(dirty.xMin - terrainBounds.xMin - 1, 0, _meshWidth);
                int endX = Mathf.Clamp(dirty.xMax - terrainBounds.xMin + 1, 0, _meshWidth);
                int startY = Mathf.Clamp(dirty.yMin - terrainBounds.yMin - 1, 0, _meshHeight);
                int endY = Mathf.Clamp(dirty.yMax - terrainBounds.yMin + 1, 0, _meshHeight);
                int width = Mathf.Max(0, endX - startX);
                int height = Mathf.Max(0, endY - startY);
                if (width == 0 || height == 0)
                {
                    continue;
                }

                long cells = (long)width * height;
                work += cells + ((long)(width + 1) * (height + 1)) + cells + cells;
            }

            return work;
        }

        private bool RebuildOrPatchTerrain(Vector2Int currentGridPos, bool dimensionsChanged)
        {
            if (BypassCpuMeshRebuild)
            {
                _wasCpuMeshRebuildBypassed = true;
                return true;
            }

            if (_wasCpuMeshRebuildBypassed)
            {
                _wasCpuMeshRebuildBypassed = false;
                _needsRefresh = true;
            }

            bool terrainWasRebuilt = currentGridPos != _lastGridPos || _needsRefresh || dimensionsChanged;
            if (terrainWasRebuilt)
            {
                bool patchedBeforeScroll = !_needsRefresh && !dimensionsChanged && !_dirtyRects.IsEmpty;
                if (patchedBeforeScroll)
                {
                    // Scroll preserves the overlap verbatim. Apply its pending
                    // edits in the OLD coordinate system before moving the rings.
                    if (!UpdateDirtyCells(_lastGridPos.x, _lastGridPos.y))
                    {
                        return false;
                    }
                }

                RebuildLedger.Count(
                    dimensionsChanged ? _RebuildResize
                    : currentGridPos != _lastGridPos ? _RebuildGridMove
                    : _RebuildRefresh);
                if (!UpdateVertexAttributes(currentGridPos.x, currentGridPos.y))
                {
                    return false;
                }

                transform.position = new Vector3(currentGridPos.x * _cellSize, currentGridPos.y * _cellSize, 0);
                _lastGridPos = currentGridPos;
                if (patchedBeforeScroll)
                {
                    // The incoming node band alone does not contain the patched
                    // overlap. Commit its updated distortion nodes as well.
                    _incrementalGridOffsetsPending = false;
                }
                _cellTexturesDirty = true;
                _dirtyRects.Clear();

                // Перемещение кольцевого terrain-кэша не меняет мировую
                // геометрию. Освещение привязано к стабильному world-region,
                // поэтому scroll не должен поднимать geometry revision и
                // запускать полный static solve.
            }
            else if (!_dirtyRects.IsEmpty)
            {
                RebuildLedger.Count(_RebuildPatch);
                if (!UpdateDirtyCells(currentGridPos.x, currentGridPos.y))
                {
                    return false;
                }

                _cellTexturesDirty = true;
                _dirtyRects.Clear();
            }

            return true;
        }

        private void PublishLightingUpdate(
            LightingEngine lightingEngine,
            int viewportMinX,
            int viewportMinY,
            int viewportWidth,
            int viewportHeight)
        {
            if (_mainCamera != null &&
                _mainCamera.orthographic &&
                lightingEngine.ActiveLightingQuality != LightingQualityMode.Off)
            {
                lightingEngine.UpdateLighting(
                    viewportMinX,
                    viewportMinY,
                    viewportWidth,
                    viewportHeight,
                    _mainCamera,
                    _storage,
                    _mapManager,
                    this);
                _materialManager.ValidateLightingBinding();
            }
        }

        public void RenderLightingMaterialFields(
            CommandBuffer commandBuffer,
            RenderTexture materialField,
            RenderTexture emissionField,
            Vector4 worldRect)
        {
            _meshManager.RenderLightingMaterialFields(
                commandBuffer,
                materialField,
                emissionField,
                worldRect,
                transform.localToWorldMatrix,
                _materialManager.CellMaterials,
                _cellIDMesh.Mesh,
                _viewOffset);
        }

        private void UpdateVisibleWindow(int viewportMinX, int viewportMinY, int viewportWidth, int viewportHeight)
        {
            if (!_cellsCommitted || _meshWidth <= 0 || _meshHeight <= 0 || _lastGridPos.x == int.MinValue)
            {
                return;
            }

            // Размер окна растёт по общей политике governor'а. Это сохраняет
            // стабильный mesh при движении камеры и не вводит отдельный
            // terrain-only порог.
            if (_visibleGridWidth != _meshWidth || _visibleGridHeight != _meshHeight)
            {
                _visibleGridWidth = _meshWidth;
                _visibleGridHeight = _meshHeight;
                _visibleWidth = 0;
                _visibleHeight = 0;
            }

            // The governor owns the persistent terrain window. The visible
            // mesh still needs a small physical margin: the camera samples
            // continuously between cell boundaries, while the ring-addressed
            // textures are integer indexed. This margin only extends the
            // presentation mesh; it never changes the streaming plan.
            StreamingPolicy policy = _viewportCalculator.Policy;
            int wantedWidth = policy.QuantizeDimension(
                viewportWidth + (PresentationMarginCells * 2));
            int wantedHeight = policy.QuantizeDimension(
                viewportHeight + (PresentationMarginCells * 2));
            _visibleWidth = Mathf.Clamp(Mathf.Max(_visibleWidth, wantedWidth), 1, _meshWidth);
            _visibleHeight = Mathf.Clamp(Mathf.Max(_visibleHeight, wantedHeight), 1, _meshHeight);
            int width = _visibleWidth;
            int height = _visibleHeight;
            int extraWidth = Mathf.Max(0, width - viewportWidth - (PresentationMarginCells * 2));
            int extraHeight = Mathf.Max(0, height - viewportHeight - (PresentationMarginCells * 2));
            int presentationMinX = viewportMinX - PresentationMarginCells - (extraWidth / 2);
            int presentationMinY = viewportMinY - PresentationMarginCells - (extraHeight / 2);
            int offsetX = Mathf.Clamp(presentationMinX - _lastGridPos.x, 0, _meshWidth - width);
            int offsetY = Mathf.Clamp(presentationMinY - _lastGridPos.y, 0, _meshHeight - height);

            _visibleIDMesh.EnsureSize(width, height, _cellSize, _meshWidth, _meshHeight);
            var offset = new Vector4(offsetX, offsetY, 0f, 0f);
            if (offset != _viewOffset)
            {
                _viewOffset = offset;
                Shader.SetGlobalVector(TerrainCellDataTextures.ViewOffsetID, offset);
            }

            if (_meshFilter != null && _meshFilter.sharedMesh != _visibleIDMesh.Mesh)
            {
                _meshFilter.sharedMesh = _visibleIDMesh.Mesh;
                Shader.SetGlobalVector(TerrainCellDataTextures.ViewOffsetID, _viewOffset);
            }
        }

        // Одна выгрузка текселей за кадр, после сборки или заплатки. Начало
        // окна публикуется вместе с ними: шейдер берёт по нему кольцевой адрес.
        private void SyncCellTextures()
        {
            if (!_cellTexturesDirty || _lastGridPos.x == int.MinValue || _cellIDMesh.Mesh == null)
            {
                return;
            }

            _cellTexturesDirty = false;
            long swUpload = System.Diagnostics.Stopwatch.GetTimestamp();
            using (_MeshUploadMarker.Auto())
            {
                _cellBuilder.Commit(
                    _precalc,
                    _lastGridPos.x,
                    _lastGridPos.y,
                    incrementalGridOffsets: _incrementalGridOffsetsPending,
                    dx: _gridOffsetDeltaX,
                    dy: _gridOffsetDeltaY);
            }

            _incrementalGridOffsetsPending = false;
            _gridOffsetDeltaX = 0;
            _gridOffsetDeltaY = 0;

            _telemetry.TerrainGpuUploadTimeMs = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - swUpload) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            _cellsCommitted = true;
        }

        private TerrainCellSources CreateCellSources(
            IReadOnlyList<IAtlasDescriptor> atlases,
            ITextureService textureService) =>
            new(
                _cellCache,
                _precalc,
                _backgroundFloodFill,
                _mapManager.WorldWidth,
                _mapManager.WorldHeight,
                atlases,
                _useColorLod,
                _mapManager,
                textureService);

        private bool UpdateVertexAttributes(int minX, int minY)
        {
            if ((_diagLogged & (1 << 4)) == 0)
            {
                LogDiag(1 << 4, $"[TerrainDiag] UpdateVertexAttributes min=({minX},{minY}) size={_meshWidth}x{_meshHeight}");
            }

            ITextureService textureService = _textureService ??
                throw new InvalidOperationException("TerrainRenderer requires ITextureService injection.");
            if (_mapManager == null || _storage == null)
            {
                if ((_diagLogged & (1 << 5)) == 0)
                {
                    LogDiag(1 << 5, $"[TerrainDiag] BAIL: textureService=ok mapManager={(_mapManager == null ? "NULL" : "ok")}");
                }

                return false;
            }

            var atlases = textureService.GetAllAtlases();
            if (atlases == null || atlases.Count == 0)
            {
                LogDiag(1 << 6, "[TerrainDiag] BAIL: atlases empty");
                return false;
            }

            if ((_diagLogged & (1 << 7)) == 0)
            {
                LogDiag(1 << 7, $"[TerrainDiag] atlases: {atlases.Count}");
            }

            bool materialsChanged = _materialManager.EnsureMaterials(
                atlases,
                _meshWidth,
                _meshHeight,
                _clientConfigManager,
                _cellCache);

            long atlasUploadStart = System.Diagnostics.Stopwatch.GetTimestamp();
            textureService.FlushDirtyAtlases();
            _telemetry.TerrainAtlasUploadTimeMs += (float)((System.Diagnostics.Stopwatch.GetTimestamp() - atlasUploadStart) *
                1000.0 / System.Diagnostics.Stopwatch.Frequency);

            try
            {
                int cacheDeltaX = (minX - 1) - _cellCache.CacheMinX;
                int cacheDeltaY = (minY - 1) - _cellCache.CacheMinY;
                bool canScrollCache =
                    !_needsRefresh &&
                    _cellCache.CacheMinX != int.MinValue &&
                    Mathf.Abs(cacheDeltaX) < _cellCache.CacheWidth &&
                    Mathf.Abs(cacheDeltaY) < _cellCache.CacheHeight;
                _incrementalGridOffsetsPending = canScrollCache;
                _gridOffsetDeltaX = canScrollCache ? cacheDeltaX : 0;
                _gridOffsetDeltaY = canScrollCache ? cacheDeltaY : 0;
                _telemetry.TerrainRebuildCount++;
                long swCache = System.Diagnostics.Stopwatch.GetTimestamp();
                using (_CacheMarker.Auto())
                {
                    if (canScrollCache)
                    {
                        _cellCache.ScrollAndFill(cacheDeltaX, cacheDeltaY, _storage, _mapManager, textureService, atlases);
                    }
                    else
                    {
                        _telemetry.TerrainFullPopulateCount++;
                        _cellCache.PopulateFull(minX, minY, _storage, _mapManager, textureService, atlases);
                    }
                }

                _telemetry.TerrainCacheTimeMs = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - swCache) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

                using (_PrecalculateMarker.Auto())
                {
                    if (canScrollCache)
                    {
                        _precalc.PrecalculateIncremental(_cellCache, _meshWidth, _meshHeight, cacheDeltaX, cacheDeltaY, _mapManager.WorldWidth, _mapManager.WorldHeight);
                    }
                    else
                    {
                        _precalc.PrecalculateFull(_cellCache, _meshWidth, _meshHeight, _mapManager.WorldWidth, _mapManager.WorldHeight);
                    }
                }

                long swFlood = System.Diagnostics.Stopwatch.GetTimestamp();
                using (_FloodFillMarker.Auto())
                {
                    // Тем же сдвигом, что кэш и предрасчёт выше: иначе на
                    // каждом переходе через границу региона заливка одна
                    // платила по площади за то, что сдвинулось на кайму.
                    if (canScrollCache)
                    {
                        _backgroundFloodFill.ComputeScrolled(cacheDeltaX, cacheDeltaY, this);
                    }
                    else
                    {
                        _backgroundFloodFill.ComputeFull(this);
                    }
                }

                _telemetry.TerrainFloodFillTimeMs = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - swFlood) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

                long swMesh = System.Diagnostics.Stopwatch.GetTimestamp();
                TerrainCellSources sources = CreateCellSources(atlases, textureService);
                using (_MeshBuildMarker.Auto())
                {
                    // Тексели лежат по кольцевому адресу и при сдвиге не
                    // двигаются: собирается только вошедшая полоса. Полная
                    // сборка остаётся там, где переносить нечего, и при смене
                    // набора атласов — индексы атласов в текселях считаны по
                    // старому набору.
                    if (canScrollCache && !materialsChanged)
                    {
                        _cellBuilder.ScrollAndBuildBand(sources, minX, minY, cacheDeltaX, cacheDeltaY);
                    }
                    else
                    {
                        _cellBuilder.BuildFull(sources, minX, minY);
                    }
                }

                _telemetry.TerrainMeshTimeMs = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - swMesh) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

                if ((_diagLogged & (1 << 8)) == 0)
                {
                    LogDiag(
                        1 << 8,
                        "[TerrainDiag] BuildFull: grid=(" +
                        $"{_lastGridPos.x},{_lastGridPos.y}) " +
                        $"world={_mapManager.WorldWidth}x{_mapManager.WorldHeight} " +
                        $"cells={_meshWidth}x{_meshHeight} transform={transform.position}");
                }

                _materialManager.BindAtlasTextures(atlases, textureService);
                if (_cellBuilder.DoorsTouched)
                {
                    RebuildDoorOverlay(sources, minX, minY);
                }
                else if (canScrollCache)
                {
                    _doorOverlayRenderer.CompensateParentTranslation(
                        new Vector3(
                            cacheDeltaX * _cellSize,
                            cacheDeltaY * _cellSize,
                            0f));
                }

                _needsRefresh = false;
            }
            catch (Exception ex)
            {
                _fatalBuildError = true;
                Debug.LogException(new InvalidOperationException(
                    $"[TerrainRenderer] Build failed: grid=({minX},{minY}) " +
                    $"size={_meshWidth}x{_meshHeight}, world=" +
                    $"{_mapManager?.WorldWidth ?? 0}x{_mapManager?.WorldHeight ?? 0}, " +
                    $"atlases={_textureService?.GetAllAtlases().Count ?? 0}, " +
                    $"storageReady={_storage?.IsReady ?? false}.",
                    ex));
                return false;
            }

            if (materialsChanged && _meshRenderer != null)
            {
                _meshRenderer.sharedMaterials = _materialManager.CellMaterials;
            }

            return true;
        }

        private bool UpdateDirtyCells(int minX, int minY)
        {
            if (_dirtyRects.IsEmpty)
            {
                return true;
            }

            if (_storage == null || !_storage.IsReady || _mapManager == null || _cellIDMesh.Mesh == null)
            {
                return false;
            }

            ITextureService? textureService = _textureService ?? _subscribedTextureService;
            if (textureService == null)
            {
                return false;
            }

            var atlases = textureService.GetAllAtlases();
            if (atlases == null || atlases.Count == 0 || _materialManager.Materials.Length == 0)
            {
                return false;
            }

            _telemetry.TerrainDirtyPatchCount++;

            TerrainCellSources sources = CreateCellSources(atlases, textureService);
            bool doorsTouched = false;
            for (int i = 0; i < _dirtyRects.Count; i++)
            {
                RectInt rect = _dirtyRects[i];

                int dirtyMinX = rect.xMin - 1;
                int dirtyMaxX = rect.xMax + 1;
                int dirtyMinY = rect.yMin - 1;
                int dirtyMaxY = rect.yMax + 1;

                int localStartX = dirtyMinX - minX;
                int localStartY = dirtyMinY - minY;
                int countX = dirtyMaxX - dirtyMinX;
                int countY = dirtyMaxY - dirtyMinY;

                _cellCache.UpdateRegion(dirtyMinX, dirtyMinY, countX, countY, _storage, _mapManager, textureService, atlases);
                _precalc.PrecalculateRegion(_cellCache, _meshWidth, _meshHeight, localStartX, localStartY, countX, countY, _mapManager.WorldWidth, _mapManager.WorldHeight);
                _backgroundFloodFill.UpdateLocalRegion(localStartX, localStartY, countX, countY, this);
                _cellBuilder.BuildRegion(sources, minX, minY, localStartX, localStartY, countX, countY);
                doorsTouched |= _cellBuilder.DoorsTouched;
            }

            // Накладка пересобирается, только если заплатка задела двери:
            // заплатка на ходу есть почти в каждом кадре, а двери в ней редки.
            if (doorsTouched)
            {
                RebuildDoorOverlay(sources, minX, minY);
            }

            return true;
        }

        private void UpdateTextureCells(int minX, int minY)
        {
            if (_mapManager == null || _cellIDMesh.Mesh == null || _textureService == null)
            {
                return;
            }

            IReadOnlyList<IAtlasDescriptor> atlases = _textureService.GetAllAtlases();
            if (atlases.Count == 0 || _materialManager.Materials.Length == 0)
            {
                return;
            }

            long atlasUploadStart = System.Diagnostics.Stopwatch.GetTimestamp();
            _textureService.FlushDirtyAtlases();
            _telemetry.TerrainAtlasUploadTimeMs += (float)((System.Diagnostics.Stopwatch.GetTimestamp() - atlasUploadStart) *
                1000.0 / System.Diagnostics.Stopwatch.Frequency);
            _cellCache.RefreshTextureMetadata(
                _pendingTextureCellTypes,
                _mapManager,
                _textureService,
                atlases);
            TerrainCellSources sources = CreateCellSources(atlases, _textureService);
            _cellBuilder.BuildTextureCells(_pendingTextureCellTypes, sources, minX, minY);
            _materialManager.BindAtlasTextures(atlases, _textureService);
            if (_cellBuilder.DoorsTouched)
            {
                RebuildDoorOverlay(sources, minX, minY);
            }
        }

        private void EnsureDoorOverlayIndices(int atlasCount)
        {
            if (_doorOverlaySubMeshIndices.Length == atlasCount)
            {
                return;
            }

            _doorOverlaySubMeshIndices = new List<int>[atlasCount];
            for (int atlasIndex = 0; atlasIndex < atlasCount; atlasIndex++)
            {
                _doorOverlaySubMeshIndices[atlasIndex] = [];
            }
        }

        private void RebuildDoorOverlay(TerrainCellSources sources, int minX, int minY)
        {
            if (!_cellBuilder.HasDoors)
            {
                _doorOverlayRenderer.Hide();
                return;
            }

            EnsureDoorOverlayIndices(sources.Atlases.Count);
            _cellBuilder.BuildDoorOverlay(sources, minX, minY, _doorOverlayVertices, _doorOverlaySubMeshIndices);
            _doorOverlayRenderer.Rebuild(
                transform,
                _sceneObjects,
                _doorOverlayVertices,
                _doorOverlaySubMeshIndices,
                _materialManager.OverlayMaterials,
                _sortingLayerName,
                _doorOverlaySortingOrder,
                _meshWidth,
                _meshHeight,
                _cellSize);
        }
    }
}

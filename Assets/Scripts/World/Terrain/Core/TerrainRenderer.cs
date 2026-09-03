#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Lifecycle;
using Fodinae.World.Lighting;
using Fodinae.World.Lighting.Quality;
using MinesServer.Data;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using VContainer;

namespace Fodinae.World.Terrain
{
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [DefaultExecutionOrder(100)]
    public class TerrainRenderer : MonoBehaviour, ICachedCellDataProvider
    {
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
        private readonly TerrainMeshBuilder _meshBuilder = new();
        private readonly BackgroundFloodFill _backgroundFloodFill = new();
        private readonly TerrainViewportCalculator _viewportCalculator = new();
        private readonly TerrainMeshManager _meshManager = new();
        private readonly TerrainMaterialManager _materialManager = new();
        private readonly TerrainDoorOverlayRenderer _doorOverlayRenderer = new();
        private List<int>[] _doorOverlaySubMeshIndices = Array.Empty<List<int>>();

        private Vector2Int _lastGridPos = new Vector2Int(int.MinValue, int.MinValue);
        private int _meshWidth;
        private int _meshHeight;
        private bool _isInitialized = false;
        private bool _hasMissingTextures;
        private bool _needsRefresh = false;
        private readonly HashSet<CellType> _pendingTextureCellTypes = [];
        private readonly DirtyRectSet _dirtyRects = new();
        private bool _useColorLod = false;
        private bool _fatalBuildError;
        private IWorldLayer<CellType>? _subscribedCellLayer;
        private ITextureService? _subscribedTextureService;
        private MapManager? _subscribedMapManager;
        private IWorldDataStorage? _subscribedStorage;

        private static readonly ProfilerMarker CacheMarker = new("Fodinae.Terrain.Cache");
        private static readonly ProfilerMarker PrecalculateMarker = new("Fodinae.Terrain.Precalculate");
        private static readonly ProfilerMarker FloodFillMarker = new("Fodinae.Terrain.BackgroundFloodFill");
        private static readonly ProfilerMarker MeshBuildMarker = new("Fodinae.Terrain.MeshBuild");
        private static readonly ProfilerMarker MeshUploadMarker = new("Fodinae.Terrain.MeshUpload");
        private static readonly ProfilerMarker TerrainLateUpdateMarker =
            new("Fodinae.Terrain.LateUpdate.CPU");
        private ulong _lightingGeometryRevision = 1;

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

        public ulong LightingGeometryRevision => _lightingGeometryRevision;

        public bool IsReadyForGameplay =>
            _isInitialized &&
            _meshManager.Mesh != null &&
            _meshManager.Mesh.vertexCount > 0 &&
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

            bool enableDistortion = config.EnableTerrainDistortion;
            if (_precalc.EnableDistortion != enableDistortion)
            {
                _precalc.EnableDistortion = enableDistortion;
                _needsRefresh = true;
            }

            _materialManager.ApplyClientConfig(config);
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

            _lightingEngine?.InvalidateRegion(
                serverX,
                minimumUnityY,
                width,
                maximumUnityY - minimumUnityY + 1);

            _dirtyRects.Add(
                new RectInt(
                    serverX,
                    minimumUnityY,
                    width,
                    maximumUnityY + 1 - minimumUnityY),
                new RectInt(_lastGridPos.x, _lastGridPos.y, _meshWidth, _meshHeight));
        }

        protected void Awake()
        {
            _materialManager.TerrainShader = _terrainShader;
            _materialManager.InitializeShader();

            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();
            _mainCamera = _gameplayCamera?.Camera;

            _meshManager.EnsureMesh(ref _meshFilter);

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
            _meshManager.EnsureMesh(ref _meshFilter);

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

            _meshManager.DestroyMesh();
            _doorOverlayRenderer.Dispose();
            _materialManager.CleanupMaterials();
        }

        private int _diagLogged;

        [System.Diagnostics.Conditional("FODINAE_TERRAIN_DIAG")]
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
                if (int.TryParse(id, out int cellTypeId) &&
                    (uint)cellTypeId <= ushort.MaxValue)
                {
                    _pendingTextureCellTypes.Add((CellType)cellTypeId);
                }
            }
        }

        private void OnWorldDataLoaded()
        {
            SubscribeToCellLayer();
            _needsRefresh = true;
            _lightingGeometryRevision++;
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
            HandleRegionChanged(serverX, serverY, width, height);
        }

        protected void LateUpdate()
        {
            if (_fatalBuildError)
            {
                return;
            }

            using var terrainLateUpdateMarker = TerrainLateUpdateMarker.Auto();
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
                out int requestedWidth,
                out int requestedHeight,
                out int effectiveViewportPadding,
                out bool dimensionsChanged);

            ResolveGridPosition(
                requestedWidth,
                requestedHeight,
                effectiveViewportPadding,
                dimensionsChanged,
                out Vector2Int currentGridPos,
                out int viewportMinX,
                out int viewportMinY,
                out int viewportWidth,
                out int viewportHeight);

            if (_meshRenderer != null)
            {
                _meshRenderer.enabled = !BypassTerrainDraw;
            }

            if (_pendingTextureCellTypes.Count > 0 && !BypassCpuMeshRebuild)
            {
                UpdateTextureCells(currentGridPos.x, currentGridPos.y);
                _pendingTextureCellTypes.Clear();
            }

            _telemetry.ResetFrameTimers();
            CoalesceOversizedDirtyRects();

            RebuildOrPatchTerrain(currentGridPos, dimensionsChanged);

            PublishLightingUpdate(lightingEngine, viewportMinX, viewportMinY, viewportWidth, viewportHeight);
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
                out int targetWidth,
                out int targetHeight,
                out effectiveViewportPadding,
                out requestedWidth,
                out requestedHeight,
                out dimensionsChanged);

            if (dimensionsChanged || !_isInitialized)
            {
                _meshWidth = targetWidth;
                _meshHeight = targetHeight;
                _isInitialized = true;
                _lastGridPos = new Vector2Int(int.MinValue, int.MinValue);
                _cellCache.EnsureCapacity(_meshWidth, _meshHeight);
                _precalc.EnsureCapacity(_meshWidth, _meshHeight);
                _meshBuilder.EnsureCapacity(_meshWidth, _meshHeight, _cellSize);
                _backgroundFloodFill.Allocate(_meshWidth, _meshHeight);

                _needsRefresh = true;
            }
        }

        private void ResolveGridPosition(
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
                _meshWidth,
                _meshHeight,
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

        private void CoalesceOversizedDirtyRects()
        {
            if (!_dirtyRects.IsEmpty && _meshWidth > 0 && _meshHeight > 0)
            {
                if (_dirtyRects.TotalArea * 2 >= (long)_meshWidth * _meshHeight)
                {
                    _needsRefresh = true;
                    _dirtyRects.Clear();
                }
            }
        }

        private void RebuildOrPatchTerrain(Vector2Int currentGridPos, bool dimensionsChanged)
        {
            bool terrainWasRebuilt = (currentGridPos != _lastGridPos || _needsRefresh || dimensionsChanged) && !BypassCpuMeshRebuild;
            if (terrainWasRebuilt)
            {
                transform.position = new Vector3(currentGridPos.x * _cellSize, currentGridPos.y * _cellSize, 0);
                _lastGridPos = currentGridPos;

                UpdateVertexAttributes(currentGridPos.x, currentGridPos.y);
                _lightingGeometryRevision++;
                _dirtyRects.Clear();
            }
            else if (!_dirtyRects.IsEmpty && !BypassCpuMeshRebuild)
            {
                UpdateDirtyCells(currentGridPos.x, currentGridPos.y);
                _lightingGeometryRevision++;
                _dirtyRects.Clear();
            }
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
                _materialManager.Materials);
        }

        private void UpdateVertexAttributes(int minX, int minY)
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

                return;
            }

            var atlases = textureService.GetAllAtlases();
            if (atlases == null || atlases.Count == 0)
            {
                LogDiag(1 << 6, "[TerrainDiag] BAIL: atlases empty");
                return;
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

            textureService.FlushDirtyAtlases();

            try
            {
                int cacheDeltaX = (minX - 1) - _cellCache.CacheMinX;
                int cacheDeltaY = (minY - 1) - _cellCache.CacheMinY;
                bool canScrollCache =
                    !_needsRefresh &&
                    _cellCache.CacheMinX != int.MinValue &&
                    Mathf.Abs(cacheDeltaX) < _cellCache.CacheWidth &&
                    Mathf.Abs(cacheDeltaY) < _cellCache.CacheHeight;
                _telemetry.TerrainRebuildCount++;
                long swCache = System.Diagnostics.Stopwatch.GetTimestamp();
                using (CacheMarker.Auto())
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

                using (PrecalculateMarker.Auto())
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
                using (FloodFillMarker.Auto())
                {
                    _backgroundFloodFill.ComputeFull(this);
                }

                _telemetry.TerrainFloodFillTimeMs = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - swFlood) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

                long swMesh = System.Diagnostics.Stopwatch.GetTimestamp();
                using (MeshBuildMarker.Auto())
                {
                    _meshBuilder.BuildFull(_cellCache, _precalc, _backgroundFloodFill, minX, minY, _meshWidth, _meshHeight, _mapManager.WorldWidth, _mapManager.WorldHeight, atlases, _materialManager.SubMeshIndices, _useColorLod, _mapManager, textureService);
                    EnsureDoorOverlayIndices(atlases.Count);
                    _meshBuilder.RebuildOverlaySubMeshIndices(
                        _meshWidth,
                        _meshHeight,
                        _doorOverlaySubMeshIndices);
                }

                _telemetry.TerrainMeshTimeMs = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - swMesh) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

                Mesh? mesh = _meshManager.Mesh;
                if (mesh != null)
                {
                    long swUpload = System.Diagnostics.Stopwatch.GetTimestamp();
                    using (MeshUploadMarker.Auto())
                    {
                        _meshManager.UploadVertexBuffer(_meshBuilder, atlases.Count, _telemetry);
                    }

                    _telemetry.TerrainGpuUploadTimeMs = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - swUpload) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

                    _meshManager.UpdateMeshBounds(_meshWidth, _meshHeight, _cellSize);

                    if ((_diagLogged & (1 << 8)) == 0)
                    {
                        string diagnostic =
                            "[TerrainDiag] BuildFull: grid=(" +
                            $"{_lastGridPos.x},{_lastGridPos.y}) " +
                            $"world={_mapManager.WorldWidth}x{_mapManager.WorldHeight} " +
                            $"verts={_meshBuilder.VertexBuffer.Length} meshVerts={mesh.vertexCount} " +
                            $"bounds={mesh.bounds} transform={transform.position}";
                        LogDiag(
                            1 << 8,
                            diagnostic);
                    }

                    _materialManager.BindAtlasTextures(atlases, textureService, mesh);
                    RebuildDoorOverlay();
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
            }

            if (materialsChanged && _meshRenderer != null)
            {
                _meshRenderer.sharedMaterials = _materialManager.Materials;
            }
        }

        private void UpdateDirtyCells(int minX, int minY)
        {
            if (_dirtyRects.IsEmpty)
            {
                return;
            }

            if (_storage == null || !_storage.IsReady || _mapManager == null || _meshManager.Mesh == null)
            {
                return;
            }

            ITextureService? textureService = _textureService ?? _subscribedTextureService;
            if (textureService == null)
            {
                return;
            }

            var atlases = textureService.GetAllAtlases();
            if (atlases == null || atlases.Count == 0 || _materialManager.SubMeshIndices.Length == 0)
            {
                return;
            }

            _telemetry.TerrainDirtyPatchCount++;

            bool anyIndicesChanged = false;
            bool anyOverlayIndicesChanged = false;
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
                _meshBuilder.BuildRegion(_cellCache, _precalc, _backgroundFloodFill, minX, minY, _meshWidth, _meshHeight, localStartX, localStartY, countX, countY, _mapManager.WorldWidth, _mapManager.WorldHeight, atlases, _materialManager.SubMeshIndices, _useColorLod, _mapManager, textureService);

                anyIndicesChanged |= _meshBuilder.IndicesChanged;
                anyOverlayIndicesChanged |= _meshBuilder.OverlayIndicesChanged;
            }

            _meshManager.UploadDirectVertexBuffer(_meshBuilder);

            if (anyIndicesChanged)
            {
                Mesh? mesh = _meshManager.Mesh;
                if (mesh != null)
                {
                    for (int i = 0; i < atlases.Count && i < _materialManager.SubMeshIndices.Length; i++)
                    {
                        mesh.SetIndices(_materialManager.SubMeshIndices[i], MeshTopology.Triangles, i, false, 0);
                    }
                }
            }

            if (anyOverlayIndicesChanged)
            {
                _meshBuilder.RebuildOverlaySubMeshIndices(
                    _meshWidth,
                    _meshHeight,
                    _doorOverlaySubMeshIndices);
            }

            RebuildDoorOverlay();
        }

        private void UpdateTextureCells(int minX, int minY)
        {
            if (_mapManager == null || _meshManager.Mesh == null || _textureService == null)
            {
                return;
            }

            IReadOnlyList<IAtlasDescriptor> atlases = _textureService.GetAllAtlases();
            if (atlases.Count == 0 || _materialManager.SubMeshIndices.Length == 0)
            {
                return;
            }

            _textureService.FlushDirtyAtlases();
            _cellCache.RefreshTextureMetadata(
                _pendingTextureCellTypes,
                _mapManager,
                _textureService,
                atlases);
            _meshBuilder.BuildTextureCells(
                _pendingTextureCellTypes,
                _cellCache,
                _precalc,
                _backgroundFloodFill,
                minX,
                minY,
                _meshWidth,
                _meshHeight,
                _mapManager.WorldWidth,
                _mapManager.WorldHeight,
                atlases,
                _materialManager.SubMeshIndices,
                _useColorLod,
                _mapManager,
                _textureService);

            if (_meshBuilder.DirtyVertexCount == 0)
            {
                return;
            }

            _meshManager.UploadDirectVertexBuffer(_meshBuilder);
            Mesh? mesh = _meshManager.Mesh;
            if (mesh != null)
            {
                _materialManager.BindAtlasTextures(atlases, _textureService, mesh);
                if (_meshBuilder.IndicesChanged)
                {
                    for (int i = 0; i < atlases.Count && i < _materialManager.SubMeshIndices.Length; i++)
                    {
                        mesh.SetIndices(_materialManager.SubMeshIndices[i], MeshTopology.Triangles, i, false, 0);
                    }
                }
            }

            if (_meshBuilder.OverlayIndicesChanged)
            {
                _meshBuilder.RebuildOverlaySubMeshIndices(
                    _meshWidth,
                    _meshHeight,
                    _doorOverlaySubMeshIndices);
            }

            RebuildDoorOverlay();
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

        private void RebuildDoorOverlay()
        {
            _doorOverlayRenderer.Rebuild(
                transform,
                _sceneObjects,
                _meshBuilder,
                _doorOverlaySubMeshIndices,
                _materialManager.Materials,
                _sortingLayerName,
                _doorOverlaySortingOrder,
                _meshWidth,
                _meshHeight,
                _cellSize);
        }
    }
}

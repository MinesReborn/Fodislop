#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.World;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae.UI
{
    public class WorldMapRenderer : MonoBehaviour
    {
        [Header("Rendering")]
        [SerializeField]
        private float _renderInterval = 0.1f;
        [SerializeField]
        private float _dragSpeed = 0.5f;

        private const int MaxChunkCacheEntries = 4096;

        private int _texWidth;
        private int _texHeight;
        private int _lastPanelWidth = -1;
        private int _lastPanelHeight = -1;
        private UIDocument? _document;
        private VisualElement? _mapOverlay;
        private Image? _mapImage;
        private Texture2D? _mapTexture;
        private IWorldLayer<CellType>? _cellLayer;
        private int _chunkSize = ProjectRuntimeContracts.World.ChunkSize;
        private readonly MapCellSampler _cellSampler = new();
        private readonly MapInteractionController _interaction = new();
        private readonly MapViewportRenderer _viewportRenderer = new();
        private MapPlayerTracker _playerTracker = null!;

        private float _viewCenterX;
        private float _viewCenterY;
        private float _cellsPerPixel = 1f;
        private float _maxCellsPerPixel = 10f;

        [Inject]
        private IWorldDataStorage _storage = null!;
        [Inject]
        private MapManager _manager = null!;
        [Inject]
        private UIDocument _injectedDocument = null!;
        [Inject]
        private ILocalPlayerState _localPlayer = null!;

        private float _lastRenderTime;
        private bool _initialRenderDone;
        private bool _renderRequested;
        private long _lastRenderedStorageRevision = -1;
        private bool _followPlayer = true;
        private IWorldLayer<CellType>? _subscribedCellLayer;
        private int _boundWorldWidth;
        private int _boundWorldHeight;
        private string _boundWorldCodeName = string.Empty;
        private bool _initialized;

        protected void Start()
        {
            _playerTracker = new MapPlayerTracker(_localPlayer);
            _playerTracker.OnPlayerSpawned += () => _renderRequested = true;
            _playerTracker.OnPlayerMoved += pos =>
            {
                if (_followPlayer)
                {
                    _viewCenterX = pos.x;
                    _viewCenterY = pos.y;
                    _renderRequested = true;
                }
            };
            _playerTracker.OnBlinkFlipped += () => _renderRequested = true;

            TryInitialize();
            if (!_initialized)
            {
                _manager.OnWorldInitialized += OnWorldReady;
                _manager.OnWorldDataLoaded += OnWorldReady;
            }

            if (IsWorldReady())
            {
                OnWorldReady();
            }
        }

        private bool IsWorldReady() =>
            _manager.IsWorldInitialized && _storage.IsReady;

        private void OnWorldReady()
        {
            if (!IsWorldReady())
            {
                return;
            }

            _manager.OnWorldInitialized -= OnWorldReady;
            _manager.OnWorldDataLoaded -= OnWorldReady;

            TryInitialize();
        }

        protected void OnEnable()
        {
            if (_initialized)
            {
                RebindRuntimeSources();
            }
        }

        private void TryInitialize()
        {
            if (_initialized || !IsWorldReady())
            {
                return;
            }

            _playerTracker ??= new MapPlayerTracker(_localPlayer);
            _playerTracker.EnsureBinding();

            BindUI();
            InitTexture();
            ResetWorldViewState(_storage);

            if (_mapOverlay != null)
            {
                Hide();
            }

            _initialized = true;
        }

        private void ResetWorldViewState(IWorldDataStorage storage)
        {
            BindWorldDimensions(_manager.WorldWidth, _manager.WorldHeight);
            _viewportRenderer.InitColorTable(_manager);
            BindCellLayer(storage.CellLayer);
            _cellsPerPixel = 1f;
            _maxCellsPerPixel = ComputeMaxZoomOut(_boundWorldWidth, _boundWorldHeight);
            _cellsPerPixel = Mathf.Min(_cellsPerPixel, _maxCellsPerPixel);

            ILocalPlayer? player = _playerTracker.CurrentPlayer;
            if (player is { HasServerPosition: true })
            {
                _viewCenterX = player.Position.x;
                _viewCenterY = player.Position.y;
            }
            else
            {
                _viewCenterX = _boundWorldWidth * 0.5f;
                _viewCenterY = _boundWorldHeight * 0.5f;
            }

            _lastRenderedStorageRevision = -1;
            _initialRenderDone = false;
            _renderRequested = true;
        }

        protected void OnDestroy()
        {
            if (_mapTexture != null)
            {
                Destroy(_mapTexture);
            }

            _manager.OnWorldInitialized -= OnWorldReady;
            _manager.OnWorldDataLoaded -= OnWorldReady;

            _playerTracker?.Dispose();

            if (_subscribedCellLayer != null)
            {
                _subscribedCellLayer.ChunkLoaded -= OnChunkLoaded;
                _subscribedCellLayer = null;
            }
        }

        private void RebindRuntimeSources()
        {
            if (_storage == null)
            {
                return;
            }

            _playerTracker?.EnsureBinding();

            if (_storage.CellLayer == null)
            {
                BindCellLayer(null);
                return;
            }

            IWorldLayer<CellType> cellLayer = _storage.CellLayer;
            if (!ReferenceEquals(_subscribedCellLayer, cellLayer))
            {
                BindCellLayer(cellLayer);
                return;
            }

            cellLayer.ChunkLoaded -= OnChunkLoaded;
            cellLayer.ChunkLoaded += OnChunkLoaded;
            _cellSampler.Bind(cellLayer);
            _cellSampler.Invalidate();
        }

        private void OnChunkLoaded(int serverX, int serverY, int width, int height)
        {
            _cellSampler.InvalidateChunk(serverX, serverY);
            _renderRequested = true;
        }

        private void BindCellLayer(IWorldLayer<CellType>? cellLayer)
        {
            if (ReferenceEquals(_subscribedCellLayer, cellLayer))
            {
                return;
            }

            if (_subscribedCellLayer != null)
            {
                _subscribedCellLayer.ChunkLoaded -= OnChunkLoaded;
            }

            _subscribedCellLayer = cellLayer;
            _cellLayer = cellLayer;
            _cellSampler.Bind(cellLayer);
            _cellSampler.Invalidate();

            if (_subscribedCellLayer != null)
            {
                _chunkSize = _subscribedCellLayer.ChunkSize;
                _subscribedCellLayer.ChunkLoaded += OnChunkLoaded;
            }
            else
            {
                _chunkSize = 0;
            }
        }

        private void BindWorldDimensions(int worldWidth, int worldHeight)
        {
            if (worldWidth <= 0 || worldHeight <= 0)
            {
                throw new InvalidOperationException(
                    $"[WorldMapRenderer] Invalid world dimensions: {worldWidth}x{worldHeight}.");
            }

            _boundWorldWidth = worldWidth;
            _boundWorldHeight = worldHeight;
            MapManager manager = _manager ??
                throw new InvalidOperationException(
                    "[WorldMapRenderer] MapManager is required before binding world dimensions.");
            if (string.IsNullOrWhiteSpace(manager.WorldCodeName))
            {
                throw new InvalidOperationException(
                    "[WorldMapRenderer] World code name is required before binding map state.");
            }

            _boundWorldCodeName = manager.WorldCodeName;
        }

        protected void Update()
        {
            if (!enabled || !_initialized)
            {
                return;
            }

            if (_manager == null || _storage == null ||
                !_manager.IsWorldInitialized || !_storage.IsReady)
            {
                BindCellLayer(null);
                _initialRenderDone = false;
                _renderRequested = false;
                return;
            }

            if (_mapOverlay != null)
            {
                Rect panelRect = _mapOverlay.worldBound;
                int curW = panelRect.width > 0f ? Mathf.RoundToInt(panelRect.width) : 0;
                int curH = panelRect.height > 0f ? Mathf.RoundToInt(panelRect.height) : 0;
                if (curW > 0 && curH > 0 && (curW != _lastPanelWidth || curH != _lastPanelHeight))
                {
                    InitTexture();
                    _renderRequested = true;
                }
            }

            _interaction.HandleMouseScroll(
                _mapOverlay,
                _mapImage,
                _document,
                _texWidth,
                _texHeight,
                _maxCellsPerPixel,
                ref _cellsPerPixel,
                ref _viewCenterX,
                ref _viewCenterY,
                ref _renderRequested,
                ClampViewCenter);

            _interaction.HandleDrag(
                _cellsPerPixel,
                _dragSpeed,
                ref _viewCenterX,
                ref _viewCenterY,
                ref _followPlayer,
                ref _renderRequested,
                ClampViewCenter);

            _playerTracker.Update(
                Time.deltaTime,
                _followPlayer,
                ref _viewCenterX,
                ref _viewCenterY,
                ref _renderRequested);

            HandleQueuedRender();
        }

        public void Show()
        {
            if (_storage == null || _manager == null || _mapOverlay == null)
            {
                return;
            }

            _mapOverlay.style.display = DisplayStyle.Flex;

            enabled = true;
            _lastRenderTime = -1f;
            _initialRenderDone = false;
            _renderRequested = true;
            _lastRenderedStorageRevision = -1;
            _followPlayer = true;
            _playerTracker?.ResetState();
        }

        public void Hide()
        {
            if (_mapOverlay != null)
            {
                _mapOverlay.style.display = DisplayStyle.None;
            }

            enabled = false;
        }

        public void SetViewCenter(float worldX, float worldY)
        {
            if (!Mathf.Approximately(_viewCenterX, worldX) ||
                !Mathf.Approximately(_viewCenterY, worldY))
            {
                _renderRequested = true;
            }

            _viewCenterX = worldX;
            _viewCenterY = worldY;
            ClampViewCenter();
        }

        private void BindUI()
        {
            _document = _injectedDocument;
            VisualElement overlay = _document.rootVisualElement.Q<VisualElement>("WorldMapOverlay") ??
                throw new InvalidOperationException(
                    "[WorldMapRenderer] WorldMapOverlay is missing from the gameplay UIDocument.");
            Image image = overlay.Q<Image>("WorldMapImage") ??
                throw new InvalidOperationException(
                    "[WorldMapRenderer] WorldMapImage is missing from the gameplay UIDocument.");

            _mapOverlay = overlay;
            _mapImage = image;
            _mapImage.image = null;
        }

        private void InitTexture()
        {
            VisualElement overlay = _mapOverlay ?? throw new InvalidOperationException(
                "[WorldMapRenderer] UI must be bound before the map texture.");
            Rect panelRect = overlay.worldBound;

            MapViewportBounds.CalculateTextureDimensions(
                panelRect.width,
                panelRect.height,
                out _texWidth,
                out _texHeight);

            _lastPanelWidth = panelRect.width > 0f ? Mathf.RoundToInt(panelRect.width) : 1920;
            _lastPanelHeight = panelRect.height > 0f ? Mathf.RoundToInt(panelRect.height) : 1080;

            if (_mapTexture != null)
            {
                Destroy(_mapTexture);
            }

            _mapTexture = RuntimeTextureFactory.CreateRgba32NoMip(
                _texWidth,
                _texHeight,
                "WorldMapTexture",
                RuntimeTextureColorSpace.Srgb,
                FilterMode.Point,
                TextureWrapMode.Clamp);

            if (_mapImage != null)
            {
                _mapImage.image = _mapTexture;
            }
        }

        private void HandleQueuedRender()
        {
            IWorldDataStorage storage = _storage ??
                throw new InvalidOperationException("WorldMapRenderer storage is not initialized.");
            if (storage.Revision != _lastRenderedStorageRevision)
            {
                _cellSampler.Invalidate();
                _renderRequested = true;
            }

            if (!ReferenceEquals(_cellLayer, storage.CellLayer))
            {
                BindCellLayer(storage.CellLayer);
                _renderRequested = true;
                _initialRenderDone = false;
                _lastRenderedStorageRevision = -1;
            }

            if (_manager != null &&
                (_manager.WorldWidth != _boundWorldWidth ||
                 _manager.WorldHeight != _boundWorldHeight ||
                 !string.Equals(_manager.WorldCodeName, _boundWorldCodeName, StringComparison.Ordinal)))
            {
                ResetWorldViewState(storage);
            }

            if (!_renderRequested)
            {
                return;
            }

            if (_initialRenderDone && Time.time - _lastRenderTime < _renderInterval)
            {
                return;
            }

            if (_manager == null || _storage == null)
            {
                return;
            }

            _viewportRenderer.Render(
                _mapTexture,
                _manager,
                _cellSampler,
                _texWidth,
                _texHeight,
                _cellsPerPixel,
                _viewCenterX,
                _viewCenterY,
                _playerTracker.CurrentPlayer,
                _playerTracker.PlayerBlinkState);

            _renderRequested = false;
            _lastRenderedStorageRevision = _storage.Revision;
            _lastRenderTime = Time.time;
            _initialRenderDone = true;
        }

        private float ComputeMaxZoomOut(int worldW, int worldH) =>
            MapViewportBounds.ComputeMaxZoomOut(_texWidth, _texHeight, _chunkSize, MaxChunkCacheEntries);

        private void ClampViewCenter()
        {
            if (_manager == null)
            {
                return;
            }

            MapViewportBounds.ClampViewCenter(
                ref _viewCenterX,
                ref _viewCenterY,
                _cellsPerPixel,
                _texWidth,
                _texHeight,
                _boundWorldWidth,
                _boundWorldHeight);
        }
    }
}

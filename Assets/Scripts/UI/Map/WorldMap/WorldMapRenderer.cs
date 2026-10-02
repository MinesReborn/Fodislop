#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.World;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Kern.UI
{
    public class WorldMapRenderer : MonoBehaviour
    {
        [Header("Rendering")]
        [SerializeField]
        private float _dragSpeed = 1f;

        private readonly WorldMapPanel _panel = new();
        private readonly MapTextureController _textureController = new();
        [Inject]
        private MapCellSampler _cellSampler = null!;
        private readonly MapInteractionController _interaction = new();
        private WorldMapPointerBinder _pointerBinder = null!;
        private WorldMapInputDispatcher _inputDispatcher = null!;
        private readonly MapViewportRenderer _viewportRenderer = new();
        private readonly WorldMapMipScan _mipScan = new();
        private WorldMapLayerBinding _layerBinding = null!;
        private MapPlayerTracker _playerTracker = null!;
        private VisualElement? _documentRoot;

        private float _viewCenterX;
        private float _viewCenterY;
        private float _cellsPerPixel = 1f;
        private float _maxCellsPerPixel = 10f;

        // Нить клик-маршрута поверх карты мира: прозрачная текстура-оверлей,
        // перерисовывается целиком при каждом рендере карты и смене маршрута.
        private Texture2D? _pathTexture;
        private IReadOnlyList<Vector2Int>? _clickPath;
        private IClickPathWalker? _pathWalker;

        [Inject]
        private IWorldDataStorage _storage = null!;
        [Inject]
        private MapManager _manager = null!;
        [Inject]
        private UIDocument _injectedDocument = null!;
        [Inject]
        private ILocalPlayerState _localPlayer = null!;

        private bool _renderRequested;
        private long _lastRenderedStorageRevision = -1;
        private bool _followPlayer = true;
        private readonly WorldMapBounds _bounds = new();
        private bool _initialized;

        [Inject]
        private ILocalizationService _localization = null!;

        public event Action? CloseRequested;

        protected void Start()
        {
            _documentRoot = _injectedDocument.rootVisualElement;
            if (_documentRoot.panel == null)
            {
                _documentRoot.RegisterCallback<AttachToPanelEvent>(OnDocumentAttached);
            }

            _mipScan.SetRequestRenderCallback(RequestRender);
            _pointerBinder = new WorldMapPointerBinder(_panel);
            _inputDispatcher = new WorldMapInputDispatcher(
                _interaction, _panel, _textureController, ClampViewCenter, RequestRender);
            _layerBinding = new WorldMapLayerBinding(
                _cellSampler,
                _mipScan,
                RequestRender,
                RequestFullRender);
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
            _playerTracker.OnPlayerRelocated += pos =>
            {
                _followPlayer = true;
                _viewCenterX = pos.x;
                _viewCenterY = pos.y;
                ClampViewCenter();
                _renderRequested = true;
            };
            _playerTracker.OnBlinkFlipped += UpdatePlayerMarker;

            TryInitialize();
            _manager.OnWorldInitialized += OnWorldReady;
            _manager.OnWorldDataLoaded += OnWorldReady;

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

            if (!_initialized)
            {
                TryInitialize();
            }
            else if (_storage != null)
            {
                ResetWorldViewState(_storage);
            }
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

            if (!TryBindUI())
            {
                return;
            }

            _pointerBinder.Bind(OnMapPointerDown, OnMapPointerMove, OnMapPointerUp, OnMapClick);

            // Клик-маршрут: подписка на изменения нити и первичный остаток пути.
            if (_localPlayer.Current is IClickPathWalker walker)
            {
                _pathWalker = walker;
                walker.OnPathChanged += OnWalkerPathChanged;
                _clickPath = walker.Path;
            }

            _playerTracker ??= new MapPlayerTracker(_localPlayer);
            _playerTracker.EnsureBinding();

            InitTexture();
            ResetWorldViewState(_storage);

            if (_panel.Overlay != null)
            {
                Hide();
            }

            _initialized = true;
            RebindRuntimeSources();
        }

        private bool TryBindUI()
        {
            // Панель может быть ещё не готова к моменту первой привязки; повторная попытка при необходимости.
            return _panel.TryBind(
                _injectedDocument,
                OnCloseButtonClicked,
                FollowPlayer,
                OnWorldMapWheel);
        }

        private void OnDocumentAttached(AttachToPanelEvent _)
        {
            if (_documentRoot?.panel == null)
            {
                return;
            }

            _documentRoot.UnregisterCallback<AttachToPanelEvent>(OnDocumentAttached);
            OnWorldReady();
        }

        private void OnCloseButtonClicked() => CloseRequested?.Invoke();

        private void FollowPlayer()
        {
            if (_playerTracker.CurrentPlayer is not { HasServerPosition: true } player)
            {
                return;
            }

            _followPlayer = true;
            _viewCenterX = player.Position.x;
            _viewCenterY = player.Position.y;
            ClampViewCenter();
            _renderRequested = true;
        }

        private void RequestRender() => _renderRequested = true;

        private void RequestFullRender()
        {
            _viewportRenderer.InvalidateViewState();
            _renderRequested = true;
        }

        private void OnWorldMapWheel(WheelEvent evt) =>
            _inputDispatcher.HandleWheel(evt, _maxCellsPerPixel, ref _cellsPerPixel, ref _viewCenterX, ref _viewCenterY);

        // Клик по карте мира (без драга): тексель -> серверная клетка относительно
        // центра вью -> клик-маршрут, та же логика, что у ЛКМ по миру и миникарты.
        private void OnMapClick(ClickEvent evt)
        {
            Image? image = _panel.Image;
            if (_interaction.WasDragging || image == null)
            {
                return;
            }

            Rect rect = image.contentRect;
            Vector2 local = new(evt.localPosition.x, evt.localPosition.y);
            if (rect.width <= 0f || rect.height <= 0f || !rect.Contains(local))
            {
                return;
            }

            int texWidth = _textureController.TexWidth;
            int texHeight = _textureController.TexHeight;
            float pixelX = (local.x - rect.x) / rect.width * texWidth;
            float pixelY = (local.y - rect.y) / rect.height * texHeight;
            Vector2 server = MapProjection.MapPixelToServer(
                pixelX,
                pixelY,
                _viewCenterX,
                _viewCenterY,
                _cellsPerPixel,
                texWidth,
                texHeight);
            var target = new Vector2Int(Mathf.FloorToInt(server.x), Mathf.FloorToInt(server.y));

            (_localPlayer.Current as IClickPathWalker)?.TryStartPath(target);
        }

        private void OnMapPointerDown(PointerDownEvent evt) =>
            _inputDispatcher.HandlePointerDown(evt);

        private void OnMapPointerMove(PointerMoveEvent evt) =>
            _inputDispatcher.HandlePointerMove(evt, _cellsPerPixel, _dragSpeed, ref _viewCenterX, ref _viewCenterY, ref _followPlayer);

        private void OnMapPointerUp(PointerUpEvent evt) =>
            _inputDispatcher.HandlePointerUp(evt);

        private void ResetWorldViewState(IWorldDataStorage storage)
        {
            _bounds.Bind(_manager, _manager.WorldWidth, _manager.WorldHeight);
            _viewportRenderer.InitColorTable(_manager);
            _viewportRenderer.InvalidateViewState();
            _layerBinding.BindCellLayer(storage.CellLayer);
            _layerBinding.BindMipScan(_viewportRenderer.CellColorTable);
            _cellsPerPixel = 1f;
            _maxCellsPerPixel = _bounds.ComputeMaxZoomOut(
                _textureController.TexWidth,
                _textureController.TexHeight);
            _cellsPerPixel = Mathf.Min(_cellsPerPixel, _maxCellsPerPixel);

            ILocalPlayer? player = _playerTracker.CurrentPlayer;
            if (player is { HasServerPosition: true })
            {
                _viewCenterX = player.Position.x;
                _viewCenterY = player.Position.y;
            }
            else
            {
                _viewCenterX = _bounds.Width * 0.5f;
                _viewCenterY = _bounds.Height * 0.5f;
            }

            _lastRenderedStorageRevision = -1;
            _renderRequested = true;
        }

        protected void OnDestroy()
        {
            if (_documentRoot != null)
            {
                _documentRoot.UnregisterCallback<AttachToPanelEvent>(OnDocumentAttached);
            }

            _pointerBinder?.Dispose(OnMapPointerDown, OnMapPointerMove, OnMapPointerUp, OnMapClick);

            if (_pathWalker != null)
            {
                _pathWalker.OnPathChanged -= OnWalkerPathChanged;
                _pathWalker = null;
            }

            _clickPath = null;

            if (_pathTexture != null)
            {
                Destroy(_pathTexture);
                _pathTexture = null;
            }

            _panel.Dispose();
            _textureController.DestroyTexture();
            _viewportRenderer.Dispose();

            _manager.OnWorldInitialized -= OnWorldReady;
            _manager.OnWorldDataLoaded -= OnWorldReady;

            _playerTracker?.Dispose();

            _layerBinding?.Dispose();
            _mipScan.Dispose();
        }

        private void RebindRuntimeSources()
        {
            if (_storage == null)
            {
                return;
            }

            _playerTracker?.EnsureBinding();
            _layerBinding.BindStorage(_storage);

            if (_storage.CellLayer == null)
            {
                _layerBinding.BindCellLayer(null);
                return;
            }

            IWorldLayer<CellType> cellLayer = _storage.CellLayer;
            if (_layerBinding.BindCellLayer(cellLayer))
            {
                _layerBinding.BindMipScan(_viewportRenderer.CellColorTable);
                return;
            }

            _layerBinding.RebindCellEvents();
        }

        private void UpdateMipStatus()
        {
            _panel.UpdatePreparationStatus(
                _mipScan.IsReady,
                _cellsPerPixel,
                _layerBinding.ChunkSize,
                _mipScan.Failed,
                _mipScan.Progress,
                _mipScan.Total,
                _localization);
        }

        protected void Update()
        {
            if (!enabled || !_initialized)
            {
                return;
            }

            if (_panel.IsDocumentDisabled)
            {
                return;
            }

            if (_manager == null || _storage == null ||
                !_manager.IsWorldInitialized || !_storage.IsReady)
            {
                _layerBinding.BindCellLayer(null);
                _renderRequested = false;
                return;
            }

            if (_panel.Image != null && _textureController.CheckPanelResize(_panel.Image))
            {
                InitTexture();
                _maxCellsPerPixel = _bounds.ComputeMaxZoomOut(
                    _textureController.TexWidth,
                    _textureController.TexHeight);
                _cellsPerPixel = Mathf.Min(_cellsPerPixel, _maxCellsPerPixel);
                ClampViewCenter();
                _renderRequested = true;
            }

            _playerTracker.Update(
                Time.deltaTime,
                _followPlayer,
                ref _viewCenterX,
                ref _viewCenterY,
                ref _renderRequested);

            UpdatePlayerMarker();

            _mipScan.UpdatePendingChunks();

            HandleQueuedRender();
        }

        public void Show()
        {
            if (!_initialized)
            {
                TryInitialize();
            }

            if (_storage == null || _manager == null || _panel.Overlay == null)
            {
                return;
            }

            _panel.Show();

            enabled = true;
            _renderRequested = true;
            _lastRenderedStorageRevision = -1;
            _followPlayer = true;
            _playerTracker?.ResetState();
            UpdatePlayerMarker();
            UpdateMipStatus();
        }

        public void Hide()
        {
            _panel.HidePlayerMarker();
            _panel.Hide();
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

        private void InitTexture()
        {
            Image viewport = _panel.Image ?? throw new InvalidOperationException(
                "[WorldMapRenderer] UI must be bound before the map texture.");
            _viewportRenderer.InvalidateViewState();
            _textureController.InitTexture(viewport, viewport);
            EnsurePathTexture();
        }

        // Создаёт/пересоздаёт прозрачную текстуру нити маршрута под размер
        // текстуры карты и привязывает её к оверлею.
        private void EnsurePathTexture()
        {
            int width = _textureController.TexWidth;
            int height = _textureController.TexHeight;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            if (_pathTexture != null &&
                _pathTexture.width == width &&
                _pathTexture.height == height)
            {
                return;
            }

            if (_pathTexture != null)
            {
                Destroy(_pathTexture);
            }

            _pathTexture = RuntimeTextureFactory.CreateRGBA32NoMip(
                width,
                height,
                "WorldMapPathTexture",
                RuntimeTextureColorSpace.Srgb,
                FilterMode.Point,
                TextureWrapMode.Clamp);
            _pathTexture.SetPixelData(new Color32[width * height], 0);
            _pathTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            DynamicAtlasConfigurator.RegisterRuntimeRedrawn(_pathTexture);

            if (_panel.PathOverlay != null)
            {
                _panel.PathOverlay.image = _pathTexture;
            }
        }

        // Перерисовывает нить маршрута в оверлее: прозрачная заливка + жирные
        // жёлтые тексели клеток остатка пути, цель — белая. Ось Y текстуры
        // инвертирована относительно serverY: строка 0 — низ карты (наибольший
        // serverY), что соответствует RenderRegion карты.
        private void UpdatePathOverlay()
        {
            Texture2D? texture = _pathTexture;
            if (texture == null)
            {
                return;
            }

            int width = texture.width;
            int height = texture.height;
            var colors = new Color32[width * height];

            // Остаток маршрута: от следующего шага робота до цели — пройденная
            // часть нити не отображается.
            IReadOnlyList<Vector2Int>? path = _pathWalker?.Path;
            int startIndex = _pathWalker?.PathIndex ?? 0;

            if (path != null && _cellsPerPixel > 0f)
            {
                Color32 pathColor = new(255, 214, 0, 255);
                Color32 targetColor = new(255, 255, 255, 255);
                int lastIndex = path.Count - 1;

                for (int i = startIndex; i <= lastIndex; i++)
                {
                    Vector2Int cell = path[i];

                    // Геометрически точная заливка: закрашиваются только те
                    // тексели, чьи центры лежат внутри клетки пути. Обратная
                    // проекция границ клетки, зеркальная формулам RenderRegion:
                    //   worldX = cx + (px + 0.5 - texW/2) * cp
                    //   worldY = cy + (texH/2 - 0.5 - py) * cp
                    float pxLo = (cell.x - _viewCenterX) / _cellsPerPixel + width * 0.5f - 0.5f;
                    float pxHi = (cell.x + 1f - _viewCenterX) / _cellsPerPixel + width * 0.5f - 0.5f;
                    float pyHi = height * 0.5f - 0.5f - (cell.y - _viewCenterY) / _cellsPerPixel;
                    float pyLo = height * 0.5f - 0.5f - (cell.y + 1f - _viewCenterY) / _cellsPerPixel;

                    int x0 = Mathf.CeilToInt(pxLo);
                    int x1 = Mathf.FloorToInt(pxHi);
                    int y0 = Mathf.CeilToInt(pyLo);
                    int y1 = Mathf.FloorToInt(pyHi);

                    // Вырожденная область (граница легла ровно на центр текселя):
                    // рисуем одиночный тексель по середине диапазона.
                    if (x1 < x0)
                    {
                        x0 = x1 = Mathf.RoundToInt((pxLo + pxHi) * 0.5f);
                    }

                    if (y1 < y0)
                    {
                        y0 = y1 = Mathf.RoundToInt((pyLo + pyHi) * 0.5f);
                    }

                    Color32 color = i == lastIndex ? targetColor : pathColor;
                    for (int y = Mathf.Max(y0, 0); y <= Mathf.Min(y1, height - 1); y++)
                    {
                        int rowStart = y * width;
                        for (int x = Mathf.Max(x0, 0); x <= Mathf.Min(x1, width - 1); x++)
                        {
                            colors[rowStart + x] = color;
                        }
                    }
                }
            }

            // Клетка робота в оверлее прозрачна: красный маркер из текстуры карты
            // всегда преобладает над нитью маршрута.
            ILocalPlayer? localPlayer = _playerTracker?.CurrentPlayer;
            if (localPlayer is { HasServerPosition: true })
            {
                Vector2Int playerPos = localPlayer.Position;
                float pxLo = (playerPos.x - _viewCenterX) / _cellsPerPixel + width * 0.5f - 0.5f;
                float pxHi = (playerPos.x + 1f - _viewCenterX) / _cellsPerPixel + width * 0.5f - 0.5f;
                float pyHi = height * 0.5f - 0.5f - (playerPos.y - _viewCenterY) / _cellsPerPixel;
                float pyLo = height * 0.5f - 0.5f - (playerPos.y + 1f - _viewCenterY) / _cellsPerPixel;

                int x0 = Mathf.Max(0, Mathf.CeilToInt(pxLo));
                int x1 = Mathf.Min(width - 1, Mathf.FloorToInt(pxHi));
                int y0 = Mathf.Max(0, Mathf.CeilToInt(pyLo));
                int y1 = Mathf.Min(height - 1, Mathf.FloorToInt(pyHi));
                for (int y = y0; y <= y1; y++)
                {
                    int rowStart = y * width;
                    for (int x = x0; x <= x1; x++)
                    {
                        colors[rowStart + x] = default;
                    }
                }
            }

            texture.SetPixelData(colors, 0);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            _panel.PathOverlay?.MarkDirtyRepaint();
        }

        private void OnWalkerPathChanged(IReadOnlyList<Vector2Int>? path)
        {
            _clickPath = path;
            _renderRequested = true;
        }

        private void HandleQueuedRender()
        {
            IWorldDataStorage storage = _storage ??
                throw new InvalidOperationException("WorldMapRenderer storage is not initialized.");
            if (storage.Revision != _lastRenderedStorageRevision)
            {
                _renderRequested = true;
            }

            if (!ReferenceEquals(_layerBinding.CellLayer, storage.CellLayer))
            {
                _layerBinding.BindCellLayer(storage.CellLayer);
                _layerBinding.BindMipScan(_viewportRenderer.CellColorTable);
                _renderRequested = true;
                _lastRenderedStorageRevision = -1;
            }

            if (_manager != null && !_bounds.Matches(_manager))
            {
                ResetWorldViewState(storage);
            }

            if (!_renderRequested)
            {
                return;
            }

            UpdateMipStatus();
            if (!_mipScan.IsReady && _cellsPerPixel >= _layerBinding.ChunkSize)
            {
                _mipScan.Begin();
                return;
            }

            if (_manager == null || _storage == null)
            {
                return;
            }

            _viewportRenderer.Render(
                _textureController.MapTexture,
                _manager,
                _cellSampler,
                _mipScan.IsReady ? _mipScan.Cache : null,
                _textureController.TexWidth,
                _textureController.TexHeight,
                _cellsPerPixel,
                _viewCenterX,
                _viewCenterY);

            _panel.Image?.MarkDirtyRepaint();
            _renderRequested = false;
            _lastRenderedStorageRevision = _storage.Revision;
            UpdatePathOverlay();
        }

        private void UpdatePlayerMarker()
        {
            ILocalPlayer? player = _playerTracker?.CurrentPlayer;
            if (player is { HasServerPosition: true })
            {
                Vector2Int pos = player.Position;
                _panel.UpdatePlayerMarker(
                    pos.x,
                    pos.y,
                    _viewCenterX,
                    _viewCenterY,
                    _cellsPerPixel,
                    _textureController.TexWidth,
                    _textureController.TexHeight,
                    _playerTracker!.PlayerBlinkState);
            }
            else
            {
                _panel.HidePlayerMarker();
            }
        }

        private void ClampViewCenter()
        {
            if (_manager == null)
            {
                return;
            }

            _bounds.Clamp(
                ref _viewCenterX,
                ref _viewCenterY,
                _cellsPerPixel,
                _textureController.TexWidth,
                _textureController.TexHeight);
        }
    }
}

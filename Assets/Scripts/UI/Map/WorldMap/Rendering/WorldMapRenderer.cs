#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Interfaces;
using Kern.World;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Kern.UI.Map
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Kern.UI", sourceAssembly: null, sourceClassName: "WorldMapRenderer")]
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
        private readonly WorldMapMipSource _mipSource = new();
        private WorldMapLayerBinding _layerBinding = null!;
        private WorldMapRenderCoordinator _renderCoordinator = null!;
        private MapPlayerTracker _playerTracker = null!;
        private VisualElement? _documentRoot;

        private float _viewCenterX;
        private float _viewCenterY;
        private float _cellsPerPixel = 1f;
        private float _maxCellsPerPixel = 10f;

        // Remaining click route drawn over the map (transparent overlay texture).
        private readonly WorldMapPathOverlay _pathOverlay = new();
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
        private bool _followPlayer = true;
        private readonly WorldMapBounds _bounds = new();
        private bool _isInitialized;

        [Inject]
        private MapModeState _mapModeState = null!;

        public event Action? CloseRequested;

        protected void Start()
        {
            _documentRoot = _injectedDocument.rootVisualElement;
            if (_documentRoot.panel == null)
            {
                _documentRoot.RegisterCallback<AttachToPanelEvent>(OnDocumentAttached);
            }

            _mipSource.SetRequestRenderCallback(RequestRender);
            _pointerBinder = new WorldMapPointerBinder(_panel);
            _inputDispatcher = new WorldMapInputDispatcher(
                _interaction, _panel, _textureController, ClampViewCenter, RequestRender);
            _layerBinding = new WorldMapLayerBinding(
                _cellSampler,
                _mipSource,
                RequestRender);
            _renderCoordinator = new WorldMapRenderCoordinator(
                _panel,
                _viewportRenderer,
                _textureController,
                _mipSource,
                _layerBinding,
                _bounds,
                _cellSampler,
                _pathOverlay,
                ResetWorldViewState);
            _playerTracker = new MapPlayerTracker(_localPlayer);
            _playerTracker.OnPlayerSpawned += () =>
            {
                BindPathWalker(_localPlayer.Current as IClickPathWalker);
                _renderRequested = true;
            };
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
                BindPathWalker(_localPlayer.Current as IClickPathWalker);
                if (_followPlayer)
                {
                    _viewCenterX = pos.x;
                    _viewCenterY = pos.y;
                    WorldMapViewportReset.Clamp(
                        _bounds, ref _viewCenterX, ref _viewCenterY, _cellsPerPixel,
                        _textureController.TexWidth, _textureController.TexHeight);
                    _renderRequested = true;
                }
            };
            _playerTracker.OnBlinkFlipped += UpdatePlayerMarker;

            TryInitialize();
            _manager.OnWorldInitialized += OnWorldReady;
        }

        private bool IsWorldReady() =>
            _manager.IsWorldInitialized && _storage.IsReady;

        private void OnWorldReady()
        {
            if (!IsWorldReady())
            {
                return;
            }

            if (!_isInitialized)
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
            if (_isInitialized)
            {
                RebindRuntimeSources();
            }
        }

        private void TryInitialize()
        {
            if (_isInitialized || !IsWorldReady())
            {
                return;
            }

            if (!TryBindUI())
            {
                return;
            }

            _pointerBinder.Bind(OnMapPointerDown, OnMapPointerMove, OnMapPointerUp, OnMapPointerCancel, OnMapClick);

            BindPathWalker(_localPlayer.Current as IClickPathWalker);

            _playerTracker ??= new MapPlayerTracker(_localPlayer);
            _playerTracker.EnsureBinding();

            InitTexture();
            ResetWorldViewState(_storage);

            _isInitialized = true;
            RebindRuntimeSources();

            if (_mapModeState != null && _mapModeState.IsOpen)
            {
                Show();
            }
            else
            {
                Hide();
            }
        }

        private bool TryBindUI()
        {
            // The panel may not be ready on the first attempt; it retries on demand.
            return _panel.TryBind(
                _injectedDocument,
                OnCloseButtonClicked,
                FollowPlayer,
                () => ZoomStep(-ZoomButtonStep),
                () => ZoomStep(ZoomButtonStep),
                OnWorldMapWheel,
                OnPanelGeometryChanged);
        }

        private void OnPanelGeometryChanged()
        {
            if (!_isInitialized || !enabled)
            {
                return;
            }

            if (_panel.Image != null && _textureController.CheckPanelResize(_panel.Image))
            {
                InitTexture();
                _maxCellsPerPixel = _bounds.ComputeMaxZoomOut(
                    _textureController.TexWidth,
                    _textureController.TexHeight);
                _cellsPerPixel = Mathf.Min(_cellsPerPixel, _maxCellsPerPixel);
                WorldMapViewportReset.Clamp(
                        _bounds, ref _viewCenterX, ref _viewCenterY, _cellsPerPixel,
                        _textureController.TexWidth, _textureController.TexHeight);
                _renderRequested = true;
            }

            UpdatePlayerMarker();
        }

        private void BindPathWalker(IClickPathWalker? walker)
        {
            if (ReferenceEquals(_pathWalker, walker))
            {
                return;
            }

            UnbindPathWalker();

            if (walker != null)
            {
                _pathWalker = walker;
                walker.OnPathChanged += OnWalkerPathChanged;
            }
        }

        private void UnbindPathWalker()
        {
            if (_pathWalker != null)
            {
                _pathWalker.OnPathChanged -= OnWalkerPathChanged;
                _pathWalker = null;
            }
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
            WorldMapViewportReset.Clamp(
                        _bounds, ref _viewCenterX, ref _viewCenterY, _cellsPerPixel,
                        _textureController.TexWidth, _textureController.TexHeight);
            _renderRequested = true;
        }

        private void RequestRender() => _renderRequested = true;

        // Кнопки «±» карты (макет: .map-tools) — шаг колеса от центра карты
        private const float ZoomButtonStep = 3f;

        private void ZoomStep(float delta) =>
            _inputDispatcher.HandleZoomStep(delta, _maxCellsPerPixel, ref _cellsPerPixel, ref _viewCenterX, ref _viewCenterY);

        private void OnWorldMapWheel(WheelEvent evt) =>
            _inputDispatcher.HandleWheel(evt, _maxCellsPerPixel, ref _cellsPerPixel, ref _viewCenterX, ref _viewCenterY);

        private void OnMapClick(ClickEvent evt) => _inputDispatcher.HandleClick(
            evt,
            _viewCenterX,
            _viewCenterY,
            _cellsPerPixel,
            _localPlayer.Current as IClickPathWalker);

        private void OnMapPointerDown(PointerDownEvent evt) =>
            _inputDispatcher.HandlePointerDown(evt);

        private void OnMapPointerMove(PointerMoveEvent evt) =>
            _inputDispatcher.HandlePointerMove(evt, _maxCellsPerPixel, ref _cellsPerPixel, _dragSpeed, ref _viewCenterX, ref _viewCenterY, ref _followPlayer);

        private void OnMapPointerUp(PointerUpEvent evt) =>
            _inputDispatcher.HandlePointerUp(evt);

        private void OnMapPointerCancel(PointerCancelEvent evt) => _inputDispatcher.Reset();

        private void ResetWorldViewState(IWorldDataStorage storage) => WorldMapViewportReset.Apply(
            _bounds,
            _manager,
            _viewportRenderer,
            _layerBinding,
            storage,
            _textureController,
            _playerTracker,
            ref _cellsPerPixel,
            ref _maxCellsPerPixel,
            ref _viewCenterX,
            ref _viewCenterY,
            ref _renderRequested);

        protected void OnDestroy()
        {
            if (_documentRoot != null)
            {
                _documentRoot.UnregisterCallback<AttachToPanelEvent>(OnDocumentAttached);
            }

            _pointerBinder?.Dispose();

            UnbindPathWalker();
            _pathOverlay.Dispose();

            _panel.Dispose();
            _textureController.DestroyTexture();
            _viewportRenderer.Dispose();

            _manager.OnWorldInitialized -= OnWorldReady;

            _playerTracker?.Dispose();

            _layerBinding?.Dispose();
            _mipSource.Dispose();
        }

        private void RebindRuntimeSources()
        {
            if (_storage == null)
            {
                return;
            }

            MapManager manager = _manager ?? throw new InvalidOperationException(
                "WorldMapRenderer cannot rebind runtime sources before MapManager injection.");

            _playerTracker?.EnsureBinding();
            BindPathWalker(_localPlayer?.Current as IClickPathWalker);
            _layerBinding.BindStorage(_storage);

            if (_storage.CellLayer == null)
            {
                _layerBinding.BindCellLayer(null);
                return;
            }

            IWorldLayer<CellType> cellLayer = _storage.CellLayer;
            if (_layerBinding.BindCellLayer(cellLayer))
            {
                _layerBinding.BindMipScan(manager.WorldWidth, manager.WorldHeight, _viewportRenderer.CellColorTable);
                return;
            }

            _layerBinding.RebindCellEvents();
        }

        protected void Update()
        {
            if (!enabled || !_isInitialized)
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
                WorldMapViewportReset.Clamp(
                        _bounds, ref _viewCenterX, ref _viewCenterY, _cellsPerPixel,
                        _textureController.TexWidth, _textureController.TexHeight);
                _renderRequested = true;
            }

            _playerTracker.Update(
                Time.deltaTime,
                _followPlayer,
                ref _viewCenterX,
                ref _viewCenterY,
                ref _renderRequested);

            _mipSource.ApplyPending();

            UpdatePlayerMarker();

            HandleQueuedRender();
        }

        public void Show()
        {
            if (!_isInitialized)
            {
                TryInitialize();
                if (!_isInitialized)
                {
                    return;
                }
            }

            if (_storage == null || _manager == null || _panel.Overlay == null)
            {
                return;
            }

            _panel.Show();

            enabled = true;
            _renderRequested = true;
            _followPlayer = true;
            _playerTracker?.ResetState();
            UpdatePlayerMarker();
        }

        public void Hide()
        {
            _inputDispatcher.Reset();
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
            WorldMapViewportReset.Clamp(
                        _bounds, ref _viewCenterX, ref _viewCenterY, _cellsPerPixel,
                        _textureController.TexWidth, _textureController.TexHeight);
        }

        private void InitTexture()
        {
            Image viewport = _panel.Image ?? throw new InvalidOperationException(
                "[WorldMapRenderer] UI must be bound before the map texture.");
            _viewportRenderer.InvalidateViewState();
            _textureController.InitTexture(viewport, viewport);
            _pathOverlay.Ensure(_panel, _textureController.TexWidth, _textureController.TexHeight);
        }

        private void OnWalkerPathChanged(IReadOnlyList<Vector2Int>? _) => _renderRequested = true;

        private void HandleQueuedRender() => _renderCoordinator.Render(
            _storage,
            _manager,
            _pathWalker,
            _playerTracker?.CurrentPlayer,
            _cellsPerPixel,
            _viewCenterX,
            _viewCenterY,
            ref _renderRequested);

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

        private void UpdatePlayerMarker() => _panel.UpdatePlayerMarker(
            _playerTracker?.CurrentPlayer,
            _viewCenterX,
            _viewCenterY,
            _cellsPerPixel,
            _textureController.TexWidth,
            _textureController.TexHeight,
            _playerTracker?.PlayerBlinkState ?? false);


    }
}

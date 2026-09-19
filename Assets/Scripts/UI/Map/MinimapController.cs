#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.World;
using Kern.Player.Logic;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VContainer;

namespace Kern.UI
{
    public class MinimapController : MonoBehaviour
    {
        [SerializeField]
        private int _uiSize = 160;

        // UI Toolkit
        [Inject]
        private UIDocument _doc = null!;
        [Inject]
        private MapModeState _mapModeState = null!;
        [Inject]
        private ILocalPlayerState _localPlayer = null!;
        private MinimapView? _view;
        private Texture2D? _minimapTexture;

        // World state
        private ILocalPlayer? _player;
        [Inject]
        private MapStorage _mapStorage = null!;

        [Inject]
        private MapManager _mapManager = null!;
        private IWorldLayer<CellType>? _cellLayer;
        private int _worldWidth;
        private int _worldHeight;

        private MinimapTextureRenderer? _textureRenderer;
        private readonly MapCellSampler _cellSampler = new();

        // Refresh & throttle state
        private readonly MinimapRefreshPolicy _refreshPolicy = new();
        private bool _ready;
        private bool _lastRefreshHadLoadedCells;
        private IWorldLayer<CellType>? _subscribedCellLayer;
        private bool _playerMoveSubscribed;

        // Toggle state
        private bool _isVisible = true;
        private bool _uiCreated;

        protected void Start()
        {
            if (_uiSize < 3)
            {
                throw new InvalidOperationException(
                    $"Minimap size must be at least 3 pixels for the player marker; got {_uiSize}.");
            }

            _textureRenderer = new MinimapTextureRenderer(_uiSize);

            _minimapTexture = RuntimeTextureFactory.CreateRGBA32NoMip(
                _uiSize,
                _uiSize,
                "MinimapTexture",
                RuntimeTextureColorSpace.Srgb,
                FilterMode.Point,
                TextureWrapMode.Clamp);

            CreateUI();
            _mapModeState.Changed += OnMapModeChanged;

            if (_mapManager != null)
            {
                _mapManager.OnWorldInitialized += OnWorldReady;
                _mapManager.OnWorldDataLoaded += OnWorldReady;
            }

            if (IsWorldReady())
            {
                OnWorldReady();
            }

            _player = _localPlayer.Current;
            if (_player != null)
            {
                BindPlayer(_player);
            }
            else
            {
                _localPlayer.Changed += OnPlayerChanged;
            }
        }

        private bool IsWorldReady() =>
            _mapManager != null && _mapManager.IsWorldInitialized &&
            _mapStorage != null && _mapStorage.IsReady;

        private void OnWorldReady()
        {
            if (!IsWorldReady())
            {
                return;
            }

            if (_mapManager != null)
            {
                _mapManager.OnWorldInitialized -= OnWorldReady;
                _mapManager.OnWorldDataLoaded -= OnWorldReady;
            }

            TryInitialize();
        }

        private void OnPlayerChanged(ILocalPlayer? player)
        {
            _localPlayer.Changed -= OnPlayerChanged;
            if (player == null)
            {
                return;
            }

            _player = player;
            BindPlayer(player);
            if (_ready)
            {
                _view?.UpdateCoordinates(_player.Position.x, _player.Position.y);
                if (_isVisible)
                {
                    RefreshTexture(_player.Position.x, _player.Position.y);
                    _refreshPolicy.RecordInitialRefresh(
                        Time.time,
                        _player.Position,
                        _mapStorage?.Revision ?? -1,
                        _isVisible,
                        _lastRefreshHadLoadedCells);
                }
            }
        }

        protected void Update()
        {
            if (!_uiCreated)
            {
                CreateUI();
            }

            if (_doc == null || !_doc.enabled)
            {
                return;
            }

            // Инициализации здесь нет и быть не должно. Единственная дорога к
            // ней — OnWorldReady, подписанный в Start на OnWorldInitialized и
            // OnWorldDataLoaded: он сам проверяет готовность и отписывается
            // только когда она действительно наступила, поэтому раннее событие
            // при неготовом хранилище просто дождётся следующего. Прежний
            // per-frame ретрай дублировал эту дорогу и прятал её отказ —
            // если бы события не пришли, никто бы этого не заметил.
            if (_ready && _mapStorage != null &&
                !ReferenceEquals(_cellLayer, _mapStorage.CellLayer))
            {
                _ready = false;
                InitializeWorldState();
            }

            if (_player != null && _player.HasServerPosition)
            {
                long currentRevision = _mapStorage != null ? _mapStorage.Revision : -1;
                if (!_refreshPolicy.InitialRefreshDone && _ready)
                {
                    _view?.UpdateCoordinates(_player.Position.x, _player.Position.y);
                    if (_isVisible)
                    {
                        RefreshTexture(_player.Position.x, _player.Position.y);
                    }

                    _refreshPolicy.RecordInitialRefresh(
                        Time.time,
                        _player.Position,
                        currentRevision,
                        _isVisible,
                        _lastRefreshHadLoadedCells);
                }
                else if (_refreshPolicy.ShouldRefreshOnStorageOrMove(Time.time, currentRevision, _ready, _isVisible, true))
                {
                    _cellSampler.Invalidate();
                    RefreshTexture(_player.Position.x, _player.Position.y);
                    _refreshPolicy.RecordRefresh(Time.time, currentRevision, _lastRefreshHadLoadedCells);
                }
                else if (_refreshPolicy.ShouldRefreshOnChunkLoad(Time.time, _ready, _isVisible, true))
                {
                    RefreshTexture(_player.Position.x, _player.Position.y);
                    MapStorage storage = _mapStorage ??
                        throw new InvalidOperationException("Minimap storage was lost after a chunk loaded.");
                    _refreshPolicy.RecordChunkLoadRefresh(Time.time, storage.Revision, _lastRefreshHadLoadedCells);
                }
            }

            if (Keyboard.current != null && Keyboard.current.nKey.wasPressedThisFrame)
            {
                ToggleVisibility();
            }
        }

        private void TryInitialize()
        {
            if (_mapManager == null || !_mapManager.IsWorldInitialized)
            {
                return;
            }

            if (_mapStorage == null || !_mapStorage.IsReady)
            {
                return;
            }

            ILocalPlayer? localPlayer = _localPlayer.Current;
            if (localPlayer != null)
            {
                BindPlayer(localPlayer);
            }

            if (!_ready)
            {
                InitializeWorldState();
            }

            if (_player != null && _player.HasServerPosition && !_refreshPolicy.InitialRefreshDone)
            {
                _view?.UpdateCoordinates(_player.Position.x, _player.Position.y);
                if (_isVisible)
                {
                    RefreshTexture(_player.Position.x, _player.Position.y);
                }

                _refreshPolicy.RecordInitialRefresh(
                    Time.time,
                    _player.Position,
                    _mapStorage.Revision,
                    _isVisible,
                    _lastRefreshHadLoadedCells);
            }
        }

        private void InitializeWorldState()
        {
            if (_mapStorage == null || _mapManager == null)
            {
                return;
            }

            _cellLayer = _mapStorage.CellLayer;
            if (_cellLayer == null)
            {
                return;
            }

            if (!ReferenceEquals(_subscribedCellLayer, _cellLayer))
            {
                if (_subscribedCellLayer != null)
                {
                    _subscribedCellLayer.ChunkLoaded -= OnChunkLoaded;
                }

                _subscribedCellLayer = _cellLayer;
                _subscribedCellLayer.ChunkLoaded += OnChunkLoaded;
                _cellSampler.Bind(_cellLayer);
                _cellSampler.Invalidate();
                _refreshPolicy.InvalidateStorageRevision();
            }

            _worldWidth = _mapManager.WorldWidth;
            _worldHeight = _mapManager.WorldHeight;
            _textureRenderer?.CacheCellColors(_mapManager);
            _ready = true;
            SetVisible(_isVisible);
        }

        private void CreateUI()
        {
            if (_uiCreated)
            {
                return;
            }

            if (_doc == null || _doc.rootVisualElement == null)
            {
                // Не бросаем: UIDocument может появиться после этого Start (PostStart-
                // инъекция или аддитивная загрузка сцены); Update ретраит CreateUI —
                // ждём молча, иначе первый кадр роняет клиент.
                return;
            }

            _view = MinimapView.Create(
                _doc,
                _minimapTexture ?? throw new InvalidOperationException("Minimap texture is required."),
                () => _mapModeState.SetOpen(true));

            _isVisible = true;
            _uiCreated = true;
            if (_ready)
            {
                SetVisible(_isVisible);
            }
        }

        protected void OnEnable()
        {
            if (_ready)
            {
                RebindRuntimeSources();
                SetVisible(_isVisible);
            }
        }

        protected void OnDisable()
        {
            SetVisible(false);
        }

        private void BindPlayer(ILocalPlayer player)
        {
            if (ReferenceEquals(_player, player) && _playerMoveSubscribed)
            {
                return;
            }

            if (_playerMoveSubscribed && _player != null)
            {
                _player.OnPlayerMoved -= OnPlayerMoved;
            }

            _player = player;
            _player.OnPlayerMoved -= OnPlayerMoved;
            _player.OnPlayerMoved += OnPlayerMoved;
            _playerMoveSubscribed = true;
        }

        private void RebindRuntimeSources()
        {
            if (_mapManager == null || _mapStorage == null)
            {
                _ready = false;
                return;
            }

            if (_localPlayer == null)
            {
                _ready = false;
                return;
            }

            _localPlayer.Changed -= OnPlayerChanged;
            if (_playerMoveSubscribed && _player != null)
            {
                _player.OnPlayerMoved -= OnPlayerMoved;
                _playerMoveSubscribed = false;
            }

            _player = _localPlayer.Current;
            if (_player != null)
            {
                BindPlayer(_player);
            }
            else
            {
                _localPlayer.Changed += OnPlayerChanged;
            }

            if (_subscribedCellLayer != null)
            {
                _subscribedCellLayer.ChunkLoaded -= OnChunkLoaded;
                _subscribedCellLayer = null;
            }

            _cellLayer = null;
            _cellSampler.Bind(null);
            _cellSampler.Invalidate();
            _refreshPolicy.Reset();
            _ready = false;
            InitializeWorldState();
        }

        private void OnPlayerMoved(Vector2Int oldPos, Vector2Int newPos)
        {
            if (!isActiveAndEnabled || !_ready)
            {
                return;
            }

            if (_player != null)
            {
                _view?.UpdateCoordinates(_player.Position.x, _player.Position.y);
            }

            if (!_isVisible)
            {
                return;
            }

            _refreshPolicy.NotifyPlayerMoved(newPos, Time.time, out bool shouldRefreshNow);
            if (shouldRefreshNow)
            {
                RefreshTexture(newPos.x, newPos.y);
                MapStorage storage = _mapStorage ??
                    throw new InvalidOperationException("Minimap storage was lost during refresh.");
                _refreshPolicy.RecordRefresh(Time.time, storage.Revision, _lastRefreshHadLoadedCells);
            }
        }

        private void OnChunkLoaded(int serverX, int serverY, int width, int height)
        {
            // Only the loaded chunk changed. Dropping the whole sampler here
            // made every load re-request every chunk under the minimap.
            _cellSampler.InvalidateChunk(serverX, serverY);
            _refreshPolicy.NotifyChunkLoaded();
        }

        private void RefreshTexture(int playerX, int playerY)
        {
            if (_textureRenderer == null)
            {
                return;
            }

            _lastRefreshHadLoadedCells = _textureRenderer.Render(
                _minimapTexture,
                playerX,
                playerY,
                _worldWidth,
                _worldHeight,
                _cellSampler);
        }

        protected void OnDestroy()
        {
            if (_mapModeState != null)
            {
                _mapModeState.Changed -= OnMapModeChanged;
            }

            _localPlayer.Changed -= OnPlayerChanged;

            if (_mapManager != null)
            {
                _mapManager.OnWorldInitialized -= OnWorldReady;
                _mapManager.OnWorldDataLoaded -= OnWorldReady;
            }

            if (_player != null)
            {
                _player.OnPlayerMoved -= OnPlayerMoved;
                _playerMoveSubscribed = false;
            }

            if (_subscribedCellLayer != null)
            {
                _subscribedCellLayer.ChunkLoaded -= OnChunkLoaded;
                _subscribedCellLayer = null;
            }

            _view?.Dispose();
            _view = null;

            if (_minimapTexture != null)
            {
                Destroy(_minimapTexture);
            }
        }

        private void ToggleVisibility()
        {
            _isVisible = !_isVisible;
            SetVisible(_isVisible);
            if (_isVisible && _player != null && _ready)
            {
                RefreshTexture(_player.Position.x, _player.Position.y);
                MapStorage storage = _mapStorage ??
                    throw new InvalidOperationException("Minimap storage was lost while becoming visible.");
                _refreshPolicy.RecordRefresh(Time.time, storage.Revision, _lastRefreshHadLoadedCells);
            }
        }

        private void SetVisible(bool visible)
        {
            _view?.SetVisible(visible);
        }

        private void OnMapModeChanged(bool mapModeEnabled)
        {
            SetVisible(!mapModeEnabled && _isVisible);
        }
    }
}

#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.World;
using Kern.Player.Logic;
using MinesServer.Data;
using UnityEngine;
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
        private IInputBlocker _inputBlocker = null!;
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
        private bool _localPlayerChangeSubscribed;
        private bool _mapDataRefreshPending;

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

            // new Texture2D не инициализирует пиксели, и до первого Refresh
            // панель показала бы неинициализированную память. Заливаем цветом
            // незагруженной клетки: миникарта без данных обязана быть чёрной.
            var unloaded = new Color32[_uiSize * _uiSize];
            Array.Fill(unloaded, new Color32(0, 0, 0, 255));
            _minimapTexture.SetPixelData(unloaded, 0);
            _minimapTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            // Текстура переписывается на каждом рефреше, поэтому динамический атлас
            // UI Toolkit обязан её исключить.
            DynamicAtlasConfigurator.RegisterRuntimeRedrawn(_minimapTexture);

            CreateUI();
            _mapModeState.Changed += OnMapModeChanged;
            SubscribeToLocalPlayerChanges();
            _mapStorage.CellChanged += OnCellChanged;
            _mapStorage.RegionChanged += OnRegionChanged;

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
            if (player == null)
            {
                if (_playerMoveSubscribed && _player != null)
                {
                    _player.OnPlayerMoved -= OnPlayerMoved;
                    _playerMoveSubscribed = false;
                }

                _player = null;
                return;
            }

            _player = player;
            BindPlayer(player);

            if (_ready)
            {
                _view?.UpdateCoordinates(_player.Position.x, _player.Position.y);
                bool minimapVisible = !_mapModeState.IsOpen;
                if (minimapVisible)
                {
                    RefreshTexture(_player.Position.x, _player.Position.y);
                }

                _refreshPolicy.RecordInitialRefresh(
                    Time.time,
                    _player.Position,
                    _mapStorage?.Revision ?? -1,
                    minimapVisible,
                    _lastRefreshHadLoadedCells);
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

            if (_ready && _mapDataRefreshPending && !_mapModeState.IsOpen &&
                _refreshPolicy.CanRefresh(Time.time))
            {
                bool hasServerPosition = _player is { HasServerPosition: true };
                int centerX = hasServerPosition ? _player!.Position.x : _worldWidth / 2;
                int centerY = hasServerPosition ? _player!.Position.y : _worldHeight / 2;
                RefreshTexture(centerX, centerY, drawPlayerMarker: hasServerPosition);
                long revision = _mapStorage?.Revision ?? -1;
                _refreshPolicy.RecordRefresh(Time.time, revision, _lastRefreshHadLoadedCells);
                _mapDataRefreshPending = false;
            }

            if (_player != null && _player.HasServerPosition)
            {
                long currentRevision = _mapStorage != null ? _mapStorage.Revision : -1;
                if (!_refreshPolicy.InitialRefreshDone &&
                    _ready &&
                    _refreshPolicy.CanRefresh(Time.time))
                {
                    _view?.UpdateCoordinates(_player.Position.x, _player.Position.y);
                    bool minimapVisible = !_mapModeState.IsOpen;
                    if (minimapVisible)
                    {
                        RefreshTexture(_player.Position.x, _player.Position.y);
                    }

                    _refreshPolicy.RecordInitialRefresh(
                        Time.time,
                        _player.Position,
                        currentRevision,
                        minimapVisible,
                        _lastRefreshHadLoadedCells);
                }
                else if (_refreshPolicy.ShouldRefreshOnStorageOrMove(
                    Time.time,
                    currentRevision,
                    _ready,
                    !_mapModeState.IsOpen,
                    true))
                {
                    _cellSampler.Invalidate();
                    RefreshTexture(_player.Position.x, _player.Position.y);
                    _refreshPolicy.RecordRefresh(Time.time, currentRevision, _lastRefreshHadLoadedCells);
                }
                else if (_refreshPolicy.ShouldRefreshOnChunkLoad(
                    Time.time,
                    _ready,
                    !_mapModeState.IsOpen,
                    true))
                {
                    RefreshTexture(_player.Position.x, _player.Position.y);
                    MapStorage storage = _mapStorage ??
                        throw new InvalidOperationException("Minimap storage was lost after a chunk loaded.");
                    _refreshPolicy.RecordChunkLoadRefresh(Time.time, storage.Revision, _lastRefreshHadLoadedCells);
                }
            }

        }

        private void TryInitialize()
        {
            if (_localPlayer == null || _mapModeState == null)
            {
                return;
            }

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
                bool minimapVisible = !_mapModeState.IsOpen;
                if (minimapVisible)
                {
                    RefreshTexture(_player.Position.x, _player.Position.y);
                }

                _refreshPolicy.RecordInitialRefresh(
                    Time.time,
                    _player.Position,
                    _mapStorage.Revision,
                    minimapVisible,
                    _lastRefreshHadLoadedCells);
            }
        }

        private void InitializeWorldState()
        {
            if (_mapModeState == null || _mapStorage == null || _mapManager == null)
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
            RefreshTexture(_worldWidth / 2, _worldHeight / 2, drawPlayerMarker: false);
            SetVisible(!_mapModeState.IsOpen);
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
                RequestOpenMap);

            _uiCreated = true;
            if (_ready)
            {
                SetVisible(!_mapModeState.IsOpen);
            }
        }

        private void RequestOpenMap()
        {
            if (!_inputBlocker.IsInputBlockedExcludingMapMode)
            {
                _mapModeState.SetOpen(true);
            }
        }

        protected void OnEnable()
        {
            if (_mapModeState == null)
            {
                return;
            }

            SubscribeToLocalPlayerChanges();
            if (_ready)
            {
                RebindRuntimeSources();
                SetVisible(!_mapModeState.IsOpen);
            }
        }

        protected void OnDisable()
        {
            UnsubscribeFromLocalPlayerChanges();
            SetVisible(false);
        }

        private void SubscribeToLocalPlayerChanges()
        {
            if (_localPlayer == null || _localPlayerChangeSubscribed)
            {
                return;
            }

            _localPlayer.Changed += OnPlayerChanged;
            _localPlayerChangeSubscribed = true;
        }

        private void UnsubscribeFromLocalPlayerChanges()
        {
            if (_localPlayer == null || !_localPlayerChangeSubscribed)
            {
                return;
            }

            _localPlayer.Changed -= OnPlayerChanged;
            _localPlayerChangeSubscribed = false;
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

            UnsubscribeFromLocalPlayerChanges();
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

            SubscribeToLocalPlayerChanges();

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

            if (_mapModeState.IsOpen)
            {
                return;
            }

            long movedCells = Math.Abs((long)newPos.x - oldPos.x) +
                Math.Abs((long)newPos.y - oldPos.y);
            if (movedCells > 1)
            {
                _cellSampler.Invalidate();
                RefreshTexture(newPos.x, newPos.y);
                MapStorage storage = _mapStorage ??
                    throw new InvalidOperationException("Minimap storage was lost during relocation.");
                _refreshPolicy.RecordRefresh(Time.time, storage.Revision, _lastRefreshHadLoadedCells);
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
            _mapDataRefreshPending = true;
        }

        private void OnCellChanged(int serverX, int serverY)
        {
            _cellSampler.InvalidateChunk(serverX, serverY);
            _mapDataRefreshPending = true;
        }

        private void OnRegionChanged(int startX, int startY, int width, int height)
        {
            if (width <= 0 || height <= 0 || _cellLayer == null)
            {
                return;
            }

            int chunkSize = _cellLayer.ChunkSize;
            int endX = startX + width - 1;
            int endY = startY + height - 1;
            for (int chunkX = Mathf.Max(0, startX / chunkSize); chunkX <= endX / chunkSize; chunkX++)
            {
                for (int chunkY = Mathf.Max(0, startY / chunkSize); chunkY <= endY / chunkSize; chunkY++)
                {
                    _cellSampler.InvalidateChunk(chunkX * chunkSize, chunkY * chunkSize);
                }
            }

            _mapDataRefreshPending = true;
        }

        private void RefreshTexture(int playerX, int playerY, bool drawPlayerMarker = true)
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
                _cellSampler,
                drawPlayerMarker);

            _view?.MarkDirty();
        }

        protected void OnDestroy()
        {
            if (_mapModeState != null)
            {
                _mapModeState.Changed -= OnMapModeChanged;
            }

            UnsubscribeFromLocalPlayerChanges();

            if (_mapStorage != null)
            {
                _mapStorage.CellChanged -= OnCellChanged;
                _mapStorage.RegionChanged -= OnRegionChanged;
            }

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

        private void SetVisible(bool visible)
        {
            _view?.SetVisible(visible);
        }

        private void OnMapModeChanged(bool mapModeEnabled)
        {
            SetVisible(!mapModeEnabled);
            if (!mapModeEnabled && _ready && _player is { HasServerPosition: true })
            {
                RefreshTexture(_player.Position.x, _player.Position.y);
                _refreshPolicy.RecordRefresh(Time.time, _mapStorage.Revision, _lastRefreshHadLoadedCells);
            }
        }
    }
}

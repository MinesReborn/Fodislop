#nullable enable

using System;
using System.Collections.Generic;
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

        [Inject]
        private UIDocument _doc = null!;
        [Inject]
        private MapModeState _mapModeState = null!;
        [Inject]
        private IInputBlocker _inputBlocker = null!;
        [Inject]
        private ILocalPlayerState _localPlayer = null!;
        private MinimapUIController? _ui;
        private RenderTexture? _minimapTexture;

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
        private MinimapCellInvalidation _cellInvalidation = null!;
        private readonly MinimapRefreshPolicy _refreshPolicy = new();
        private readonly MinimapRefreshLoop _refreshLoop = new();
        private bool _ready;
        private bool _lastRefreshHadLoadedCells;
        private IWorldLayer<CellType>? _subscribedCellLayer;
        private bool _playerMoveSubscribed;
        private bool _localPlayerChangeSubscribed;

        // Остаток клик-маршрута для отрисовки нитью на миникарте (null - нет).
        private IReadOnlyList<Vector2Int>? _clickPath;
        private IClickPathWalker? _pathWalker;

        protected void Start()
        {
            _cellInvalidation = new MinimapCellInvalidation(_cellSampler);
            if (_uiSize < 3)
            {
                throw new InvalidOperationException(
                    $"Minimap size must be at least 3 pixels for the player marker; got {_uiSize}.");
            }

            _textureRenderer = new MinimapTextureRenderer(_uiSize);

            _minimapTexture = new RenderTexture(_uiSize, _uiSize, 0, RenderTextureFormat.ARGB32)
            {
                name = "MinimapRenderTexture",
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            _minimapTexture.Create();

            _ui = new MinimapUIController(_doc, _minimapTexture, _textureRenderer.PathOverlay, RequestMinimapMove);
            _ui.TryCreate();
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

            if (!_ready)
            {
                TryInitialize();
            }
            else
            {
                ReinitializeWorldState();
            }
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

                UnbindPathWalker();
                _player = null;
                return;
            }

            _player = player;
            BindPlayer(player);

            if (_ready)
            {
                _ui?.UpdateCoordinates(_player.Position.x, _player.Position.y);
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
        protected void Update() => _refreshLoop.Process(
                _doc,
                _ui,
                _mapModeState,
                _mapStorage,
                _cellLayer,
                _player,
                _worldWidth,
                _worldHeight,
                _ready,
                _lastRefreshHadLoadedCells,
                _cellInvalidation,
                _refreshPolicy,
                _cellSampler,
                ReinitializeWorldState,
                RefreshTexture,
                SetVisible);
        private void ReinitializeWorldState()
        {
            _ready = false;
            InitializeWorldState();
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
                _ui?.UpdateCoordinates(_player.Position.x, _player.Position.y);
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

        // Клик по блоку миникарты: пиксель текстуры -> серверная клетка
        // относительно центра (робота) -> клик-маршрут, та же логика, что
        // у ЛКМ по миру (PlayerInteractionController.HandleMouseClick).
        private void RequestMinimapMove(int texX, int texY)
        {
            if (_inputBlocker.IsInputBlockedExcludingMapMode)
            {
                return;
            }

            if (_player is not { HasServerPosition: true })
            {
                return;
            }

            if (_textureRenderer == null)
            {
                return;
            }

            Vector2Int server = _textureRenderer.PixelToServerCell(
                texX,
                texY,
                _player.Position.x,
                _player.Position.y);
            if (server.x < 0 || server.y < 0 ||
                server.x >= _worldWidth || server.y >= _worldHeight)
            {
                return;
            }

            (_localPlayer.Current as IClickPathWalker)?.TryStartPath(server);
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

            UnbindPathWalker();

            _player = player;
            _player.OnPlayerMoved -= OnPlayerMoved;
            _player.OnPlayerMoved += OnPlayerMoved;
            _playerMoveSubscribed = true;

            // Клик-маршрут: подписка на изменения и первичный остаток пути
            // (если маршрут уже активен к моменту привязки игрока).
            if (player is IClickPathWalker walker)
            {
                _pathWalker = walker;
                walker.OnPathChanged += OnWalkerPathChanged;
                _clickPath = walker.Path;
            }
        }

        private void UnbindPathWalker()
        {
            if (_pathWalker != null)
            {
                _pathWalker.OnPathChanged -= OnWalkerPathChanged;
                _pathWalker = null;
            }

            _clickPath = null;
        }

        // Маршрут стартовал или снят: мгновенная перерисовка миникарты, чтобы
        // нить пути появилась/исчезла без ожидания троттлинга политики.
        private void OnWalkerPathChanged(IReadOnlyList<Vector2Int>? path)
        {
            _clickPath = path;

            if (!isActiveAndEnabled || !_ready || _mapModeState.IsOpen)
            {
                return;
            }

            if (_player is { HasServerPosition: true })
            {
                RefreshTexture(_player.Position.x, _player.Position.y);
                _refreshPolicy.RecordRefresh(Time.time, _mapStorage?.Revision ?? -1, _lastRefreshHadLoadedCells);
            }
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
                _ui?.UpdateCoordinates(_player.Position.x, _player.Position.y);
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
            _cellInvalidation.OnChunkLoaded(serverX, serverY);
            _refreshPolicy.NotifyChunkLoaded();
        }

        private void OnCellChanged(int serverX, int serverY)
        {
            _cellInvalidation.OnCellChanged(serverX, serverY);
        }

        private void OnRegionChanged(int startX, int startY, int width, int height)
        {
            _cellInvalidation.OnRegionChanged(startX, startY, width, height, _cellLayer);
        }

        private void RefreshTexture(int playerX, int playerY, bool drawPlayerMarker = true)
        {
            if (_textureRenderer == null)
            {
                return;
            }

            // Путь рисуется от текущего индекса: пройденная часть не отображается.
            IReadOnlyList<Vector2Int>? path;
            int pathStart;
            if (_pathWalker != null)
            {
                path = _pathWalker.Path;
                pathStart = _pathWalker.PathIndex;
            }
            else
            {
                path = _clickPath;
                pathStart = 0;
            }

            _lastRefreshHadLoadedCells = _textureRenderer.Render(
                _minimapTexture,
                playerX,
                playerY,
                _worldWidth,
                _worldHeight,
                _cellSampler,
                drawPlayerMarker ? path : null,
                pathStart);

            _ui?.MarkDirty();
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

            _textureRenderer?.Dispose();
            _ui?.Dispose();
            _ui = null;

            UnbindPathWalker();

            if (_minimapTexture != null)
            {
                if (_minimapTexture.IsCreated())
                {
                    _minimapTexture.Release();
                }

                Destroy(_minimapTexture);
                _minimapTexture = null;
            }
        }

        private void SetVisible(bool visible) => _ui?.SetVisible(visible);

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

#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Lifecycle;
using Fodinae.Player;
using Fodinae.Player.Logic;
using Fodinae.World;
using Fodinae.World.Lighting;
using Fodinae.World.Terrain;
using UnityEngine;
using VContainer;

namespace Fodinae.Game.Managers
{
    /// <summary>
    /// Высокоуровневые состояния игрового сеанса.
    /// Расширяют сетевой статус <see cref="MinesServer.Networking.Shared.ConnectionStatus"/>,
    /// разделяя состояния оффлайн режима, подключения, геймплея и дисконнекта.
    /// </summary>
    public enum GameState
    {
        Offline,
        Connecting,
        InGame,
        Disconnected,
    }

    /// <summary>
    /// Единый менеджер жизненного цикла игры и сессии.
    ///
    /// Управляет высокими состояниями сессии и связывает событийно геймплейные подсистемы.
    /// </summary>
    public sealed class GameManager : MonoBehaviour, IWorldReadiness
    {
        public GameState CurrentState { get; private set; } = GameState.Offline;
        public bool IsUIAuthorized { get; private set; }
        public bool IsWorldLoaded { get; private set; }

        public event Action<GameState>? OnGameStateChanged;
        public event Action? OnWorldLoaded;

        [Inject]
        private IAssetLoader _assetLoader = null!;
        [Inject]
        private ITextureService _textureService = null!;
        [Inject]
        private IRobotService _robotService = null!;
        [Inject]
        private ILocalPlayerState _localPlayer = null!;
        [Inject]
        private IPlayerStats _playerStats = null!;
        [Inject]
        private IWorldLoadProgress _loadProgress = null!;
        [Inject]
        private TerrainRenderer _terrainRenderer = null!;
        [Inject]
        private SurfaceRenderer _surfaceRenderer = null!;
        [Inject]
        private LightingEngine _lightingEngine = null!;
        [Inject]
        private ISceneObjectFactory _sceneObjects = null!;

        private GameObject? _uiRoot;
        private bool _worldLoadPending;
        private bool _worldLoadPublished;
        private bool _uiSetup;

        private void OnDestroy()
        {
            if (_uiRoot != null)
            {
                Destroy(_uiRoot);
                _uiRoot = null;
            }
        }

        public void EnsureUISetup()
        {
            if (_uiSetup)
            {
                return;
            }

            try
            {
                SetupUI();
                _uiSetup = true;
            }
            catch
            {
                if (_uiRoot != null)
                {
                    Destroy(_uiRoot);
                    _uiRoot = null;
                }

                _uiSetup = false;
                throw;
            }
        }

        private void SetupUI()
        {
            _uiRoot = _sceneObjects.Create("UIRoot", RuntimeOwner.FloatingUI);
            _uiRoot.SetActive(false);
            _uiRoot.transform.SetParent(transform);
        }

        public void SetState(GameState newState)
        {
            if (CurrentState == newState)
            {
                return;
            }

            CurrentState = newState;
            Debug.Log($"[GameManager] Game state changed to: {newState}");
            OnGameStateChanged?.Invoke(newState);
        }

        private const float ReadinessDiagInterval = 2.5f;
        private float _readinessDiagNextLog;

        public void NotifyWorldLoaded()
        {
            // WorldInit can arrive again after reconnect or an offline-world
            // restart. A published load belongs to the previous world session
            // and must never suppress the next load notification.
            IsWorldLoaded = false;
            _worldLoadPublished = false;
            _worldLoadPending = true;
            _readinessDiagNextLog = Time.unscaledTime + ReadinessDiagInterval;
            _loadProgress.Report(WorldLoadPhase.WorldManifest);
            TryPublishWorldLoaded();
        }

        private void Update()
        {
            if (_worldLoadPending)
            {
                TryPublishWorldLoaded();
            }
        }

        private void TryPublishWorldLoaded()
        {
            if (_worldLoadPublished)
            {
                return;
            }

            ILocalPlayer? player = _localPlayer.Current;
            Robot? robot = player != null ? player.GetComponent<Robot>() : null;
            TerrainRenderer? terrain = _terrainRenderer;
            int pendingAssets = _assetLoader.PendingAssetCount;
            int queuedAssets = _assetLoader.QueuedAssetCount;

            // Re-log the readiness gate roughly every two seconds while the
            // world is pending. The conditions converge at different times
            // (player position, robot meta/visuals and stats latch on packets
            // that arrive after WorldInit), so a one-shot snapshot at WorldInit
            // cannot show what is actually stuck.
            if (Time.unscaledTime >= _readinessDiagNextLog)
            {
                _readinessDiagNextLog = Time.unscaledTime + ReadinessDiagInterval;
                UnityEngine.Debug.Log(
                    $"[GameManager] World readiness gate (t={Time.unscaledTime:F1}s): " +
                    $"player={player != null && player.HasServerPosition}," +
                    $"robotMeta={(robot != null && robot.IsMetadataLoaded)}," +
                    $"robotVisuals={(robot != null && robot.IsVisualsLoaded)}," +
                    $"statsReady={(_playerStats != null && _playerStats.IsReady)}," +
                    $"statsDetail=hp={_playerStats?.MaxHealth}/basket={_playerStats?.BasketCapacity}/nick=({_playerStats?.Nickname})/lvl={_playerStats?.Level}," +
                    $"terrain={(terrain != null && terrain.IsReadyForGameplay)}," +
                    $"surface={(_surfaceRenderer == null || _surfaceRenderer.IsInitialized)}," +
                    $"lighting={(_lightingEngine == null || _lightingEngine.IsInitialized)}," +
                    $"assetPending={pendingAssets}," +
                    $"assetQueued={queuedAssets}," +
                    $"cellTexPending={_textureService.PendingCellTextureRequests}");
            }

            // Publish monotonic loader phases from the gate itself: the same
            // conditions that block WorldReady drive the descent loader, so the
            // MainMenu progress bar reflects real readiness rather than a timer.
            if (player != null && player.HasServerPosition)
            {
                _loadProgress.Report(WorldLoadPhase.SpawnSync);
            }

            if (terrain != null && terrain.IsReadyForGameplay)
            {
                _loadProgress.Report(WorldLoadPhase.TerrainMesh);
            }

            if ((_surfaceRenderer == null || _surfaceRenderer.IsInitialized) &&
                (_lightingEngine == null || _lightingEngine.IsInitialized) &&
                pendingAssets == 0 && queuedAssets == 0 &&
                _textureService.PendingCellTextureRequests == 0)
            {
                _loadProgress.Report(WorldLoadPhase.SurfaceAssets);
            }

            if (player == null || !player.HasServerPosition ||
                robot == null || !robot.IsVisualsLoaded ||
                _playerStats == null || !_playerStats.IsReady ||
                terrain == null || !terrain.IsReadyForGameplay ||
                _lightingEngine == null || !_lightingEngine.IsInitialized ||
                (_surfaceRenderer != null && !_surfaceRenderer.IsInitialized) ||
                (pendingAssets > 0) ||
                (queuedAssets > 0) ||
                _loadProgress == null)
            {
                return;
            }

            _loadProgress.Report(WorldLoadPhase.Done);

            _worldLoadPending = false;
            _worldLoadPublished = true;
            IsWorldLoaded = true;
            Debug.Log($"[Probe] WorldLoaded {UnityEngine.Time.realtimeSinceStartup:F3}");
            SetState(GameState.InGame);
            player.SetGameplayVisible();
            AuthorizeUI();
            int robotCount = _robotService?.RobotCount ?? -1;
            Debug.Log(
                $"[GameManager] World load completed: server position, terrain, shaders and textures are ready. " +
                $"robots={robotCount}, pendingAssets={_assetLoader.PendingAssetCount}, " +
                $"queuedAssets={_assetLoader.QueuedAssetCount}, " +
                $"pendingCellTextures={_textureService.PendingCellTextureRequests}");
            OnWorldLoaded?.Invoke();
        }

        public void AuthorizeUI()
        {
            IsUIAuthorized = true;
            if (_uiRoot != null)
            {
                _uiRoot.SetActive(true);
            }
        }

        public void DeauthorizeUI()
        {
            IsUIAuthorized = false;
            if (_uiRoot != null)
            {
                _uiRoot.SetActive(false);
            }
        }
    }
}

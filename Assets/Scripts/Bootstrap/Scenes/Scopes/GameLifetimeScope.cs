#nullable enable

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Kern.Core.Interfaces;
using Kern.Core.Interfaces.WorldLighting;
using Kern.Core.Lifecycle;
using Kern.Game;
using Kern.Networking;
using Kern.Networking.Connection;
using Kern.Player;
using Kern.Rendering;
using Kern.Rendering.PostProcessing;
using Kern.UI;
using Kern.Game.Inventory;
using Kern.UI.Inventory;
using Kern.Game.Player.Stats;
using Kern.UI.Programmator;
using Kern.World;
using Kern.World.Lighting;
using Kern.World.Terrain;
using global::Kern.Core.Localization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UIElements;
using VContainer;
using VContainer.Unity;

using Kern.Bootstrap.WorldLighting;
using Kern.Core.Rendering;
using Kern.Diagnostics.Scenes;
using Kern.Game.Configuration;
using Kern.Game.Entities.Buildings;
using Kern.Game.Entities.Robots;
using Kern.Game.Events.Playback;
using Kern.Game.Player.Actions;
using Kern.Game.Player.Cameras;
using Kern.Game.Player.Input;
using Kern.Game.Player.Movement;
using Kern.Game.Rendering;
using Kern.Game.Session;
using Kern.Game.Visual.Effects.Cells;
using Kern.Game.Visual.Effects.Pooling;
using Kern.Game.Visual.Effects.Sprites;
using Kern.Networking.Gateways;
using Kern.Networking.Processing;
using Kern.Networking.Status;
using Kern.Rendering.Diagnostics.Profiling;
using Kern.Rendering.Display;
using Kern.UI.Chat;
using Kern.UI.Connection;
using Kern.UI.Diagnostics;
using Kern.UI.HUD;
using Kern.UI.Input;
using Kern.UI.Loading;
using Kern.UI.Map;
using Kern.UI.Missions;
using Kern.UI.Screens;
using Kern.UI.ServerWindows;
using Kern.UI.Settings;
using Kern.UI.WorldLabels;
using Kern.Core;


using Kern.Diagnostics.World;

namespace Kern.Bootstrap.Scenes
{
    [DefaultExecutionOrder(-20000)]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Kern.Core", sourceAssembly: null, sourceClassName: "GameLifetimeScope")]
    public class GameLifetimeScope : TransitionSceneLifetimeScope
    {
        private Scene _ownScene;
        private GameSceneBindings? _sceneBindings;
        private GameSceneBindings SceneBindings => _sceneBindings ??= new GameSceneBindings(_managerBindings, _ownScene);
        private readonly UniTaskCompletionSource _readiness = new();
        private IWorldPresentationState? _presentationState;
        private VisualElement? _gameUIRoot;
        private bool _presentationStable;
        private bool _presentationFailed;
        private bool _hasPublishedPresentation;
        private PlayerStatsModel? _heldPlayerStats;
        private InventoryModel? _heldInventory;
        private bool _presentationDataHeld;
        private bool? _lastAppliedGameUIVisibility;
        private PickingMode? _lastAppliedGameUIPickingMode;

        [SerializeField] private Transform _servicesRoot = null!;
        [SerializeField] private Transform _runtimeRoot = null!;
        [SerializeField] private Transform _robotsRoot = null!;
        [SerializeField] private Transform _buildingsRoot = null!;
        [FormerlySerializedAs("_vfxRoot")]
        [SerializeField] private Transform _visualEffectRoot = null!;
        [SerializeField] private Transform _floatingUIRoot = null!;
        [SerializeField] private Transform _audioEventsRoot = null!;
        [SerializeField] private UIDocument _uiDocument = null!;
        [SerializeField] private Volume _postProcessVolume = null!;
        [SerializeField] private PlayerMovementController _playerMovement = null!;
        [SerializeField] private List<ManagerBinding> _managerBindings = new();
        private bool _isGameUIActive = true;

        public Transform ServicesRoot => _servicesRoot;
        public IReadOnlyList<ManagerBinding> ManagerBindings => _managerBindings;

        protected override void Awake()
        {
            try
            {
                _ownScene = gameObject.scene;
                base.Awake();
            }
            catch (Exception exception)
            {
                _readiness.TrySetException(exception);
                throw;
            }
        }

        protected override void OnDestroy()
        {
            _readiness.TrySetCanceled();
            if (_presentationState != null)
            {
                _presentationState.Changed -= OnPresentationStateChanged;
            }

            ReleasePresentationDataHold();

            base.OnDestroy();
        }

        protected override void Configure(IContainerBuilder builder)
        {
            if (Parent is not BootstrapLifetimeScope)
            {
                throw new InvalidOperationException(
                    "Game scope requires BootstrapLifetimeScope as its runtime parent.");
            }

            _ownScene = gameObject.scene;
            ValidateSceneRoots();
            ValidateServiceGroups();

            builder.RegisterInstance(_ownScene);
            builder.Register<SceneObjectFactory>(resolver => new SceneObjectFactory(
                _runtimeRoot, _robotsRoot, _buildingsRoot, _visualEffectRoot,
                _floatingUIRoot, _audioEventsRoot, resolver), Lifetime.Singleton)
                .AsImplementedInterfaces();

            if (_uiDocument == null || _uiDocument.panelSettings == null)
            {
                throw new SceneContractException(
                    "MainGame scene scope is missing serialized _uiDocument with PanelSettings.");
            }

            Kern.UI.Screens.DynamicAtlasConfigurator.Apply(_uiDocument.panelSettings);

            builder.RegisterInstance(_uiDocument);
            builder.Register<MapStorage>(Lifetime.Singleton)
                .As<IWorldDataStorage>()
                .As<IWorldPersistence>()
                .AsSelf();
            builder.Register<FrameTelemetry>(Lifetime.Singleton).As<IFrameTelemetry>();
            builder.Register<SharedMaterialCache>(Lifetime.Singleton).As<ISharedMaterialCache>();
            builder.Register<AsyncOperationSupervisor>(Lifetime.Singleton)
                .AsSelf()
                .As<IAsyncOperationSupervisor>();
            builder.Register<InventoryModel>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
            builder.Register<PlayerStatsModel>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
            builder.Register<LightingGeometryRegistry>(Lifetime.Singleton);
            builder.Register<Kern.World.Streaming.WorldViewTransition>(Lifetime.Singleton)
                .As<Kern.Core.Interfaces.IWorldPresentationState>()
                .AsSelf();
            builder.Register<GraphicsSettingsController>(Lifetime.Singleton);
            builder.Register<MapModeState>(Lifetime.Singleton);
            builder.Register<ChatEventGateway>(Lifetime.Singleton);
            builder.RegisterEntryPoint<WorldLabels>().As<IWorldLabels>();
        builder.Register<ServerWindowPresenter>(Lifetime.Singleton);
            builder.Register<InputBlockState>(Lifetime.Singleton).As<IInputBlocker>();
            builder.Register<ProgrammatorData>(Lifetime.Singleton);
            builder.Register<ProgrammatorTextureRegistry>(Lifetime.Singleton)
                .As<IProgrammatorTextureCatalog>();
            builder.Register<NetworkStatusModel>(Lifetime.Singleton);
            builder.Register<WorldInitProcessor>(Lifetime.Singleton);
            builder.Register<AuthTokenProcessor>(Lifetime.Singleton);
            RegisterManager<MapManager>(builder, "World").AsImplementedInterfaces().AsSelf();
            RegisterManager<TerrainRenderer>(builder, "Rendering");
            RegisterManager<WorldBackgroundSetup>(builder, "World");
            RegisterManager<WorldTextureManager>(builder, "World").AsImplementedInterfaces().AsSelf();
            builder.Register<CellDestructionClip>(Lifetime.Singleton);
            builder.Register<SpriteAnimationLibrary>(Lifetime.Singleton);
            RegisterManager<ServerEventPlaybackManager>(builder, "Audio").AsImplementedInterfaces().AsSelf();
            builder.RegisterEntryPoint<PacketHandler>().AsSelf();

            GamePacketProcessorRegistration.Register(builder);
            builder.RegisterEntryPoint<GameManager>().AsSelf();
            RegisterManager<VisualEffectPool>(builder, "Rendering").AsImplementedInterfaces().AsSelf();
            builder.Register<BuildingManager>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
            builder.Register<RobotManager>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
            RegisterManager<WorldEntityBatchRenderer>(builder, "Rendering");

            GamePlayerRegistration.Register(builder, this, _playerMovement);

            RegisterManager<GlobalChatUI>(builder, "UI");
            RegisterManager<FPSCounter>(builder, "UI");
            RegisterManager<FloatingChatManager>(builder, "UI");
            RegisterManager<ReconnectUI>(builder, "UI");
            RegisterManager<AssetLoadingIndicator>(builder, "UI");
            RegisterManager<MissionArrowUI>(builder, "UI");
            RegisterManager<PostProcessController>(builder, "Rendering");
            RegisterManager<LightingEngine>(builder, "Rendering");
            RegisterManager<SurfaceRenderer>(builder, "Rendering");
            RegisterManager<CameraFollow>(builder, "Rendering");
            RegisterManager<PlayerHUDView>(builder, "UI");
            RegisterManager<InventoryView>(builder, "UI");
            RegisterManager<PauseMenu>(builder, "UI");
            RegisterManager<MinimapController>(builder, "UI");
            RegisterManager<WorldMapController>(builder, "UI");
            RegisterManager<WorldMapRenderer>(builder, "UI");
            RegisterManager<InGameDebugOverlay>(builder, "UI");
            GameUIServiceRegistration.Register(builder, _postProcessVolume);
            builder.Register<GameInfrastructureStartup>(Lifetime.Singleton);
            builder.Register<GamePresentationStartup>(Lifetime.Singleton);
            builder.Register<GameStartupPipeline>(Lifetime.Singleton);
            builder.Register<TerrainLightingExchange>(Lifetime.Singleton)
                .As<ITerrainLightingExchange>();
            builder.RegisterEntryPoint<GameBootstrap>();
        }

        public void ActivateSceneServices()
        {
            if (_servicesRoot.gameObject.activeSelf)
            {
                throw new SceneContractException(
                    "MainGame Services root must be authored inactive and activated only after dependency injection.");
            }

            _servicesRoot.gameObject.SetActive(true);
        }

        public UniTask WaitUntilReadyAsync() => _readiness.Task;
        public void MarkReady() => _readiness.TrySetResult();
        public void MarkFailed(Exception exception) => _readiness.TrySetException(exception);

        public void InitializePresentationState(
            IWorldPresentationState presentationState,
            PlayerStatsModel playerStats,
            InventoryModel inventory)
        {
            if (_presentationState != null)
            {
                throw new InvalidOperationException("Game presentation state is already initialized.");
            }

            _presentationState = presentationState;
            _heldPlayerStats = playerStats;
            _heldInventory = inventory;
            _presentationState.Changed += OnPresentationStateChanged;
            _presentationStable = _presentationState.IsStable;
            _presentationFailed = _presentationState.Current.Phase == WorldPresentationPhase.Failed;
            _hasPublishedPresentation = _presentationState.HasPublishedView;
            UpdatePresentationDataHold();
            ApplyGameUIVisibility();
        }

        protected void Update()
        {
            ApplyGameUIVisibility();

            if (Keyboard.current == null || _uiDocument == null)
            {
                return;
            }

            if (Keyboard.current.f11Key.wasPressedThisFrame ||
                ((Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed ||
                  Keyboard.current.leftCommandKey.isPressed || Keyboard.current.rightCommandKey.isPressed) &&
                 Keyboard.current.uKey.wasPressedThisFrame))
            {
                SetGameUIActive(!_isGameUIActive);
            }
        }

        public void SetGameUIActive(bool active)
        {
            _isGameUIActive = active;
            ApplyGameUIVisibility();

            Debug.Log($"[GameLifetimeScope] Game UI {(active ? "ENABLED" : "DISABLED")}.");
        }

        private void OnPresentationStateChanged(WorldPresentationSnapshot snapshot)
        {
            _presentationStable = snapshot.Phase == WorldPresentationPhase.Stable;
            _presentationFailed = snapshot.Phase == WorldPresentationPhase.Failed;
            _hasPublishedPresentation = _presentationState?.HasPublishedView == true;
            UpdatePresentationDataHold();
            ApplyGameUIVisibility();
        }

        private void UpdatePresentationDataHold()
        {
            bool shouldHold = _hasPublishedPresentation && !_presentationStable && !_presentationFailed;
            if (shouldHold == _presentationDataHeld)
            {
                return;
            }

            _presentationDataHeld = shouldHold;
            if (shouldHold)
            {
                _heldPlayerStats?.BeginBatch();
                _heldInventory?.BeginBatch();
            }
            else
            {
                _heldInventory?.EndBatch();
                _heldPlayerStats?.EndBatch();
            }
        }

        private void ReleasePresentationDataHold()
        {
            if (!_presentationDataHeld)
            {
                return;
            }

            _presentationDataHeld = false;
            _heldInventory?.EndBatch();
            _heldPlayerStats?.EndBatch();
        }

        private void ApplyGameUIVisibility()
        {
            bool visible = _isGameUIActive && (_presentationStable || _hasPublishedPresentation);
            VisualElement? root = _uiDocument != null ? _uiDocument.rootVisualElement : null;
            if (root != null &&
                (!ReferenceEquals(root, _gameUIRoot) || _lastAppliedGameUIVisibility != visible))
            {
                _gameUIRoot = root;
                root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                _lastAppliedGameUIVisibility = visible;
                _lastAppliedGameUIPickingMode = null;
            }

            if (root != null)
            {
                PickingMode pickingMode = visible && (_presentationStable || _presentationFailed)
                    ? PickingMode.Position
                    : PickingMode.Ignore;
                if (_lastAppliedGameUIPickingMode != pickingMode)
                {
                    root.pickingMode = pickingMode;
                    _lastAppliedGameUIPickingMode = pickingMode;
                }
            }

            if (_floatingUIRoot != null && _floatingUIRoot.gameObject.activeSelf != visible)
            {
                _floatingUIRoot.gameObject.SetActive(visible);
            }
        }

        public async UniTask PrepareForUnloadAsync()
        {
            if (_uiDocument != null)
            {
                Kern.UI.Screens.UIScreen.Unbind(_uiDocument);
            }

            if (Container == null)
            {
                return;
            }

            Container.TryResolve(out PacketHandler? packetHandler);
            Container.TryResolve(out GameManager? gameManager);
            Container.TryResolve(out MapManager? mapManager);
            Container.TryResolve(out AsyncOperationSupervisor? operations);

            // Соединение живёт на Bootstrap, а игровая сессия — ровно столько,
            // сколько эта сцена. Отключение здесь, а не только в
            // ReturnToMainMenu: любой уход из MainGame (прямой переход, сбой,
            // выход) иначе оставлял циклы офлайн-сервера слать пакеты в меню.
            // Первым — пока контейнер сцены жив. Повторный Disconnect безвреден.
            if (Container.TryResolve(out IConnectionService? connection) &&
                connection != null &&
                !connection.IsAutoReconnecting)
            {
                connection.Disconnect();
            }

            if (packetHandler != null)
            {
                packetHandler.Shutdown();
            }

            if (gameManager != null)
            {
                gameManager.DeauthorizeUI();
            }

            if (operations != null)
            {
                await operations.StopAsync();
            }

            if (mapManager != null)
            {
                await mapManager.FlushForUnloadAsync();
                mapManager.ResetWorldState();
            }

            // LocalPlayerState lives on the persistent Bootstrap scope and
            // survives this scene's unload. Without an explicit Clear the current
            // player still points at the soon-to-be-destroyed PlayerMovementController;
            // re-entering MainGame then routes the first PlayerInfoPacket at a
            // destroyed object (MissingReferenceException). Publish a fresh player
            // on re-entry is idempotent only for the same reference, so clear it here.
            // ILocalPlayerState is registered on the persistent Bootstrap
            // container and normally resolvable from the game scope here; the
            // same half-disposed-container caveat as above applies.
            if (Container.TryResolve(out ILocalPlayerState? localPlayer) && localPlayer != null)
            {
                ILocalPlayer? current = localPlayer.Current;
                if (current != null)
                {
                    localPlayer.Clear(current);
                }

                // Session-only flag on a persistent object: stale "authenticated"
                // from a previous MainGame must not leak into the next session.
                localPlayer.SetAuthenticated(false);
            }
        }

        private RegistrationBuilder RegisterManager<T>(IContainerBuilder builder, string group)
            where T : MonoBehaviour
        {
            T typed = SceneBindings.ResolveTypedBinding<T>(group);
            return builder.RegisterComponent(typed);
        }

        private void ValidateSceneRoots()
        {
            (Transform value, string name)[] required =
            new[] {

                (_servicesRoot, nameof(_servicesRoot)),
                (_runtimeRoot, nameof(_runtimeRoot)),
                (_robotsRoot, nameof(_robotsRoot)),
                (_buildingsRoot, nameof(_buildingsRoot)),
                (_visualEffectRoot, nameof(_visualEffectRoot)),
                (_floatingUIRoot, nameof(_floatingUIRoot)),
                (_audioEventsRoot, nameof(_audioEventsRoot)),

            };

            foreach ((Transform value, string name) in required)
            {
                if (value == null || value.gameObject.scene != _ownScene)
                {
                    throw new SceneContractException(
                        $"Scene '{_ownScene.name}' has no valid authored {name} reference.");
                }
            }
        }

        private void ValidateServiceGroups()
        {
            // Группа существует, только пока в ней есть компонент, которому нужен
            // GameObject. Сеть и геймплей — чистый C# в контейнере (docs/architecture/SCENE_STANDARD.md §1).
            string[] requiredGroups = new[] {
"World", "Rendering", "UI", "Audio"
 };
            foreach (string group in requiredGroups)
            {
                if (!SceneBindings.HasSerializedServiceGroup(group))
                {
                    throw new SceneContractException(
                        $"Required service group '{group}' is missing from the typed scene contract.");
                }
            }
        }
    }
}

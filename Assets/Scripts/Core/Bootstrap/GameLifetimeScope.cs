#nullable enable

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fodinae.Audio.Backend;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Lifecycle;
using Fodinae.Game;
using Fodinae.Game.Managers;
using Fodinae.Networking;
using Fodinae.Networking.Connection;
using Fodinae.Networking.Processors;
using Fodinae.Player;
using Fodinae.Player.Logic;
using Fodinae.Rendering;
using Fodinae.Rendering.PostProcessing;
using Fodinae.UI;
using Fodinae.UI.HUD.Inventory.Interfaces;
using Fodinae.UI.HUD.Inventory.Model;
using Fodinae.UI.HUD.Inventory.View;
using Fodinae.UI.HUD.Player.Model;
using Fodinae.UI.HUD.Player.View;
using Fodinae.UI.Programmator;
using Fodinae.World;
using Fodinae.World.Lighting;
using Fodinae.World.Terrain;
using global::Fodinae.Core.Localization;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using VContainer;
using VContainer.Unity;

namespace Fodinae.Core
{
    [DefaultExecutionOrder(-20000)]
    public class GameLifetimeScope : TransitionSceneLifetimeScope
    {
        private Scene _ownScene;
        private readonly UniTaskCompletionSource _readiness = new();

        [SerializeField] private Transform _servicesRoot = null!;
        [SerializeField] private Transform _runtimeRoot = null!;
        [SerializeField] private Transform _robotsRoot = null!;
        [SerializeField] private Transform _buildingsRoot = null!;
        [SerializeField] private Transform _vfxRoot = null!;
        [SerializeField] private Transform _floatingUIRoot = null!;
        [SerializeField] private Transform _audioEventsRoot = null!;
        [SerializeField] private UIDocument _uiDocument = null!;
        [SerializeField] private Volume _postProcessVolume = null!;
        [SerializeField] private PlayerMovementController _playerMovement = null!;
        [SerializeField] private List<ManagerBinding> _managerBindings = new();

        public Transform ServicesRoot => _servicesRoot;
        public IReadOnlyList<ManagerBinding> ManagerBindings => _managerBindings;

        protected override void Awake()
        {
            try
            {
                _ownScene = gameObject.scene;
                base.Awake();
                // VContainer injects registered components lazily on first
                // resolve. Nothing resolves the local player's Robot from the
                // graph (RobotManager GetComponents it instead), so inject it
                // explicitly now — before Robot.Start fires this same frame and
                // dereferences its [Inject] fields (IAssetLoader etc.).
                if (Container != null && _playerMovement != null)
                {
                    if (_playerMovement.TryGetComponent<Robot>(out Robot? playerRobot))
                    {
                        Container.Inject(playerRobot);
                    }

                    // The authored local player is published only by the editor
                    // preview path today; at runtime nothing ever called
                    // Publish(this), so ILocalPlayerState.Current stays null and
                    // GameManager's world-readiness gate never converges. Publish
                    // it here (after DI, before auth) so PlayerInfoProcessor can
                    // initialize it from PlayerInfoPacket and GameManager can
                    // finally release WorldReady.
                    ILocalPlayerState localPlayer = Container.Resolve<ILocalPlayerState>();
                    localPlayer.Publish(_playerMovement);
                }

                // SceneSetup is an authored scene-root component (not part of the
                // ManagerBinding contract), so the container never injects it
                // automatically. Without this its [Inject] ITextureStorageService
                // stays null and TryStartSurfaceRendererSetup silently no-ops:
                // SetLocalAssets is never called, the surface textures never get
                // assigned, and the world-readiness gate is stuck on
                // surface=false forever. SceneSetup.Update retries until the
                // injection lands, so injecting here (after base.Awake built the
                // container) is safe regardless of Start/Update ordering.
                SceneSetup? sceneSetup = null;
                foreach (GameObject root in _ownScene.GetRootGameObjects())
                {
                    foreach (SceneSetup candidate in root.GetComponentsInChildren<SceneSetup>(true))
                    {
                        sceneSetup = candidate;
                        break;
                    }

                    if (sceneSetup != null)
                    {
                        break;
                    }
                }

                if (Container != null && sceneSetup != null)
                {
                    Container.Inject(sceneSetup);
                }
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
                _runtimeRoot, _robotsRoot, _buildingsRoot, _vfxRoot,
                _floatingUIRoot, _audioEventsRoot, resolver), Lifetime.Singleton)
                .AsImplementedInterfaces();

            if (_uiDocument == null || _uiDocument.panelSettings == null)
            {
                throw new SceneContractException(
                    "MainGame scene scope is missing serialized _uiDocument with PanelSettings.");
            }

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
            builder.Register<GraphicsSettingsController>(Lifetime.Singleton);
            builder.Register<MapModeState>(Lifetime.Singleton);
            builder.Register<ChatEventGateway>(Lifetime.Singleton);
            builder.Register<WindowCommandStream>(Lifetime.Singleton);
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
            RegisterManager<ServerAudioEventManager>(builder, "Audio").AsImplementedInterfaces().AsSelf();
            RegisterManager<PacketHandler>(builder, "Networking").AsImplementedInterfaces().AsSelf();

            builder.Register<ClanProcessor>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
            builder.Register<InventoryProcessor>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
            builder.Register<PlayerStatsProcessor>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
            builder.Register<StatusProcessor>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
            builder.Register<MapRegionProcessor>(Lifetime.Singleton);
            builder.Register<AudioPacketProcessor>(Lifetime.Singleton);
            builder.Register<PlayerInfoProcessor>(Lifetime.Singleton);
            builder.Register<ChatProcessor>(Lifetime.Singleton);
            builder.Register<MissionProcessor>(Lifetime.Singleton);
            builder.Register<BuildingProcessor>(Lifetime.Singleton);
            builder.Register<ConnectionProcessor>(Lifetime.Singleton);
            builder.Register<MissionArrowProcessor>(Lifetime.Singleton);
            builder.Register<WindowPacketProcessor>(Lifetime.Singleton);
            RegisterManager<GameManager>(builder, "Gameplay").AsImplementedInterfaces().AsSelf();
            RegisterManager<VFXPool>(builder, "Rendering").AsImplementedInterfaces().AsSelf();
            RegisterManager<BuildingManager>(builder, "Gameplay").AsImplementedInterfaces().AsSelf();
            RegisterManager<RobotManager>(builder, "Gameplay").AsImplementedInterfaces().AsSelf();
            RegisterManager<WorldEntityBatchRenderer>(builder, "Rendering");

            if (_playerMovement == null)
            {
                throw new SceneContractException(
                    "MainGame scene must contain an authored PlayerMovementController reference.");
            }

            builder.RegisterComponent(_playerMovement);
            if (_playerMovement.TryGetComponent<Robot>(out Robot? playerRobot))
            {
                // The local player's Robot carries [Inject] dependencies
                // (IAssetLoader, LightingEngine, MapManager, RobotManager) that
                // VContainer applies only to components it registers. Without
                // this, the authored fallback skin path triggers Start-time
                // loading against a null loader.
                builder.RegisterComponent(playerRobot);
            }

            RegisterManager<ServerConfig>(builder, "Gameplay").AsImplementedInterfaces().AsSelf();
            RegisterManager<GlobalChatUI>(builder, "UI");
            RegisterManager<UIInputManager>(builder, "UI");
            RegisterManager<FPSCounter>(builder, "UI");
            RegisterManager<FloatingChatManager>(builder, "UI");
            RegisterManager<ReconnectUI>(builder, "UI");
            RegisterManager<AssetLoadingIndicator>(builder, "UI");
            RegisterManager<MissionArrowUI>(builder, "UI");
            RegisterManager<DiagnosticRunner>(builder, "UI");

            if (_postProcessVolume == null)
            {
                throw new SceneContractException(
                    "MainGame scene scope is missing serialized _postProcessVolume reference.");
            }

            builder.RegisterComponent(_postProcessVolume);
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
            RegisterManager<DisplayManager>(builder, "UI");
            RegisterManager<InGameDebugOverlay>(builder, "UI");
            builder.Register<GameInfrastructureStartup>(Lifetime.Singleton);
            builder.Register<GamePresentationStartup>(Lifetime.Singleton);
            builder.Register<GameStartupPipeline>(Lifetime.Singleton);
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

        /// <summary>
        /// Winds the scene's subsystems down before the scene unloads.
        /// </summary>
        /// <remarks>
        /// Every service is looked up here rather than handed in by the caller:
        /// the container belongs to this scope, and a scope torn down halfway by
        /// an aborted previous unload has some registrations already gone. Each
        /// lookup is independent, so one missing subsystem cannot cancel the
        /// teardown of the rest. TryResolve reports that absence as a result
        /// instead of an exception; the null check after it also covers a
        /// component whose GameObject is already destroyed, which still resolves.
        /// </remarks>
        public async UniTask PrepareForUnloadAsync()
        {
            if (Container == null)
            {
                return;
            }

            Container.TryResolve(out PacketHandler? packetHandler);
            Container.TryResolve(out GameManager? gameManager);
            Container.TryResolve(out MapManager? mapManager);
            Container.TryResolve(out AsyncOperationSupervisor? operations);

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
            T typed = ResolveTypedBinding<T>(group);
            return builder.RegisterComponent(typed);
        }

        private T ResolveTypedBinding<T>(string group)
            where T : MonoBehaviour
        {
            string key = typeof(T).AssemblyQualifiedName
                ?? throw new SceneContractException($"Cannot resolve assembly name for '{typeof(T).Name}'.");

            ManagerBinding? match = null;
            foreach (ManagerBinding binding in _managerBindings)
            {
                if (!string.Equals(binding.ManagerType, key, StringComparison.Ordinal))
                {
                    continue;
                }

                if (match != null)
                {
                    throw new SceneContractException(
                        $"Duplicate ManagerBinding entries exist for '{typeof(T).Name}'.");
                }

                match = binding;
            }

            if (match == null)
            {
                throw new SceneContractException(
                    $"No typed ManagerBinding exists for '{typeof(T).Name}'.");
            }

            if (match.Target is not T target)
            {
                throw new SceneContractException(
                    $"Manager binding for '{typeof(T).Name}' points to an invalid target.");
            }

            if (target.gameObject.scene != _ownScene)
            {
                throw new SceneContractException(
                    $"Manager binding for '{typeof(T).Name}' references another scene.");
            }

            if (!string.Equals(match.ServiceGroup, group, StringComparison.Ordinal))
            {
                throw new SceneContractException(
                    $"Manager binding for '{typeof(T).Name}' declares group '{match.ServiceGroup}', expected '{group}'.");
            }

            return target;
        }

        private void ValidateSceneRoots()
        {
            (Transform value, string name)[] required =
            [
                (_servicesRoot, nameof(_servicesRoot)),
                (_runtimeRoot, nameof(_runtimeRoot)),
                (_robotsRoot, nameof(_robotsRoot)),
                (_buildingsRoot, nameof(_buildingsRoot)),
                (_vfxRoot, nameof(_vfxRoot)),
                (_floatingUIRoot, nameof(_floatingUIRoot)),
                (_audioEventsRoot, nameof(_audioEventsRoot)),
            ];

            foreach ((Transform value, string name) in required)
            {
                if (value == null || value.gameObject.scene != _ownScene)
                {
                    throw new SceneContractException(
                        $"Scene '{_ownScene.name}' has no valid authored {name} reference.");
                }
            }
        }

        private bool HasSerializedServiceGroup(string group)
        {
            foreach (ManagerBinding binding in _managerBindings)
            {
                if (string.Equals(binding.ServiceGroup, group, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void ValidateServiceGroups()
        {
            string[] requiredGroups = ["Networking", "World", "Rendering", "Gameplay", "UI", "Audio"];
            foreach (string group in requiredGroups)
            {
                if (!HasSerializedServiceGroup(group))
                {
                    throw new SceneContractException(
                        $"Required service group '{group}' is missing from the typed scene contract.");
                }
            }
        }
    }
}

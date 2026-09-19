#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Audio.Backend;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.Core.Localization;
using Kern.Game.Managers;
using Kern.AssetPipeline;
using Kern.Networking;
using Kern.Networking.Auth;
using Kern.Networking.Connection;
using Kern.Rendering;
using MinesServer.Networking.Connection.Client;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

namespace Kern.Core
{
    [DefaultExecutionOrder(-30000)]
    public class BootstrapLifetimeScope : LifetimeScope, IMainMenuNavigation, ISceneNavigator
    {
        private const string MainMenuSceneName = ProjectRuntimeContracts.SceneNames.MainMenu;

        // Первой после Bootstrap грузится не меню, а Gateway: вход и онбординг.
        // Он сам загрузит MainMenu и выгрузится, когда игрок пройдёт ворота.
        private const string GatewaySceneName = ProjectRuntimeContracts.SceneNames.Gateway;

        [SerializeField] private Camera _applicationCamera = null!;
        [SerializeField] private ConnectionManager _connectionManager = null!;
        [SerializeField] private NetworkService _networkService = null!;
        [SerializeField] private AudioSystem _audioSystem = null!;
        [SerializeField] private ClientConfigManager _clientConfigManager = null!;
        [SerializeField] private ClientAssetLoader _clientAssetLoader = null!;
        [SerializeField] private TextureStorageManager _textureStorageManager = null!;
        [SerializeField] private BootstrapLoadingScreen _loadingScreen = null!;
        [SerializeField] private FMODUnity.StudioListener _studioListener = null!;

        private readonly SemaphoreSlim _transitionGate = new(1, 1);

        private string? _currentSceneName;

        public string? CurrentSceneName => _currentSceneName;

        public event Action<SceneTransitionStatus>? TransitionChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ConfigureLogStackTraces()
        {
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
        }

        protected override void Awake()
        {
            // Bootstrap only owns application services. Scene transitions are
            // started by ApplicationBootstrap after the container is built.
            DontDestroyOnLoad(gameObject);
            try
            {
                base.Awake();
            }
            catch (Exception ex)
            {
                // Without this catch the failed container build takes the whole
                // process down before ApplicationBootstrap or any UI can show
                // a diagnostic. Surface a single error and rethrow so the
                // bootstrap scene contract still fails fast in development.
                Debug.LogError($"[Bootstrap] Container build failed at Awake: {ex}");
                throw;
            }

            if (Container != null)
            {
                try
                {
                    BindApplicationCamera();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Bootstrap] Application camera bind failed: {ex.Message}");
                    throw;
                }
            }
        }

        protected override void OnDestroy()
        {
            // Do not dispose the gate here. A transition can still be unwinding
            // after the scope receives OnDestroy (for example when the Test
            // Runner exits PlayMode). Disposing it makes the continuation's
            // finally block throw ObjectDisposedException and masks the real
            // transition result. SemaphoreSlim is managed state and can be
            // reclaimed normally once no transition references it.
            base.OnDestroy();
        }

        private void BindApplicationCamera()
        {
            Camera camera = _applicationCamera ?? throw new InvalidOperationException(
                "Bootstrap scene contract requires an authored application camera reference.");
            camera.enabled = true;
            camera.tag = "MainCamera";
            camera.backgroundColor = new Color(0.012f, 0.018f, 0.032f, 1f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            if (_studioListener == null || _studioListener.gameObject.scene != gameObject.scene)
            {
                throw new InvalidOperationException(
                    "Bootstrap scene contract requires an authored FMOD StudioListener reference in this scene.");
            }

            GameplayCamera.BindPersistent(camera);
        }

        private async UniTask EnsureMainMenuLoadedAsync()
        {
            await TransitionAsync(MainMenuSceneName, destroyCancellationToken);
        }

        public async UniTask TransitionAsync(
            string sceneName,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                throw new ArgumentException("Scene name is required.", nameof(sceneName));
            }

            bool gateEntered = false;
            await _transitionGate.WaitAsync(cancellationToken);
            gateEntered = true;
            try
            {
                await TransitionExclusiveAsync(sceneName, cancellationToken);
            }
            finally
            {
                if (gateEntered)
                {
                    _transitionGate.Release();
                }
            }
        }

        private async UniTask TransitionExclusiveAsync(
            string sceneName,
            CancellationToken cancellationToken)
        {
            Scene? previousScene = null;
            string? currentSceneName = _currentSceneName;
            if (!string.IsNullOrEmpty(currentSceneName))
            {
                // Do not use SceneManager.GetSceneByName here: it resolves by
                // an internal name->scene map that is unreliable once multiple
                // additive scenes are loaded, and can return an invalid Scene
                // even when a correctly-named scene is resident. Scan the live
                // scene list instead, matching the target-side lookup.
                Scene previous = SceneTransitionSceneLookup.FindFirstLoaded(currentSceneName);
                if (previous.IsValid() && previous.isLoaded)
                {
                    previousScene = previous;
                }
            }

            Debug.Log($"[Bootstrap] Loading scene '{sceneName}'...");
            // The outgoing scene is no longer the current transition owner.
            // Keep it loaded while the ticketed replacement reaches presentation.
            _currentSceneName = null;
            Scene candidateScene = default;
            var ticket = new SceneTransitionTicket(sceneName);
            ticket.Changed += PublishTransitionStatus;
            PublishTransitionStatus(new SceneTransitionStatus(sceneName, SceneTransitionPhase.Created));
            PublishTransitionStatus(new SceneTransitionStatus(sceneName, SceneTransitionPhase.Loading));
            try
            {
                Scene existing = SceneTransitionSceneLookup.FindFirstLoaded(sceneName);
                if (existing.IsValid() && existing.isLoaded)
                {
                    throw new InvalidOperationException(
                        $"[Bootstrap] Transition target '{sceneName}' was already loaded outside the current transition. " +
                        "Content scenes must be loaded only through BootstrapLifetimeScope.");
                }

                if (!Application.CanStreamedLevelBeLoaded(sceneName))
                {
                    throw new SceneContractException(
                        $"Transition target scene '{sceneName}' is not present in the active build profile.");
                }

                using (LifetimeScope.EnqueueParent(this))
                using (LifetimeScope.Enqueue(builder => builder.RegisterInstance(ticket)))
                {
                    await SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive).ToUniTask();
                    // Yield one frame so Unity finishes Awake/OnEnable on the new
                    // scene's root objects before we touch them. Without this the
                    // very first lookup against candidateScene can race the
                    // internal scene registration and return default.
                    await UniTask.Yield();
                }

                candidateScene = SceneTransitionSceneLookup.FindUniqueLoaded(sceneName);

                await ticket.WaitUntilAttachedAsync().AttachExternalCancellation(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                SceneManager.SetActiveScene(candidateScene);
                ticket.RequestActivation();
                await ticket.WaitForPresentationAsync().AttachExternalCancellation(cancellationToken);
                _currentSceneName = sceneName;

                if (previousScene.HasValue && previousScene.Value != candidateScene &&
                    previousScene.Value.IsValid() && previousScene.Value.isLoaded)
                {
                    PublishTransitionStatus(new SceneTransitionStatus(
                        sceneName,
                        SceneTransitionPhase.CleaningPrevious));
                    Exception? cleanupFailure = await SceneTransitionRuntime.TryCleanupPreviousSceneAsync(
                        previousScene.Value,
                        PrepareSceneForUnloadAsync);
                    if (cleanupFailure != null)
                    {
                        Debug.LogError(
                            $"[Bootstrap] Scene '{sceneName}' is ready, but previous scene " +
                            $"'{previousScene.Value.name}' could not be unloaded: {cleanupFailure}");
                        PublishTransitionStatus(new SceneTransitionStatus(
                            sceneName,
                            SceneTransitionPhase.CompletedWithWarnings,
                            cleanupFailure));
                        return;
                    }
                }

                Debug.Log($"[Bootstrap] Scene '{sceneName}' entered successfully.");
                PublishTransitionStatus(new SceneTransitionStatus(sceneName, SceneTransitionPhase.Completed));
            }
            catch (Exception ex)
            {
                ticket.Fail(ex);
                _currentSceneName = previousScene?.name;
                if (candidateScene.IsValid() && candidateScene.isLoaded &&
                    !string.Equals(candidateScene.name, _currentSceneName, StringComparison.Ordinal))
                {
                    try
                    {
                        await SceneManager.UnloadSceneAsync(candidateScene).ToUniTask();
                    }
                    catch (Exception cleanupException)
                    {
                        Debug.LogError(
                            $"[Bootstrap] Failed to roll back candidate scene '{sceneName}': {cleanupException}");
                    }
                }

                throw;
            }
            finally
            {
                ticket.Changed -= PublishTransitionStatus;
                ticket.Dispose();
            }
        }

        private void PublishTransitionStatus(SceneTransitionStatus status)
        {
            SceneTransitionRuntime.PublishSafely(TransitionChanged, status);
        }

        private static GameLifetimeScope? FindGameScope(Scene scene)
        {
            GameLifetimeScope? result = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (GameLifetimeScope scope in root.GetComponentsInChildren<GameLifetimeScope>(true))
                {
                    if (scope.gameObject.scene == scene)
                    {
                        if (result != null)
                        {
                            throw new InvalidOperationException(
                                $"[Bootstrap] Scene '{scene.name}' contains multiple GameLifetimeScope components. Keep exactly one composition root.");
                        }

                        result = scope;
                    }
                }
            }

            return result;
        }

        private static async UniTask PrepareSceneForUnloadAsync(Scene scene)
        {
            GameLifetimeScope? scope = FindGameScope(scene);
            if (scope == null || scope.Container == null)
            {
                return;
            }

            // Which subsystems are still alive is the game scope's own business:
            // it owns that container and knows what an aborted previous unload
            // may have left half-disposed.
            await scope.PrepareForUnloadAsync();
        }

        public void ReturnToMainMenu()
        {
            Container.Resolve<AsyncOperationSupervisor>().Run(
                "return_to_main_menu",
                ReturnToMainMenuAsync);
        }

        private async UniTask ReturnToMainMenuAsync(CancellationToken cancellationToken)
        {
            // Packet subscriptions come off first, while the game scope is still
            // alive and this resolve is still valid. Leaving it to
            // PacketHandler.OnDestroy means it happens inside the unload, after
            // packets have already had a chance to reach processors that resolve
            // managers out of a dying container.
            Container.Resolve<IConnectionService>().Disconnect();

            // Ambient resolution is pointed back at Bootstrap BEFORE the unload,
            // not after it.
            //
            // When the Game scope disposes, VContainer clears sharedInstances but
            // leaves the registry intact, so a Resolve on a disposed scope silently
            // re-runs the provider. For RegisterComponent registrations the provider
            // resolves the existing authored component reference — once Bootstrap
            // unloads the scene, those references point at destroyed objects.
            // Repointing first means late resolves hit the Bootstrap container, where
            // Game-scoped types are not registered, and TryResolve returns null.
            await TransitionAsync(MainMenuSceneName, cancellationToken);
            await Resources.UnloadUnusedAssets().ToUniTask();
        }

        protected override void Configure(IContainerBuilder builder)
        {
            // NB: this scope is already registered by LifetimeScope.InstallTo as
            // RegisterInstance<LifetimeScope>(this).AsSelf() — an explicit
            // RegisterInstance(this) here duplicates the BootstrapLifetimeScope
            // contract and VContainer rejects the conflicting singleton.
            Camera applicationCamera = _applicationCamera;
            if (applicationCamera == null || applicationCamera.gameObject.scene != gameObject.scene)
            {
                throw new InvalidOperationException("BootstrapLifetimeScope requires an authored application camera reference in this scene.");
            }
            builder.RegisterComponent(applicationCamera);
            builder.Register<IMainMenuNavigation>(
                resolver => resolver.Resolve<BootstrapLifetimeScope>(),
                Lifetime.Singleton);
            builder.Register<ISceneNavigator>(
                resolver => resolver.Resolve<BootstrapLifetimeScope>(),
                Lifetime.Singleton);
            builder.RegisterInstance(GraphicsQualityProfileLoader.LoadRequired());
            builder.Register<ShaderWarmupService>(Lifetime.Singleton).As<IShaderWarmupService>();
            builder.Register<AsyncOperationSupervisor>(Lifetime.Singleton)
                .AsSelf()
                .As<IAsyncOperationSupervisor>();
            builder.Register<PersistentAssetCache>(Lifetime.Singleton).As<IPersistentAssetCache>();
            builder.Register<RuntimeAssetPaths>(
                _ => new RuntimeAssetPaths(),
                Lifetime.Singleton).As<IRuntimeAssetPaths>();

            // DummyConnection emulates the game server in offline mode. External
            // identity providers do not route authentication through it.
            builder.Register<RealtimeDummyClock>(Lifetime.Singleton).As<IDummyClock>();
            builder.Register<DummyConnection>(Lifetime.Singleton).AsSelf().AsImplementedInterfaces();
            builder.Register<DummyWorldMapSource>(Lifetime.Singleton);
            builder.RegisterEntryPoint<Kern.Networking.Connection.WorldEntryPreparation>()
                .As<IWorldEntryPreparation>();
            builder.Register<GameTokenStore>(Lifetime.Singleton).As<IGameTokenStore>();
            builder.Register<RuntimeDebugSettings>(Lifetime.Singleton).As<IRuntimeDebugSettings>();
            builder.Register<OfflineScenarioSettings>(Lifetime.Singleton).As<IOfflineScenarioSettings>();
            builder.Register<VKIdentityProvider>(Lifetime.Singleton);
            builder.Register<AuthenticationService>(Lifetime.Singleton).As<IAuthenticationService>();

            // Application-tier session state: NetworkService (Bootstrap) and
            // Game-tier processors both resolve the same local-player state.
            builder.Register<LocalPlayerState>(Lifetime.Singleton).As<ILocalPlayerState>();

            // World-load phases and server-window visibility are published by
            // the Game scope but consumed by the MainMenu sibling scope, so
            // both relays live at the application tier.
            builder.Register<WorldLoadProgress>(Lifetime.Singleton).As<IWorldLoadProgress>();
            builder.Register<WindowCommandStream>(Lifetime.Singleton);
            builder.Register<ItemRegistry>(Lifetime.Singleton).As<IItemCatalog>();

            // The persistent application camera as a typed DI dependency.
            // RegisterComponent(applicationCamera) below exposes the Camera
            // itself; IGameplayCamera gives a named contract so DI components
            // do not reach into the static GameplayCamera holder.
            builder.Register<GameplayCameraService>(Lifetime.Singleton).As<IGameplayCamera>();

            RegisterAuthored(builder, _connectionManager, nameof(_connectionManager));
            RegisterAuthored(builder, _networkService, nameof(_networkService));
            RegisterAuthored(builder, _audioSystem, nameof(_audioSystem));
            RegisterAuthored(builder, _clientConfigManager, nameof(_clientConfigManager));
            RegisterAuthored(builder, _clientAssetLoader, nameof(_clientAssetLoader));
            RegisterAuthored(builder, _textureStorageManager, nameof(_textureStorageManager));
            RegisterAuthored(builder, _loadingScreen, nameof(_loadingScreen));
            builder.Register<LocalizationService>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
            builder.RegisterEntryPoint<HDROutputReconciler>();
            builder.RegisterEntryPoint<ApplicationBootstrap>();
        }

        private static RegistrationBuilder RegisterAuthored<T>(
            IContainerBuilder builder,
            T component,
            string fieldName)
            where T : MonoBehaviour
        {
            if (component == null)
            {
                throw new InvalidOperationException(
                    $"Bootstrap scene contract is missing authored reference '{fieldName}'.");
            }

            return builder.RegisterComponent(component).AsImplementedInterfaces();
        }
    }
}

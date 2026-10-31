#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.Core.Localization;
using Kern.AssetPipeline;
using Kern.Networking;
using Kern.Networking.Connection;
using Kern.Rendering;
using Kern.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

using Kern.AssetPipeline.Cache;
using Kern.AssetPipeline.Loading;
using Kern.Bootstrap.Diagnostics;
using Kern.Core.Configuration;
using Kern.Core.Diagnostics;
using Kern.Core.Rendering;
using Kern.Diagnostics.Scenes;
using Kern.Game.Inventory;
using Kern.Game.Player.Stats;
using Kern.Infrastructure.Audio;
using Kern.Networking.Authentication;
using Kern.Networking.Gateways;
using Kern.Networking.Offline;
using Kern.Networking.Processing;
using Kern.Networking.WorldEntry;
using Kern.Rendering.Display;
using Kern.UI.Menu;
using Kern.Core;


namespace Kern.Bootstrap.Scenes
{
    [DefaultExecutionOrder(-30000)]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Kern.Core", sourceAssembly: null, sourceClassName: "BootstrapLifetimeScope")]
    public class BootstrapLifetimeScope : LifetimeScope, IMainMenuNavigation, ISceneNavigator
    {
        private const string MainMenuSceneName = SceneNames.MainMenu;

        // Первой после Bootstrap грузится не меню, а Gateway: вход и онбординг.
        // Он сам загрузит MainMenu и выгрузится, когда игрок пройдёт ворота.
        private const string GatewaySceneName = SceneNames.Gateway;

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
                cancellationToken.ThrowIfCancellationRequested();
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
            Scene alreadyCurrent = SceneTransitionSceneLookup.FindFirstLoaded(sceneName);
            if (string.Equals(_currentSceneName, sceneName, StringComparison.Ordinal) &&
                alreadyCurrent.IsValid() && alreadyCurrent.isLoaded)
            {
                SceneManager.SetActiveScene(alreadyCurrent);
                Debug.Log($"[Bootstrap] Scene '{sceneName}' is already current; transition skipped.");
                return;
            }

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
            bool candidateLoadStarted = false;
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
                    cancellationToken.ThrowIfCancellationRequested();
                    AsyncOperation loadOperation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive)
                        ?? throw new SceneContractException(
                            $"Unity refused to start loading scene '{sceneName}' (isPlaying={Application.isPlaying}).");
                    candidateLoadStarted = true;
                    await loadOperation.ToUniTask();
                    // Yield one frame so Unity finishes Awake/OnEnable on the new
                    // scene's root objects before we touch them. Without this the
                    // very first lookup against candidateScene can race the
                    // internal scene registration and return default.
                    await UniTask.Yield();
                }

                candidateScene = SceneTransitionSceneLookup.FindUniqueLoaded(sceneName);
                ThrowIfSceneScopeAwakeFailed(candidateScene);

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
                if (previousScene.HasValue && previousScene.Value.IsValid() && previousScene.Value.isLoaded)
                {
                    SceneManager.SetActiveScene(previousScene.Value);
                }

                bool worldStartupTimedOut =
                    string.Equals(sceneName, SceneNames.MainGame, StringComparison.Ordinal) &&
                    ex is TimeoutException &&
                    _connectionManager.IsWorldStartupPending &&
                    !_connectionManager.IsOffline &&
                    _connectionManager.IsConnected;
                if (worldStartupTimedOut)
                {
                    _connectionManager.HandleServerReconnect();
                }
                else if (string.Equals(sceneName, SceneNames.MainGame, StringComparison.Ordinal) &&
                         !_connectionManager.IsAutoReconnecting)
                {
                    // The connection and DummyConnection outlive the candidate
                    // scene. If world initialization times out, keeping that
                    // transport alive would make the next Connect() a no-op
                    // after its WorldInit packet was already lost with this scope.
                    try
                    {
                        _connectionManager.Disconnect();
                    }
                    catch (Exception disconnectException)
                    {
                        Debug.LogError(
                            $"[Bootstrap] Failed to disconnect after '{sceneName}' transition failure: " +
                            disconnectException);
                    }
                }

                // LoadSceneAsync can finish loading the scene and then a later
                // step (scene lookup, ticket attach, activation, or readiness)
                // can fail before candidateScene is assigned. Recover the
                // additive scene by name so failed transitions do not strand a
                // second scene in the hierarchy.
                if (candidateLoadStarted && (!candidateScene.IsValid() || !candidateScene.isLoaded))
                {
                    candidateScene = SceneTransitionSceneLookup.FindFirstLoaded(sceneName);
                }

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

        private static void ThrowIfSceneScopeAwakeFailed(Scene scene)
        {
            bool foundScope = false;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (TransitionSceneLifetimeScope scope in
                         root.GetComponentsInChildren<TransitionSceneLifetimeScope>(true))
                {
                    if (scope.gameObject.scene != scene)
                    {
                        continue;
                    }

                    foundScope = true;
                    if (scope.AwakeFailure != null)
                    {
                        throw new SceneContractException(
                            $"Scene '{scene.name}' composition root failed during Awake/Configure.",
                            scope.AwakeFailure);
                    }
                }
            }

            if (!foundScope)
            {
                throw new SceneContractException(
                    $"Scene '{scene.name}' loaded without a TransitionSceneLifetimeScope composition root.");
            }
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
            BootstrapServiceRegistration.Register(builder);

            RegisterAuthored(builder, _connectionManager, nameof(_connectionManager));
            RegisterAuthored(builder, _networkService, nameof(_networkService));
            RegisterAuthored(builder, _audioSystem, nameof(_audioSystem));
            RegisterAuthored(builder, _clientConfigManager, nameof(_clientConfigManager));
            RegisterAuthored(builder, _clientAssetLoader, nameof(_clientAssetLoader));
            RegisterAuthored(builder, _textureStorageManager, nameof(_textureStorageManager));
            RegisterAuthored(builder, _loadingScreen, nameof(_loadingScreen));
            builder.Register<LocalizationService>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
            // Проверки целостности файлов для окна «Диагностика и целостность файлов».
            builder.Register<MenuFileDiagnosticsService>(Lifetime.Singleton).As<IMenuFileDiagnosticsService>();
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

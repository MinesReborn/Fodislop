#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.Core.Localization;
using Kern.Rendering;
using VContainer.Unity;

namespace Kern.Core;

public sealed class ApplicationBootstrap : IStartable
{
    private readonly BootstrapLifetimeScope _scope;
    private readonly IClientConfigManager _clientConfig;
    private readonly BootstrapLoadingScreen _loadingScreen;
    private readonly AsyncOperationSupervisor _operations;
    private readonly IRuntimeAssetPaths _runtimeAssetPaths;
    private readonly IShaderWarmupService _shaderWarmup;
    private readonly ILocalizationService _localization;
    private readonly IAudioSystem _audioSystem;
    private readonly IWorldEntryPreparation _worldEntryPreparation;

    public ApplicationBootstrap(
        BootstrapLifetimeScope scope,
        IClientConfigManager clientConfig,
        BootstrapLoadingScreen loadingScreen,
        AsyncOperationSupervisor operations,
        IRuntimeAssetPaths runtimeAssetPaths,
        IShaderWarmupService shaderWarmup,
        ILocalizationService localization,
        IAudioSystem audioSystem,
        IWorldEntryPreparation worldEntryPreparation)
    {
        _scope = scope;
        _clientConfig = clientConfig;
        _loadingScreen = loadingScreen;
        _operations = operations;
        _runtimeAssetPaths = runtimeAssetPaths;
        _shaderWarmup = shaderWarmup;
        _localization = localization;
        _audioSystem = audioSystem;
        _worldEntryPreparation = worldEntryPreparation;
    }

    public void Start()
    {
        _operations.Run("application_startup", _ => StartAsync());
    }

    private async UniTask StartAsync()
    {
        CancellationToken scopeToken = _scope.destroyCancellationToken;
        try
        {
            _clientConfig.EnsureInitialized();
            ClientConfig config = _clientConfig.Config;

            DisplayManager.ApplyInitialSettings(config.Display);

            _loadingScreen.Initialize();

            string shaderPhase = _localization.Get("bootstrap.loading.shaders");
            _loadingScreen.ShowDirect($"{shaderPhase} (0%)");
            await UniTask.Yield(PlayerLoopTiming.Update, scopeToken);

            await _shaderWarmup.WarmupAsync(
                (_, progress) =>
                {
                    int percent = UnityEngine.Mathf.RoundToInt(progress * 100f);
                    _loadingScreen.SetPhaseText($"{shaderPhase} ({percent}%)");
                },
                scopeToken);

            _loadingScreen.SetPhaseText(_localization.Get("assetload.resources"));
            await UniTask.WhenAll(
                _runtimeAssetPaths.EnsureReadyAsync(),
                _audioSystem.WaitUntilBanksReadyAsync(scopeToken));

            string targetScene = ResolveInitialScene();
            if (targetScene == ProjectRuntimeContracts.SceneNames.MainGame)
            {
                await _worldEntryPreparation.EnsureReadyAsync(scopeToken);
            }

            await _scope.TransitionAsync(targetScene, scopeToken);
        }
        catch (OperationCanceledException) when (scopeToken.IsCancellationRequested)
        {
            // Application shutdown.
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogException(exception);
        }
    }

    private static string ResolveInitialScene()
    {
#if UNITY_EDITOR
        string target = UnityEditor.SessionState.GetString(
            ProjectRuntimeContracts.EditorSession.PlayModeTargetScene,
            string.Empty);
        UnityEditor.SessionState.SetString(ProjectRuntimeContracts.EditorSession.PlayModeTargetScene, string.Empty);
        if (!string.IsNullOrWhiteSpace(target) &&
            target != ProjectRuntimeContracts.SceneNames.Bootstrap)
        {
            if (UnityEngine.Application.CanStreamedLevelBeLoaded(target))
            {
                UnityEngine.Debug.Log(
                    $"[Bootstrap] Starting with scene '{target}' requested from Editor.");
                return target;
            }

            UnityEngine.Debug.LogWarning(
                $"[Bootstrap] Selected scene '{target}' is not present in Build Settings; falling back to Gateway.");
        }
#endif
        return ProjectRuntimeContracts.SceneNames.Gateway;
    }
}

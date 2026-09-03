#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Lifecycle;
using Fodinae.Core.Localization;
using Fodinae.Rendering;
using VContainer.Unity;

namespace Fodinae.Core;

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

    public ApplicationBootstrap(
        BootstrapLifetimeScope scope,
        IClientConfigManager clientConfig,
        BootstrapLoadingScreen loadingScreen,
        AsyncOperationSupervisor operations,
        IRuntimeAssetPaths runtimeAssetPaths,
        IShaderWarmupService shaderWarmup,
        ILocalizationService localization,
        IAudioSystem audioSystem)
    {
        _scope = scope;
        _clientConfig = clientConfig;
        _loadingScreen = loadingScreen;
        _operations = operations;
        _runtimeAssetPaths = runtimeAssetPaths;
        _shaderWarmup = shaderWarmup;
        _localization = localization;
        _audioSystem = audioSystem;
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
            await _scope.TransitionAsync(ProjectRuntimeContracts.SceneNames.Gateway, scopeToken);
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
}

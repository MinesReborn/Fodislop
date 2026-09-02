#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Lifecycle;
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
    private readonly IGameplayCamera _gameplayCamera;

    public ApplicationBootstrap(
        BootstrapLifetimeScope scope,
        IClientConfigManager clientConfig,
        BootstrapLoadingScreen loadingScreen,
        AsyncOperationSupervisor operations,
        IRuntimeAssetPaths runtimeAssetPaths,
        IGameplayCamera gameplayCamera)
    {
        _scope = scope;
        _clientConfig = clientConfig;
        _loadingScreen = loadingScreen;
        _operations = operations;
        _runtimeAssetPaths = runtimeAssetPaths;
        _gameplayCamera = gameplayCamera;
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
            DisplayManager.HDROutput.SetEnabled(config.Display.HDREnabled);
            DisplayManager.HDROutput.ConfigureCamera(_gameplayCamera.Camera);
            _loadingScreen.Initialize();
            await _runtimeAssetPaths.EnsureReadyAsync();
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

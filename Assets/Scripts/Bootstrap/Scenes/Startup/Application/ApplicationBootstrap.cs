#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.Core.Localization;
using Kern.Rendering;
using VContainer.Unity;

using Kern.Rendering.Display;
using Kern.Core;


namespace Kern.Bootstrap.Scenes;

public sealed class ApplicationBootstrap : IStartable
{
    private readonly BootstrapLifetimeScope _scope;
    private readonly IClientConfigManager _clientConfig;
    private readonly BootstrapLoadingScreen _loadingScreen;
    private readonly AsyncOperationSupervisor _operations;
    private readonly IRuntimeAssetPaths _runtimeAssetPaths;
    private readonly IShaderWarmupService _shaderWarmup;
    private readonly ILocalizationService _localization;
    private readonly IWorldEntryPreparation _worldEntryPreparation;

    public ApplicationBootstrap(
        BootstrapLifetimeScope scope,
        IClientConfigManager clientConfig,
        BootstrapLoadingScreen loadingScreen,
        AsyncOperationSupervisor operations,
        IRuntimeAssetPaths runtimeAssetPaths,
        IShaderWarmupService shaderWarmup,
        ILocalizationService localization,
        IWorldEntryPreparation worldEntryPreparation)
    {
        _scope = scope;
        _clientConfig = clientConfig;
        _loadingScreen = loadingScreen;
        _operations = operations;
        _runtimeAssetPaths = runtimeAssetPaths;
        _shaderWarmup = shaderWarmup;
        _localization = localization;
        _worldEntryPreparation = worldEntryPreparation;
    }

    public void Start()
    {
        UnityEngine.Application.runInBackground = true;
        _operations.Run("application_startup", _ => StartAsync());
    }

    private async UniTask StartAsync()
    {
        CancellationToken scopeToken = _scope.destroyCancellationToken;
        try
        {
            _clientConfig.EnsureInitialized();
            ClientConfig config = _clientConfig.Config;

            if (DisplayManager.ApplyInitialSettings(config.Display))
            {
                _clientConfig.SaveDeferred();
            }

            _loadingScreen.Initialize();

            string shaderPhase = _localization.Get("bootstrap.loading.shaders");
            _loadingScreen.ShowDirect(shaderPhase);
            _loadingScreen.SetProgress(0f);
            await UniTask.Yield(Cysharp.Threading.Tasks.PlayerLoopTiming.Update, scopeToken);

            await _shaderWarmup.WarmupAsync(
                (_, progress) =>
                {
                    _loadingScreen.SetProgress(progress);
                },
                scopeToken);

            await UniTask.Yield(Cysharp.Threading.Tasks.PlayerLoopTiming.Update, scopeToken);
            _loadingScreen.SetPhaseText(_localization.Get("assetload.resources"));
            _loadingScreen.SetProgress(1f);
            await _runtimeAssetPaths.EnsureReadyAsync();

            string targetScene = ResolveInitialScene();
            if (targetScene == SceneNames.MainGame)
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
            try
            {
                _loadingScreen.ShowDirect($"Startup failed: {exception.Message}");
            }
            catch (Exception displayException)
            {
                UnityEngine.Debug.LogException(displayException);
            }
        }
    }

    private static string ResolveInitialScene()
    {
#if UNITY_EDITOR
        string target = UnityEditor.SessionState.GetString(
            EditorSessionKeys.PlayModeTargetScene,
            string.Empty);
        UnityEditor.SessionState.SetString(EditorSessionKeys.PlayModeTargetScene, string.Empty);
        if (!string.IsNullOrWhiteSpace(target) &&
            target != SceneNames.Bootstrap)
        {
            if (UnityEngine.Application.CanStreamedLevelBeLoaded(target))
            {
                UnityEngine.Debug.Log(
                    $"[Bootstrap] Starting with scene '{target}' requested from Editor.");
                return target;
            }

            UnityEngine.Debug.LogWarning(
                $"[Bootstrap] Selected scene '{target}' is not present in Build Settings; falling back to MainMenu.");
        }
#endif
        // Вход и онбординг — слой главного меню: сцен три, Bootstrap → MainMenu → MainGame.
        return SceneNames.MainMenu;
    }
}

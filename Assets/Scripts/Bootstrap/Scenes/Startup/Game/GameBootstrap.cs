#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.Game.Inventory;
using Kern.Game.Player.Stats;
using UnityEngine.SceneManagement;
using VContainer.Unity;
using Kern.Core;
using Kern.Networking.Gateways;


namespace Kern.Bootstrap.Scenes
{
    public sealed class GameBootstrap : IPostStartable
    {
        private readonly Scene _ownScene;
        private readonly GameLifetimeScope _scope;
        private readonly SceneTransitionTicket _ticket;
        private readonly AsyncOperationSupervisor _operations;
        private readonly GameStartupPipeline _pipeline;
        private readonly IWorldPresentationState _presentationState;
        private readonly PlayerStatsModel _playerStats;
        private readonly InventoryModel _inventory;
        private readonly WindowCommandStream _windows;
        private readonly IWorldLoadProgress _loadProgress;

        public GameBootstrap(
            Scene ownScene,
            GameLifetimeScope scope,
            SceneTransitionTicket ticket,
            AsyncOperationSupervisor operations,
            GameStartupPipeline pipeline,
            IWorldPresentationState presentationState,
            PlayerStatsModel playerStats,
            InventoryModel inventory,
            WindowCommandStream windows,
            IWorldLoadProgress loadProgress)
        {
            _ownScene = ownScene;
            _scope = scope;
            _ticket = ticket;
            _operations = operations;
            _pipeline = pipeline;
            _presentationState = presentationState;
            _playerStats = playerStats;
            _inventory = inventory;
            _windows = windows;
            _loadProgress = loadProgress;
            _ticket.Attach(_ownScene);
        }

        public void PostStart()
        {
            _scope.InitializePresentationState(_presentationState, _playerStats, _inventory);
            _operations.Run("game_startup", _ => StartAsync());
        }

        private async UniTask StartAsync()
        {
            CancellationToken scopeToken = _scope.destroyCancellationToken;
            void UpdateTimeout() => _ticket.SetWaitingForExternalWork(
                _windows.HasOpenWindows || _loadProgress.CurrentPhase == WorldLoadPhase.WorldManifest);
            void OnWindowVisibilityChanged(bool _) => UpdateTimeout();
            void OnLoadPhaseChanged(WorldLoadPhase _) => UpdateTimeout();
            void OnLoadFailed(Exception exception) => _ticket.Fail(exception);
            _windows.OpenWindowVisibilityChanged += OnWindowVisibilityChanged;
            _loadProgress.PhaseChanged += OnLoadPhaseChanged;
            _loadProgress.Failed += OnLoadFailed;
            UpdateTimeout();
            try
            {
                UnityEngine.Debug.Log($"[GameBootstrap] Waiting for activation of '{_ownScene.name}'.");
                await _ticket.WaitForActivationAsync()
                    .AttachExternalCancellation(scopeToken);
                _scope.ActivateSceneServices();
                UniTask lightingValidation = _pipeline.BeginLightingOrientationValidationAsync();
                float preparationStartedAt = UnityEngine.Time.realtimeSinceStartup;
                UnityEngine.Debug.Log("[GameBootstrap] Preparing persistent world data before connecting.");
                await _pipeline.PreparePersistentDataAsync(scopeToken);
                UnityEngine.Debug.Log(
                    $"[GameBootstrap] Persistent data ready in " +
                    $"{(UnityEngine.Time.realtimeSinceStartup - preparationStartedAt) * 1000f:F0} ms; connecting.");
                // Соединение стартует здесь и идёт параллельно проверке света и инициализации презентации
                GameStartupReport report = _pipeline.InitializeInfrastructure();
                await lightingValidation;
                UnityEngine.Debug.Log(
                    $"[GameBootstrap] Lighting validation ready at " +
                    $"{(UnityEngine.Time.realtimeSinceStartup - preparationStartedAt) * 1000f:F0} ms.");
                _pipeline.InitializePresentation(report);
                _ticket.MarkStartupReady();
                UnityEngine.Debug.Log("[GameBootstrap] Startup initialized; waiting for world presentation readiness.");
                await _pipeline.WaitUntilReadyAsync(_ticket, report, scopeToken);
                _scope.MarkReady();
                _ticket.MarkPresentationReady();
                UnityEngine.Debug.Log($"[GameBootstrap] Scene '{_ownScene.name}' is ready.");
            }
            catch (OperationCanceledException) when (scopeToken.IsCancellationRequested)
            {
                UnityEngine.Debug.Log($"[GameBootstrap] Startup of '{_ownScene.name}' cancelled because the scene was destroyed.");
                _ticket.Fail(new OperationCanceledException(
                    $"Game scene '{_ownScene.name}' was destroyed during startup."));
            }
            catch (Exception exception)
            {
                _presentationState.FailTransition(_presentationState.Current.TransitionId);
                UnityEngine.Debug.LogError(
                    $"[GameBootstrap] Startup failed in scene '{_ownScene.name}' " +
                    $"during transition phase '{_ticket.Phase}'.");
                UnityEngine.Debug.LogException(exception);
                if (exception is not ConnectionStartupInterruptedException and not TimeoutException)
                {
                    _scope.MarkFailed(exception);
                }

                _ticket.Fail(exception);
            }
            finally
            {
                _windows.OpenWindowVisibilityChanged -= OnWindowVisibilityChanged;
                _loadProgress.PhaseChanged -= OnLoadPhaseChanged;
                _loadProgress.Failed -= OnLoadFailed;
            }
        }

    }
}

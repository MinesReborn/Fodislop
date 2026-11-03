#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Networking;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using VContainer;
using Stopwatch = System.Diagnostics.Stopwatch;
using Kern.Networking.Gateways;
using Kern.Networking.Status;
using Kern.UI.Localization;
using Kern.UI.Screens;

namespace Kern.UI.Menu;

internal sealed class MenuWorldEntryController
{
    private readonly ILocalizationService _loc;
    private readonly ISceneNavigator _sceneNavigator;
    private readonly IConnectionService _connection;
    private readonly IWorldEntryPreparation _worldEntryPreparation;
    private readonly IWorldLoadProgress _loadProgress;
    private readonly IAsyncOperationSupervisor _operations;
    private readonly WindowCommandStream _windowCommands;
    private readonly MenuLoaderPanel _loaderPanel;
    private readonly MenuModalManager _modalManager;
    private readonly MenuNavigationPresenter _navigationPresenter;
    private readonly MenuSceneryPresenter _sceneryPresenter;
    private readonly Func<CancellationToken> _destroyToken;
    private VisualElement? _mainMenuContainer;
    private VisualElement? _root;
    private VisualElement? _tree;
    private bool _teardownStarted;

    internal MenuWorldEntryController(ILocalizationService localization,
        ISceneNavigator sceneNavigator, IConnectionService connection,
        IWorldEntryPreparation preparation, IWorldLoadProgress loadProgress,
        IAsyncOperationSupervisor operations, WindowCommandStream windowCommands,
        MenuLoaderPanel loaderPanel, MenuModalManager modalManager,
        MenuNavigationPresenter navigationPresenter, MenuSceneryPresenter sceneryPresenter,
        Func<CancellationToken> destroyToken)
    {
        _loc = localization;
        _sceneNavigator = sceneNavigator;
        _connection = connection;
        _worldEntryPreparation = preparation;
        _loadProgress = loadProgress;
        _operations = operations;
        _windowCommands = windowCommands;
        _loaderPanel = loaderPanel;
        _modalManager = modalManager;
        _navigationPresenter = navigationPresenter;
        _sceneryPresenter = sceneryPresenter;
        _destroyToken = destroyToken;
    }

    internal bool IsLoading => _loadingActive;

    internal void Bind(VisualElement? tree, VisualElement? root, VisualElement? mainMenuContainer)
    {
        _tree = tree;
        _root = root;
        _mainMenuContainer = mainMenuContainer;
    }

    internal void Dispose()
    {
        _teardownStarted = true;
        if (_windowVisibilitySubscribed)
        {
            _windowCommands.OpenWindowVisibilityChanged -= OnServerWindowVisibilityChanged;
            _windowVisibilitySubscribed = false;
        }

        _descentCancellation?.Cancel();
        _descentCancellation?.Dispose();
        _descentCancellation = null;
    }

        internal const string GameSceneName = SceneNames.MainGame;

        internal bool _loadingActive;
        internal bool _windowVisibilitySubscribed;
        internal CancellationTokenSource? _descentCancellation;

        internal void UpdateLoaderProgress()
        {
            WorldLoadPhase phase = _loadProgress != null
                ? _loadProgress.CurrentPhase
                : WorldLoadPhase.Handshake;
            _loaderPanel.UpdateProgress(
                phase,
                _loadProgress?.AssetSync ?? default,
                ReleaseInputToGameplay);
        }

        internal void HideMenu()
        {
            UIState.Hide(_mainMenuContainer);
        }

        internal void ReleaseInputToGameplay()
        {
            _loadingActive = false;
            UIState.Hide(_tree);
            if (_root != null)
            {
                _root.pickingMode = PickingMode.Ignore;
            }
        }

        internal void SubscribeWindowVisibility()
        {
            if (_windowVisibilitySubscribed || _windowCommands == null)
            {
                return;
            }

            _windowCommands.OpenWindowVisibilityChanged += OnServerWindowVisibilityChanged;
            _windowVisibilitySubscribed = true;
            OnServerWindowVisibilityChanged(_windowCommands.HasOpenWindows);
        }

        internal void OnServerWindowVisibilityChanged(bool visible)
        {
            if (_teardownStarted || !_loadingActive)
            {
                return;
            }

            UIState.SetHidden(_tree, visible);
            if (_root != null)
            {
                _root.pickingMode = PickingMode.Ignore;
            }

            if (!visible)
            {
                _loaderPanel.Show();
                UpdateLoaderProgress();
            }
        }

        internal void OnPlayButtonClicked()
        {
            if (_loadingActive || _teardownStarted)
            {
                return;
            }

            Debug.Log($"[Probe] T0 {UnityEngine.Time.realtimeSinceStartup:F3}");
            Debug.Log("[MainMenu] Play button clicked - initiating descent sequence");

            HideMenu();
            _modalManager.CloseCurrentModal();
            _loadingActive = true;
            _descentCancellation?.Dispose();
            _descentCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _destroyToken());

            _loaderPanel.Show();
            _loaderPanel.Reset();

            if (_windowCommands != null && _windowCommands.HasOpenWindows)
            {
                OnServerWindowVisibilityChanged(visible: true);
            }

            _navigationPresenter.SetDescentRouteActive();

            _sceneryPresenter.DescentTarget = 1f;
            // Провайдер живёт всё приложение: без сброса второй заход видит Done от первого и сразу отпускает ввод
            _loadProgress?.Report(WorldLoadPhase.Handshake);
            _loadProgress?.ResetAssetSync();
            UpdateLoaderProgress();

            _operations.Run("main_menu_descent", RunDescentAsync);
        }

        internal async UniTask RunDescentAsync(CancellationToken supervisorToken)
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                supervisorToken,
                _descentCancellation?.Token ?? CancellationToken.None);
            CancellationToken transitionToken = linkedCancellation.Token;
            try
            {
                // Подготовка мира не входит в таймаут перехода: он меряет только сцену.
                var preparationTimer = Stopwatch.StartNew();
                await _worldEntryPreparation.EnsureReadyAsync(transitionToken);
                Debug.Log(
                    $"[MainMenu] World-entry preparation completed in {preparationTimer.Elapsed.TotalSeconds:F2} s; " +
                    "starting MainGame scene transition.");
                transitionToken.ThrowIfCancellationRequested();
                await _sceneNavigator.TransitionAsync(GameSceneName, transitionToken);
            }
            catch (OperationCanceledException) when (transitionToken.IsCancellationRequested)
            {
            }
            catch (ConnectionStartupInterruptedException exception)
            {
                if (!_teardownStarted)
                {
                    // Не warning: вход в мир оборвался, а по консоли это было
                    // неотличимо от штатного спуска. Причина обязана быть
                    // слышна — иначе отказ ищут по одному экрану.
                    Debug.LogError(
                        $"[MainMenu] MainGame startup was interrupted by a disconnected transport; " +
                        $"descent remains active. {exception}");
                    _loaderPanel.ShowFailure(exception, _loc);
                }
            }
            catch (TimeoutException exception) when (IsWaitingForWorldStartup())
            {
                if (!_teardownStarted)
                {
                    Debug.LogError(
                        $"[MainMenu] MainGame startup timed out; keeping the descent screen while " +
                        $"waiting for the server. {exception}");
                    _loaderPanel.ShowFailure(exception, _loc);
                }
            }
            catch (Exception exception)
            {
                if (_teardownStarted)
                {
                    return;
                }

                _loaderPanel.ShowFailure(exception, _loc);
            }
        }

        internal bool IsWaitingForWorldStartup() =>
            _connection.IsAutoReconnecting || _connection.IsWorldStartupPending;

        internal void CancelDescent()
        {
            if (!_loadingActive || _teardownStarted)
            {
                return;
            }

            Debug.Log("[MainMenu] Descent cancelled by user - returning to main menu");

            // Переход сцены отменяется токеном: навигатор сам откатит частично
            // загруженную сцену и разорвёт соединение. CTS сознательно не
            // освобождается: операция могла ещё не начать исполняться и
            // прочитает токен позже; освободит её следующий запуск спуска.
            _descentCancellation?.Cancel();

            _loadingActive = false;
            _sceneryPresenter.DescentTarget = 0f;
            _sceneryPresenter.ResumeRenderers();
            _navigationPresenter.ResetDescentRoute();

            _loaderPanel.Hide();
            _loaderPanel.Reset();
            UIState.Show(_tree);
            if (_root != null)
            {
                _root.pickingMode = PickingMode.Ignore;
            }

            UIState.Show(_mainMenuContainer);
        }

}

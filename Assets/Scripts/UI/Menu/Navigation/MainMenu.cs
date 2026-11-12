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
using Kern.UI.Gateway;
using Kern.UI.Localization;
using Kern.UI.Screens;

namespace Kern.UI.Menu
{
    [ExecuteAlways]
    [RequireComponent(typeof(UIDocument))]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Kern.UI", sourceAssembly: null, sourceClassName: "MainMenu")]
    public class MainMenu : MonoBehaviour, ILocalizableUI
    {

        private UIDocument? _doc;
        private VisualElement? _root;
        private VisualElement? _tree;
        private VisualElement? _mainMenuContainer;
        private readonly MenuLoaderPanel _loaderPanel = new();
        private readonly MenuModalManager _modalManager = new();
        private readonly MenuNavigationPresenter _navigationPresenter = new();
        private MenuServerStatusPresenter _serverStatusPresenter = null!;
        private MenuPlayerIdentityPresenter _identityPresenter = null!;
        private MenuWorldEntryController _worldEntry = null!;
        private bool _built;
        private bool _teardownStarted;

        [Inject]
        private ILocalizationService _loc = null!;
        [Inject]
        private ISceneNavigator _sceneNavigator = null!;
        [Inject]
        private IConnectionService _connection = null!;
        [Inject]
        private IWorldEntryPreparation _worldEntryPreparation = null!;
        [Inject]
        private IWorldLoadProgress _loadProgress = null!;
        [Inject]
        private IClientConfigManager _clientConfig = null!;
        [Inject]
        private IAsyncOperationSupervisor _operations = null!;
        [Inject]
        private WindowCommandStream _windowCommands = null!;
        [Inject]
        private IPlayerStats _playerStats = null!;
        [Inject]
        private ILoadingTip _loadingTip = null!;
        [Inject]
        private ILocalPlayerState _localPlayer = null!;
        [Inject]
        private Kern.UI.ServerWindows.ServerWindowPresenter _presenter = null!;
        [Inject]
        private Kern.UI.Gateway.MenuGate _gate = null!;
        [Inject]
        private IAudioSystem _audio = null!;
        [Inject]
        private Kern.Rendering.Display.DisplayManager _display = null!;

        // Окно настроек — общее с игрой; в меню живых систем графики нет, выбор сохраняется в конфиг
        private Kern.UI.Settings.SettingsWindow? _settings;

        [Inject]
        private void Construct()
        {
            _serverStatusPresenter = new MenuServerStatusPresenter(_loc, _operations, _clientConfig);
            _identityPresenter = new MenuPlayerIdentityPresenter(_loc, _playerStats);
            _worldEntry = new MenuWorldEntryController(_loc, _sceneNavigator, _connection,
                _worldEntryPreparation, _loadProgress, _operations, _windowCommands,
                _loaderPanel, _modalManager, _localPlayer, _presenter,
                () => destroyCancellationToken);
        }

        protected void OnValidate()
        {
            if (!Application.isPlaying)
            {
                _built = false;
            }
        }

        protected void OnEnable()
        {
            if (_teardownStarted)
            {
                return;
            }

            if (_built && Application.isPlaying && _tree != null)
            {
                UIDocument doc = GetComponent<UIDocument>();
                if (doc == null || doc.rootVisualElement == null)
                {
                    // Реактивация — best-effort: панель может пересоздаться позже
                    // (повторный OnEnable документа); первичная сборка в Start
                    // уже прошла, поэтому тихий возврат не теряет экран.
                    return;
                }

                _root = doc.rootVisualElement;
                _root.pickingMode = PickingMode.Ignore;
                if (!_root.Contains(_tree))
                {
                    _root.Add(_tree);
                }
                _worldEntry.Bind(_tree, _root, _mainMenuContainer);
                _worldEntry.SubscribeWindowVisibility();

                if (_loc != null)
                {
                    _loc.RegisterLocalizable(this);
                    ApplyLocalizedText();
                }
            }
        }

        public void InitializeScene()
        {
            if (_teardownStarted)
            {
                return;
            }

            if (_built && _tree != null)
            {
                return;
            }

            if (_built)
            {
                Debug.LogWarning("[MainMenu] _built was true but _tree is null (likely a hot-reload while in Play Mode) - rebuilding UI from scratch.");
                _built = false;
            }

            _doc = GetComponent<UIDocument>();
            _root = _doc != null ? _doc.rootVisualElement : null;
            if (_doc == null || _root == null)
            {
                throw new InvalidOperationException(
                    "[MainMenu] UIDocument panel is not available at Start (панель создаётся в OnEnable документа и к Start обязана существовать).");
            }

            PanelSettings panelSettings = _doc.panelSettings ??
                throw new InvalidOperationException(
                    "[MainMenu] UIDocument requires an authored PanelSettings asset.");

            var mainMenuUxml = Resources.Load<VisualTreeAsset>(ResourcePaths.MainMenuUxml);
            if (mainMenuUxml == null)
            {
                throw new InvalidOperationException(
                    "Required UI asset 'Resources/UI/Menu/Templates/MainMenu.uxml' was not found.");
            }

            _root.Clear();
            _root.pickingMode = PickingMode.Ignore;
            VisualElement tree = mainMenuUxml.CloneTree();
            tree.AddToClassList("ui-fullscreen");
            _root.Add(tree);

            _doc.panelSettings = panelSettings;
            UIScreen.Bind(_doc, _clientConfig?.Config.Interface.UIScale ?? 0f, _clientConfig?.Config.Interface.TextScale);
            _tree = tree;

            BindUIElements(tree);
            _settings?.Dispose();
            _settings = new Kern.UI.Settings.SettingsWindow(
                _doc,
                _root,
                _clientConfig!,
                _audio,
                _display,
                new Kern.UI.Settings.ConfigSettingsApply(_clientConfig!),
                _loc,
                _playerStats,
                _ => { });
            _modalManager.UseSettingsWindow(_settings);

            _worldEntry.SubscribeWindowVisibility();

            if (_loc != null)
            {
                _loc.RegisterLocalizable(this);
            }

            ApplyLocalizedText();
            _built = true;

            // Ворота (онбординг; авторизация — серверное окно поверх них)
            // перестройка меню не открывает заново.
            if (!_gate.Passed)
            {
                _gate.Open(_doc, _root);
            }

            Debug.Log($"[MainMenu] UI BUILT successfully: children={_root.childCount}");
        }

        public async UniTask WaitUntilReadyAsync(CancellationToken cancellationToken = default)
        {
            float timeout = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _serverStatusPresenter.TickOnlineBadge(_worldEntry.IsLoading);
                if (_built)
                {
                    return;
                }

                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(
                "Разметка главного меню не построена за 3 секунды.");
        }

        private void BindUIElements(VisualElement tree)
        {
            VisualElement searchRoot = _root ?? tree;
            _mainMenuContainer = tree.Q<VisualElement>("MainMenuContainer") ?? searchRoot.Q<VisualElement>("MainMenuContainer");
            _worldEntry.Bind(tree, searchRoot, _mainMenuContainer);

            // Живое имя и уровень шахтёра (бейдж справа сверху и досье):
            // сервер присылает их по сокету ещё в меню, см. PlayerIdentityProcessor.

            _serverStatusPresenter.Bind(tree);
            _identityPresenter.Bind(tree);
            _identityPresenter.SubscribePlayerIdentity();
            _loaderPanel.Bind(tree, searchRoot, _loc, _loadingTip);

            _navigationPresenter.Bind(
                tree,
                _modalManager,
                _worldEntry.OnPlayButtonClicked,
                _worldEntry.CancelDescent,
                _worldEntry.RetryDescent,
                _loc);

            _loaderPanel.Reset();
        }

        protected void Update()
        {
            if (_teardownStarted)
            {
                return;
            }

            if (Application.isPlaying && !_built)
            {
                InitializeScene();
                if (!_built)
                {
                    return;
                }
            }

            if (Application.isPlaying && _built && _doc != null && _tree != null)
            {
                var liveRoot = _doc.rootVisualElement;
                if (liveRoot == null || !ReferenceEquals(_tree.parent, liveRoot))
                {
                    _tree = null;
                    _built = false;
                    InitializeScene();
                    return;
                }
            }

            // [ExecuteAlways]: вне Play Mode VContainer не вызывает Construct.
            if (!Application.isPlaying)
            {
                return;
            }

            if (_worldEntry.IsLoading)
            {
                _worldEntry.UpdateLoaderProgress();
            }

            _serverStatusPresenter.TickOnlineBadge(_worldEntry.IsLoading);
            // Пока открыты ворота (онбординг), клавиши меню (Enter — «Играть») не действуют.
            if (_gate.IsOpen)
            {
                return;
            }

            MenuKeyboardHandler.HandleInput(_modalManager, _worldEntry.IsLoading, _worldEntry.OnPlayButtonClicked, _worldEntry.CancelDescent);

        }

        public void CloseCurrentModal() => _modalManager.CloseCurrentModal();

        public void ApplyLocalizedText()
        {
            UILocalizer.AssertLocalizationServiceAvailable(_loc, nameof(MainMenu));
            if (_tree == null || _loc == null)
            {
                return;
            }

            UILocalizer.Apply(_tree, _loc);
            _navigationPresenter.ApplyLocalization(_loc);
            _loaderPanel.RefreshLocalization();
            // UILocalizer.Apply сбрасывает тексты на ключи разметки — возвращаем живые.
            _identityPresenter.ApplyPlayerIdentity();
            UILocalizer.AssertLocalized(_tree, _loc);
        }

        protected void OnDestroy()
        {
            _teardownStarted = true;
            if (_doc != null)
            {
                UIScreen.Unbind(_doc);
            }

            _identityPresenter?.Dispose();
            _loaderPanel.Dispose();

            if (_loc != null)
            {
                _loc.UnregisterLocalizable(this);
            }

            _worldEntry?.Dispose();
            _settings?.Dispose();
            _settings = null;

            _tree?.RemoveFromHierarchy();
            _tree = null;
        }
    }
}

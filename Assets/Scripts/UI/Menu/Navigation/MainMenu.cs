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
using Kern.Core.Platform;
using Kern.UI.Localization;
using Kern.UI.Screens;

namespace Kern.UI.Menu
{
    [ExecuteAlways]
    [RequireComponent(typeof(UIDocument))]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Kern.UI", sourceAssembly: null, sourceClassName: "MainMenu")]
    public class MainMenu : MonoBehaviour, ILocalizableUI
    {

        [SerializeField]
        private Texture2D? _shadeTexture;
        [SerializeField]
        private Texture2D? _spaceBgTexture;

        private UIDocument? _doc;
        private VisualElement? _root;
        private VisualElement? _tree;
        private VisualElement? _mainMenuContainer;
        private readonly MenuLoaderPanel _loaderPanel = new();
        private readonly MenuModalManager _modalManager = new();
        private readonly MenuNavigationPresenter _navigationPresenter = new();
        private MenuRepairPresenter _repairPresenter = null!;
        private MenuServerStatusPresenter _serverStatusPresenter = null!;
        private MenuPlayerIdentityPresenter _identityPresenter = null!;
        private MenuWorldEntryController _worldEntry = null!;
        private bool _built;
        private bool _subscribed;
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
        private IMenuFileDiagnosticsService _fileDiagnostics = null!;
        [Inject]
        private WindowCommandStream _windowCommands = null!;
        [Inject]
        private IPlayerStats _playerStats = null!;

        private MenuStarfield? _sceneStarfield;
        private MenuSceneryController? _sceneScenery;
        private MenuSceneryPresenter _sceneryPresenter = null!;

        [Inject]
        private void Construct(IRuntimeAssetPaths runtimeAssetPaths)
        {
            _sceneryPresenter = new MenuSceneryPresenter(runtimeAssetPaths);
            _repairPresenter = new MenuRepairPresenter(_loc, _operations, _fileDiagnostics);
            _serverStatusPresenter = new MenuServerStatusPresenter(_loc, _operations, _clientConfig, _modalManager);
            _identityPresenter = new MenuPlayerIdentityPresenter(_loc, _playerStats);
            _worldEntry = new MenuWorldEntryController(_loc, _sceneNavigator, _connection,
                _worldEntryPreparation, _loadProgress, _operations, _windowCommands,
                _loaderPanel, _modalManager, _navigationPresenter, _sceneryPresenter,
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
                SubscribeEvents();
                _worldEntry.SubscribeWindowVisibility();
                _sceneryPresenter.Bind(_tree);
                _sceneryPresenter.ApplyTextures(ref _shadeTexture, ref _spaceBgTexture);

                if (_loc != null)
                {
                    _loc.RegisterLocalizable(this);
                    ApplyLocalizedText();
                }
            }
        }

        public void InitializeScene(MenuStarfield? starfield, MenuSceneryController? scenery)
        {
            _sceneStarfield = starfield;
            _sceneScenery = scenery;
            _sceneryPresenter.BindScene(starfield, scenery);

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
            _modalManager.Bind(tree);
            _sceneryPresenter.Bind(tree);

            _subscribed = false;
            SubscribeEvents();
            _worldEntry.SubscribeWindowVisibility();
            _sceneryPresenter.ApplyTextures(ref _shadeTexture, ref _spaceBgTexture);

            if (_loc != null)
            {
                _loc.RegisterLocalizable(this);
            }

            ApplyLocalizedText();
            _built = true;

            _sceneryPresenter.MarkUIBuilt();
            Debug.Log($"[MainMenu] UI BUILT successfully: children={_root.childCount}");
        }

        public async UniTask WaitUntilReadyAsync(CancellationToken cancellationToken = default)
        {
            float timeout = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_built && _sceneryPresenter.IsSceneryReady)
                {
                    return;
                }

                _serverStatusPresenter.TickOnlineBadge(_worldEntry.IsLoading);
            _sceneryPresenter.Tick(ref _spaceBgTexture);
                if (_built && _sceneryPresenter.IsSceneryReady)
                {
                    return;
                }

                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(
                "Сценка главного меню не готова за 3 секунды. " +
                $"Разметка построена={_built}; {_sceneryPresenter.DescribeReadiness()}.");
        }

        private void BindUIElements(VisualElement tree)
        {
            VisualElement searchRoot = _root ?? tree;
            _mainMenuContainer = tree.Q<VisualElement>("MainMenuContainer") ?? searchRoot.Q<VisualElement>("MainMenuContainer");
            _worldEntry.Bind(tree, searchRoot, _mainMenuContainer);

            // Живое имя и уровень шахтёра (бейдж справа сверху и досье):
            // сервер присылает их по сокету ещё в меню, см. PlayerIdentityProcessor.

            // Строка и карточка реального сервера в окне выбора: метки без
            // имён, ищутся классами внутри карточки ServerItemHades.

            _repairPresenter.Bind(tree);
            _serverStatusPresenter.Bind(tree);
            _identityPresenter.Bind(tree);
            _identityPresenter.SubscribePlayerIdentity();
            _loaderPanel.Bind(tree, searchRoot, _loc);

            _navigationPresenter.Bind(
                tree,
                _modalManager,
                _worldEntry.OnPlayButtonClicked,
                _worldEntry.CancelDescent,
                _loc,
                ClientMarketPolicy.IsVkAvailable);

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
                InitializeScene(_sceneStarfield, _sceneScenery);
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
                    InitializeScene(_sceneStarfield, _sceneScenery);
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
            _sceneryPresenter.Tick(ref _spaceBgTexture);
            MenuKeyboardHandler.HandleInput(_modalManager, _worldEntry.IsLoading, _worldEntry.OnPlayButtonClicked, _worldEntry.CancelDescent);

        }

        private void SubscribeEvents()
        {
            if (_subscribed)
            {
                return;
            }

            if (_tree != null)
            {
                _modalManager.SubscribeEvents(
                    _tree,
                    _worldEntry.OnPlayButtonClicked,
                    _clientConfig,
                    _sceneNavigator,
                    _operations,
                    _loc);

                _repairPresenter.Subscribe();

                _modalManager.RepairModalOpened += _repairPresenter.OnRepairModalOpened;
            }

            _subscribed = true;
        }

        public void OpenModal(VisualElement? modal) => _modalManager.OpenModal(modal);

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
            _repairPresenter.ApplyRepairTexts();
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

            if (_repairPresenter != null)
            {
                _modalManager.RepairModalOpened -= _repairPresenter.OnRepairModalOpened;
                _repairPresenter.Dispose();
            }

            _identityPresenter?.Dispose();


            if (_loc != null)
            {
                _loc.UnregisterLocalizable(this);
            }

            _worldEntry?.Dispose();

            _tree?.RemoveFromHierarchy();
            _tree = null;
        }
    }
}

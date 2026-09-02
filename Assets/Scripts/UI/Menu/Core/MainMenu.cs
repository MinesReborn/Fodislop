#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Localization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae.UI
{
    [ExecuteAlways]
    [RequireComponent(typeof(UIDocument))]
    public class MainMenu : MonoBehaviour, ILocalizableUI
    {
        private const string GameSceneName = ProjectRuntimeContracts.SceneNames.MainGame;

        [SerializeField]
        private Texture2D? _shadeTexture;
        [SerializeField]
        private Texture2D? _spaceBgTexture;

        private UIDocument? _doc;
        private VisualElement? _root;
        private VisualElement? _tree;
        private VisualElement? _mainMenuContainer;
        private VisualElement? _loaderContainer;
        private VisualElement? _loaderContent;
        private MenuLoaderProgress? _loaderProgress;
        private readonly MenuModalManager _modalManager = new();

        // Шаги маршрута в футере
        private VisualElement? _routeOrbit;
        private VisualElement? _routeDescent;
        private VisualElement? _routeSurface;

        // Кнопки основного экрана
        private Button? _playButton;
        private Button? _serverSelectButton;
        private Button? _updateAlertBanner;
        private Button? _userPillButton;
        private Button? _cancelDescentButton;

        // Правая боковая панель (Genshin Sidebar)
        private Button? _sideChronicleButton;
        private Button? _sideSettingsButton;
        private Button? _sideRepairButton;
        private Button? _sideUpdateButton;
        private Button? _sideDiscordButton;
        private Button? _sideTelegramButton;
        private Button? _sideVkButton;
        private Button? _sideExitButton;

        // Футер
        private Button? _newsTickerButton;
        private Button? _footerVersionButton;

        private bool _loadingActive;
        private bool _built;
        private bool _subscribed;
        private bool _teardownStarted;
        private CancellationTokenSource? _descentCancellation;

        [Inject]
        private ILocalizationService _loc = null!;
        [Inject]
        private ISceneNavigator _sceneNavigator = null!;
        [Inject]
        private IWorldLoadProgress _loadProgress = null!;
        [Inject]
        private IClientConfigManager _clientConfig = null!;
        [Inject]
        private IAsyncOperationSupervisor _operations = null!;

        private bool _loaderHiddenAtDone;
        private MenuStarfield? _sceneStarfield;
        private MenuSceneryController? _sceneScenery;
        private MenuSceneryPresenter _sceneryPresenter = null!;

        [Inject]
        private void Construct(IRuntimeAssetPaths runtimeAssetPaths)
        {
            _sceneryPresenter = new MenuSceneryPresenter(runtimeAssetPaths);
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
                SubscribeEvents();
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

            var mainMenuUXML = Resources.Load<VisualTreeAsset>(ProjectRuntimeContracts.ResourcePaths.MainMenuUxml);
            if (mainMenuUXML == null)
            {
                throw new InvalidOperationException(
                    "Required UI asset 'Resources/UI/MainMenu.uxml' was not found.");
            }

            _root.Clear();
            VisualElement tree = mainMenuUXML.CloneTree();
            tree.AddToClassList("ui-fullscreen");
            _root.Add(tree);
            _tree = tree;

            UILayoutTier.Attach(tree);

            BindUIElements(tree);
            _modalManager.Bind(tree);
            _sceneryPresenter.Bind(tree);

            _subscribed = false;
            SubscribeEvents();
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
            while (Time.realtimeSinceStartup < timeout && !cancellationToken.IsCancellationRequested)
            {
                if (_built && _sceneryPresenter.IsSceneryReady)
                {
                    return;
                }

                _sceneryPresenter.Tick(ref _spaceBgTexture);
                if (_built && _sceneryPresenter.IsSceneryReady)
                {
                    return;
                }

                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
        }

        private void BindUIElements(VisualElement tree)
        {
            VisualElement searchRoot = _root ?? tree;
            _mainMenuContainer = tree.Q<VisualElement>("MainMenuContainer") ?? searchRoot.Q<VisualElement>("MainMenuContainer");
            _loaderContainer = tree.Q<VisualElement>("LoaderContainer") ?? searchRoot.Q<VisualElement>("LoaderContainer");
            _loaderContent = tree.Q<VisualElement>("LoaderContent") ?? searchRoot.Q<VisualElement>("LoaderContent");
            VisualElement? loaderProgressFill = tree.Q<VisualElement>("LoaderProgressFill") ?? searchRoot.Q<VisualElement>("LoaderProgressFill");
            Label? loaderPhaseLabel = tree.Q<Label>("LoaderPhaseLabel") ?? searchRoot.Q<Label>("LoaderPhaseLabel");
            Label? loaderPhaseCount = tree.Q<Label>("LoaderPhaseCount") ?? searchRoot.Q<Label>("LoaderPhaseCount");
            VisualElement? loaderPhaseList = tree.Q<VisualElement>("LoaderPhaseList") ?? searchRoot.Q<VisualElement>("LoaderPhaseList");

            if (_loaderContainer == null || _loaderContent == null ||
                loaderProgressFill == null || loaderPhaseLabel == null ||
                loaderPhaseCount == null || loaderPhaseList == null)
            {
                Debug.LogWarning("[MainMenu] Some loader elements missing from MainMenu.uxml, synthesizing placeholders to prevent startup crash.");
                _loaderContainer ??= new VisualElement { name = "LoaderContainer" };
                _loaderContent ??= new VisualElement { name = "LoaderContent" };
                loaderProgressFill ??= new VisualElement { name = "LoaderProgressFill" };
                loaderPhaseLabel ??= new Label { name = "LoaderPhaseLabel" };
                loaderPhaseCount ??= new Label { name = "LoaderPhaseCount" };
                loaderPhaseList ??= new VisualElement { name = "LoaderPhaseList" };

                _loaderContainer.Add(_loaderContent);
                _loaderContent.Add(loaderProgressFill);
                _loaderContent.Add(loaderPhaseLabel);
                _loaderContent.Add(loaderPhaseCount);
                _loaderContent.Add(loaderPhaseList);
                if (searchRoot != null && !searchRoot.Contains(_loaderContainer))
                {
                    searchRoot.Add(_loaderContainer);
                }
            }

            _loaderProgress = new MenuLoaderProgress(
                loaderProgressFill,
                loaderPhaseLabel,
                loaderPhaseCount,
                loaderPhaseList,
                _loc);
            _routeOrbit = tree.Q<VisualElement>("MainMenuRouteOrbit");
            _routeDescent = tree.Q<VisualElement>("MainMenuRouteDescent");
            _routeSurface = tree.Q<VisualElement>("MainMenuRouteSurface");

            _playButton = tree.Q<Button>("PlayButton");
            _serverSelectButton = tree.Q<Button>("ServerSelectButton");
            _updateAlertBanner = tree.Q<Button>("UpdateAlertBanner");
            _userPillButton = tree.Q<Button>("UserPillButton");
            _cancelDescentButton = tree.Q<Button>("CancelDescentButton");

            _sideChronicleButton = tree.Q<Button>("SideChronicleButton");
            _sideSettingsButton = tree.Q<Button>("SideSettingsButton");
            _sideRepairButton = tree.Q<Button>("SideRepairButton");
            _sideUpdateButton = tree.Q<Button>("SideUpdateButton");
            _sideDiscordButton = tree.Q<Button>("SideDiscordButton");
            _sideTelegramButton = tree.Q<Button>("SideTelegramButton");
            _sideVkButton = tree.Q<Button>("SideVkButton");
            _sideExitButton = tree.Q<Button>("SideExitButton");

            if (_loc != null)
            {
                Label? playLabel = _playButton?.Q<Label>(null, "mm-btn-primary-text");
                if (playLabel != null)
                {
                    playLabel.text = _loc.Get("menu.play");
                }

                Label? serverLabel = _serverSelectButton?.Q<Label>();
                if (serverLabel != null)
                {
                    serverLabel.text = _loc.Get("menu.server_select");
                }

                if (_cancelDescentButton != null)
                {
                    _cancelDescentButton.text = _loc.Get("menu.cancel_descent");
                }

                Label? orbitLabel = _routeOrbit?.Q<Label>(null, "mm-route-text");
                if (orbitLabel != null)
                {
                    orbitLabel.text = _loc.Get("menu.orbit");
                }

                Label? descentLabel = _routeDescent?.Q<Label>(null, "mm-route-text");
                if (descentLabel != null)
                {
                    descentLabel.text = _loc.Get("menu.descent");
                }

                if (_sideChronicleButton != null)
                {
                    _sideChronicleButton.tooltip = _loc.Get("menu.chronicle");
                }

                if (_sideSettingsButton != null)
                {
                    _sideSettingsButton.tooltip = _loc.Get("menu.settings");
                }

                if (_sideRepairButton != null)
                {
                    _sideRepairButton.tooltip = _loc.Get("menu.repair");
                }

                if (_sideUpdateButton != null)
                {
                    _sideUpdateButton.tooltip = _loc.Get("menu.update");
                }

                if (_sideExitButton != null)
                {
                    _sideExitButton.tooltip = _loc.Get("menu.exit");
                }
            }

            _newsTickerButton = tree.Q<Button>("NewsTickerButton");
            _footerVersionButton = tree.Q<Button>("FooterVersionButton");

            if (_loaderContainer != null)
            {
                _loaderContainer.pickingMode = PickingMode.Ignore;
            }

            UIState.Hide(_loaderContainer);
            UIState.Hide(_loaderContent);
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

            if (_loadingActive)
            {
                UpdateLoaderProgress();
            }

            _sceneryPresenter.Tick(ref _spaceBgTexture);
            HandleKeyboardInput();
        }

        private void HandleKeyboardInput()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                if (_modalManager.HasActiveModal)
                {
                    _modalManager.CloseCurrentModal();
                }
                else if (_loadingActive)
                {
                    CancelDescent();
                }
            }
            else if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                if (!_modalManager.HasActiveModal && !_loadingActive)
                {
                    OnPlayButtonClicked();
                }
            }
        }

        private void UpdateLoaderProgress()
        {
            WorldLoadPhase phase = _loadProgress != null
                ? _loadProgress.CurrentPhase
                : WorldLoadPhase.Handshake;
            _loaderProgress?.UpdateProgress(phase);

            if (phase == WorldLoadPhase.Done && !_loaderHiddenAtDone)
            {
                _loaderHiddenAtDone = true;
                HideLoader();
            }
        }

        private void SubscribeEvents()
        {
            if (_subscribed)
            {
                return;
            }

            if (_playButton != null)
            {
                _playButton.clicked += OnPlayButtonClicked;
            }

            if (_serverSelectButton != null)
            {
                _serverSelectButton.clicked += _modalManager.OpenServerBrowser;
            }

            if (_updateAlertBanner != null)
            {
                _updateAlertBanner.clicked += _modalManager.OpenUpdate;
            }

            if (_userPillButton != null)
            {
                _userPillButton.clicked += _modalManager.OpenProfile;
            }

            if (_cancelDescentButton != null)
            {
                _cancelDescentButton.clicked += CancelDescent;
            }

            if (_sideChronicleButton != null)
            {
                _sideChronicleButton.clicked += _modalManager.OpenChronicle;
            }

            if (_sideSettingsButton != null)
            {
                _sideSettingsButton.clicked += _modalManager.OpenSettings;
            }

            if (_sideRepairButton != null)
            {
                _sideRepairButton.clicked += _modalManager.OpenRepair;
            }

            if (_sideUpdateButton != null)
            {
                _sideUpdateButton.clicked += _modalManager.OpenUpdate;
            }

            if (_sideDiscordButton != null)
            {
                _sideDiscordButton.clicked += OpenDiscord;
            }

            if (_sideTelegramButton != null)
            {
                _sideTelegramButton.clicked += OpenTelegram;
            }

            if (_sideVkButton != null)
            {
                _sideVkButton.clicked += OpenVk;
            }

            if (_sideExitButton != null)
            {
                _sideExitButton.clicked += QuitGame;
            }

            if (_newsTickerButton != null)
            {
                _newsTickerButton.clicked += _modalManager.OpenChronicle;
            }

            if (_footerVersionButton != null)
            {
                ApplyVersionLabel();
                _footerVersionButton.clicked += _modalManager.OpenUpdate;
            }

            if (_tree != null)
            {
                _modalManager.SubscribeEvents(
                    _tree,
                    OnPlayButtonClicked,
                    _clientConfig,
                    _sceneNavigator,
                    _operations,
                    _loc);
            }

            _subscribed = true;
        }

        protected void OnDisable()
        {
        }

        public void OpenModal(VisualElement? modal) => _modalManager.OpenModal(modal);
        public void CloseCurrentModal() => _modalManager.CloseCurrentModal();

        private void QuitGame()
        {
            Debug.Log("[MainMenu] Exiting game client...");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static void OpenDiscord()
        {
            Application.OpenURL("https://discord.gg/fodinae");
        }

        private static void OpenTelegram()
        {
            Application.OpenURL("https://t.me/fodinae");
        }

        private static void OpenVk()
        {
            Application.OpenURL("https://vk.com/fodinae");
        }

        private void ApplyVersionLabel()
        {
            if (_footerVersionButton == null || _loc == null)
            {
                return;
            }

            _footerVersionButton.text = Application.isEditor
                ? _loc.Get("mainmenu.version_editor", Application.version)
                : Debug.isDebugBuild
                    ? _loc.Get("mainmenu.version_dev", Application.version)
                    : _loc.Get("mainmenu.version", Application.version);
        }

        public void ApplyLocalizedText()
        {
            UILocalizer.AssertLocalizationServiceAvailable(_loc, nameof(MainMenu));
            if (_tree == null || _loc == null)
            {
                return;
            }

            UILocalizer.Apply(_tree, _loc);
            ApplyVersionLabel();
            _loaderProgress?.RefreshLocalization();
            UILocalizer.AssertLocalized(_tree, _loc);
        }

        protected void OnDestroy()
        {
            _teardownStarted = true;
            if (_loc != null)
            {
                _loc.UnregisterLocalizable(this);
            }

            _descentCancellation?.Cancel();
            _descentCancellation?.Dispose();
            _descentCancellation = null;

            _tree?.RemoveFromHierarchy();
            _tree = null;
        }

        private void HideLoader()
        {
            UIState.Hide(_loaderContainer);
        }

        private void HideMenu()
        {
            UIState.Hide(_mainMenuContainer);
        }

        private void OnPlayButtonClicked()
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
                destroyCancellationToken);

            UIState.Show(_loaderContainer);
            UIState.Show(_loaderContent);

            _routeOrbit?.RemoveFromClassList("mm-route-item--active");
            _routeDescent?.AddToClassList("mm-route-item--active");

            _sceneryPresenter.DescentTarget = 1f;
            UpdateLoaderProgress();

            _operations.Run("main_menu_descent", RunDescentAsync);
        }

        private async UniTask RunDescentAsync(CancellationToken supervisorToken)
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                supervisorToken,
                _descentCancellation?.Token ?? CancellationToken.None);
            CancellationToken transitionToken = linkedCancellation.Token;
            try
            {
                await _sceneNavigator.TransitionAsync(GameSceneName, transitionToken);
            }
            catch (OperationCanceledException) when (transitionToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                if (_teardownStarted)
                {
                    return;
                }

                _loadingActive = false;
                _sceneryPresenter.ResumeRenderers();
                HideLoader();
                UIState.Show(_mainMenuContainer);

                Debug.LogError($"[MainMenu] MainGame transition failed: {exception.Message}");
            }
        }

        private void CancelDescent()
        {
            Debug.Log("[MainMenu] Descent is already in progress; waiting for MainGame.");
        }
    }
}

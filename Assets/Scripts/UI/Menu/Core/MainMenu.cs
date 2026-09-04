#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Localization;
using Fodinae.Networking;
using UnityEngine;
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
        private readonly MenuNavigationPresenter _navigationPresenter = new();

        private bool _loadingActive;
        private bool _built;
        private bool _subscribed;
        private bool _windowVisibilitySubscribed;
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
        [Inject]
        private WindowCommandStream _windowCommands = null!;

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
                _root.pickingMode = PickingMode.Ignore;
                SubscribeEvents();
                SubscribeWindowVisibility();
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

            var mainMenuUXML = Resources.Load<VisualTreeAsset>(ProjectRuntimeContracts.ResourcePaths.MainMenuUxml);
            if (mainMenuUXML == null)
            {
                throw new InvalidOperationException(
                    "Required UI asset 'Resources/UI/MainMenu.uxml' was not found.");
            }

            _root.Clear();
            _root.pickingMode = PickingMode.Ignore;
            VisualElement tree = mainMenuUXML.CloneTree();
            tree.AddToClassList("ui-fullscreen");
            _root.Add(tree);

            _doc.panelSettings = panelSettings;
            _tree = tree;

            UILayoutTier.Attach(tree);

            BindUIElements(tree);
            _modalManager.Bind(tree);
            _sceneryPresenter.Bind(tree);

            _subscribed = false;
            SubscribeEvents();
            SubscribeWindowVisibility();
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

            _navigationPresenter.Bind(
                tree,
                _modalManager,
                OnPlayButtonClicked,
                CancelDescent,
                _loc);

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
            MenuKeyboardHandler.HandleInput(_modalManager, _loadingActive, OnPlayButtonClicked, CancelDescent);
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
                ReleaseInputToGameplay();
            }
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
                    OnPlayButtonClicked,
                    _clientConfig,
                    _sceneNavigator,
                    _operations,
                    _loc);
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
            _loaderProgress?.RefreshLocalization();
            UILocalizer.AssertLocalized(_tree, _loc);
        }

        protected void OnDestroy()
        {
            _teardownStarted = true;
            if (_windowVisibilitySubscribed && _windowCommands != null)
            {
                _windowCommands.OpenWindowVisibilityChanged -= OnServerWindowVisibilityChanged;
                _windowVisibilitySubscribed = false;
            }

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

        private void ReleaseInputToGameplay()
        {
            _loadingActive = false;
            UIState.Hide(_tree);
            if (_root != null)
            {
                _root.pickingMode = PickingMode.Ignore;
            }
        }

        private void SubscribeWindowVisibility()
        {
            if (_windowVisibilitySubscribed || _windowCommands == null)
            {
                return;
            }

            _windowCommands.OpenWindowVisibilityChanged += OnServerWindowVisibilityChanged;
            _windowVisibilitySubscribed = true;
            OnServerWindowVisibilityChanged(_windowCommands.HasOpenWindows);
        }

        private void OnServerWindowVisibilityChanged(bool visible)
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
                UIState.Show(_loaderContainer);
                UIState.Show(_loaderContent);
                UpdateLoaderProgress();
            }
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

            if (_windowCommands != null && _windowCommands.HasOpenWindows)
            {
                OnServerWindowVisibilityChanged(visible: true);
            }

            _navigationPresenter.SetDescentRouteActive();

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
                UIState.Show(_tree);
                if (_root != null)
                {
                    _root.pickingMode = PickingMode.Ignore;
                }

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

#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Networking.Connection;
using Kern.Player;
using Kern.Rendering;
using Kern.Rendering.PostProcessing;
using Kern.UI;
using Kern.UI.Programmator;
using Kern.World.Lighting;
using Kern.World.Terrain;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Contracts.Packets;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VContainer;

using Kern.Rendering.Display;
using Kern.UI.Input;
using Kern.UI.Localization;
using Kern.UI.Screens;
using Kern.UI.ServerWindows;

namespace Kern.UI.Settings
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Kern.UI", sourceAssembly: null, sourceClassName: "PauseMenu")]
    public class PauseMenu : MonoBehaviour, ILocalizableUI
    {
        [Inject]
        private UIDocument _doc = null!;
        [Inject]
        private ServerWindowPresenter _windows = null!;
        [Inject]
        private IClientConfigManager _clientConfig = null!;
        [Inject]
        private LightingEngine _lightingEngine = null!;
        [Inject]
        private PostProcessController _postProcessController = null!;
        [Inject]
        private TerrainRenderer _terrainRenderer = null!;
        [Inject]
        private GraphicsSettingsController _graphicsSettings = null!;
        [Inject]
        private DisplayManager _displayManager = null!;
        [Inject]
        private ILocalPlayerState _localPlayer = null!;
        [Inject]
        private IMainMenuNavigation _mainMenuNavigation = null!;
        [Inject]
        private ILocalizationService _loc = null!;

        private VisualElement? _menuPanel;
        private TemplateContainer? _menuTree;
        private VisualElement? _mainPage;
        private VisualElement? _settingsPage;
        private bool _isOpen;
        private readonly List<Action> _settingsRefreshers = new();
        private bool _initialized;
        private bool _initializationFailed;

        [Inject]
        private INetworkService _networkService = null!;
        [Inject]
        private IAudioSystem _audioSystem = null!;
        [Inject]
        private IConnectionService _connectionService = null!;
        [Inject]
        private IInputBlocker _inputBlocker = null!;
        [Inject]
        private UIInputManager _uiInput = null!;
        [Inject]
        private TouchCommands _touchCommands = null!;
        private bool _touchSubscribed;
        private PauseMenuTabRouter? _tabRouter;

        protected void Start()
        {
            // Школа (одна дорога): зависимости и панель к Start гарантированы.
            // Освещение инициализируется в PostStart — один переход по событию
            // OnInitialized, без ретраев из Update.
            TryInitialize();
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                // Escape в открытом чате принадлежит чату, даже если его
                // Update в этом кадре ещё не выполнялся.
                if (!_isOpen && (_uiInput.IsChatFocused || _uiInput.IsEscapeConsumedThisFrame))
                {
                    return;
                }

                HandleBack();
            }
        }

        // Кнопка «Меню» HUD: на сенсорном экране это единственный путь в
        // паузу (у iOS нет системной «Назад», у Android она приходит как
        // Escape и идёт через Update).
        private void OnTouchPauseRequested()
        {
            if (!_isOpen && _uiInput.IsChatFocused)
            {
                return;
            }

            HandleBack();
        }

        private void HandleBack()
        {
            // Перехват клавиши на вкладке «Управление» поглощает Escape:
            // он отменяет перехват и не должен заодно закрывать меню.
            if (_uiInput.IsKeyCaptureInProgress)
            {
                return;
            }

            // Показанное серверное окно — верхний владелец Escape: оно
            // модальное и держит IInputBlocker.IsInputBlocked, а закрыть его
            // больше нечем. Escape в паузе не открывает меню поверх
            // заблокированного ввода, а гасит окно.
            if (TryDismissServerModal())
            {
                return;
            }

            ToggleMenu();
        }

        private bool TryDismissServerModal()
        {
            if (_windows == null || !_windows.IsModalShowing)
            {
                return false;
            }

            if (_uiInput.IsChatFocused)
            {
                return false;
            }

            _windows.DismissModal();
            _uiInput.ConsumeEscape();
            return true;
        }

        private void TryInitialize()
        {
            if (_initialized || _initializationFailed)
            {
                return;
            }

            if (_doc == null || _doc.rootVisualElement == null || _doc.panelSettings == null)
            {
                // К Start панель гарантирована (создаётся в OnEnable документа);
                // null здесь — дефект проводки, а не гонка. Молчаливый пропуск
                // оставил бы меню паузы вечно мёртвым без ошибки.
                throw new InvalidOperationException(
                    "[PauseMenu] Required UIDocument injection is missing or has no root/PanelSettings; " +
                    "PauseMenu must be registered in the Game scope before Start.");
            }

            string? missing =
                _clientConfig == null ? nameof(IClientConfigManager) :
                _clientConfig.Config == null ? "ClientConfig" :
                _networkService == null ? nameof(INetworkService) :
                _audioSystem == null ? nameof(IAudioSystem) :
                _connectionService == null ? nameof(IConnectionService) :
                _inputBlocker == null ? nameof(IInputBlocker) :
                _lightingEngine == null ? nameof(LightingEngine) :
                _postProcessController == null ? nameof(PostProcessController) :
                _terrainRenderer == null ? nameof(TerrainRenderer) :
                _graphicsSettings == null ? nameof(GraphicsSettingsController) :
                _displayManager == null ? nameof(DisplayManager) :
                _loc == null ? nameof(ILocalizationService) :
                null;
            if (missing != null)
            {
                // ClientConfig is loaded by ApplicationBootstrap before any
                // content scene; a null Config at MainGame Start is a defect.
                throw new InvalidOperationException(
                    $"[PauseMenu] Required injection '{missing}' is missing. " +
                    "PauseMenu must be registered in the Game scope before Start.");
            }

            // The throw above guards these required injections; the compiler
            // cannot narrow fields through the string? 'missing' pattern.
            if (!_lightingEngine!.IsInitialized)
            {
                // Единственный детерминированный переход: событие готовности
                // освещения (EnsureInitialized в PostStart), без ретраев из Update.
                _lightingEngine!.OnInitialized += OnLightingReady;
                return;
            }

            CompleteInitialize();
        }

        private void OnLightingReady()
        {
            _lightingEngine.OnInitialized -= OnLightingReady;
            CompleteInitialize();
        }

        private void CompleteInitialize()
        {
            if (_initialized || _initializationFailed)
            {
                return;
            }

            try
            {
                CreateMenu(_doc.rootVisualElement);
            }
            catch (InvalidOperationException exception)
            {
                Debug.LogWarning($"[PauseMenu] Menu unavailable: {exception.Message}");
                _initializationFailed = true;
                return;
            }

            HideMenu();

            UIScreen.Bind(_doc, _clientConfig.Config.Interface.UIScale, _clientConfig.Config.Interface.TextScale);

            // Реестр применяет текст сразу и на каждой смене языка — подписка
            // вручную не нужна и запрещена линтером.
            _loc.RegisterLocalizable(this);

            _touchCommands.PauseRequested += OnTouchPauseRequested;
            _touchSubscribed = true;

            _initialized = true;
        }

        private void OnDestroy()
        {
            if (_doc != null)
            {
                UIScreen.Unbind(_doc);
            }

            if (_loc != null)
            {
                _loc.UnregisterLocalizable(this);
            }

            if (_lightingEngine != null)
            {
                _lightingEngine.OnInitialized -= OnLightingReady;
            }

            if (_touchSubscribed)
            {
                _touchCommands.PauseRequested -= OnTouchPauseRequested;
                _touchSubscribed = false;
            }

            _uiInput.IsPauseMenuOpen = false;

            if (_menuTree != null && _menuTree.parent != null)
            {
                _menuTree.parent.Remove(_menuTree);
            }
        }

        private void CreateMenu(VisualElement root)
        {
            VisualTreeAsset template = Resources.Load<VisualTreeAsset>(ResourcePaths.PauseMenuUxml) ??
                throw new InvalidOperationException(
                    "[PauseMenu] Resources/UI/Settings/Pause/Templates/PauseMenu.uxml is required.");
            PauseMenuViewState view = new PauseMenuViewBuilder(
                _doc,
                _clientConfig,
                _audioSystem,
                _displayManager,
                _graphicsSettings,
                _lightingEngine,
                _postProcessController,
                _networkService,
                _connectionService,
                _localPlayer,
                _uiInput,
                _mainMenuNavigation,
                _loc)
                .Build(template, root, CloseMenu, OpenSettings, CloseSettings, _settingsRefreshers);

            _menuTree = view.MenuTree;
            _menuPanel = view.MenuPanel;
            _mainPage = view.MainPage;
            _settingsPage = view.SettingsPage;
            _tabRouter = view.TabRouter;
        }

        private void ToggleMenu()
        {
            if (!enabled)
            {
                return;
            }

            if (_uiInput.IsProgrammatorOpen)
            {
                return;
            }

            if (_inputBlocker != null && _inputBlocker.IsInputBlocked && !_isOpen)
            {
                var topTag = _inputBlocker.TopWindowTag;
                if (topTag != null)
                {
                    // v2-окно: NotClosable молча гасит ESC, Closable шлёт
                    // серверу WindowClosed. Легаси — старый путь action-строкой.
                    if (_windows.TryRequestCloseTopWindow())
                    {
                        return;
                    }

                    _networkService.Send(new ElementClickPacket(topTag, 0, []));
                    return;
                }
            }

            if (_settingsPage != null && _settingsPage.style.display == DisplayStyle.Flex)
            {
                CloseSettings();
                return;
            }

            if (_isOpen)
            {
                CloseMenu();
            }
            else
            {
                OpenMenu();
            }
        }

        private void OpenMenu()
        {
            _isOpen = true;
            _uiInput.IsPauseMenuOpen = true;
            if (_doc?.rootVisualElement != null && _menuTree != null && !_doc.rootVisualElement.Contains(_menuTree))
            {
                _doc.rootVisualElement.Add(_menuTree);
            }

            if (_menuTree != null)
            {
                _menuTree.BringToFront();
                _menuTree.style.display = DisplayStyle.Flex;
            }

            if (_menuPanel != null)
            {
                _menuPanel.BringToFront();
                _menuPanel.style.display = DisplayStyle.Flex;
            }

            if (_mainPage != null)
            {
                _mainPage.style.display = DisplayStyle.Flex;
            }

            if (_settingsPage != null)
            {
                _settingsPage.style.display = DisplayStyle.None;
            }
        }

        private void CloseMenu()
        {
            HideMenu();
        }

        private void HideMenu()
        {
            _isOpen = false;
            _uiInput.IsPauseMenuOpen = false;
            if (_menuPanel != null)
            {
                _menuPanel.style.display = DisplayStyle.None;
            }

            if (_menuTree != null)
            {
                _menuTree.style.display = DisplayStyle.None;
            }
        }

        private void OpenSettings()
        {
            foreach (Action refresh in _settingsRefreshers)
            {
                refresh();
            }

            if (_mainPage != null)
            {
                _mainPage.style.display = DisplayStyle.None;
            }

            if (_settingsPage != null)
            {
                _settingsPage.style.display = DisplayStyle.Flex;
            }
        }

        public void ApplyLocalizedText()
        {
            UILocalizer.AssertLocalizationServiceAvailable(_loc, nameof(PauseMenu));
            if (_menuTree == null || _loc == null)
            {
                return;
            }

            bool wasOpen = _isOpen;
            bool wasSettings = _settingsPage != null && _settingsPage.style.display == DisplayStyle.Flex;
            int activeTab = _tabRouter?.ActiveTab ?? 0;

            CreateMenu(_doc.rootVisualElement);

            if (wasOpen && _menuTree != null)
            {
                _menuTree.style.display = DisplayStyle.Flex;
                _menuTree.BringToFront();
                if (_menuPanel != null)
                {
                    _menuPanel.style.display = DisplayStyle.Flex;
                }

                if (wasSettings)
                {
                    OpenSettings();
                    _tabRouter?.ShowTab(activeTab);
                }
                else if (_mainPage != null)
                {
                    _mainPage.style.display = DisplayStyle.Flex;
                }
            }

            if (_menuTree != null)
            {
                UILocalizer.AssertLocalized(_menuTree, _loc);
            }
        }

        private void CloseSettings()
        {
            if (_settingsPage != null)
            {
                _settingsPage.style.display = DisplayStyle.None;
            }

            if (_mainPage != null)
            {
                _mainPage.style.display = DisplayStyle.Flex;
            }
        }


    }
}

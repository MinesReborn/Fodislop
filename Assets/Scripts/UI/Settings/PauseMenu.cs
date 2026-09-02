#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Localization;
using Fodinae.Networking;
using Fodinae.Networking.Connection;
using Fodinae.Player;
using Fodinae.Player.Logic;
using Fodinae.Rendering;
using Fodinae.Rendering.PostProcessing;
using Fodinae.UI;
using Fodinae.UI.Programmator;
using Fodinae.World.Lighting;
using Fodinae.World.Terrain;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Shared.Packets;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae.UI
{
    public class PauseMenu : MonoBehaviour, ILocalizableUI
    {
        [Inject]
        private UIDocument _doc = null!;
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
        private float _originalScale;
        private bool _originalScaleCaptured;
        private readonly List<Action> _settingsRefreshers = [];
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

        private PauseMenuSettingsBuilder? _settingsBuilder;
        private VisualElement[] _settingsPages = System.Array.Empty<VisualElement>();
        private Button[] _settingsTabs = System.Array.Empty<Button>();
        private int _activeSettingsTab;

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
                ToggleMenu();
            }
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

            _originalScale = _doc.panelSettings.scale;
            _originalScaleCaptured = true;

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

            var savedScale = _clientConfig.Config.Interface.UIScale;
            if (Mathf.Abs(_doc.panelSettings.scale - savedScale) > 0.0001f)
            {
                _doc.panelSettings.scale = savedScale;
            }

            // Реестр применяет текст сразу и на каждой смене языка — подписка
            // вручную не нужна и запрещена линтером.
            _loc.RegisterLocalizable(this);

            _initialized = true;
        }

        private void OnDestroy()
        {
            if (_loc != null)
            {
                _loc.UnregisterLocalizable(this);
            }

            if (_lightingEngine != null)
            {
                _lightingEngine.OnInitialized -= OnLightingReady;
            }

            _uiInput.IsPauseMenuOpen = false;

            if (_menuTree != null && _menuTree.parent != null)
            {
                _menuTree.parent.Remove(_menuTree);
            }

            if (_originalScaleCaptured && _doc != null && _doc.panelSettings != null)
            {
                if (Mathf.Abs(_doc.panelSettings.scale - _originalScale) > 0.0001f)
                {
                    _doc.panelSettings.scale = _originalScale;
                }
            }
        }

        private void CreateMenu(VisualElement root)
        {
            VisualElement? existingMenu = root.Q<VisualElement>("PauseOverlay");
            if (existingMenu != null)
            {
                VisualElement existingTree = existingMenu;
                while (existingTree.parent != null && existingTree.parent != root)
                {
                    existingTree = existingTree.parent;
                }

                if (existingTree.parent == root)
                {
                    root.Remove(existingTree);
                }
            }

            VisualTreeAsset menuTemplate = Resources.Load<VisualTreeAsset>(
                ProjectRuntimeContracts.ResourcePaths.PauseMenuUxml) ??
                throw new InvalidOperationException(
                    "[PauseMenu] Resources/UI/PauseMenu.uxml is required.");
            TemplateContainer menuTree = menuTemplate.Instantiate();
            _menuTree = menuTree;
            menuTree.AddToClassList("ui-fullscreen");
            menuTree.pickingMode = PickingMode.Ignore;
            menuTree.style.display = DisplayStyle.None;

            // Статические ключи UXML (settings.*, pause.*) резолвятся сразу при
            // сборке, а не только по событию смены языка.
            UILocalizer.Apply(menuTree, _loc);
            _menuPanel = menuTree.Q<VisualElement>("PauseOverlay") ??
                throw new InvalidOperationException("[PauseMenu] PauseOverlay is missing from PauseMenu.uxml.");
            _mainPage = menuTree.Q<VisualElement>("MainPage") ??
                throw new InvalidOperationException("[PauseMenu] MainPage is missing from PauseMenu.uxml.");
            // Static main-page buttons live in PauseMenu.uxml; the scroll container
            // itself is validated for the UXML contract even though it is not
            // modified from code anymore.
            _ = menuTree.Q<ScrollView>("MainPageScroll") ??
                throw new InvalidOperationException("[PauseMenu] MainPageScroll is missing from PauseMenu.uxml.");
            Button resumeButton = menuTree.Q<Button>("ResumeButton") ??
                throw new InvalidOperationException("[PauseMenu] ResumeButton is missing from PauseMenu.uxml.");
            resumeButton.clicked += CloseMenu;
            resumeButton.text = _loc.Get("pause.resume");
            Button settingsButton = menuTree.Q<Button>("SettingsButton") ??
                throw new InvalidOperationException("[PauseMenu] SettingsButton is missing from PauseMenu.uxml.");
            settingsButton.clicked += OpenSettings;
            settingsButton.text = _loc.Get("pause.settings");
            Button mainMenuButton = menuTree.Q<Button>("MainMenuButton") ??
                throw new InvalidOperationException("[PauseMenu] MainMenuButton is missing from PauseMenu.uxml.");
            mainMenuButton.clicked += ExitToMainMenu;
            mainMenuButton.text = _loc.Get("pause.quit");
            Button quitButton = menuTree.Q<Button>("QuitButton") ??
                throw new InvalidOperationException("[PauseMenu] QuitButton is missing from PauseMenu.uxml.");
            quitButton.clicked += QuitGame;
            quitButton.text = _loc.Get("pause.quit_game");
            Label pauseTitle = menuTree.Q<Label>("PauseTitle") ??
                throw new InvalidOperationException("[PauseMenu] PauseTitle is missing from PauseMenu.uxml.");
            pauseTitle.text = _loc.Get("pause.title");
            Label settingsTitle = menuTree.Q<Label>("SettingsTitle") ??
                throw new InvalidOperationException("[PauseMenu] SettingsTitle is missing from PauseMenu.uxml.");
            settingsTitle.text = _loc.Get("pause.settings");
            _settingsPage = menuTree.Q<VisualElement>("SettingsPage") ??
                throw new InvalidOperationException("[PauseMenu] SettingsPage is missing from PauseMenu.uxml.");
            ScrollView graphicsScroll = menuTree.Q<ScrollView>("GraphicsScroll") ??
                throw new InvalidOperationException("[PauseMenu] GraphicsScroll is missing from PauseMenu.uxml.");
            ScrollView displayScroll = menuTree.Q<ScrollView>("DisplayScroll") ??
                throw new InvalidOperationException("[PauseMenu] DisplayScroll is missing from PauseMenu.uxml.");
            ScrollView effectsScroll = menuTree.Q<ScrollView>("EffectsScroll") ??
                throw new InvalidOperationException("[PauseMenu] EffectsScroll is missing from PauseMenu.uxml.");
            ScrollView audioScroll = menuTree.Q<ScrollView>("AudioScroll") ??
                throw new InvalidOperationException("[PauseMenu] AudioScroll is missing from PauseMenu.uxml.");
            ScrollView interfaceScroll = menuTree.Q<ScrollView>("InterfaceScroll") ??
                throw new InvalidOperationException("[PauseMenu] InterfaceScroll is missing from PauseMenu.uxml.");
            ScrollView advancedScroll = menuTree.Q<ScrollView>("AdvancedScroll") ??
                throw new InvalidOperationException("[PauseMenu] AdvancedScroll is missing from PauseMenu.uxml.");
            Button settingsBack = menuTree.Q<Button>("SettingsBack") ??
                throw new InvalidOperationException("[PauseMenu] SettingsBack is missing from PauseMenu.uxml.");
            settingsBack.clicked += CloseSettings;
            settingsBack.text = _loc.Get("common.back");

            Button graphicsTab = menuTree.Q<Button>("GraphicsTab") ??
                throw new InvalidOperationException("[PauseMenu] GraphicsTab is missing from PauseMenu.uxml.");
            graphicsTab.text = _loc.Get("menu.settings.graphics");
            Button displayTab = menuTree.Q<Button>("DisplayTab") ??
                throw new InvalidOperationException("[PauseMenu] DisplayTab is missing from PauseMenu.uxml.");
            displayTab.text = _loc.Get("menu.settings.display");
            Button effectsTab = menuTree.Q<Button>("EffectsTab") ??
                throw new InvalidOperationException("[PauseMenu] EffectsTab is missing from PauseMenu.uxml.");
            effectsTab.text = _loc.Get("pause.tab.effects");
            Button audioTab = menuTree.Q<Button>("AudioTab") ??
                throw new InvalidOperationException("[PauseMenu] AudioTab is missing from PauseMenu.uxml.");
            audioTab.text = _loc.Get("menu.settings.audio");
            Button interfaceTab = menuTree.Q<Button>("InterfaceTab") ??
                throw new InvalidOperationException("[PauseMenu] InterfaceTab is missing from PauseMenu.uxml.");
            interfaceTab.text = _loc.Get("pause.tab.interface");
            Button advancedTab = menuTree.Q<Button>("AdvancedTab") ??
                throw new InvalidOperationException("[PauseMenu] AdvancedTab is missing from PauseMenu.uxml.");
            advancedTab.text = _loc.Get("pause.tab.advanced");

            VisualElement[] settingsPages =
            [
                graphicsScroll,
                displayScroll,
                effectsScroll,
                audioScroll,
                interfaceScroll,
                advancedScroll,
            ];
            Button[] settingsTabs =
            [
                graphicsTab,
                displayTab,
                effectsTab,
                audioTab,
                interfaceTab,
                advancedTab,
            ];
            _settingsPages = settingsPages;
            _settingsTabs = settingsTabs;
            void ShowSettingsPage(int index)
            {
                _activeSettingsTab = index;
                for (int i = 0; i < settingsPages.Length; i++)
                {
                    settingsPages[i].style.display = i == index
                        ? DisplayStyle.Flex
                        : DisplayStyle.None;
                    settingsTabs[i].EnableInClassList("settings-tab--active", i == index);
                }
            }

            graphicsTab.clicked += () => ShowSettingsPage(0);
            displayTab.clicked += () => ShowSettingsPage(1);
            effectsTab.clicked += () => ShowSettingsPage(2);
            audioTab.clicked += () => ShowSettingsPage(3);
            interfaceTab.clicked += () => ShowSettingsPage(4);
            advancedTab.clicked += () => ShowSettingsPage(5);
            root.Add(menuTree);

            _settingsRefreshers.Clear();
            _settingsBuilder = new PauseMenuSettingsBuilder(
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
                _settingsRefreshers,
                CloseMenu,
                _loc);

#if UNITY_EDITOR || UNITY_ENABLE_CHECKS
            // Built first: BuildAdvancedPage appends the lighting debug view
            // and the diagnostics readout to this section.
            VisualElement debugSection = _settingsBuilder.BuildDebugSection();
#endif

            _settingsBuilder.BuildAudioPage(audioScroll);
            _settingsBuilder.BuildDisplayPage(displayScroll);
            _settingsBuilder.BuildGraphicsPage(graphicsScroll);
            _settingsBuilder.BuildEffectsPage(effectsScroll);
            _settingsBuilder.BuildInterfacePage(interfaceScroll);
            _settingsBuilder.BuildAdvancedPage(advancedScroll);

#if UNITY_EDITOR || UNITY_ENABLE_CHECKS
            advancedScroll.contentContainer.Add(debugSection);
#endif

            // Apply the initial page after all dynamic content has been attached.
            // ScrollView owns its content container; adding sections directly to it
            // can leave the viewport empty after a domain reload.
            ShowSettingsPage(0);

            _settingsPage.style.display = DisplayStyle.None;
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
                    _networkService.Send(new ElementClickPacket(topTag, 0, System.Array.Empty<StringPairPacket>()));
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

        /// <summary>
        /// Переприменяет локализованный текст после смены языка. Меню паузы
        /// строится один раз (CreateMenu идемпотентен), поэтому при смене языка
        /// пересобираем всё дерево и восстанавливаем состояние: было ли меню
        /// открыто и какая страница настроек была активна.
        /// </summary>
        public void ApplyLocalizedText()
        {
            UILocalizer.AssertLocalizationServiceAvailable(_loc, nameof(PauseMenu));
            if (_menuTree == null || _loc == null)
            {
                return;
            }

            bool wasOpen = _isOpen;
            bool wasSettings = _settingsPage != null && _settingsPage.style.display == DisplayStyle.Flex;
            int activeTab = _activeSettingsTab;

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
                    for (int i = 0; i < _settingsPages.Length; i++)
                    {
                        _settingsPages[i].style.display = i == activeTab
                            ? DisplayStyle.Flex
                            : DisplayStyle.None;
                        _settingsTabs[i].EnableInClassList("settings-tab--active", i == activeTab);
                    }
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

        private void QuitGame()
        {
            PauseMenuUIFactory.ShowConfirmation(
                _doc,
                _loc.Get("pause.quit_confirm_title"),
                _loc.Get("pause.quit_confirm_msg"),
                _loc.Get("pause.quit_confirm_btn"),
                () =>
                {
#if UNITY_EDITOR
                    Debug.Log("[PauseMenu] Выход из игры");
#else
                    Application.Quit();
#endif
                },
                _loc);
        }

        private void ExitToMainMenu()
        {
            PauseMenuUIFactory.ShowConfirmation(
                _doc,
                _loc.Get("pause.quit"),
                _loc.Get("pause.exit_menu_confirm_msg"),
                _loc.Get("pause.exit_menu_btn"),
                () =>
                {
                    CloseMenu();
                    _mainMenuNavigation.ReturnToMainMenu();
                },
                _loc);
        }
    }
}

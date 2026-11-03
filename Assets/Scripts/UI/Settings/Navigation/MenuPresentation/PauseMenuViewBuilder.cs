#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Player;
using Kern.Rendering;
using Kern.Rendering.Display;
using Kern.Rendering.PostProcessing;
using Kern.UI;
using Kern.UI.Input;
using Kern.UI.Localization;
using Kern.UI.Screens;
using Kern.World.Lighting;
using Kern.World.Terrain;
using MinesServer.Networking.Client.Packets.GUI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Settings;

internal sealed class PauseMenuViewBuilder
{
    private readonly UIDocument _document;
    private readonly IClientConfigManager _clientConfig;
    private readonly IAudioSystem _audioSystem;
    private readonly DisplayManager _displayManager;
    private readonly GraphicsSettingsController _graphicsSettings;
    private readonly LightingEngine _lightingEngine;
    private readonly PostProcessController _postProcessController;
    private readonly INetworkService _networkService;
    private readonly IConnectionService _connectionService;
    private readonly ILocalPlayerState _localPlayer;
    private readonly UIInputManager _uiInput;
    private readonly IMainMenuNavigation _mainMenuNavigation;
    private readonly ILocalizationService _localization;

    public PauseMenuViewBuilder(
        UIDocument document,
        IClientConfigManager clientConfig,
        IAudioSystem audioSystem,
        DisplayManager displayManager,
        GraphicsSettingsController graphicsSettings,
        LightingEngine lightingEngine,
        PostProcessController postProcessController,
        INetworkService networkService,
        IConnectionService connectionService,
        ILocalPlayerState localPlayer,
        UIInputManager uiInput,
        IMainMenuNavigation mainMenuNavigation,
        ILocalizationService localization)
    {
        _document = document;
        _clientConfig = clientConfig;
        _audioSystem = audioSystem;
        _displayManager = displayManager;
        _graphicsSettings = graphicsSettings;
        _lightingEngine = lightingEngine;
        _postProcessController = postProcessController;
        _networkService = networkService;
        _connectionService = connectionService;
        _localPlayer = localPlayer;
        _uiInput = uiInput;
        _mainMenuNavigation = mainMenuNavigation;
        _localization = localization;
    }

    public PauseMenuViewState Build(
        VisualTreeAsset template,
        VisualElement root,
        Action closeMenu,
        Action openSettings,
        Action closeSettings,
        List<Action> settingsRefreshers)
    {
        RemoveExistingMenu(root);
        TemplateContainer tree = template.Instantiate();
        tree.AddToClassList("ui-fullscreen");
        tree.pickingMode = PickingMode.Ignore;
        tree.style.display = DisplayStyle.None;
        UILocalizer.Apply(tree, _localization);

        VisualElement menuPanel = tree.Q<VisualElement>("PauseOverlay") ??
            throw new InvalidOperationException("[PauseMenu] PauseOverlay is missing from PauseMenu.uxml.");
        VisualElement mainPage = tree.Q<VisualElement>("MainPage") ??
            throw new InvalidOperationException("[PauseMenu] MainPage is missing from PauseMenu.uxml.");
        VisualElement settingsPage = tree.Q<VisualElement>("SettingsPage") ??
            throw new InvalidOperationException("[PauseMenu] SettingsPage is missing from PauseMenu.uxml.");

        PauseMenuMainPage.Bind(
            tree,
            _localization,
            closeMenu,
            openSettings,
            () => PauseMenuConfirmation.ConfirmExitToMainMenu(
                _document, _mainMenuNavigation, closeMenu, _localization),
            () => PauseMenuConfirmation.ConfirmQuitGame(_document, _localization));

        var tabRouter = new PauseMenuTabRouter(tree, _localization, closeSettings);
        root.Add(tree);
        settingsRefreshers.Clear();
        var settingsBuilder = new PauseMenuSettingsBuilder(
            _document,
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
            settingsRefreshers,
            closeMenu,
            _localization);

#if UNITY_EDITOR || UNITY_ENABLE_CHECKS
        VisualElement debugSection = settingsBuilder.BuildDebugSection();
#endif

        settingsBuilder.BuildAudioPage(tabRouter.AudioScroll);
        settingsBuilder.BuildDisplayPage(tabRouter.DisplayScroll);
        settingsBuilder.BuildGraphicsPage(tabRouter.GraphicsScroll);
        settingsBuilder.BuildEffectsPage(tabRouter.EffectsScroll);
        settingsBuilder.BuildInterfacePage(tabRouter.InterfaceScroll);
        settingsBuilder.BuildControlsPage(tabRouter.ControlsScroll);
        settingsBuilder.BuildAdvancedPage(tabRouter.AdvancedScroll);
#if UNITY_EDITOR || UNITY_ENABLE_CHECKS
        tabRouter.AdvancedScroll.contentContainer.Add(debugSection);
#endif
        tabRouter.ShowTab(0);
        settingsPage.style.display = DisplayStyle.None;
        return new PauseMenuViewState(tree, menuPanel, mainPage, settingsPage, tabRouter);
    }

    private static void RemoveExistingMenu(VisualElement root)
    {
        VisualElement? existingMenu = root.Q<VisualElement>("PauseOverlay");
        if (existingMenu == null)
        {
            return;
        }

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
}

internal readonly record struct PauseMenuViewState(
    TemplateContainer MenuTree,
    VisualElement MenuPanel,
    VisualElement MainPage,
    VisualElement SettingsPage,
    PauseMenuTabRouter TabRouter);

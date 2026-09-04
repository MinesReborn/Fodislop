#nullable enable

using System;
using Fodinae.Core.Localization;
using UnityEngine.UIElements;

namespace Fodinae.UI;

/// <summary>
/// Manages switching between settings tabs and their corresponding scroll views in PauseMenu.
/// </summary>
internal sealed class PauseMenuTabRouter
{
    private readonly ScrollView[] _pages;
    private readonly Button[] _tabs;
    private int _activeTab;

    public PauseMenuTabRouter(TemplateContainer menuTree, ILocalizationService loc, Action onCloseSettings)
    {
        var graphicsScroll = menuTree.Q<ScrollView>("GraphicsScroll") ??
            throw new InvalidOperationException("[PauseMenu] GraphicsScroll is missing from PauseMenu.uxml.");
        var displayScroll = menuTree.Q<ScrollView>("DisplayScroll") ??
            throw new InvalidOperationException("[PauseMenu] DisplayScroll is missing from PauseMenu.uxml.");
        var effectsScroll = menuTree.Q<ScrollView>("EffectsScroll") ??
            throw new InvalidOperationException("[PauseMenu] EffectsScroll is missing from PauseMenu.uxml.");
        var audioScroll = menuTree.Q<ScrollView>("AudioScroll") ??
            throw new InvalidOperationException("[PauseMenu] AudioScroll is missing from PauseMenu.uxml.");
        var interfaceScroll = menuTree.Q<ScrollView>("InterfaceScroll") ??
            throw new InvalidOperationException("[PauseMenu] InterfaceScroll is missing from PauseMenu.uxml.");
        var advancedScroll = menuTree.Q<ScrollView>("AdvancedScroll") ??
            throw new InvalidOperationException("[PauseMenu] AdvancedScroll is missing from PauseMenu.uxml.");

        var settingsBack = menuTree.Q<Button>("SettingsBack") ??
            throw new InvalidOperationException("[PauseMenu] SettingsBack is missing from PauseMenu.uxml.");
        settingsBack.clicked += onCloseSettings;
        settingsBack.text = loc.Get("common.back");

        var graphicsTab = menuTree.Q<Button>("GraphicsTab") ??
            throw new InvalidOperationException("[PauseMenu] GraphicsTab is missing from PauseMenu.uxml.");
        graphicsTab.text = loc.Get("menu.settings.graphics");

        var displayTab = menuTree.Q<Button>("DisplayTab") ??
            throw new InvalidOperationException("[PauseMenu] DisplayTab is missing from PauseMenu.uxml.");
        displayTab.text = loc.Get("menu.settings.display");

        var effectsTab = menuTree.Q<Button>("EffectsTab") ??
            throw new InvalidOperationException("[PauseMenu] EffectsTab is missing from PauseMenu.uxml.");
        effectsTab.text = loc.Get("pause.tab.effects");

        var audioTab = menuTree.Q<Button>("AudioTab") ??
            throw new InvalidOperationException("[PauseMenu] AudioTab is missing from PauseMenu.uxml.");
        audioTab.text = loc.Get("menu.settings.audio");

        var interfaceTab = menuTree.Q<Button>("InterfaceTab") ??
            throw new InvalidOperationException("[PauseMenu] InterfaceTab is missing from PauseMenu.uxml.");
        interfaceTab.text = loc.Get("pause.tab.interface");

        var advancedTab = menuTree.Q<Button>("AdvancedTab") ??
            throw new InvalidOperationException("[PauseMenu] AdvancedTab is missing from PauseMenu.uxml.");
        advancedTab.text = loc.Get("pause.tab.advanced");

        _pages =
        [
            graphicsScroll,
            displayScroll,
            effectsScroll,
            audioScroll,
            interfaceScroll,
            advancedScroll,
        ];
        _tabs =
        [
            graphicsTab,
            displayTab,
            effectsTab,
            audioTab,
            interfaceTab,
            advancedTab,
        ];

        graphicsTab.clicked += () => ShowTab(0);
        displayTab.clicked += () => ShowTab(1);
        effectsTab.clicked += () => ShowTab(2);
        audioTab.clicked += () => ShowTab(3);
        interfaceTab.clicked += () => ShowTab(4);
        advancedTab.clicked += () => ShowTab(5);
    }

    public ScrollView GraphicsScroll => _pages[0];

    public ScrollView DisplayScroll => _pages[1];

    public ScrollView EffectsScroll => _pages[2];

    public ScrollView AudioScroll => _pages[3];

    public ScrollView InterfaceScroll => _pages[4];

    public ScrollView AdvancedScroll => _pages[5];

    public int ActiveTab => _activeTab;

    public void ShowTab(int index)
    {
        _activeTab = index;
        for (int i = 0; i < _pages.Length; i++)
        {
            _pages[i].style.display = i == index
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            _tabs[i].EnableInClassList("settings-tab--active", i == index);
        }
    }
}

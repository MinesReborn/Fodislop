#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.UI.Localization;
using UnityEngine.UIElements;

namespace Kern.UI.Settings;

// Пауза — макет visual/kern-ui (#menuDialog): «Продолжить», «Настройки», «Выход в меню».
// Настройки — отдельное окно (SettingsWindow), общее с главным меню.
internal sealed class PauseMenuViewBuilder
{
    private readonly IMainMenuNavigation _mainMenuNavigation;
    private readonly ILocalizationService _localization;

    public PauseMenuViewBuilder(IMainMenuNavigation mainMenuNavigation, ILocalizationService localization)
    {
        _mainMenuNavigation = mainMenuNavigation;
        _localization = localization;
    }

    public PauseMenuViewState Build(VisualTreeAsset template, VisualElement root, Action closeMenu, Action openSettings)
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

        PauseMenuMainPage.Bind(
            tree,
            _localization,
            closeMenu,
            openSettings,
            // Выход в меню — сразу, без подтверждения (макет: #menuExit)
            () =>
            {
                closeMenu();
                _mainMenuNavigation.ReturnToMainMenu();
            });

        root.Add(tree);
        return new PauseMenuViewState(tree, menuPanel, mainPage);
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
    VisualElement MainPage);

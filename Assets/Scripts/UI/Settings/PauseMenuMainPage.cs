#nullable enable

using System;
using Fodinae.Core.Localization;
using UnityEngine.UIElements;

namespace Fodinae.UI;

/// <summary>
/// Handles element lookup, localization, and click handler binding for the main page of PauseMenu.
/// </summary>
internal static class PauseMenuMainPage
{
    public static void Bind(
        TemplateContainer menuTree,
        ILocalizationService loc,
        Action onResume,
        Action onOpenSettings,
        Action onExitToMainMenu,
        Action onQuitGame)
    {
        _ = menuTree.Q<ScrollView>("MainPageScroll") ??
            throw new InvalidOperationException("[PauseMenu] MainPageScroll is missing from PauseMenu.uxml.");

        var resumeButton = menuTree.Q<Button>("ResumeButton") ??
            throw new InvalidOperationException("[PauseMenu] ResumeButton is missing from PauseMenu.uxml.");
        resumeButton.clicked += onResume;
        resumeButton.text = loc.Get("pause.resume");

        var settingsButton = menuTree.Q<Button>("SettingsButton") ??
            throw new InvalidOperationException("[PauseMenu] SettingsButton is missing from PauseMenu.uxml.");
        settingsButton.clicked += onOpenSettings;
        settingsButton.text = loc.Get("pause.settings");

        var mainMenuButton = menuTree.Q<Button>("MainMenuButton") ??
            throw new InvalidOperationException("[PauseMenu] MainMenuButton is missing from PauseMenu.uxml.");
        mainMenuButton.clicked += onExitToMainMenu;
        mainMenuButton.text = loc.Get("pause.quit");

        var quitButton = menuTree.Q<Button>("QuitButton") ??
            throw new InvalidOperationException("[PauseMenu] QuitButton is missing from PauseMenu.uxml.");
        quitButton.clicked += onQuitGame;
        quitButton.text = loc.Get("pause.quit_game");

        var pauseTitle = menuTree.Q<Label>("PauseTitle") ??
            throw new InvalidOperationException("[PauseMenu] PauseTitle is missing from PauseMenu.uxml.");
        pauseTitle.text = loc.Get("pause.title");

        var settingsTitle = menuTree.Q<Label>("SettingsTitle") ??
            throw new InvalidOperationException("[PauseMenu] SettingsTitle is missing from PauseMenu.uxml.");
        settingsTitle.text = loc.Get("pause.settings");
    }
}

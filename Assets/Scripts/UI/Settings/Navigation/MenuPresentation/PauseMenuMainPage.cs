#nullable enable

using System;
using Kern.Core.Localization;
using UnityEngine.UIElements;

namespace Kern.UI.Settings;

internal static class PauseMenuMainPage
{
    public static void Bind(
        TemplateContainer menuTree,
        ILocalizationService loc,
        Action onResume,
        Action onOpenSettings,
        Action onExitToMainMenu)
    {
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

        var pauseTitle = menuTree.Q<Label>("PauseTitle") ??
            throw new InvalidOperationException("[PauseMenu] PauseTitle is missing from PauseMenu.uxml.");
        pauseTitle.text = loc.Get("pause.title").ToUpperInvariant();
    }
}

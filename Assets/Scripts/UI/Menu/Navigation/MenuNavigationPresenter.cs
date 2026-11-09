#nullable enable

using System;
using Kern.Core.Localization;
using Kern.UI.Screens;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Menu;

public sealed class MenuNavigationPresenter
{
    private Button? _playButton;
    private Button? _cancelDescentButton;
    private Button? _retryDescentButton;

    private Button? _sideSettingsButton;
    private Button? _sideDiscordButton;
    private Button? _sideTelegramButton;
    private Button? _sideVKButton;
    private Button? _sideExitButton;

    private Button? _footerVersionButton;

    public void Bind(
        VisualElement tree,
        MenuModalManager modalManager,
        Action onPlayClicked,
        Action onCancelDescent,
        Action onRetryDescent,
        ILocalizationService? loc)
    {
        _playButton = tree.Q<Button>("PlayButton");
        _cancelDescentButton = tree.Q<Button>("CancelDescentButton");
        _retryDescentButton = tree.Q<Button>("RetryDescentButton");

        _sideSettingsButton = tree.Q<Button>("SideSettingsButton");
        _sideDiscordButton = tree.Q<Button>("SideDiscordButton");
        _sideTelegramButton = tree.Q<Button>("SideTelegramButton");
        _sideVKButton = tree.Q<Button>("SideVkButton");
        _sideExitButton = tree.Q<Button>("SideExitButton");

        _footerVersionButton = tree.Q<Button>("FooterVersionButton");
        ApplyBuildStamp(tree);

        if (_playButton != null)
        {
            _playButton.clicked += onPlayClicked;
        }

        if (_cancelDescentButton != null)
        {
            _cancelDescentButton.clicked += onCancelDescent;
        }

        if (_retryDescentButton != null)
        {
            _retryDescentButton.clicked += onRetryDescent;
        }

        if (_sideSettingsButton != null)
        {
            _sideSettingsButton.clicked += modalManager.OpenSettings;
        }

        if (_sideDiscordButton != null)
        {
            _sideDiscordButton.clicked += OpenDiscord;
        }

        if (_sideTelegramButton != null)
        {
            _sideTelegramButton.clicked += OpenTelegram;
        }

        if (_sideVKButton != null)
        {
            _sideVKButton.clicked += OpenVK;
        }

        if (_sideExitButton != null)
        {
            _sideExitButton.clicked += QuitGame;
        }

        // Строка «Новое» в макете ничего не открывает: окна хроники в нём нет

        ApplyLocalization(loc);
    }

    public void ApplyLocalization(ILocalizationService? loc)
    {
        if (loc == null)
        {
            return;
        }

        if (_cancelDescentButton != null)
        {
            _cancelDescentButton.text = loc.Get("menu.cancel_descent");
        }

        if (_retryDescentButton != null)
        {
            _retryDescentButton.text = loc.Get("menu.retry_descent");
        }

        if (_sideSettingsButton != null)
        {
            _sideSettingsButton.tooltip = loc.Get("menu.settings");
        }

        if (_sideExitButton != null)
        {
            _sideExitButton.tooltip = loc.Get("menu.exit");
        }

        ApplyVersionLabel(loc);
    }

    public void ApplyVersionLabel(ILocalizationService? loc)
    {
        if (_footerVersionButton == null || loc == null)
        {
            return;
        }

        _footerVersionButton.text = Application.isEditor
            ? loc.Get("mainmenu.version_editor", Application.version)
            : Debug.isDebugBuild
                ? loc.Get("mainmenu.version_dev", Application.version)
                : loc.Get("mainmenu.version", Application.version);
    }

    // «Сборка» подвала: первые 8 знаков GUID сборки и его штрихкод. В редакторе GUID нулевой — сборки нет, пункт скрыт
    private static void ApplyBuildStamp(VisualElement tree)
    {
        VisualElement? stamp = tree.Q<VisualElement>("FooterBuild");
        if (stamp == null)
        {
            return;
        }

        string guid = Application.buildGUID ?? string.Empty;
        bool known = guid.Length >= 8 && guid.Trim('0').Length > 0;
        stamp.style.display = known ? DisplayStyle.Flex : DisplayStyle.None;
        if (!known)
        {
            return;
        }

        string hash = guid[..8];
        if (tree.Q<Label>("FooterBuildHash") is Label label)
        {
            label.text = hash;
        }

        if (tree.Q<VisualElement>("FooterBuildBars") is VisualElement bars)
        {
            KernBarcode.Fill(bars, hash);
        }
    }

    private static void QuitGame()
    {
        Debug.Log("[MainMenu] Exiting game client...");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private static void OpenDiscord() =>
        Application.OpenURL("https://discord.gg/kern");

    private static void OpenTelegram() =>
        Application.OpenURL("https://t.me/kern");

    private static void OpenVK() =>
        Application.OpenURL("https://vk.com/kern");
}

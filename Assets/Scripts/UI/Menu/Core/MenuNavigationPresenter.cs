#nullable enable

using System;
using Fodinae.Core.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI;

/// <summary>
/// Manages main menu primary buttons, route indicators, sidebar shortcuts, and footer info.
/// </summary>
public sealed class MenuNavigationPresenter
{
    private Button? _playButton;
    private Button? _serverSelectButton;
    private Button? _updateAlertBanner;
    private Button? _userPillButton;
    private Button? _cancelDescentButton;

    private VisualElement? _routeOrbit;
    private VisualElement? _routeDescent;
    private VisualElement? _routeSurface;

    private Button? _sideChronicleButton;
    private Button? _sideSettingsButton;
    private Button? _sideRepairButton;
    private Button? _sideUpdateButton;
    private Button? _sideDiscordButton;
    private Button? _sideTelegramButton;
    private Button? _sideVkButton;
    private Button? _sideExitButton;

    private Button? _newsTickerButton;
    private Button? _footerVersionButton;

    public void Bind(
        VisualElement tree,
        MenuModalManager modalManager,
        Action onPlayClicked,
        Action onCancelDescent,
        ILocalizationService? loc)
    {
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

        _newsTickerButton = tree.Q<Button>("NewsTickerButton");
        _footerVersionButton = tree.Q<Button>("FooterVersionButton");

        if (_playButton != null)
        {
            _playButton.clicked += onPlayClicked;
        }

        if (_serverSelectButton != null)
        {
            _serverSelectButton.clicked += modalManager.OpenServerBrowser;
        }

        if (_updateAlertBanner != null)
        {
            _updateAlertBanner.clicked += modalManager.OpenUpdate;
        }

        if (_userPillButton != null)
        {
            _userPillButton.clicked += modalManager.OpenProfile;
        }

        if (_cancelDescentButton != null)
        {
            _cancelDescentButton.clicked += onCancelDescent;
        }

        if (_sideChronicleButton != null)
        {
            _sideChronicleButton.clicked += modalManager.OpenChronicle;
        }

        if (_sideSettingsButton != null)
        {
            _sideSettingsButton.clicked += modalManager.OpenSettings;
        }

        if (_sideRepairButton != null)
        {
            _sideRepairButton.clicked += modalManager.OpenRepair;
        }

        if (_sideUpdateButton != null)
        {
            _sideUpdateButton.clicked += modalManager.OpenUpdate;
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
            _newsTickerButton.clicked += modalManager.OpenChronicle;
        }

        if (_footerVersionButton != null)
        {
            _footerVersionButton.clicked += modalManager.OpenUpdate;
        }

        ApplyLocalization(loc);
    }

    public void SetDescentRouteActive()
    {
        _routeOrbit?.RemoveFromClassList("mm-route-item--active");
        _routeDescent?.AddToClassList("mm-route-item--active");
    }

    public void ApplyLocalization(ILocalizationService? loc)
    {
        if (loc == null)
        {
            return;
        }

        Label? playLabel = _playButton?.Q<Label>(null, "mm-btn-primary-text");
        if (playLabel != null)
        {
            playLabel.text = loc.Get("menu.play");
        }

        Label? serverLabel = _serverSelectButton?.Q<Label>();
        if (serverLabel != null)
        {
            serverLabel.text = loc.Get("menu.server_select");
        }

        if (_cancelDescentButton != null)
        {
            _cancelDescentButton.text = loc.Get("menu.cancel_descent");
        }

        Label? orbitLabel = _routeOrbit?.Q<Label>(null, "mm-route-text");
        if (orbitLabel != null)
        {
            orbitLabel.text = loc.Get("menu.orbit");
        }

        Label? descentLabel = _routeDescent?.Q<Label>(null, "mm-route-text");
        if (descentLabel != null)
        {
            descentLabel.text = loc.Get("menu.descent");
        }

        if (_sideChronicleButton != null)
        {
            _sideChronicleButton.tooltip = loc.Get("menu.chronicle");
        }

        if (_sideSettingsButton != null)
        {
            _sideSettingsButton.tooltip = loc.Get("menu.settings");
        }

        if (_sideRepairButton != null)
        {
            _sideRepairButton.tooltip = loc.Get("menu.repair");
        }

        if (_sideUpdateButton != null)
        {
            _sideUpdateButton.tooltip = loc.Get("menu.update");
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
        Application.OpenURL("https://discord.gg/fodinae");

    private static void OpenTelegram() =>
        Application.OpenURL("https://t.me/fodinae");

    private static void OpenVk() =>
        Application.OpenURL("https://vk.com/fodinae");
}

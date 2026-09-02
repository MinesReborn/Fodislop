#nullable enable

using System;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Localization;
using Fodinae.Networking.Connection;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI;

/// <summary>
/// Manages modal dialog visibility, tabs and backdrop in the Main Menu.
/// </summary>
public sealed class MenuModalManager
{
    private const string SettingsPaneActiveClass = "mm-tab-pane--active";
    private const int TartarusServerPort = 7778;
    private const int CyberServerPort = 7779;

    private VisualElement? _modalOverlay;
    private VisualElement? _serverBrowserModal;
    private VisualElement? _settingsModal;
    private VisualElement? _chronicleModal;
    private VisualElement? _repairModal;
    private VisualElement? _profileModal;
    private VisualElement? _updateModal;
    private VisualElement? _activeModal;

    private Button? _settingsTabGraphics;
    private Button? _settingsTabAudio;
    private Button? _settingsTabControls;
    private Button? _settingsTabNetwork;
    private VisualElement? _settingsPaneGraphics;
    private VisualElement? _settingsPaneAudio;
    private VisualElement? _settingsPaneControls;
    private VisualElement? _settingsPaneNetwork;

    private Button? _serverItemHades;
    private Button? _serverItemTartarus;
    private Button? _serverItemCyber;
    private Button? _confirmServerButton;

    public bool HasActiveModal => _activeModal != null;

    public void Bind(VisualElement tree)
    {
        _modalOverlay = tree.Q<VisualElement>("ModalOverlay");
        _serverBrowserModal = tree.Q<VisualElement>("ServerBrowserModal");
        _settingsModal = tree.Q<VisualElement>("SettingsModal");
        _chronicleModal = tree.Q<VisualElement>("ChronicleModal");
        _repairModal = tree.Q<VisualElement>("RepairModal");
        _profileModal = tree.Q<VisualElement>("ProfileModal");
        _updateModal = tree.Q<VisualElement>("UpdateModal");

        _settingsTabGraphics = tree.Q<Button>("SettingsTabGraphics");
        _settingsTabAudio = tree.Q<Button>("SettingsTabAudio");
        _settingsTabControls = tree.Q<Button>("SettingsTabControls");
        _settingsTabNetwork = tree.Q<Button>("SettingsTabNetwork");
        _settingsPaneGraphics = tree.Q<VisualElement>("SettingsPaneGraphics");
        _settingsPaneAudio = tree.Q<VisualElement>("SettingsPaneAudio");
        _settingsPaneControls = tree.Q<VisualElement>("SettingsPaneControls");
        _settingsPaneNetwork = tree.Q<VisualElement>("SettingsPaneNetwork");

        _serverItemHades = tree.Q<Button>("ServerItemHades");
        _serverItemTartarus = tree.Q<Button>("ServerItemTartarus");
        _serverItemCyber = tree.Q<Button>("ServerItemCyber");
        _confirmServerButton = tree.Q<Button>("ConfirmServerButton");

        UIState.Hide(_modalOverlay);
    }

    public void SubscribeEvents(
        VisualElement tree,
        Action onPlay,
        IClientConfigManager? clientConfig = null,
        ISceneNavigator? sceneNavigator = null,
        IAsyncOperationSupervisor? operations = null,
        ILocalizationService? loc = null)
    {
        BindModalClose(tree, "CloseServerModalButton");
        BindModalClose(tree, "CloseSettingsModalButton");
        BindModalClose(tree, "CloseChronicleModalButton");
        BindModalClose(tree, "CloseChronicleFooterButton");
        BindModalClose(tree, "CloseRepairModalButton");
        BindModalClose(tree, "ConfirmRepairButton");
        BindModalClose(tree, "CloseProfileModalButton");
        BindModalClose(tree, "CloseProfileFooterButton");
        BindModalClose(tree, "CloseUpdateModalButton");

        if (_settingsTabGraphics != null)
        {
            _settingsTabGraphics.clicked += () => SwitchSettingsTab(_settingsTabGraphics, _settingsPaneGraphics);
        }

        if (_settingsTabAudio != null)
        {
            _settingsTabAudio.clicked += () => SwitchSettingsTab(_settingsTabAudio, _settingsPaneAudio);
        }

        if (_settingsTabControls != null)
        {
            _settingsTabControls.clicked += () => SwitchSettingsTab(_settingsTabControls, _settingsPaneControls);
        }

        if (_settingsTabNetwork != null)
        {
            _settingsTabNetwork.clicked += () => SwitchSettingsTab(_settingsTabNetwork, _settingsPaneNetwork);
        }

        var controlSchemeDropdown = tree.Q<DropdownField>("MenuSettingsControlScheme");
        if (controlSchemeDropdown != null && clientConfig != null)
        {
            controlSchemeDropdown.choices = new System.Collections.Generic.List<string>
            {
                loc?.Get("gateway.onb.controls.keyboard") ?? "WASD",
                loc?.Get("gateway.onb.controls.mouse") ?? "Mouse",
            };
            controlSchemeDropdown.index = Mathf.Clamp(clientConfig.Config.Interface.ControlScheme, 0, 1);
            controlSchemeDropdown.RegisterValueChangedCallback(_ =>
            {
                clientConfig.UpdateAndSave(cfg => cfg.Interface.ControlScheme = controlSchemeDropdown.index);
            });
        }

        var serverItemDummy = tree.Q<Button>("ServerItemDummy");
        var directIpInput = tree.Q<TextField>("DirectServerIpInput");
        var directConnectBtn = tree.Q<Button>("DirectConnectButton");
        var srvTitle = tree.Q<Label>("ServerDetailTitle");
        var srvDesc = tree.Q<Label>("ServerDetailDesc");
        var srvDepth = tree.Q<Label>("ServerDetailDepth");
        var srvSeed = tree.Q<Label>("ServerDetailSeed");
        var srvPing = tree.Q<Label>("ServerDetailPing");
        var srvHazard = tree.Q<Label>("ServerDetailHazard");

        if (_serverItemHades != null)
        {
            _serverItemHades.clicked += () =>
            {
                SelectServer(_serverItemHades);
                if (srvTitle != null) srvTitle.text = "HADES-ALPHA";
                if (srvDesc != null) srvDesc.text = loc?.Get("server.hades.desc") ?? "HADES-ALPHA";
                if (srvDepth != null) srvDepth.text = "-2480m";
                if (srvSeed != null) srvSeed.text = "#849201";
                if (srvPing != null) srvPing.text = "32 ms";
                if (srvHazard != null) srvHazard.text = loc?.Get("server.hazard.high") ?? "High";
                clientConfig?.UpdateAndSave(cfg =>
                {
                    cfg.Connection.UseDummyConnection = false;
                    cfg.Connection.ServerHost = ConnectionTransportConfig.DefaultServerHost;
                    cfg.Connection.ServerPort = ConnectionTransportConfig.DefaultServerPort;
                });
            };
        }

        if (_serverItemTartarus != null)
        {
            _serverItemTartarus.clicked += () =>
            {
                SelectServer(_serverItemTartarus);
                if (srvTitle != null) srvTitle.text = "TARTARUS-02";
                if (srvDesc != null) srvDesc.text = loc?.Get("server.tartarus.desc") ?? "TARTARUS-02";
                if (srvDepth != null) srvDepth.text = "-1920m";
                if (srvSeed != null) srvSeed.text = "#104928";
                if (srvPing != null) srvPing.text = "44 ms";
                if (srvHazard != null) srvHazard.text = loc?.Get("server.hazard.extreme") ?? "Extreme";
                clientConfig?.UpdateAndSave(cfg =>
                {
                    cfg.Connection.UseDummyConnection = false;
                    cfg.Connection.ServerHost = ConnectionTransportConfig.DefaultServerHost;
                    cfg.Connection.ServerPort = TartarusServerPort;
                });
            };
        }

        if (_serverItemCyber != null)
        {
            _serverItemCyber.clicked += () =>
            {
                SelectServer(_serverItemCyber);
                if (srvTitle != null) srvTitle.text = "CYBER-PROSPECTORS";
                if (srvDesc != null) srvDesc.text = loc?.Get("server.cyber.desc") ?? "CYBER-PROSPECTORS";
                if (srvDepth != null) srvDepth.text = "-950m";
                if (srvSeed != null) srvSeed.text = "#559102";
                if (srvPing != null) srvPing.text = "118 ms";
                if (srvHazard != null) srvHazard.text = loc?.Get("server.hazard.medium") ?? "Medium";
                clientConfig?.UpdateAndSave(cfg =>
                {
                    cfg.Connection.UseDummyConnection = false;
                    cfg.Connection.ServerHost = ConnectionTransportConfig.DefaultServerHost;
                    cfg.Connection.ServerPort = CyberServerPort;
                });
            };
        }

        if (serverItemDummy != null)
        {
            serverItemDummy.clicked += () =>
            {
                SelectServer(serverItemDummy);
                if (srvTitle != null) srvTitle.text = "DUMMY OFFLINE";
                if (srvDesc != null) srvDesc.text = loc?.Get("server.dummy.desc") ?? "Offline Sandbox";
                if (srvDepth != null) srvDepth.text = "-100m";
                if (srvSeed != null) srvSeed.text = "#000000";
                if (srvPing != null) srvPing.text = "0 ms";
                if (srvHazard != null) srvHazard.text = loc?.Get("server.hazard.test") ?? "Test";
                clientConfig?.UpdateAndSave(cfg => cfg.Connection.UseDummyConnection = true);
            };
        }

        if (directConnectBtn != null && directIpInput != null)
        {
            directConnectBtn.clicked += () =>
            {
                if (!ConnectionTransportConfig.TryParseEndpoint(
                        directIpInput.value,
                        out string host,
                        out int port))
                {
                    Debug.LogWarning($"[MenuModalManager] Invalid direct-connect endpoint: '{directIpInput.value}'.");
                    return;
                }

                clientConfig?.UpdateAndSave(cfg =>
                {
                    cfg.Connection.UseDummyConnection = false;
                    cfg.Connection.ServerHost = host;
                    cfg.Connection.ServerPort = port;
                });
                CloseCurrentModal();
                onPlay();
            };
        }

        BindModalClose(tree, "CloseServerFooterButton");

        // Profile Modal Logic
        var copyTokenBtn = tree.Q<Button>("CopyTokenButton");
        var tokenField = tree.Q<TextField>("ProfileTokenField");
        var copyFeedback = tree.Q<Label>("CopyTokenFeedback");
        if (copyTokenBtn != null && tokenField != null)
        {
            copyTokenBtn.clicked += () =>
            {
                GUIUtility.systemCopyBuffer = tokenField.value;
                if (copyFeedback != null)
                {
                    copyFeedback.text = loc?.Get("mainmenu.token_copied") ?? "Token copied!";
                    UIState.Show(copyFeedback);
                }
            };
        }

        var switchAccountBtn = tree.Q<Button>("SwitchAccountButton");
        if (switchAccountBtn != null && sceneNavigator != null && operations != null)
        {
            switchAccountBtn.clicked += () =>
            {
                CloseCurrentModal();
                operations.Run(
                    "main_menu_switch_account",
                    cancellationToken => sceneNavigator.TransitionAsync(
                        ProjectRuntimeContracts.SceneNames.Gateway,
                        cancellationToken));
            };
        }

        // Update Modal Logic
        var deferOfflineBtn = tree.Q<Button>("DeferUpdateOfflineButton");
        if (deferOfflineBtn != null)
        {
            deferOfflineBtn.clicked += () =>
            {
                clientConfig?.UpdateAndSave(cfg => cfg.Connection.UseDummyConnection = true);
                CloseCurrentModal();
                onPlay();
            };
        }

        var confirmUpdateBtn = tree.Q<Button>("ConfirmUpdateButton");
        var updateBlock = tree.Q<VisualElement>("UpdateProgressBlock");
        var updateFill = tree.Q<VisualElement>("UpdateProgressFill");
        var updatePercent = tree.Q<Label>("UpdateProgressPercent");
        if (confirmUpdateBtn != null)
        {
            confirmUpdateBtn.clicked += () =>
            {
                UIState.Show(updateBlock);
                if (updateFill != null) updateFill.style.width = new Length(100, LengthUnit.Percent);
                if (updatePercent != null) updatePercent.text = "100%";
                CloseCurrentModal();
                onPlay();
            };
        }

        if (_confirmServerButton != null)
        {
            _confirmServerButton.clicked += () =>
            {
                CloseCurrentModal();
                onPlay();
            };
        }

        var saveSettingsBtn = tree.Q<Button>("SaveSettingsButton");
        if (saveSettingsBtn != null)
        {
            saveSettingsBtn.clicked += CloseCurrentModal;
        }

        _modalOverlay?.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.target == _modalOverlay)
            {
                CloseCurrentModal();
            }
        });
    }

    public void OpenServerBrowser() => OpenModal(_serverBrowserModal);
    public void OpenSettings() => OpenModal(_settingsModal);
    public void OpenChronicle() => OpenModal(_chronicleModal);
    public void OpenRepair() => OpenModal(_repairModal);
    public void OpenProfile() => OpenModal(_profileModal);
    public void OpenUpdate() => OpenModal(_updateModal);

    public void OpenModal(VisualElement? modal)
    {
        if (modal == null || _modalOverlay == null)
        {
            return;
        }

        HideAllModals();
        UIState.Show(_modalOverlay);
        UIState.Show(modal);
        _activeModal = modal;
    }

    public void CloseCurrentModal()
    {
        UIState.Hide(_modalOverlay);
        HideAllModals();
        _activeModal = null;
    }

    private void HideAllModals()
    {
        UIState.Hide(_serverBrowserModal);
        UIState.Hide(_settingsModal);
        UIState.Hide(_chronicleModal);
        UIState.Hide(_repairModal);
        UIState.Hide(_profileModal);
        UIState.Hide(_updateModal);
    }

    private void BindModalClose(VisualElement tree, string buttonName)
    {
        var btn = tree.Q<Button>(buttonName);
        if (btn != null)
        {
            btn.clicked += CloseCurrentModal;
        }
    }

    private void SwitchSettingsTab(Button tabBtn, VisualElement? targetPane)
    {
        _settingsTabGraphics?.RemoveFromClassList("mm-nav-tab--active");
        _settingsTabAudio?.RemoveFromClassList("mm-nav-tab--active");
        _settingsTabControls?.RemoveFromClassList("mm-nav-tab--active");
        _settingsTabNetwork?.RemoveFromClassList("mm-nav-tab--active");

        // Пара mm-tab-pane / mm-tab-pane--active объявлена и в разметке, и в
        // Theme.uss. Раньше код писал поверх неё инлайн, и класс не значил
        // ничего: активная вкладка оставалась активной навсегда, потому что
        // снять инлайн можно только инлайном.
        foreach (var pane in new[] { _settingsPaneGraphics, _settingsPaneAudio, _settingsPaneControls, _settingsPaneNetwork })
        {
            pane?.EnableInClassList(SettingsPaneActiveClass, ReferenceEquals(pane, targetPane));
        }

        tabBtn.AddToClassList("mm-nav-tab--active");
    }

    private void SelectServer(Button serverCard)
    {
        _serverItemHades?.RemoveFromClassList("mm-server-card--active");
        _serverItemTartarus?.RemoveFromClassList("mm-server-card--active");
        _serverItemCyber?.RemoveFromClassList("mm-server-card--active");

        serverCard.AddToClassList("mm-server-card--active");
    }
}

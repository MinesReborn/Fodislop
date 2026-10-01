#nullable enable

using System;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.GUI;
using UnityEngine.UIElements;

namespace Kern.UI.HUD.Player.View;

public sealed class PlayerHUDPopups
{
    private readonly Action<SuicidePacket> _sendSuicide;
    private readonly Action<OpenHelpClickPacket> _sendHelpRequest;
    private VisualElement? _respawnPopup;
    private VisualElement? _buildingsPopup;

    public PlayerHUDPopups(Action<SuicidePacket> sendSuicide, Action<OpenHelpClickPacket> sendHelpRequest)
    {
        _sendSuicide = sendSuicide;
        _sendHelpRequest = sendHelpRequest;
    }

    public void Initialize(VisualElement root)
    {
        _respawnPopup = root.Q<VisualElement>("RespawnPopup") ??
            throw new InvalidOperationException("[PlayerHUD] RespawnPopup is missing from PlayerHUD.uxml.");
        Button respawnConfirm = root.Q<Button>("RespawnConfirmButton") ??
            throw new InvalidOperationException("[PlayerHUD] RespawnConfirmButton is missing from PlayerHUD.uxml.");
        respawnConfirm.clicked += () =>
        {
            _sendSuicide(new SuicidePacket());
            _respawnPopup.style.display = DisplayStyle.None;
        };
        Button respawnCancel = root.Q<Button>("RespawnCancelButton") ??
            throw new InvalidOperationException("[PlayerHUD] RespawnCancelButton is missing from PlayerHUD.uxml.");
        respawnCancel.clicked += () => _respawnPopup.style.display = DisplayStyle.None;
        Button respawnButton = root.Q<Button>("RespawnButton") ??
            throw new InvalidOperationException("[PlayerHUD] RespawnButton is missing from PlayerHUD.uxml.");
        respawnButton.clicked += () => _respawnPopup.style.display = DisplayStyle.Flex;

        _buildingsPopup = root.Q<VisualElement>("BuildingsPopup") ??
            throw new InvalidOperationException("[PlayerHUD] BuildingsPopup is missing from PlayerHUD.uxml.");
        Button buildingsClose = root.Q<Button>("BuildingsCloseButton") ??
            throw new InvalidOperationException("[PlayerHUD] BuildingsCloseButton is missing from PlayerHUD.uxml.");
        buildingsClose.clicked += () => _buildingsPopup.style.display = DisplayStyle.None;
        Button buildingsButton = root.Q<Button>("BuildingsButton") ??
            throw new InvalidOperationException("[PlayerHUD] BuildingsButton is missing from PlayerHUD.uxml.");
        buildingsButton.clicked += () => _buildingsPopup.style.display = DisplayStyle.Flex;

        // FAQ — серверная страница: кнопка шлёт OpenHelpClickPacket, сервер
        // (Session.HelpHandler → Settings.FAQWindow) отвечает модальным окном
        // FAQ через ServerWindowPresenter. Локальный FaqPopup из UXML больше
        // не используется.
        Button faqButton = root.Q<Button>("FaqButton") ??
            throw new InvalidOperationException("[PlayerHUD] FaqButton is missing from PlayerHUD.uxml.");
        faqButton.clicked += () => _sendHelpRequest(new OpenHelpClickPacket());
    }
}

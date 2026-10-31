#nullable enable

using System;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.Chat;
using MinesServer.Networking.Client.Packets.GUI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.HUD;

public sealed class PlayerHUDPopups
{
    // Серверного GUI-пакета «открыть консоль» в протоколе нет: сервер
    // (Session.LocalChatHandler) перехватывает локальное сообщение "console",
    // не рассылая его другим игрокам, и открывает MConsole.OpenWindow -
    // серверное окно с табами ОБЩИЕ/МОДЕРАТОР/АДМИН по правам игрока.
    private const string MConsoleLocalChatCommand = "console";

    private readonly Action<SuicidePacket> _sendSuicide;
    private readonly Action<OpenHelpClickPacket> _sendHelpRequest;
    private readonly Action<OpenBuildingsClickPacket> _sendBuildingsRequest;
    private readonly Action<SendLocalChatMessagePacket> _sendLocalChat;
    private VisualElement? _respawnPopup;

    public PlayerHUDPopups(
        Action<SuicidePacket> sendSuicide,
        Action<OpenHelpClickPacket> sendHelpRequest,
        Action<OpenBuildingsClickPacket> sendBuildingsRequest,
        Action<SendLocalChatMessagePacket> sendLocalChat)
    {
        _sendSuicide = sendSuicide;
        _sendHelpRequest = sendHelpRequest;
        _sendBuildingsRequest = sendBuildingsRequest;
        _sendLocalChat = sendLocalChat;
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

        // «Мои здания» и FAQ — серверные страницы: кнопки шлют OpenBuildingsClickPacket (27)
        // и OpenHelpClickPacket (18); сервер отвечает окном (Session.BuildingsHandler →
        // Player.MyBuildingsWindow, Session.HelpHandler → Settings.FAQWindow) через
        // ServerWindowPresenter. Локальные попапы из UXML удалены.
        Button buildingsButton = root.Q<Button>("BuildingsButton") ??
            throw new InvalidOperationException("[PlayerHUD] BuildingsButton is missing from PlayerHUD.uxml.");
        buildingsButton.clicked += () => _sendBuildingsRequest(new OpenBuildingsClickPacket());

        Button faqButton = root.Q<Button>("FaqButton") ??
            throw new InvalidOperationException("[PlayerHUD] FaqButton is missing from PlayerHUD.uxml.");
        faqButton.clicked += () => _sendHelpRequest(new OpenHelpClickPacket());

        // MConsole — серверная консоль: открытие через локальный чат "console"
        // (см. MConsoleLocalChatCommand). Окно рендерится существующим
        // ServerWindowPresenter, вкладки собирает сервер по правам игрока.
        Button mconsoleButton = root.Q<Button>("MConsoleButton") ??
            throw new InvalidOperationException("[PlayerHUD] MConsoleButton is missing from PlayerHUD.uxml.");
        mconsoleButton.clicked += () =>
        {
            Debug.Log("[MConsole] Клик: отправляю SendLocalChatMessagePacket(\"console\")");
            _sendLocalChat(new SendLocalChatMessagePacket(MConsoleLocalChatCommand));
        };
    }
}

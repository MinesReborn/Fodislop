#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Networking.Gateways;
using MinesServer.Networking.Client.Packets.Chat;
using MinesServer.Networking.Server.Packets.Chat;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Chat;

internal sealed class GlobalChatChannelPresenter
{
    private readonly ChatEventGateway _chatEvents;
    private readonly INetworkService _networkService;
    private readonly ILocalizationService _localization;
    private readonly GlobalChatMessagePresenter _messages;
    private readonly Func<bool> _isChatOpen;
    private readonly Action<bool> _refreshChatControls;
    private readonly Func<bool> _isMuted;
    private readonly HashSet<string> _knownChatTags = new(StringComparer.OrdinalIgnoreCase);
    private ChatViewElements? _view;
    private string _activeTag = ChatProtocolLimits.GlobalChannelTag;
    private string? _activeChatName;
    private bool _chatListRequested;

    public GlobalChatChannelPresenter(
        ChatEventGateway chatEvents,
        INetworkService networkService,
        ILocalizationService localization,
        GlobalChatMessagePresenter messages,
        Func<bool> isChatOpen,
        Action<bool> refreshChatControls,
        Func<bool> isMuted)
    {
        _chatEvents = chatEvents;
        _networkService = networkService;
        _localization = localization;
        _messages = messages;
        _isChatOpen = isChatOpen;
        _refreshChatControls = refreshChatControls;
        _isMuted = isMuted;
    }

    public string ActiveTag => _activeTag;

    public bool CanSendToActiveChannel => _knownChatTags.Count == 0 ||
        _knownChatTags.Contains(_activeTag);

    public void BindView(ChatViewElements view) => _view = view;

    public void UpdateChannelHeader()
    {
        ChatChannelPresenter.UpdatePresentation(
            _view?.ChatHeader,
            _view?.GlobalChannelButton,
            _localization,
            _activeChatName);
    }

    public void OpenChatList()
    {
        _chatListRequested = true;
        ChatListPacket? cached = _chatEvents.LastChatList;
        try
        {
            _networkService.Send(new OpenChatListPacket());
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[GlobalChatUI] Не удалось запросить список чатов: {exception}");
            if (cached is ChatListPacket snapshot)
            {
                RenderChatList(snapshot);
            }
        }
    }

    public void CloseChatListPopup()
    {
        _chatListRequested = false;
        if (_view?.ChatListPopup != null)
        {
            _view.ChatListPopup.style.display = DisplayStyle.None;
        }
    }

    public void ApplyChatList(ChatListPacket packet)
    {
        _knownChatTags.Clear();
        foreach (var (tag, _, _) in packet.Chats)
        {
            if (!string.IsNullOrEmpty(tag))
            {
                _knownChatTags.Add(tag);
            }
        }

        _refreshChatControls(_isMuted());
        if (_chatListRequested)
        {
            RenderChatList(packet);
        }
    }

    public void OnMessageClicked(long messageId)
    {
        if (messageId <= 0)
        {
            return;
        }

        try
        {
            _networkService.Send(new ChatMessageClickPacket(_activeTag, messageId));
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[GlobalChatUI] Не удалось открыть окно отправителя: {exception}");
        }
    }

    private void RenderChatList(ChatListPacket packet)
    {
        if (_view?.ChatListScroll == null || _view.ChatListPopup == null)
        {
            return;
        }

        ScrollView scroll = _view.ChatListScroll;
        scroll.Clear();
        foreach (var (tag, name, lastMessage) in packet.Chats)
        {
            string chatName = string.IsNullOrEmpty(name) ? tag : name;
            string preview = string.IsNullOrEmpty(lastMessage.Message) ? chatName : lastMessage.Message;
            var row = new Button { text = string.Empty };
            row.AddToClassList("gchat-chat-row");
            var nameLabel = new Label(ChatMessageFormatter.PlainText(chatName));
            nameLabel.AddToClassList("gchat-chat-row-name");
            var previewLabel = new Label(ChatMessageFormatter.PlainText(preview));
            previewLabel.AddToClassList("gchat-chat-row-preview");
            row.Add(nameLabel);
            row.Add(previewLabel);
            string chosenTag = tag;
            string chosenName = chatName;
            row.clicked += () => ChooseChat(chosenTag, chosenName);
            scroll.Add(row);
        }

        _view.ChatListPopup.style.display = DisplayStyle.Flex;
    }

    private void ChooseChat(string tag, string chatName)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return;
        }

        _activeTag = tag;
        _activeChatName = chatName;
        _chatListRequested = false;
        _chatEvents.SetFocusedTag(_isChatOpen() ? tag : null);
        if (_view?.ChatListPopup != null)
        {
            _view.ChatListPopup.style.display = DisplayStyle.None;
        }

        _messages.Clear();
        UpdateChannelHeader();
        _refreshChatControls(_isMuted());
        try
        {
            _networkService.Send(new QueryChatHistoryPacket(tag, 0));
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[GlobalChatUI] Не удалось переключить чат: {exception}");
        }
    }
}

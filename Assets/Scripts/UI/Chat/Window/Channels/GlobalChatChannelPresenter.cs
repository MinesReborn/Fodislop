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
    private string _activeTitle = string.Empty;

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

    public bool IsChatListOpen => _view?.ChatListScroll?.style.display == DisplayStyle.Flex;

    // Шапка окна — имя текущего канала из списка сервера (ChatListPacket); до списка — тег канала.
    // Метка свёрнутого чата — тег канала
    public void UpdateChannelHeader()
    {
        _activeTitle = string.IsNullOrEmpty(_activeChatName)
            ? _activeTag
            : ChatMessageFormatter.PlainText(_activeChatName!);
        if (_view?.ChatHeader != null)
        {
            _view.ChatHeader.text = _activeTitle;
        }

        if (_view?.PeekTag != null)
        {
            _view.PeekTag.text = _activeTag.ToUpperInvariant();
        }
    }

    // Кнопка списка: открыть все каналы поверх ленты или закрыть список
    public void ToggleChatList()
    {
        if (IsChatListOpen)
        {
            CloseChatListPopup();
        }
        else
        {
            OpenChatList();
        }
    }

    // Счётчики непрочитанного: на кнопке списка — сумма по каналам, в строках списка — по каналу
    public void RefreshUnread()
    {
        int total = _chatEvents.GetTotalUnread();
        if (_view?.ListBadge != null)
        {
            _view.ListBadge.text = total > 99 ? "99+" : total.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _view.ListBadge.EnableInClassList("is-hidden", total <= 0);
        }

        if (IsChatListOpen && _chatEvents.LastChatList is ChatListPacket list)
        {
            RenderChatList(list);
        }
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
        SetChatListShown(false);
    }

    // Открыт список — название текущего канала в шапке не нужно, место держим (шапка не прыгает)
    private void SetChatListShown(bool shown)
    {
        if (_view?.ChatListScroll != null)
        {
            _view.ChatListScroll.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (_view?.ChatHeader != null)
        {
            _view.ChatHeader.style.visibility = shown ? Visibility.Hidden : Visibility.Visible;
        }
    }

    public void ApplyChatList(ChatListPacket packet)
    {
        _knownChatTags.Clear();
        foreach (var (tag, name, _) in packet.Chats)
        {
            if (!string.IsNullOrEmpty(tag))
            {
                _knownChatTags.Add(tag);
            }

            if (string.Equals(tag, _activeTag, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(name))
            {
                _activeChatName = name;
            }
        }

        UpdateChannelHeader();

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

    // Строка канала (макет: .chat-list button): «FED – Федеральный чат», счётчик непрочитанного,
    // ниже — последнее сообщение «ник: текст» или «Сообщений нет»; текущий канал отмечен
    private void RenderChatList(ChatListPacket packet)
    {
        if (_view?.ChatListScroll == null)
        {
            return;
        }

        ScrollView scroll = _view.ChatListScroll;
        scroll.Clear();
        foreach (var (tag, name, lastMessage) in packet.Chats)
        {
            string chatName = string.IsNullOrEmpty(name) ? tag : name;
            string title = string.IsNullOrEmpty(name) ? tag : tag + " – " + name;
            string preview = string.IsNullOrEmpty(lastMessage.Message)
                ? _localization.Get("chat.list.empty")
                : lastMessage.PlayerName + ": " + lastMessage.Message;
            var row = new Button { text = string.Empty };
            row.AddToClassList("chat-list-row");
            row.EnableInClassList("is-on", string.Equals(tag, _activeTag, StringComparison.OrdinalIgnoreCase));
            var head = new VisualElement { pickingMode = PickingMode.Ignore };
            head.AddToClassList("chat-list-row__head");
            var nameLabel = new Label(ChatMessageFormatter.PlainText(title)) { pickingMode = PickingMode.Ignore };
            nameLabel.AddToClassList("chat-list-row__name");
            head.Add(nameLabel);
            int unread = _chatEvents.GetUnread(tag);
            if (unread > 0)
            {
                var badge = new Label(unread > 99 ? "99+" : unread.ToString(System.Globalization.CultureInfo.InvariantCulture)) { pickingMode = PickingMode.Ignore };
                badge.AddToClassList("chat-list-row__count");
                head.Add(badge);
            }

            var previewLabel = new Label(ChatMessageFormatter.PlainText(preview)) { pickingMode = PickingMode.Ignore };
            previewLabel.AddToClassList("chat-list-row__preview");
            row.Add(head);
            row.Add(previewLabel);
            string chosenTag = tag;
            string chosenName = chatName;
            row.clicked += () => ChooseChat(chosenTag, chosenName);
            scroll.Add(row);
        }

        SetChatListShown(true);
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
        SetChatListShown(false);

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

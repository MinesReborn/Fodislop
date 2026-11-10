#nullable enable

using System;
using System.Collections.Generic;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.World;

namespace Kern.Networking.Gateways;

public sealed class ChatEventGateway
{
    private readonly Dictionary<string, int> _unreadByTag = new(StringComparer.Ordinal);
    private string? _focusedTag;

    public ChatListPacket? LastChatList { get; private set; }

    public event Action<ChatMessagePacket>? MessageReceived;
    public event Action<ChatMessageListPacket>? HistoryReceived;
    public event Action<ChatMutePacket>? MuteReceived;
    public event Action<LocalChatMessagePacket>? LocalMessageReceived;
    public event Action<ChatListPacket>? ChatListReceived;
    public event Action? UnreadChanged;

    public void Publish(ChatMessagePacket packet) => MessageReceived?.Invoke(packet);

    public void Publish(ChatMessageListPacket packet)
    {
        if (!string.Equals(packet.Tag, _focusedTag, StringComparison.Ordinal) && packet.Messages.Count > 0)
        {
            _unreadByTag[packet.Tag] = GetUnread(packet.Tag) + packet.Messages.Count;
            UnreadChanged?.Invoke();
        }

        HistoryReceived?.Invoke(packet);
    }

    public void Publish(ChatMutePacket packet) => MuteReceived?.Invoke(packet);

    public void Publish(LocalChatMessagePacket packet) => LocalMessageReceived?.Invoke(packet);

    public void Publish(ChatListPacket packet)
    {
        LastChatList = packet;
        ChatListReceived?.Invoke(packet);
    }

    public int GetUnread(string tag) => _unreadByTag.TryGetValue(tag, out int unread) ? unread : 0;

    public int GetTotalUnread()
    {
        int total = 0;
        foreach (int unread in _unreadByTag.Values)
        {
            total += unread;
        }

        return total;
    }

    public void SetFocusedTag(string? tag)
    {
        if (string.Equals(_focusedTag, tag, StringComparison.Ordinal))
        {
            return;
        }

        _focusedTag = tag;
        if (tag != null && _unreadByTag.Remove(tag))
        {
            UnreadChanged?.Invoke();
        }
    }
}

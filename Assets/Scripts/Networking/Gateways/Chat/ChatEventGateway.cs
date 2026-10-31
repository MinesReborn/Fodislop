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

    /// <summary>Счётчик непрочитанных изменился (E2-C). Бейдж HUD-кнопки читает
    /// <see cref="GetTotalUnread"/> после события.</summary>
    public event Action? UnreadChanged;

    public void Publish(ChatMessagePacket packet) => MessageReceived?.Invoke(packet);

    // Пакет 47 несёт все сообщения канала — и живые, и историю (сервер шлёт их
    // только участникам текущего канала). Пока канал не в фокусе (окно закрыто
    // или открыт другой), пачка считается непрочитанной.
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

    /// <summary>Число непрочитанных канала; 0, если записей нет.</summary>
    public int GetUnread(string tag) => _unreadByTag.TryGetValue(tag, out int unread) ? unread : 0;

    /// <summary>Сумма непрочитанных по всем каналам — число для бейджа.</summary>
    public int GetTotalUnread()
    {
        int total = 0;
        foreach (int unread in _unreadByTag.Values)
        {
            total += unread;
        }

        return total;
    }

    /// <summary>
    /// Фокус окна чата на канале: <c>null</c> — окно закрыто, ничего не
    /// просматривается. Фокус на канале означает «прочитано» — его счётчик
    /// сбрасывается (E2-C: подтверждений прочтения в протоколе нет, серверу
    /// ничего не отправляется).
    /// </summary>
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

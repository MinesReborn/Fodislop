#nullable enable

using System;
using Fodinae.Core.Localization;
using MinesServer.Networking.Server.Packets.Chat;

namespace Fodinae.UI;

internal sealed class ChatMuteTracker
{
    private long _mutedUntilUnixMilliseconds = -1;

    public bool IsMuted
    {
        get
        {
            if (_mutedUntilUnixMilliseconds == -1)
            {
                return false;
            }

            return _mutedUntilUnixMilliseconds == 0 ||
                _mutedUntilUnixMilliseconds > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }

    public void ApplyMute(
        ChatMutePacket packet,
        ILocalizationService loc,
        out string statusMessage,
        out string notificationMessage)
    {
        _mutedUntilUnixMilliseconds = packet.EndsAt;
        string reason = string.IsNullOrWhiteSpace(packet.Reason)
            ? loc.Get("chat.mute.no_reason")
            : packet.Reason.Trim();
        string moderator = string.IsNullOrWhiteSpace(packet.ModeratorName)
            ? loc.Get("chat.mute.by_server")
            : packet.ModeratorName.Trim();
        string duration = packet.EndsAt <= 0
            ? loc.Get("chat.mute.forever")
            : loc.Get("chat.mute.until", ChatMessageFormatter.FormatMuteEnd(packet.EndsAt));

        statusMessage = loc.Get("chat.mute.blocked", moderator, reason, duration);
        notificationMessage = loc.Get("chat.mute.received");
    }

    public bool CheckExpiration()
    {
        if (_mutedUntilUnixMilliseconds == -1)
        {
            return false;
        }

        if (!IsMuted && _mutedUntilUnixMilliseconds > 0)
        {
            _mutedUntilUnixMilliseconds = -1;
            return true;
        }

        return false;
    }
}

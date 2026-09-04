#nullable enable

using System;
using System.Text;
using Fodinae.Core.Localization;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;

namespace Fodinae.UI;

internal static class ChatMessageFormatter
{
    private static bool _invalidMuteExpiryLogged;

    public static string FormatGlobal(ChatMessagePacket msg, DateTime now)
    {
        var sb = new StringBuilder(128);
        sb.Append("<color=#888888>[");
        sb.Append(now.Hour.ToString("D2"));
        sb.Append(':');
        sb.Append(now.Minute.ToString("D2"));
        sb.Append("]</color> <color=#");
        sb.Append(msg.NicknameColor.R.ToString("X2"));
        sb.Append(msg.NicknameColor.G.ToString("X2"));
        sb.Append(msg.NicknameColor.B.ToString("X2"));
        sb.Append('>');
        sb.Append(msg.PlayerName);
        sb.Append("</color>: <color=#");
        sb.Append(msg.MessageColor.R.ToString("X2"));
        sb.Append(msg.MessageColor.G.ToString("X2"));
        sb.Append(msg.MessageColor.B.ToString("X2"));
        sb.Append('>');
        sb.Append(msg.Message);
        sb.Append("</color>");
        return sb.ToString();
    }

    public static string FormatLocal(LocalChatMessagePacket packet, DateTime now, ILocalizationService loc)
    {
        var sb = new StringBuilder(128);
        sb.Append("<color=#888888>[");
        sb.Append(now.Hour.ToString("D2"));
        sb.Append(':');
        sb.Append(now.Minute.ToString("D2"));
        sb.Append("]</color> <color=#B2A680>");
        sb.Append(loc.Get("chat.local.sender", packet.BotId));
        sb.Append("</color>: ");
        sb.Append(packet.Text);
        return sb.ToString();
    }

    public static string FormatMuteEnd(long unixMilliseconds)
    {
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds)
                .ToLocalTime()
                .ToString("g");
        }
        catch (ArgumentOutOfRangeException)
        {
            // Серверный ввод не должен ронять клиент: битый timestamp в пакете
            // мута — это данные, а не контрактная ошибка. Отображаем как есть.
            if (_invalidMuteExpiryLogged)
            {
                return unixMilliseconds.ToString();
            }

            _invalidMuteExpiryLogged = true;
            Debug.LogWarning(
                $"[GlobalChat] Mute packet contains invalid expiry timestamp: {unixMilliseconds}");
            return unixMilliseconds.ToString();
        }
    }
}

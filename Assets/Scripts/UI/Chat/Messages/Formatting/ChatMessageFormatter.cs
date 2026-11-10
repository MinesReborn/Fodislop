#nullable enable

using System;
using System.Text;
using System.Text.RegularExpressions;
using MinesServer.Networking.Server.Packets.Chat;
using UnityEngine;

namespace Kern.UI.Chat;

internal static class ChatMessageFormatter
{
    private static readonly Regex s_noparseClose = new("</noparse", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool s_invalidMuteExpiryLogged;

    // Ник и текст пишет другой игрок, а строка уходит в метку с разметкой:
    // без экранирования <size>, <color>, <sprite> и подделка системного
    // сообщения доступны любому. Текст оборачивается в noparse, а закрывающий
    // тег внутри разрывается невидимым пробелом, чтобы из обёртки не выйти.
    public static string PlainText(string? text)
    {
        var sb = new StringBuilder();
        AppendPlainText(sb, text);
        return sb.ToString();
    }

    private static void AppendPlainText(StringBuilder sb, string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        sb.Append("<noparse>");
        sb.Append(s_noparseClose.Replace(text, "<\u200B/noparse"));
        sb.Append("</noparse>");
    }

    // Строка ленты (макет visual/kern-ui .chat-log li): ник жирно цветом сервера, текст цветом сервера.
    // Время — отдельной моно-подписью (FormatTime): в rich text панели нет тега шрифта
    public static string FormatGlobal(ChatMessagePacket msg)
    {
        var sb = new StringBuilder(160);
        AppendBody(sb, msg);
        return sb.ToString();
    }

    // Время сообщения — из пакета (Timestamp, мс Unix), в местном часовом поясе: «18:42».
    // Битая метка — время не показывается, строка остаётся
    public static string FormatTime(ChatMessagePacket msg)
    {
        try
        {
            DateTimeOffset local = DateTimeOffset.FromUnixTimeMilliseconds(msg.Timestamp).ToLocalTime();
            return local.Hour.ToString("D2") + ":" + local.Minute.ToString("D2");
        }
        catch (ArgumentOutOfRangeException)
        {
            return string.Empty;
        }
    }

    // Строка свёрнутого чата (макет .chat-peek li): «ник: текст» без времени
    public static string FormatPeek(ChatMessagePacket msg)
    {
        var sb = new StringBuilder(128);
        AppendBody(sb, msg);
        return sb.ToString();
    }

    private static void AppendBody(StringBuilder sb, ChatMessagePacket msg)
    {
        sb.Append("<b><color=#");
        AppendHex(sb, msg.NicknameColor);
        sb.Append('>');
        AppendPlainText(sb, msg.PlayerName);
        sb.Append("</color></b>: <color=#");
        AppendHex(sb, msg.MessageColor);
        sb.Append('>');
        AppendPlainText(sb, msg.Message);
        sb.Append("</color>");
    }

    private static void AppendHex(StringBuilder sb, System.Drawing.Color color)
    {
        sb.Append(color.R.ToString("X2"));
        sb.Append(color.G.ToString("X2"));
        sb.Append(color.B.ToString("X2"));
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
            if (s_invalidMuteExpiryLogged)
            {
                return unixMilliseconds.ToString();
            }

            s_invalidMuteExpiryLogged = true;
            Debug.LogWarning(
                $"[GlobalChat] Mute packet contains invalid expiry timestamp: {unixMilliseconds}");
            return unixMilliseconds.ToString();
        }
    }
}

#nullable enable

using System;
using System.Text.RegularExpressions;
using Kern.UI;
using MinesServer.Networking.Server.Packets.Chat;
using NUnit.Framework;
using Color = System.Drawing.Color;

using Kern.UI.Chat;

namespace Kern.Tests.UI;

public sealed class ChatMessageFormatterTests
{

    [TestCase("<size=999>BIG</size>")]
    [TestCase("</noparse><color=#FF0000>[Система] бан</color>")]
    [TestCase("</NoParse ><sprite=0>")]
    [TestCase("</noparse></noparse></noparse><b>x")]
    public void PlayerTextNeverLeavesNoparse(string hostile)
    {
        string formatted = ChatMessageFormatter.FormatGlobal(
            new ChatMessagePacket(1, 0, 1, 0, Color.White, hostile, Color.White, hostile));

        // Оракул: каждый закрывающий noparse в выводе — ровно тот, что
        // поставил форматтер; игрок не может закрыть обёртку раньше.
        int opened = Regex.Matches(formatted, "<noparse>", RegexOptions.IgnoreCase).Count;
        int closed = Regex.Matches(formatted, "</noparse", RegexOptions.IgnoreCase).Count;
        Assert.That(opened, Is.EqualTo(2));
        Assert.That(closed, Is.EqualTo(2));
        Assert.That(formatted, Does.EndWith("</noparse></color>"));
    }

    [Test]
    public void OrdinaryTextIsKeptVerbatim()
    {
        string formatted = ChatMessageFormatter.FormatGlobal(
            new ChatMessagePacket(1, 0, 1, 0, Color.White, "Шахтёр", Color.White, "привет 2 < 3"));

        Assert.That(formatted, Does.Contain("<noparse>Шахтёр</noparse>"));
        Assert.That(formatted, Does.Contain("<noparse>привет 2 < 3</noparse>"));
    }

    // Время строки — из пакета, а не момент получения: история не должна вся показывать «сейчас»
    [Test]
    public void TimeComesFromPacketTimestamp()
    {
        long timestamp = new DateTimeOffset(2026, 10, 4, 9, 7, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        DateTimeOffset local = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).ToLocalTime();

        string time = ChatMessageFormatter.FormatTime(
            new ChatMessagePacket(1, timestamp, 1, 0, Color.White, "Шахтёр", Color.White, "привет"));

        Assert.That(time, Is.EqualTo($"{local.Hour:D2}:{local.Minute:D2}"));
    }

    [Test]
    public void BrokenTimestampGivesNoTime()
    {
        Assert.That(
            ChatMessageFormatter.FormatTime(new ChatMessagePacket(1, long.MaxValue, 1, 0, Color.White, "a", Color.White, "b")),
            Is.Empty);
    }

    [Test]
    public void EmptyTextAddsNoWrapper()
    {
        Assert.That(ChatMessageFormatter.PlainText(null), Is.Empty);
        Assert.That(ChatMessageFormatter.PlainText(string.Empty), Is.Empty);
    }
}

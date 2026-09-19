#nullable enable

using System.Collections.Generic;
using MinesServer.Networking.Client.Packets.Chat;
using MinesServer.Networking.Connection.Client;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.World;
using NUnit.Framework;

namespace Kern.Tests.Networking;

public sealed class DummyChatResponderTests
{
    private List<ServerPacket> _sent = null!;
    private DummyChatResponder _responder = null!;

    [SetUp]
    public void SetUp()
    {
        _sent = [];
        _responder = new DummyChatResponder(_sent.Add, new VirtualDummyClock(seed: 1));
    }

    [Test]
    public void SendHistory_PreservesRequestedTagAndReturnsSeedHistory()
    {
        _responder.SendHistory(new QueryChatHistoryPacket("global"));

        Assert.That(_sent, Has.Count.EqualTo(1));
        var payload = (ChatMessageListPacket)_sent[0].Payload;
        Assert.That(payload.Tag, Is.EqualTo("global"));
        Assert.That(payload.Messages, Has.Count.EqualTo(10));
    }

    [Test]
    public void SendGlobal_UsesSelectedColorForPlayerNameAndText()
    {
        var color = System.Drawing.Color.FromArgb(255, 10, 20, 30);
        _responder.ChangeColor(new ChangeChatColorPacket(color));

        _responder.SendGlobal(new SendChatMessagePacket("global", "hello"));

        var payload = (ChatMessageListPacket)_sent[0].Payload;
        ChatMessagePacket message = payload.Messages[0];
        Assert.That(message.Message, Is.EqualTo("hello"));
        Assert.That(message.NicknameColor, Is.EqualTo(color));
        Assert.That(message.MessageColor, Is.EqualTo(color));
    }

    [Test]
    public void SendLocal_PreservesBotPositionAndText()
    {
        _responder.SendLocal(new SendLocalChatMessagePacket("nearby"), 456, 12, 34);

        var payload = (LocalChatMessagePacket)_sent[0].Payload;
        Assert.That(payload.BotId, Is.EqualTo(456));
        Assert.That(payload.FallbackX, Is.EqualTo(12));
        Assert.That(payload.FallbackY, Is.EqualTo(34));
        Assert.That(payload.Text, Is.EqualTo("nearby"));
    }
}

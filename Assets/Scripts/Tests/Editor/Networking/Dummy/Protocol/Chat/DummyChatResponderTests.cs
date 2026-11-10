#nullable enable

using System.Collections.Generic;
using Kern.Core;
using MinesServer.Networking.Client.Packets.Chat;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.World;
using NUnit.Framework;

using Kern.Networking.Dummy;

namespace Kern.Tests.Networking;

public sealed class DummyChatResponderTests
{
    private List<ServerPacket> _sent = null!;
    private DummyChatResponder _responder = null!;

    [SetUp]
    public void SetUp()
    {
        _sent = new();
        _responder = new DummyChatResponder(_sent.Add, new VirtualDummyClock(seed: 1));
    }

    [Test]
    public void SendHistory_PreservesRequestedTagAndReturnsSeedHistory()
    {
        _responder.SendHistory(new QueryChatHistoryPacket(ChatProtocolLimits.GlobalChannelTag));

        Assert.That(_sent, Has.Count.EqualTo(1));
        var payload = (ChatMessageListPacket)_sent[0].Payload;
        Assert.That(payload.Tag, Is.EqualTo(ChatProtocolLimits.GlobalChannelTag));
        Assert.That(payload.Messages, Has.Length.EqualTo(10));
    }

    [Test]
    public void SendGlobal_UsesSelectedColorForPlayerNameAndText()
    {
        var color = System.Drawing.Color.FromArgb(255, 10, 20, 30);
        _responder.ChangeColor(new ChangeChatColorPacket(color));

        _responder.SendGlobal(new SendChatMessagePacket(
            ChatProtocolLimits.GlobalChannelTag,
            "hello"));

        var payload = (ChatMessageListPacket)_sent[0].Payload;
        ChatMessagePacket message = payload.Messages[0];
        Assert.That(message.Message, Is.EqualTo("hello"));
        Assert.That(message.NicknameColor, Is.EqualTo(color));
        Assert.That(message.MessageColor, Is.EqualTo(color));
    }

    // Заглушка отвечает тегом из контракта, а не присланным в пакете: клиент
    // отбрасывает ChatMessageListPacket с незнакомым Tag, и ответ под другим
    // тегом оставил бы окно глобального чата пустым.
    [Test]
    public void SendGlobal_StampsContractChannelTagRegardlessOfPacketTag()
    {
        _responder.SendGlobal(new SendChatMessagePacket("some-other-channel", "hello"));

        var payload = (ChatMessageListPacket)_sent[0].Payload;
        Assert.That(payload.Tag, Is.EqualTo(ChatProtocolLimits.GlobalChannelTag));
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

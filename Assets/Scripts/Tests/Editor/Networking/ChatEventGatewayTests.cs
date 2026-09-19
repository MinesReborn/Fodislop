#nullable enable

using Kern.Networking;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.World;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.Networking;

[TestFixture]
public class ChatEventGatewayTests
{
    [Test]
    public void PublishMessage_RaisesMessageReceived()
    {
        var gateway = new ChatEventGateway();
        ChatMessagePacket? received = null;
        gateway.MessageReceived += packet => received = packet;

        var packet = new ChatMessagePacket(
            Id: 1,
            Timestamp: 123,
            PlayerId: 1,
            ClanId: 0,
            NicknameColor: System.Drawing.Color.Yellow,
            PlayerName: "tester",
            MessageColor: System.Drawing.Color.White,
            Message: "hello");
        gateway.Publish(packet);

        Assert.AreEqual(packet, received);
    }

    [Test]
    public void PublishMute_RaisesMuteReceived()
    {
        var gateway = new ChatEventGateway();
        ChatMutePacket? received = null;
        gateway.MuteReceived += packet => received = packet;

        var packet = new ChatMutePacket(1000, 1030, "flood", 2, "mod");
        gateway.Publish(packet);

        Assert.AreEqual(packet, received);
    }

    [Test]
    public void PublishLocalMessage_RaisesLocalMessageReceived()
    {
        var gateway = new ChatEventGateway();
        LocalChatMessagePacket? received = null;
        gateway.LocalMessageReceived += packet => received = packet;

        var packet = new LocalChatMessagePacket(0, 10, 20, "local hello");
        gateway.Publish(packet);

        Assert.AreEqual(packet, received);
    }

    [Test]
    public void PublishWithoutSubscribers_DoesNotThrow()
    {
        var gateway = new ChatEventGateway();

        Assert.DoesNotThrow(() => gateway.Publish(new ChatMessagePacket(
            1, 123, 1, 0, System.Drawing.Color.White, "tester", System.Drawing.Color.White, "m")));
    }

    [Test]
    public void Unsubscribe_PreventsOldListenerFromReceivingMessages()
    {
        var gateway = new ChatEventGateway();
        int calls = 0;
        void Handler(ChatMessagePacket _) => calls++;
        gateway.MessageReceived += Handler;
        gateway.MessageReceived -= Handler;

        gateway.Publish(new ChatMessagePacket(
            1, 123, 1, 0, System.Drawing.Color.White, "tester", System.Drawing.Color.White, "m"));

        Assert.AreEqual(0, calls);
    }
}

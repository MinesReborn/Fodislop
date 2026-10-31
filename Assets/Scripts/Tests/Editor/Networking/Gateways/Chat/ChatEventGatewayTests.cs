#nullable enable

using Kern.Networking;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.World;
using NUnit.Framework;
using UnityEngine;

using Kern.Networking.Gateways;

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
    public void PublishChatList_CachesAndRaisesChatListReceived()
    {
        var gateway = new ChatEventGateway();
        ChatListPacket? received = null;
        gateway.ChatListReceived += packet => received = packet;
        var message = new ChatMessagePacket(
            1,
            123,
            1,
            0,
            System.Drawing.Color.White,
            "tester",
            System.Drawing.Color.White,
            "hello");
        var packet = new ChatListPacket(new[] { ("global", "Global", message) });

        gateway.Publish(packet);

        Assert.AreEqual(packet, received);
        Assert.AreEqual(packet, gateway.LastChatList);
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

    // ─── E2-C: счётчик непрочитанных ───

    private static ChatMessageListPacket MakeMessages(string tag, int count, long firstId = 1)
    {
        var messages = new ChatMessagePacket[count];
        for (int i = 0; i < count; i++)
        {
            messages[i] = new ChatMessagePacket(
                firstId + i,
                123,
                1,
                0,
                System.Drawing.Color.White,
                "tester",
                System.Drawing.Color.White,
                "m");
        }

        return new ChatMessageListPacket(tag, messages);
    }

    [Test]
    public void PublishMessages_WithoutFocus_CountsUnreadPerTag()
    {
        var gateway = new ChatEventGateway();

        gateway.Publish(MakeMessages("FED", 3));
        gateway.Publish(MakeMessages("TRADE", 2));

        Assert.AreEqual(3, gateway.GetUnread("FED"));
        Assert.AreEqual(2, gateway.GetUnread("TRADE"));
        Assert.AreEqual(5, gateway.GetTotalUnread());
    }

    [Test]
    public void PublishMessages_ForFocusedTag_DoesNotCountUnread()
    {
        var gateway = new ChatEventGateway();
        gateway.SetFocusedTag("FED");

        gateway.Publish(MakeMessages("FED", 4));

        Assert.AreEqual(0, gateway.GetUnread("FED"));
        Assert.AreEqual(0, gateway.GetTotalUnread());
    }

    [Test]
    public void SetFocusedTag_ResetsUnreadOfFocusedTag()
    {
        var gateway = new ChatEventGateway();
        gateway.Publish(MakeMessages("FED", 3));
        gateway.Publish(MakeMessages("TRADE", 2));

        gateway.SetFocusedTag("FED");

        Assert.AreEqual(0, gateway.GetUnread("FED"));
        Assert.AreEqual(2, gateway.GetUnread("TRADE"));
        Assert.AreEqual(2, gateway.GetTotalUnread());
    }

    [Test]
    public void HideChat_ClearsFocus_NextMessagesCountAgain()
    {
        var gateway = new ChatEventGateway();
        gateway.SetFocusedTag("FED");
        gateway.Publish(MakeMessages("FED", 2));

        gateway.SetFocusedTag(null);
        gateway.Publish(MakeMessages("FED", 2));

        Assert.AreEqual(2, gateway.GetUnread("FED"));
    }

    [Test]
    public void UnreadChanged_RaisesOnlyOnActualChange()
    {
        var gateway = new ChatEventGateway();
        int notifications = 0;
        gateway.UnreadChanged += () => notifications++;

        gateway.Publish(MakeMessages("FED", 3));
        Assert.AreEqual(1, notifications);

        gateway.SetFocusedTag("FED");
        Assert.AreEqual(2, notifications);

        gateway.SetFocusedTag("FED");
        Assert.AreEqual(2, notifications);

        // Фокусный канал не копит непрочитанных — событие не срабатывает.
        gateway.Publish(MakeMessages("FED", 2));
        Assert.AreEqual(2, notifications);
    }

    [Test]
    public void PublishMessages_EmptyBatch_DoesNotRaiseUnreadChanged()
    {
        var gateway = new ChatEventGateway();
        int notifications = 0;
        gateway.UnreadChanged += () => notifications++;

        gateway.Publish(MakeMessages("FED", 0));

        Assert.AreEqual(0, notifications);
        Assert.AreEqual(0, gateway.GetTotalUnread());
    }
}

#nullable enable

using System;
using System.Drawing;
using Kern.Networking;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.World;
using NUnit.Framework;

namespace Kern.Tests.Networking;

[TestFixture]
public class ChatEventGatewayFuzzTests
{
    [Test]
    public void Publish_NoSubscribers_DoesNotThrow()
    {
        var random = new System.Random(42);
        var gw = new ChatEventGateway();
        for (int i = 0; i < 50; i++)
        {
            Assert.DoesNotThrow(() => gw.Publish(MakeMessage(random)), $"i={i}");
            Assert.DoesNotThrow(() => gw.Publish(MakeMute(random)), $"i={i}");
            Assert.DoesNotThrow(() => gw.Publish(MakeLocal(random)), $"i={i}");
        }
    }

    [Test]
    public void Publish_Subscribers_ReceiveExactInstance()
    {
        var random = new System.Random(42);
        var gw = new ChatEventGateway();
        for (int i = 0; i < 50; i++)
        {
            ChatMessagePacket? received = null;
            gw.MessageReceived += p => received = p;
            var msg = MakeMessage(random);
            gw.Publish(msg);
            Assert.That(Equals(received, msg), $"i={i}");
        }
    }

    [Test]
    public void Publish_MultipleSubscribers_AllReceive()
    {
        var random = new System.Random(42);
        var gw = new ChatEventGateway();
        for (int i = 0; i < 50; i++)
        {
            ChatMessagePacket? r1 = null, r2 = null, r3 = null;
            gw.MessageReceived += p => r1 = p;
            gw.MessageReceived += p => r2 = p;
            gw.MessageReceived += p => r3 = p;
            var msg = MakeMessage(random);
            gw.Publish(msg);
            Assert.That(Equals(r1, msg), $"i={i}");
            Assert.That(Equals(r2, msg), $"i={i}");
            Assert.That(Equals(r3, msg), $"i={i}");
        }
    }

    [Test]
    public void Unsubscribe_StopsDelivery()
    {
        var random = new System.Random(42);
        var gw = new ChatEventGateway();
        for (int i = 0; i < 50; i++)
        {
            ChatMessagePacket? r1 = null, r2 = null;
            Action<ChatMessagePacket> handler = p => r2 = p;
            gw.MessageReceived += p => r1 = p;
            gw.MessageReceived += handler;
            gw.Publish(MakeMessage(random));
            Assert.That(r1, Is.Not.Null);
            Assert.That(r2, Is.Not.Null);
            gw.MessageReceived -= handler;
            var msg2 = MakeMessage(random);
            gw.Publish(msg2);
            Assert.That(Equals(r1, msg2), $"i={i}");
        }
    }

    [Test]
    public void LocalMessage_DoesNotTriggerGlobalHandler()
    {
        var random = new System.Random(42);
        var gw = new ChatEventGateway();
        for (int i = 0; i < 50; i++)
        {
            ChatMessagePacket? global = null;
            LocalChatMessagePacket? local = null;
            gw.MessageReceived += p => global = p;
            gw.LocalMessageReceived += p => local = p;
            var l = MakeLocal(random);
            gw.Publish(l);
            Assert.That(global, Is.Null, $"i={i}");
            Assert.That(Equals(local, l), $"i={i}");
        }
    }

    private static ChatMessagePacket MakeMessage(System.Random r)
    {
        return new ChatMessagePacket(
            NextLong(r), NextLong(r), r.Next(0, 1000), (byte)r.Next(0, 10),
            Color.FromArgb(r.Next(0, 256), r.Next(0, 256), r.Next(0, 256)),
            Str(r, r.Next(1, 16)),
            Color.FromArgb(r.Next(0, 256), r.Next(0, 256), r.Next(0, 256)),
            Str(r, r.Next(1, 32)));
    }

    private static ChatMutePacket MakeMute(System.Random r)
    {
        return new ChatMutePacket(NextLong(r), NextLong(r), Str(r, r.Next(0, 32)), r.Next(0, 1000), Str(r, r.Next(1, 16)));
    }

    private static LocalChatMessagePacket MakeLocal(System.Random r)
    {
        return new LocalChatMessagePacket(
            (uint)r.Next(0, 1000),
            (ushort)r.Next(0, 1000),
            (ushort)r.Next(0, 1000),
            Str(r, r.Next(1, 32)));
    }

    private static long NextLong(System.Random r) =>
        (long)r.Next(int.MinValue, int.MaxValue) << 32 | (uint)r.Next(int.MinValue, int.MaxValue);

    private static string Str(System.Random r, int len)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        var s = new char[len];
        for (int i = 0; i < len; i++) s[i] = chars[r.Next(chars.Length)];
        return new string(s);
    }
}

#nullable enable

using System;
using System.Linq;
using MinesServer.Networking.Client.Packets.Chat;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.World;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyChatResponder(Action<ServerPacket> sendPacket)
{
    private readonly Action<ServerPacket> _sendPacket = sendPacket ??
        throw new ArgumentNullException(nameof(sendPacket));
    private readonly ChatMessagePacket[] _seedMessages = CreateSeedMessages();
    private System.Drawing.Color _chatColor =
        System.Drawing.Color.FromArgb(255, 200, 180, 100);

    public void ChangeColor(ChangeChatColorPacket packet)
    {
        _chatColor = packet.Color;
    }

    public void SendHistory(QueryChatHistoryPacket packet)
    {
        long startFrom = (long)packet.StartFrom;
        ChatMessagePacket[] filtered = _seedMessages
            .Where(message => startFrom == 0 || message.Timestamp >= startFrom)
            .ToArray();
        _sendPacket(new ServerPacket(new ChatMessageListPacket(packet.Tag, filtered)));
    }

    public void SendLocal(
        SendLocalChatMessagePacket packet,
        ushort botId,
        ushort x,
        ushort y)
    {
        _sendPacket(new ServerPacket(new LocalChatMessagePacket(botId, x, y, packet.Message)));
    }

    public void SendGlobal(SendChatMessagePacket packet)
    {
        var message = new ChatMessagePacket(
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            999,
            1,
            _chatColor,
            "You",
            _chatColor,
            packet.Message);
        _sendPacket(new ServerPacket(new ChatMessageListPacket("global", new[] { message })));
    }

    private static ChatMessagePacket[] CreateSeedMessages()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var gray = System.Drawing.Color.FromArgb(255, 120, 120, 120);
        var green = System.Drawing.Color.FromArgb(255, 80, 220, 80);
        var blue = System.Drawing.Color.FromArgb(255, 80, 140, 255);
        var red = System.Drawing.Color.FromArgb(255, 255, 80, 80);
        var orange = System.Drawing.Color.FromArgb(255, 255, 180, 60);
        var cyan = System.Drawing.Color.FromArgb(255, 60, 255, 255);
        var magenta = System.Drawing.Color.FromArgb(255, 220, 60, 220);
        var yellow = System.Drawing.Color.FromArgb(255, 255, 220, 60);
        var white = System.Drawing.Color.White;

        return
        [
            new ChatMessagePacket(1, now - 300000, 0, 0, gray, "System", gray, "Добро пожаловать на Fodinae!"),
            new ChatMessagePacket(2, now - 270000, 1, 1, green, "Miner77", white, "привет всем!"),
            new ChatMessagePacket(3, now - 240000, 2, 0, blue, "DeepDrill", white, "кто на сервере?"),
            new ChatMessagePacket(4, now - 210000, 3, 2, red, "CrystalMage", white, "иду копать алмазы"),
            new ChatMessagePacket(5, now - 180000, 4, 0, orange, "RockBreaker", white, "нужна помощь с мобом"),
            new ChatMessagePacket(6, now - 150000, 5, 1, cyan, "OreTrader", white, "продам редкий блок"),
            new ChatMessagePacket(7, now - 120000, 6, 0, magenta, "NightMiner", white, "всем удачной шахты!"),
            new ChatMessagePacket(8, now - 90000, 1, 1, green, "Miner77", white, "кто-нибудь на базе?"),
            new ChatMessagePacket(9, now - 60000, 7, 0, yellow, "Newbie42", white, "я только зашел"),
            new ChatMessagePacket(10, now - 30000, 3, 2, red, "CrystalMage", white, "сервер лагает?"),
        ];
    }
}

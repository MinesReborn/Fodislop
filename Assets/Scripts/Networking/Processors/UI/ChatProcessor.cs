#nullable enable

using System;
using Kern.Networking;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.World;

namespace Kern.Networking.Processors;

public sealed class ChatProcessor(ChatEventGateway events) :
    IPacketProcessor<ChatMessageListPacket>,
    IPacketProcessor<LocalChatMessagePacket>,
    IPacketProcessor<ChatMutePacket>,
    IPacketProcessor<ChatListPacket>
{
    public void Process(ChatMessageListPacket packet)
        => events.Publish(packet);

    public void Process(LocalChatMessagePacket packet) => events.Publish(packet);

    public void Process(ChatMutePacket packet) => events.Publish(packet);

    public void Process(ChatListPacket packet) => events.Publish(packet);
}

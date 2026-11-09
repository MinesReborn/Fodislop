#nullable enable

using System;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.Utilities;

using Kern.Networking.Gateways;

namespace Kern.Networking.Processing;

public sealed class WindowPacketProcessor :
    IPacketProcessor<OpenWindowPacket>,
    IPacketProcessor<CloseWindowPacket>,
    IPacketProcessor<ModalWindowPacket>,
    IPacketProcessor<OpenURLPacket>
{
    private readonly WindowCommandStream _commands;
    public WindowPacketProcessor(WindowCommandStream commands)
    {
        _commands = commands;
    }

    public void Process(OpenWindowPacket packet) => _commands.PublishOpenWindow(packet);

    public void Process(CloseWindowPacket packet) => _commands.PublishCloseWindow(packet);

    public void Process(ModalWindowPacket packet) => _commands.PublishModalWindow(packet);

    public void Process(OpenURLPacket packet) => _commands.PublishOpenLink(packet.URL);
}

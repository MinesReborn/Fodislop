#nullable enable

using System;
using MinesServer.Networking.Server.Packets.GUI;

namespace Fodinae.Networking;

public sealed class WindowCommandStream
{
    public event Action<OpenWindowPacket>? OpenRequested;

    public event Action<CloseWindowPacket>? CloseRequested;

    public event Action<ModalWindowPacket>? ModalRequested;

    public event Action<bool>? OpenWindowVisibilityChanged;

    public bool HasOpenWindows { get; private set; }

    public void PublishOpen(OpenWindowPacket packet) => OpenRequested?.Invoke(packet);

    public void PublishClose(CloseWindowPacket packet) => CloseRequested?.Invoke(packet);

    public void PublishModal(ModalWindowPacket packet) => ModalRequested?.Invoke(packet);

    public void SetOpenWindowVisibility(bool visible)
    {
        if (HasOpenWindows == visible)
        {
            return;
        }

        HasOpenWindows = visible;
        OpenWindowVisibilityChanged?.Invoke(visible);
    }
}

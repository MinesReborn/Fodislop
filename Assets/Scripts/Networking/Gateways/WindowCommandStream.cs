#nullable enable

using System;
using MinesServer.Networking.Server.Packets.GUI;

namespace Kern.Networking;

public sealed class WindowCommandStream
{
    public event Action<OpenWindowPacket>? OpenRequested;

    public event Action<CloseWindowPacket>? CloseRequested;

    public event Action<ModalWindowPacket>? ModalRequested;

    public event Action<bool>? OpenWindowVisibilityChanged;

    public bool HasOpenWindows { get; private set; }

    public void PublishOpenWindow(OpenWindowPacket packet) => OpenRequested?.Invoke(packet);

    public void PublishCloseWindow(CloseWindowPacket packet) => CloseRequested?.Invoke(packet);

    public void PublishModalWindow(ModalWindowPacket packet) => ModalRequested?.Invoke(packet);

    public void SetServerWindowVisibility(bool visible)
    {
        if (HasOpenWindows == visible)
        {
            return;
        }

        HasOpenWindows = visible;
        OpenWindowVisibilityChanged?.Invoke(visible);
    }
}

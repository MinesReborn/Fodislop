#nullable enable

using System;
using MinesServer.Networking.Server.Packets.GUI;

namespace Kern.Networking.Gateways;

public sealed class WindowCommandStream
{
    public event Action<OpenWindowPacket>? OpenRequested;

    public event Action<CloseWindowPacket>? CloseRequested;

    public event Action<ModalWindowPacket>? ModalRequested;

    public event Action<bool>? OpenWindowVisibilityChanged;

    // Сервер просит открыть ссылку (OpenURLPacket): окно спрашивает игрока и только тогда открывает браузер
    public event Action<string>? LinkRequested;

    public bool HasOpenWindows { get; private set; }

    public void PublishOpenWindow(OpenWindowPacket packet)
    {
        Action<OpenWindowPacket>? handler = OpenRequested;
        if (handler == null)
        {
            string message = $"[PacketUI] OpenWindowPacket '{packet.WindowTag}' arrived " +
                "without a subscribed window presenter.";
            throw new InvalidOperationException(message);
        }

        handler(packet);
    }

    public void PublishCloseWindow(CloseWindowPacket packet)
    {
        Action<CloseWindowPacket> handler = CloseRequested ??
            throw new InvalidOperationException("[PacketUI] CloseWindowPacket arrived without a subscribed window presenter.");
        handler(packet);
    }

    public void PublishModalWindow(ModalWindowPacket packet)
    {
        Action<ModalWindowPacket> handler = ModalRequested ??
            throw new InvalidOperationException("[PacketUI] ModalWindowPacket arrived without a subscribed window presenter.");
        handler(packet);
    }

    public void PublishOpenLink(string url)
    {
        Action<string>? handler = LinkRequested;
        if (handler == null)
        {
            throw new InvalidOperationException(
                "[PacketUI] OpenURLPacket arrived without a subscribed link presenter.");
        }

        handler(url);
    }

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

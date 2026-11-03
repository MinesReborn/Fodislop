#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using UnityEngine;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;

namespace Kern.UI.ServerWindows;

internal static class ServerWindowPacketValidator
{
    public static void Validate(OpenWindowPacket packet)
    {
        if (packet.Content == null || packet.Width == 0 || packet.Height == 0)
        {
            Fail(packet, "missing content or zero window dimensions");
        }

        string? format = AttachedProperties.Find(packet.Content, "Popup.Format");
        if (format != null)
        {
            Fail(packet,
                $"unsupported string-based Popup.Format='{format}'; expected GUI components");
        }

        if (packet.Content is ScrollViewerPacket scroll)
        {
            foreach (IGUIComponentPacket child in scroll.Children)
            {
                if (child is TextPacket text &&
                    (text.Text.Contains("\nbutton\n", StringComparison.Ordinal) ||
                     text.Text.Contains("\n3card\n", StringComparison.Ordinal) ||
                     text.Text.Contains("\ndrop\n", StringComparison.Ordinal) ||
                     text.Text.Contains("=I#", StringComparison.Ordinal)))
                {
                    Fail(packet,
                        "a TextPacket contains serialized controls; expected clickable GUI components");
                }
            }
        }
    }

    [DoesNotReturn]
    private static void Fail(OpenWindowPacket packet, string reason)
    {
        string message = $"[PacketUI] Invalid OpenWindowPacket tag='{packet.WindowTag}', " +
            $"size={packet.Width}x{packet.Height}, root={packet.Content?.GetType().Name ?? "null"}: {reason}.";
        Debug.LogError(message);
        throw new InvalidOperationException(message);
    }

}

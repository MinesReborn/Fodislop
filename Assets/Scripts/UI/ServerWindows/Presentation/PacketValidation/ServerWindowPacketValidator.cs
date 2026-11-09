#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using MinesServer.Networking.Server.Packets.GUI;

namespace Kern.UI.ServerWindows;

internal static class ServerWindowPacketValidator
{
    public static void Validate(OpenWindowPacket packet)
    {
        if (packet.Content == null || packet.Width == 0 || packet.Height == 0)
        {
            Fail(packet, "missing content or zero window dimensions");
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

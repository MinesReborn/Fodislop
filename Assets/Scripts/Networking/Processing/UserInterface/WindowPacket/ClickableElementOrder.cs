#nullable enable

using System;
using System.Collections.Generic;
using MinesServer.Networking.Server.Packets.GUI.Components;

namespace Kern.Networking;

/// <summary>
/// Defines the preorder index of clickable components in a server window.
/// A component is clickable when it has a non-empty OnClickContext.
/// </summary>
public static class ClickableElementOrder
{
    public static IReadOnlyList<IGUIComponentPacket> Collect(IGUIComponentPacket root)
    {
        if (root is null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        var result = new List<IGUIComponentPacket>();
        Visit(root, result.Add);
        return result;
    }

    public static int IndexOf(IGUIComponentPacket root, IGUIComponentPacket target)
    {
        if (root is null)
        {
            throw new ArgumentNullException(nameof(root));
        }
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        int index = 0;
        int result = -1;
        Visit(root, packet =>
        {
            if (result >= 0)
            {
                return;
            }

            if (ReferenceEquals(packet, target))
            {
                result = index;
                return;
            }

            index++;
        });
        return result;
    }

    private static void Visit(IGUIComponentPacket packet, Action<IGUIComponentPacket> visit)
    {
        if (!string.IsNullOrEmpty(packet.OnClickContext))
        {
            visit(packet);
        }

        if (packet is not IContainerComponentPacket container)
        {
            return;
        }

        foreach (IGUIComponentPacket child in container.Children)
        {
            Visit(child, visit);
        }
    }
}

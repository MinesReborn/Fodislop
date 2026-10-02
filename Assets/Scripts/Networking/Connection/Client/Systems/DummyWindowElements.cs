#nullable enable

using MinesServer.Networking.Server.Packets.GUI.Components;

namespace MinesServer.Networking.Connection.Client;

// ElementClickPacket.ElementIndex is the clicked component's pre-order index
// in the window tree (root 0, then each container before its children), the
// order the client collects it in. The dummy server resolves clicks by the
// packets it built instead of counting positions by hand.
internal static class DummyWindowElements
{
    public static int IndexOf(IGUIComponentPacket root, IGUIComponentPacket target)
    {
        int index = 0;
        return Find(root, target, ref index) ? index : -1;
    }

    private static bool Find(IGUIComponentPacket packet, IGUIComponentPacket target, ref int index)
    {
        if (ReferenceEquals(packet, target))
        {
            return true;
        }

        index++;
        if (packet is IContainerComponentPacket container)
        {
            foreach (IGUIComponentPacket child in container.Children)
            {
                if (Find(child, target, ref index))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

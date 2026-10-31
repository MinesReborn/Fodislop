#nullable enable

using MinesServer.Networking.Server.Packets.GUI.Components;

namespace Kern.Networking.Offline;

// ElementClickPacket.ElementIndex is the component's pre-order index among
// clickable packets only, matching ServerWindowPresenter. The dummy server
// resolves clicks by the packets it built instead of counting positions by hand.
internal static class DummyWindowElements
{
    public static int IndexOf(IGUIComponentPacket root, IGUIComponentPacket target)
    {
        int index = 0;
        return Find(root, target, ref index) ? index : -1;
    }

    private static bool Find(IGUIComponentPacket packet, IGUIComponentPacket target, ref int index)
    {
        if (!string.IsNullOrEmpty(packet.OnClickContext))
        {
            if (ReferenceEquals(packet, target))
            {
                return true;
            }

            index++;
        }

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

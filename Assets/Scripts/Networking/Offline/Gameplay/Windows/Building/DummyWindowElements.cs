#nullable enable

using MinesServer.Networking.Server.Packets.GUI.Components;

namespace Kern.Networking.Offline;

// The dummy server resolves clicks with the same preorder index contract as
// the client, using the packet tree it built.
internal static class DummyWindowElements
{
    public static int IndexOf(IGUIComponentPacket root, IGUIComponentPacket target)
    {
        return ClickableElementOrder.IndexOf(root, target);
    }
}

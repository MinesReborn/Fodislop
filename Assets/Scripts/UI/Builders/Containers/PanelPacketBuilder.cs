#nullable enable

using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using UnityEngine.UIElements;

namespace Kern.UI.Builders;
public class PanelPacketBuilder : PacketUIBuilderBase<PanelPacket>
{
    protected override VisualElement BuildTyped(PanelPacket packet, PacketUIBuilder builder)
    {
        var element = new VisualElement();
        element.AddToClassList("sci-fi-panel");

        return element;
    }
}

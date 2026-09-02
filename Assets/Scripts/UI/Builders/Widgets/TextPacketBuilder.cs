#nullable enable

using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using UnityEngine.UIElements;

namespace Fodinae.UI.Builders
{
    public class TextPacketBuilder : PacketUIBuilderBase<TextPacket>
    {
        protected override VisualElement BuildTyped(TextPacket packet, PacketUIBuilder builder)
        {
            var label = new Label(packet.Text);
            label.AddToClassList("sci-fi-text-body");
            label.AddToClassList("fit-wrap");
            if (!string.IsNullOrEmpty(packet.OnClickContext))
            {
                label.pickingMode = PickingMode.Position;
            }

            return label;
        }
    }
}

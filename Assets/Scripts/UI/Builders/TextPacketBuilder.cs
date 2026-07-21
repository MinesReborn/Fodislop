using Fodinae.Scripts;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI.Builders
{
    public class TextPacketBuilder : PacketUIBuilderBase
    {
        public override VisualElement Build(IGUIComponentPacket packet, PacketUIBuilder builder)
        {
            if (packet is not TextPacket textPkt)
            {
                return null;
            }

            var label = new Label(textPkt.Text);
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }
    }
}

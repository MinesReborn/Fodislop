using Fodinae.Scripts;

using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI.Builders
{
    public class PanelPacketBuilder : PacketUIBuilderBase
    {
        public override VisualElement Build(IGUIComponentPacket packet, PacketUIBuilder builder)
        {
            if (packet is not PanelPacket panelPacket)
            {
                return null;
            }

            var element = new VisualElement();

            return element;
        }
    }
}

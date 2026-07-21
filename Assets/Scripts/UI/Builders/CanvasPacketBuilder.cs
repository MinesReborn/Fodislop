using Fodinae.Scripts;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI.Builders
{
    public class CanvasPacketBuilder : PacketUIBuilderBase
    {
        public override VisualElement Build(IGUIComponentPacket packet, PacketUIBuilder builder)
        {
            if (packet is not CanvasPacket canvasPacket)
            {
                return null;
            }

            var element = new VisualElement
            {
                style =
                {
                    position = Position.Relative,
                },
            };

            foreach (var childPacket in canvasPacket.Children)
            {
                var childElement = builder.Build(childPacket);
                element.Add(childElement);
            }

            return element;
        }
    }
}

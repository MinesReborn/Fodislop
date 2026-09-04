#nullable enable

using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using UnityEngine.UIElements;

namespace Fodinae.UI.Builders
{
    public class CanvasPacketBuilder : PacketUIBuilderBase<CanvasPacket>
    {
        protected override VisualElement BuildTyped(CanvasPacket packet, PacketUIBuilder builder)
        {
            var element = new VisualElement();
            element.AddToClassList("rel");
            builder.AddChildren(element, packet);
            return element;
        }
    }
}

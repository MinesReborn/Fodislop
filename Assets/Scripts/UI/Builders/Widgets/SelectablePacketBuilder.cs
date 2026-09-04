#nullable enable

using Fodinae.UI.Controls;
using MinesServer.Networking.Server.Packets.GUI.Components.Input;
using UnityEngine.UIElements;

namespace Fodinae.UI.Builders
{
    public class SelectablePacketBuilder : PacketUIBuilderBase<SelectablePacket>
    {
        protected override VisualElement BuildTyped(SelectablePacket packet, PacketUIBuilder builder)
        {
            var selectable = new Selectable
            {
                Group = packet.Name,
                value = packet.DefaultValue,
            };

            selectable.SetVisuals(builder.Build(packet.Checked), builder.Build(packet.Unchecked));
            selectable.SetEnabled(packet.IsEnabled);

            return selectable;
        }
    }
}

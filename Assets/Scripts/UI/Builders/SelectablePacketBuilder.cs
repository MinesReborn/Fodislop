using Fodinae.Scripts;
using Fodinae.UI.Controls;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Input;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI.Builders
{
    public class SelectablePacketBuilder : PacketUIBuilderBase
    {
        public override VisualElement Build(IGUIComponentPacket packet, PacketUIBuilder builder)
        {
            if (packet is not SelectablePacket selectablePacket)
            {
                return null;
            }

            var checkedVisual = builder.Build(selectablePacket.Checked);
            var uncheckedVisual = builder.Build(selectablePacket.Unchecked);

            var selectable = new Selectable
            {
                Group = selectablePacket.Name,
                value = selectablePacket.DefaultValue,
            };

            selectable.SetVisuals(checkedVisual, uncheckedVisual);
            selectable.SetEnabled(selectablePacket.IsEnabled);

            return selectable;
        }
    }
}

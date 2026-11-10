#nullable enable

using Kern.UI.Controls;
using MinesServer.Networking.Server.Packets.GUI.Components.Input;
using UnityEngine.UIElements;

namespace Kern.UI.ServerWindows;
public class SelectablePacketBuilder : PacketUIBuilderBase<SelectablePacket>
{
    protected override VisualElement BuildTyped(SelectablePacket packet, PacketUIBuilder builder)
    {
        var selectable = new Selectable
        {
            name = packet.Name,
            value = packet.DefaultValue,
        };

        selectable.SetVisuals(builder.Build(packet.Checked), builder.Build(packet.Unchecked));
        selectable.SetEnabled(packet.IsEnabled);

        return selectable;
    }
}

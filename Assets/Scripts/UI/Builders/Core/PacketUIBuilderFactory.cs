#nullable enable

using System;
using System.Collections.Generic;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using MinesServer.Networking.Server.Packets.GUI.Components.Input;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;

namespace Kern.UI.Builders;
public class PacketUIBuilderFactory
{
    private static readonly IReadOnlyDictionary<Type, PacketUIBuilderBase> _Builders =
        new Dictionary<Type, PacketUIBuilderBase>
        {
            [typeof(TextPacket)] = new TextPacketBuilder(),
            [typeof(ImagePacket)] = new ImagePacketBuilder(),
            [typeof(PanelPacket)] = new PanelPacketBuilder(),
            [typeof(LinePacket)] = new LinePacketBuilder(),
            [typeof(DockPanelPacket)] = new DockPanelPacketBuilder(),
            [typeof(CanvasPacket)] = new CanvasPacketBuilder(),
            [typeof(GridPacket)] = new GridPacketBuilder(),
            [typeof(ScrollViewerPacket)] = new ScrollViewerPacketBuilder(),
            [typeof(TextBoxPacket)] = new TextBoxPacketBuilder(),
            [typeof(SelectablePacket)] = new SelectablePacketBuilder(),
            [typeof(SliderPacket)] = new SliderPacketBuilder(),
            [typeof(IntDropdownPacket)] = new IntDropdownPacketBuilder(),
            [typeof(StringDropdownPacket)] = new StringDropdownPacketBuilder(),
        };

    public PacketUIBuilderBase? CreateBuilder(IGUIComponentPacket packet)
    {
        return _Builders.TryGetValue(packet.GetType(), out PacketUIBuilderBase? builder)
            ? builder
            : null;
    }
}

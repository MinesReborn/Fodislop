using System;
using System.Collections.Generic;
using Fodinae.Scripts;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using MinesServer.Networking.Server.Packets.GUI.Components.Input;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;

namespace Fodinae.Scripts.UI.Builders
{
    public class PacketUIBuilderFactory
    {
        private readonly Dictionary<Type, Func<PacketUIBuilderBase>> _builders = new();

        public PacketUIBuilderFactory()
        {
            _builders.Add(typeof(TextPacket), () => new TextPacketBuilder());
            _builders.Add(typeof(ImagePacket), () => new ImagePacketBuilder());
            _builders.Add(typeof(PanelPacket), () => new PanelPacketBuilder());
            _builders.Add(typeof(LinePacket), () => new LinePacketBuilder());
            _builders.Add(typeof(DockPanelPacket), () => new DockPanelPacketBuilder());
            _builders.Add(typeof(CanvasPacket), () => new CanvasPacketBuilder());
            _builders.Add(typeof(GridPacket), () => new GridPacketBuilder());
            _builders.Add(typeof(ScrollViewerPacket), () => new ScrollViewerPacketBuilder());
            _builders.Add(typeof(TextBoxPacket), () => new TextBoxPacketBuilder());
            _builders.Add(typeof(SelectablePacket), () => new SelectablePacketBuilder());
            _builders.Add(typeof(SliderPacket), () => new SliderPacketBuilder());
            _builders.Add(typeof(IntDropdownPacket), () => new IntDropdownPacketBuilder());
            _builders.Add(typeof(StringDropdownPacket), () => new StringDropdownPacketBuilder());
        }

        public PacketUIBuilderBase CreateBuilder(IGUIComponentPacket packet)
        {
            if (_builders.TryGetValue(packet.GetType(), out var builderFactory))
            {
                return builderFactory();
            }

            return null;
        }
    }
}

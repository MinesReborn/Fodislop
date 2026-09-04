#nullable enable

using System;
using System.Collections.Generic;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using MinesServer.Networking.Server.Packets.GUI.Components.Input;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;

namespace Fodinae.UI.Builders
{
    /// <summary>
    /// Выбор строителя по виду пакета.
    /// </summary>
    /// <remarks>
    /// Строители не имеют состояния, поэтому здесь лежат готовые экземпляры, а
    /// не фабричные лямбды: прежняя таблица создавала новый строитель на каждый
    /// узел каждого серверного окна — мусор ради ничего.
    /// </remarks>
    public class PacketUIBuilderFactory
    {
        private static readonly IReadOnlyDictionary<Type, PacketUIBuilderBase> Builders =
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
            return Builders.TryGetValue(packet.GetType(), out PacketUIBuilderBase? builder)
                ? builder
                : null;
        }
    }
}

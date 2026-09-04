#nullable enable

using MinesServer.Data;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using UnityEngine.UIElements;

namespace Fodinae.UI.Builders
{
    public class LinePacketBuilder : PacketUIBuilderBase<LinePacket>
    {
        protected override VisualElement BuildTyped(LinePacket packet, PacketUIBuilder builder)
        {
            var line = new UILine
            {
                Direction = packet.Direction,
            };

            if (packet.Style.HasValue)
            {
                line.LineColor = StyleApplicator.ConvertColor(packet.Style.Value.Background);
                if (packet.Style.Value.BorderWidth > 0)
                {
                    line.Thickness = packet.Style.Value.BorderWidth;
                }
            }

            // Длинная ось тянется на 100% — это константа и живёт в USS.
            // Инлайном остаётся только толщина: она пришла из пакета.
            switch (packet.Direction)
            {
                case LineDirection.Horizontal:
                    line.AddToClassList("packet-line--h");
                    line.style.height = line.Thickness;
                    break;
                case LineDirection.Vertical:
                    line.AddToClassList("packet-line--v");
                    line.style.width = line.Thickness;
                    break;
                default:
                    line.AddToClassList("packet-line--both");
                    break;
            }

            return line;
        }
    }
}

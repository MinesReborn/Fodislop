using Fodinae.Scripts.UI;
using Fodinae.Scripts.UI.Builders;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI.Builders
{
    public class LinePacketBuilder : PacketUIBuilderBase
    {
        public override VisualElement Build(IGUIComponentPacket packet, PacketUIBuilder builder)
        {
            if (packet is not LinePacket linePkt)
            {
                return null;
            }

            var line = new UILine
            {
                Direction = linePkt.Direction,
            };

            if (linePkt.Style.HasValue)
            {
                line.LineColor = StyleApplicator.ConvertColor(linePkt.Style.Value.Background);
                if (linePkt.Style.Value.BorderWidth > 0)
                {
                    line.Thickness = linePkt.Style.Value.BorderWidth;
                }
            }

            if (linePkt.Direction == LineDirection.Horizontal)
            {
                line.style.width = Length.Percent(100);
                line.style.height = line.Thickness;
            }
            else if (linePkt.Direction == LineDirection.Vertical)
            {
                line.style.height = Length.Percent(100);
                line.style.width = line.Thickness;
            }
            else
            {
                line.style.width = Length.Percent(100);
                line.style.height = Length.Percent(100);
            }

            return line;
        }
    }
}

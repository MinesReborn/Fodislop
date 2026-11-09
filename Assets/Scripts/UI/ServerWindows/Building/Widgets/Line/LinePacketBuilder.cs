#nullable enable

using MinesServer.Data;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using UnityEngine;
using UnityEngine.UIElements;

using Kern.UI.Controls;

namespace Kern.UI.ServerWindows;
public class LinePacketBuilder : PacketUIBuilderBase<LinePacket>
{
    protected override VisualElement BuildTyped(LinePacket packet, PacketUIBuilder builder)
    {
        // Цвет линии приходит стилем — заливкой фона (GUIStylePacket.Background),
        // как и толщина (BorderWidth): цвет задаёт сервер, клиент только
        // рендерит. Без стиля используется стандартный белый цвет.
        var line = new UILine
        {
            Direction = packet.Direction,
            LineColor = Color.white,
        };

        if (packet.Style is { } style)
        {
            if (style.BorderWidth > 0)
            {
                line.Thickness = style.BorderWidth;
            }

            line.LineColor = new Color32(style.Background.R, style.Background.G, style.Background.B, style.Background.A);
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

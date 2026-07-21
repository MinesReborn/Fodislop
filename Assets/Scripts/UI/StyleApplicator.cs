using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI.Builders
{
    public static class StyleApplicator
    {
        public static void ApplyStyles(VisualElement element, IGUIComponentPacket packet)
        {
            if (packet.Style is null)
            {
                return;
            }

            var s = packet.Style.Value;

            if (s.Background.A > 0)
            {
                element.style.backgroundColor = ConvertColor(s.Background);
            }

            element.style.borderTopColor = ConvertColor(s.Border);
            element.style.borderBottomColor = ConvertColor(s.Border);
            element.style.borderLeftColor = ConvertColor(s.Border);
            element.style.borderRightColor = ConvertColor(s.Border);

            element.style.borderTopWidth = s.BorderWidth;
            element.style.borderBottomWidth = s.BorderWidth;
            element.style.borderLeftWidth = s.BorderWidth;
            element.style.borderRightWidth = s.BorderWidth;

            element.style.marginTop = s.Margin.Top;
            element.style.marginBottom = s.Margin.Bottom;
            element.style.marginLeft = s.Margin.Left;
            element.style.marginRight = s.Margin.Right;

            element.style.paddingTop = s.Padding.Top;
            element.style.paddingBottom = s.Padding.Bottom;
            element.style.paddingLeft = s.Padding.Left;
            element.style.paddingRight = s.Padding.Right;
        }

        public static UnityEngine.Color ConvertColor(System.Drawing.Color c)
        {
            return new UnityEngine.Color(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
        }
    }
}

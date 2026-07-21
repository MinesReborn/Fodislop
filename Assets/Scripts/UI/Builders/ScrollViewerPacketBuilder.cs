using Fodinae.Scripts;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI.Builders
{
    public class ScrollViewerPacketBuilder : PacketUIBuilderBase
    {
        public override VisualElement Build(IGUIComponentPacket packet, PacketUIBuilder builder)
        {
            if (packet is not ScrollViewerPacket scrollPkt)
            {
                return null;
            }

            var scrollView = new ScrollView
            {
                horizontalScrollerVisibility = MapScrollVisibility(scrollPkt.HorizontalScrollBar),
                verticalScrollerVisibility = MapScrollVisibility(scrollPkt.VerticalScrollBar),
            };

            foreach (var childPacket in scrollPkt.Children)
            {
                var childElement = builder.Build(childPacket);
                scrollView.contentContainer.Add(childElement);
            }

            return scrollView;
        }

        private static ScrollerVisibility MapScrollVisibility(MinesServer.Networking.Server.Packets.GUI.ScrollbarVisibility v)
        {
            return v switch
            {
                MinesServer.Networking.Server.Packets.GUI.ScrollbarVisibility.Hidden => ScrollerVisibility.Hidden,
                MinesServer.Networking.Server.Packets.GUI.ScrollbarVisibility.Auto => ScrollerVisibility.Auto,
                _ => ScrollerVisibility.AlwaysVisible,
            };
        }
    }
}

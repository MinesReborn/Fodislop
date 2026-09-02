#nullable enable

using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using UnityEngine.UIElements;

namespace Fodinae.UI.Builders
{
    public class ScrollViewerPacketBuilder : PacketUIBuilderBase<ScrollViewerPacket>
    {
        protected override VisualElement BuildTyped(ScrollViewerPacket packet, PacketUIBuilder builder)
        {
            var scrollView = new ScrollView
            {
                horizontalScrollerVisibility = MapScrollVisibility(packet.HorizontalScrollBar),
                verticalScrollerVisibility = MapScrollVisibility(packet.VerticalScrollBar),
            };

            builder.AddChildren(scrollView.contentContainer, packet);
            return scrollView;
        }

        private static ScrollerVisibility MapScrollVisibility(ScrollbarVisibility visibility)
        {
            return visibility switch
            {
                ScrollbarVisibility.Hidden => ScrollerVisibility.Hidden,
                ScrollbarVisibility.Auto => ScrollerVisibility.Auto,
                _ => ScrollerVisibility.AlwaysVisible,
            };
        }
    }
}

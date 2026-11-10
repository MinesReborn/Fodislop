#nullable enable

using System;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using UnityEngine.UIElements;

namespace Kern.UI.ServerWindows;
public class ScrollViewerPacketBuilder : PacketUIBuilderBase<ScrollViewerPacket>
{
    protected override VisualElement BuildTyped(ScrollViewerPacket packet, PacketUIBuilder builder)
    {
        // Страница без прокрутки приходит с сервера другим корнем
        // (DockPanelPacket): скролл создаёт только ScrollViewerPacket —
        // клиент рендерит то, что прислано, и ничего не решает сам.
        var scrollView = new ScrollView(ScrollViewMode.VerticalAndHorizontal)
        {
            name = packet.Name,
            horizontalScrollerVisibility = MapScrollVisibility(packet.HorizontalScrollBar),
            verticalScrollerVisibility = MapScrollVisibility(packet.VerticalScrollBar),
        };
        scrollView.verticalScroller.AddToClassList("packet-scrollbar");
        scrollView.verticalScroller.AddToClassList("packet-scrollbar--vertical");
        scrollView.horizontalScroller.AddToClassList("packet-scrollbar");
        scrollView.horizontalScroller.AddToClassList("packet-scrollbar--horizontal");

        builder.AddChildren(scrollView.contentContainer, packet);
        return scrollView;
    }

    private static ScrollerVisibility MapScrollVisibility(ScrollbarVisibility visibility)
    {
        return visibility switch
        {
            ScrollbarVisibility.Hidden => ScrollerVisibility.Hidden,
            ScrollbarVisibility.Auto => ScrollerVisibility.Auto,
            ScrollbarVisibility.Visible => ScrollerVisibility.AlwaysVisible,
            _ => throw new InvalidOperationException(
                $"Invalid ScrollbarVisibility value {(int)visibility}."),
        };
    }
}

#nullable enable

using System;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using UnityEngine.UIElements;

namespace Kern.UI.Builders;
public class ScrollViewerPacketBuilder : PacketUIBuilderBase<ScrollViewerPacket>
{
    protected override VisualElement BuildTyped(ScrollViewerPacket packet, PacketUIBuilder builder)
    {
        var scrollView = new ScrollView
        {
            name = packet.Name,
            horizontalScrollerVisibility = MapScrollVisibility(packet.HorizontalScrollBar),
            verticalScrollerVisibility = MapScrollVisibility(packet.VerticalScrollBar),
        };

        scrollView.verticalScroller.AddToClassList("packet-scrollbar");
        scrollView.verticalScroller.AddToClassList("packet-scrollbar--vertical");
        scrollView.horizontalScroller.AddToClassList("packet-scrollbar");
        scrollView.horizontalScroller.AddToClassList("packet-scrollbar--horizontal");
        var footer = new VisualElement();
        footer.AddToClassList("packet-window-footer");
        for (int index = 0; index < packet.Children.Count; index++)
        {
            IGUIComponentPacket child = packet.Children[index];
            if (AttachedProperties.Has(child, "PacketUI.ExitAction"))
            {
                // Футерная кнопка «ВЫЙТИ» больше не строится: вместо неё окно
                // закрывает белый крестик в углу, который добавляет
                // ServerWindowPresenter. Пакет остаётся в packetOrder, поэтому
                // индексы элементов для сервера не сдвигаются.
                continue;
            }

            if (AttachedProperties.Has(child, "PacketUI.FooterAction"))
            {
                VisualElement action = builder.Build(child);
                if (footer.childCount > 0)
                {
                    action.AddToClassList("packet-window-footer-action--spaced");
                }

                footer.Add(action);
                continue;
            }

            if (AttachedProperties.Has(child, "PacketUI.RowLabel"))
            {
                if (index + 1 >= packet.Children.Count ||
                    string.IsNullOrEmpty(packet.Children[index + 1].OnClickContext))
                {
                    throw new InvalidOperationException("[PacketUI] Serialized row has no action component.");
                }

                var row = new VisualElement();
                row.AddToClassList("packet-window-row");
                VisualElement label = builder.Build(child);
                label.AddToClassList("packet-window-row-label");
                row.Add(label);
                row.Add(builder.Build(packet.Children[++index]));
                scrollView.contentContainer.Add(row);
                continue;
            }

            scrollView.contentContainer.Add(builder.Build(child));
        }

        if (footer.childCount == 0)
        {
            return scrollView;
        }

        var layout = new VisualElement();
        layout.AddToClassList("packet-window-body-layout");
        scrollView.AddToClassList("packet-window-body-scroll");
        layout.Add(scrollView);
        layout.Add(footer);
        return layout;
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

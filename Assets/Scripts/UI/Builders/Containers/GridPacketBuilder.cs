#nullable enable

using System.Collections.Generic;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using UnityEngine.UIElements;

namespace Fodinae.UI.Builders
{
    public class GridPacketBuilder : PacketUIBuilderBase<GridPacket>
    {
        protected override VisualElement BuildTyped(GridPacket packet, PacketUIBuilder builder)
        {
            var gridRoot = new VisualElement();
            gridRoot.AddToClassList("rel");
            gridRoot.AddToClassList("grow");

            var elements = new List<VisualElement>(packet.Children.Count);
            var placements = new List<(int Row, int Column, int RowSpan, int ColumnSpan)>(
                packet.Children.Count);

            foreach (IGUIComponentPacket childPacket in packet.Children)
            {
                VisualElement child = builder.Build(childPacket);
                child.AddToClassList("as-start");
                gridRoot.Add(child);
                elements.Add(child);
                placements.Add((
                    Row: Placement(childPacket, "Grid.Row", 0),
                    Column: Placement(childPacket, "Grid.Column", 0),
                    RowSpan: Placement(childPacket, "Grid.RowSpan", 1),
                    ColumnSpan: Placement(childPacket, "Grid.ColumnSpan", 1)));
            }

            // Расставлять можно только после того, как элементы измерены: размер
            // дорожки «по содержимому» неизвестен, пока панель не разложена.
            EventCallback<GeometryChangedEvent> place = null!;
            place = _ =>
            {
                gridRoot.UnregisterCallback(place);
                Place(gridRoot, packet, elements, placements);
            };
            gridRoot.RegisterCallback(place);

            return gridRoot;
        }

        private static void Place(
            VisualElement gridRoot,
            GridPacket packet,
            List<VisualElement> elements,
            List<(int Row, int Column, int RowSpan, int ColumnSpan)> placements)
        {
            var items = new GridItem[elements.Count];
            for (int i = 0; i < elements.Count; i++)
            {
                items[i] = new GridItem(
                    placements[i].Row,
                    placements[i].Column,
                    placements[i].RowSpan,
                    placements[i].ColumnSpan,
                    MeasuredWidth(elements[i]),
                    MeasuredHeight(elements[i]));
            }

            GridRect[] rects = PacketGridLayout.Measure(
                packet.Columns,
                packet.Rows,
                items,
                gridRoot.resolvedStyle.width,
                gridRoot.resolvedStyle.height);

            for (int i = 0; i < elements.Count; i++)
            {
                // Класс — на положение, инлайн — на вычисленные координаты:
                // они следуют из размеров ячейки и токеном быть не могут.
                VisualElement element = elements[i];
                element.AddToClassList("abs");
                IStyle style = element.style;
                style.left = rects[i].Left;
                style.top = rects[i].Top;
                style.width = rects[i].Width;
                style.height = rects[i].Height;
            }
        }

        // Подпись меряется вместе с полями: перенос строки уже случился внутри
        // её собственной ширины, и без полей дорожка выходит уже содержимого.
        private static float MeasuredWidth(VisualElement element)
        {
            IResolvedStyle style = element.resolvedStyle;
            return element is Label
                ? style.width + style.marginLeft + style.marginRight
                : style.width;
        }

        private static float MeasuredHeight(VisualElement element)
        {
            IResolvedStyle style = element.resolvedStyle;
            return element is Label
                ? style.height + style.marginTop + style.marginBottom
                : style.height;
        }

        private static int Placement(IGUIComponentPacket packet, string key, int fallback)
        {
            return AttachedProperties.TryGetInt(packet, key, out int value) ? value : fallback;
        }
    }
}

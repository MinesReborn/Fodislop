#nullable enable

using System;
using System.Collections.Generic;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using UnityEngine.UIElements;

namespace Kern.UI.ServerWindows;
public class GridPacketBuilder : PacketUIBuilderBase<GridPacket>
{
    protected override VisualElement BuildTyped(GridPacket packet, PacketUIBuilder builder)
    {
        if ((packet.Columns.Length == 0 || packet.Rows.Length == 0) && packet.Children.Count > 0)
        {
            throw new InvalidOperationException("GridPacket with children must define at least one row and column.");
        }

        var gridRoot = new VisualElement();
        gridRoot.AddToClassList("rel");
        gridRoot.AddToClassList("grow");

        var elements = new List<VisualElement>(packet.Children.Count);
        var placements = new List<(int Row, int Column, int RowSpan, int ColumnSpan)>(
            packet.Children.Count);

        foreach (IGUIComponentPacket childPacket in packet.Children)
        {
            int row = Placement(childPacket, "Grid.Row", 0);
            int column = Placement(childPacket, "Grid.Column", 0);
            int rowSpan = Placement(childPacket, "Grid.RowSpan", 1);
            int columnSpan = Placement(childPacket, "Grid.ColumnSpan", 1);
            if (rowSpan == 0 || columnSpan == 0 ||
                row >= packet.Rows.Length || column >= packet.Columns.Length ||
                rowSpan > packet.Rows.Length - row ||
                columnSpan > packet.Columns.Length - column)
            {
                throw new InvalidOperationException(
                    $"GridPacket child {childPacket.GetType().Name} has invalid placement " +
                    $"row={row}, column={column}, rowSpan={rowSpan}, columnSpan={columnSpan} " +
                    $"for {packet.Rows.Length} rows and {packet.Columns.Length} columns.");
            }

            VisualElement child = builder.Build(childPacket);
            child.AddToClassList("as-start");
            gridRoot.Add(child);
            elements.Add(child);
            placements.Add((row, column, rowSpan, columnSpan));
        }

        // Расставлять можно только после того, как элементы измерены: размер
        // дорожки «по содержимому» неизвестен, пока панель не разложена.
        GridItem[]? measuredItems = null;
        gridRoot.RegisterCallback<GeometryChangedEvent>(change =>
        {
            if (measuredItems != null && change.oldRect.size == change.newRect.size)
            {
                return;
            }

            measuredItems = Place(gridRoot, packet, elements, placements, measuredItems);
        });

        return gridRoot;
    }

    private static GridItem[] Place(
        VisualElement gridRoot,
        GridPacket packet,
        List<VisualElement> elements,
        List<(int Row, int Column, int RowSpan, int ColumnSpan)> placements,
        GridItem[]? measuredItems)
    {
        GridItem[] items = measuredItems ?? new GridItem[elements.Count];
        if (measuredItems == null)
        {
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
        }

        GridRect[] rects = PacketGridLayout.Measure(
            packet.Columns,
            packet.Rows,
            items,
            gridRoot.resolvedStyle.width,
            gridRoot.resolvedStyle.height,
            out _,
            out float totalHeight);

        for (int i = 0; i < elements.Count; i++)
        {
            // Класс — на положение, инлайн — на вычисленные координаты:
            // они следуют из размеров ячейки и токеном быть не могут.
            VisualElement element = elements[i];
            element.AddToClassList("abs");
            IStyle style = element.style;
            style.left = rects[i].Left;
            style.top = rects[i].Top;
            IResolvedStyle resolved = element.resolvedStyle;
            style.width = Math.Max(0f, rects[i].Width - resolved.marginLeft - resolved.marginRight);
            style.height = Math.Max(0f, rects[i].Height - resolved.marginTop - resolved.marginBottom);
        }

        // Дети переведены в position:absolute и выпали из потока: без резерва
        // контент-высота грида схлопывается в ноль, и контейнер, сжимающийся по
        // контенту (например, строка заголовка серверного окна), наезжает на
        // отрисованное содержимое. minHeight держит высоту дорожек, не мешая
        // гриду растягиваться в контейнерах побольше (flex-grow).
        gridRoot.style.minHeight = totalHeight;
        return items;
    }

    // Дорожки учитывают внешние отступы каждого серверного компонента.
    private static float MeasuredWidth(VisualElement element)
    {
        IResolvedStyle style = element.resolvedStyle;
        return style.width + style.marginLeft + style.marginRight;
    }

    private static float MeasuredHeight(VisualElement element)
    {
        IResolvedStyle style = element.resolvedStyle;
        return style.height + style.marginTop + style.marginBottom;
    }

    private static int Placement(IGUIComponentPacket packet, string key, int fallback)
    {
        if (!AttachedProperties.Has(packet, key))
        {
            return fallback;
        }

        if (!AttachedProperties.TryGetInt(packet, key, out int value) ||
            value < 0)
        {
            string raw = AttachedProperties.Find(packet, key) ?? string.Empty;
            throw new InvalidOperationException(
                $"Invalid {key}='{raw}' on {packet.GetType().Name}.");
        }

        return value;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Fodinae.Scripts;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI.Builders
{
    public class GridPacketBuilder : PacketUIBuilderBase
    {
        public override VisualElement Build(IGUIComponentPacket packet, PacketUIBuilder builder)
        {
            if (packet is not GridPacket gridPkt)
            {
                return null;
            }

            return BuildGrid(gridPkt, builder);
        }

        private static VisualElement BuildGrid(GridPacket gridPkt, PacketUIBuilder builder)
        {
            var gridRoot = new VisualElement { style = { flexGrow = 1, position = Position.Relative } };

            var gridItems = new List<(VisualElement element, int r, int c, int rs, int cs)>();
            foreach (var childPacket in gridPkt.Children)
            {
                var row = 0;
                var col = 0;
                var rowSpan = 1;
                var colSpan = 1;
                if (childPacket.AttachedProperties != null)
                {
                    foreach (var prop in childPacket.AttachedProperties)
                    {
                        if (prop.Key == "Grid.Row" && int.TryParse(prop.Value, out var r))
                        {
                            row = r;
                        }

                        if (prop.Key == "Grid.Column" && int.TryParse(prop.Value, out var c))
                        {
                            col = c;
                        }

                        if (prop.Key == "Grid.RowSpan" && int.TryParse(prop.Value, out var rs))
                        {
                            rowSpan = rs;
                        }

                        if (prop.Key == "Grid.ColumnSpan" && int.TryParse(prop.Value, out var cs))
                        {
                            colSpan = cs;
                        }
                    }
                }

                var childElement = builder.Build(childPacket);
                childElement.style.alignSelf = Align.FlexStart;
                gridRoot.Add(childElement);
                gridItems.Add((childElement, row, col, rowSpan, colSpan));
            }

            EventCallback<GeometryChangedEvent> onGeometryChanged = null;
            onGeometryChanged = (evt) =>
            {
                gridRoot.UnregisterCallback<GeometryChangedEvent>(onGeometryChanged);

                var availableWidth = gridRoot.resolvedStyle.width;
                var availableHeight = gridRoot.resolvedStyle.height;

                var columnTracks = new float[gridPkt.Columns.Length];
                var rowTracks = new float[gridPkt.Rows.Length];

                // Step 1: Calculate auto track sizes
                for (var c = 0; c < columnTracks.Length; c++)
                {
                    if (gridPkt.Columns[c] == 0) // auto
                    {
                        var maxWidth = 0f;
                        foreach (var (element, r, gc, rs, cs) in gridItems)
                        {
                            if (gc == c && cs == 1)
                            {
                                var elementWidth = element.resolvedStyle.width;
                                if (element is Label) // Labels need special consideration for text wrapping
                                {
                                    elementWidth += element.resolvedStyle.marginLeft + element.resolvedStyle.marginRight;
                                }

                                if (elementWidth > maxWidth)
                                {
                                    maxWidth = elementWidth;
                                }
                            }
                        }

                        columnTracks[c] = maxWidth;
                    }
                }

                for (var r = 0; r < rowTracks.Length; r++)
                {
                    if (gridPkt.Rows[r] == 0) // auto
                    {
                        var maxHeight = 0f;
                        foreach (var (element, gr, c, rs, cs) in gridItems)
                        {
                            if (gr == r && rs == 1)
                            {
                                var elementHeight = element.resolvedStyle.height;
                                if (element is Label)
                                {
                                    elementHeight += element.resolvedStyle.marginTop + element.resolvedStyle.marginBottom;
                                }

                                if (elementHeight > maxHeight)
                                {
                                    maxHeight = elementHeight;
                                }
                            }
                        }

                        rowTracks[r] = maxHeight;
                    }
                }

                // Step 2: Calculate 'fr' track sizes
                var totalColumnFr = gridPkt.Columns.Where(fr => fr > 0).Sum(fr => fr);
                var totalRowFr = gridPkt.Rows.Where(fr => fr > 0).Sum(fr => fr);
                var nonFrWidth = columnTracks.Sum();
                var nonFrHeight = rowTracks.Sum();
                var remainingWidth = availableWidth - nonFrWidth;
                var remainingHeight = availableHeight - nonFrHeight;

                if (totalColumnFr > 0)
                {
                    for (var c = 0; c < columnTracks.Length; c++)
                    {
                        if (gridPkt.Columns[c] > 0)
                        {
                            columnTracks[c] = (gridPkt.Columns[c] / (float)totalColumnFr) * remainingWidth;
                        }
                    }
                }

                if (totalRowFr > 0)
                {
                    for (var r = 0; r < rowTracks.Length; r++)
                    {
                        if (gridPkt.Rows[r] > 0)
                        {
                            rowTracks[r] = (gridPkt.Rows[r] / (float)totalRowFr) * remainingHeight;
                        }
                    }
                }

                // Step 3: Calculate track start positions
                var columnStarts = new float[columnTracks.Length + 1];
                var rowStarts = new float[rowTracks.Length + 1];
                for (var c = 1; c < columnStarts.Length; c++)
                {
                    columnStarts[c] = columnStarts[c - 1] + columnTracks[c - 1];
                }

                for (var r = 1; r < rowStarts.Length; r++)
                {
                    rowStarts[r] = rowStarts[r - 1] + rowTracks[r - 1];
                }

                // Step 4: Position all elements
                foreach (var (element, r, c, rs, cs) in gridItems)
                {
                    var finalCol = Math.Min(c + cs, columnStarts.Length - 1);
                    var finalRow = Math.Min(r + rs, rowStarts.Length - 1);

                    var left = columnStarts[c];
                    var top = rowStarts[r];
                    var width = columnStarts[finalCol] - left;
                    var height = rowStarts[finalRow] - top;

                    var style = element.style;
                    style.position = Position.Absolute;
                    style.left = left;
                    style.top = top;
                    style.width = width;
                    style.height = height;
                }
            };

            gridRoot.RegisterCallback<GeometryChangedEvent>(onGeometryChanged);

            return gridRoot;
        }
    }
}

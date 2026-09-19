#nullable enable

using System;
using System.Collections.Generic;

namespace Kern.UI.Builders;
public readonly record struct GridItem(int Row, int Column, int RowSpan, int ColumnSpan, float Width, float Height);

public readonly record struct GridRect(float Left, float Top, float Width, float Height);

public static class PacketGridLayout
{
    public static GridRect[] Measure(
        IReadOnlyList<byte> columns,
        IReadOnlyList<byte> rows,
        IReadOnlyList<GridItem> items,
        float availableWidth,
        float availableHeight)
    {
        float[] columnTracks = Tracks(
            columns, items, availableWidth,
            item => item.Column, item => item.ColumnSpan, item => item.Width);
        float[] rowTracks = Tracks(
            rows, items, availableHeight,
            item => item.Row, item => item.RowSpan, item => item.Height);

        float[] columnStarts = Starts(columnTracks);
        float[] rowStarts = Starts(rowTracks);

        var rects = new GridRect[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            GridItem item = items[i];
            int lastColumn = Math.Min(item.Column + item.ColumnSpan, columnStarts.Length - 1);
            int lastRow = Math.Min(item.Row + item.RowSpan, rowStarts.Length - 1);
            float left = columnStarts[item.Column];
            float top = rowStarts[item.Row];
            rects[i] = new GridRect(
                left, top,
                columnStarts[lastColumn] - left,
                rowStarts[lastRow] - top);
        }

        return rects;
    }

    private static float[] Tracks(
        IReadOnlyList<byte> definitions,
        IReadOnlyList<GridItem> items,
        float available,
        Func<GridItem, int> indexOf,
        Func<GridItem, int> spanOf,
        Func<GridItem, float> sizeOf)
    {
        var tracks = new float[definitions.Count];
        float used = 0f;
        int totalFractions = 0;

        for (int track = 0; track < tracks.Length; track++)
        {
            if (definitions[track] > 0)
            {
                totalFractions += definitions[track];
                continue;
            }

            float largest = 0f;
            foreach (GridItem item in items)
            {
                // Элемент, растянутый на несколько дорожек, не задаёт размер
                // ни одной из них: его ширина принадлежит всем сразу.
                if (indexOf(item) == track && spanOf(item) == 1 && sizeOf(item) > largest)
                {
                    largest = sizeOf(item);
                }
            }

            tracks[track] = largest;
            used += largest;
        }

        if (totalFractions <= 0)
        {
            return tracks;
        }

        float remaining = available - used;
        for (int track = 0; track < tracks.Length; track++)
        {
            if (definitions[track] > 0)
            {
                tracks[track] = definitions[track] / (float)totalFractions * remaining;
            }
        }

        return tracks;
    }

    private static float[] Starts(float[] tracks)
    {
        var starts = new float[tracks.Length + 1];
        for (int i = 1; i < starts.Length; i++)
        {
            starts[i] = starts[i - 1] + tracks[i - 1];
        }

        return starts;
    }
}

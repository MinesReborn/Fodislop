#nullable enable

using System;
using System.Collections.Generic;

namespace Fodinae.UI.Builders
{
    /// <summary>Место элемента в сетке и его измеренный размер.</summary>
    public readonly struct GridItem
    {
        public GridItem(int row, int column, int rowSpan, int columnSpan, float width, float height)
        {
            Row = row;
            Column = column;
            RowSpan = rowSpan;
            ColumnSpan = columnSpan;
            Width = width;
            Height = height;
        }

        public int Row { get; }
        public int Column { get; }
        public int RowSpan { get; }
        public int ColumnSpan { get; }
        public float Width { get; }
        public float Height { get; }
    }

    /// <summary>Вычисленный прямоугольник элемента.</summary>
    public readonly struct GridRect
    {
        public GridRect(float left, float top, float width, float height)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        public float Left { get; }
        public float Top { get; }
        public float Width { get; }
        public float Height { get; }
    }

    /// <summary>
    /// Раскладка сетки протокола: из описания дорожек и мест элементов —
    /// координаты. Ни одного обращения к UI Toolkit.
    /// </summary>
    /// <remarks>
    /// Раньше это жило внутри лямбды обработчика геометрии в строителе: разбор
    /// мест, размер дорожек, накопленные смещения и расстановка — всё в одном
    /// методе на сто тридцать строк, причём столбцы и строки были написаны
    /// дважды слово в слово. Здесь тот же расчёт разложен по шагам и не зависит
    /// от элементов: его видно целиком и можно проверить числами, не поднимая
    /// панель.
    ///
    /// Ноль в описании дорожки значит «по содержимому», положительное число —
    /// доля свободного места (fr).
    /// </remarks>
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

        /// <summary>
        /// Размеры дорожек одной оси: сначала «по содержимому», затем остаток
        /// делится между долевыми.
        /// </summary>
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

        /// <summary>Накопленные смещения дорожек: на одну границу больше, чем дорожек.</summary>
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
}

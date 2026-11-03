#nullable enable

using System;
using MinesServer.Data;
using UnityEngine;

namespace Kern.UI.Map;

internal static class WorldMapMipResampling
{
    internal static Color32 AverageChildren(
        Color32[][] levels, int[] widths, int[] heights, float[][] weights,
        int childLevel, int parentX, int parentY, out double totalWeight)
    {
        Color32[] children = levels[childLevel];
        int childWidth = widths[childLevel];
        int childHeight = heights[childLevel];
        int parentWidth = widths[childLevel + 1];
        int parentHeight = heights[childLevel + 1];
        int firstChildX = parentX * childWidth / parentWidth;
        int endChildX = ((parentX + 1) * childWidth + parentWidth - 1) / parentWidth;
        int firstChildY = parentY * childHeight / parentHeight;
        int endChildY = ((parentY + 1) * childHeight + parentHeight - 1) / parentHeight;
        double red = 0;
        double green = 0;
        double blue = 0;
        double alpha = 0;
        totalWeight = 0;

        for (int childY = firstChildY; childY < endChildY; childY++)
        {
            int overlapY = Math.Min((childY + 1) * parentHeight, (parentY + 1) * childHeight) -
                           Math.Max(childY * parentHeight, parentY * childHeight);

            for (int childX = firstChildX; childX < endChildX; childX++)
            {
                int overlapX = Math.Min((childX + 1) * parentWidth, (parentX + 1) * childWidth) -
                               Math.Max(childX * parentWidth, parentX * childWidth);
                int childIndex = childY * childWidth + childX;
                double weight = weights[childLevel][childIndex] * overlapX * overlapY /
                                ((double)parentWidth * parentHeight);
                Color32 color = children[childIndex];
                red += WorldMapMipColors.DecodeSrgbChannel(color.r) * weight;
                green += WorldMapMipColors.DecodeSrgbChannel(color.g) * weight;
                blue += WorldMapMipColors.DecodeSrgbChannel(color.b) * weight;
                alpha += color.a * weight;
                totalWeight += weight;
            }
        }

        return WorldMapMipColors.CreateAverage(red, green, blue, alpha, totalWeight);
    }
}

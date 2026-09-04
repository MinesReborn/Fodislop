#nullable enable

using System;
using System.IO;
using UnityEngine;

namespace Fodinae.World;

internal static class GifFrameCompositor
{
    private static readonly int[] InterlaceRowStarts = [0, 4, 2, 1];
    private static readonly int[] InterlaceRowSteps = [8, 8, 4, 2];

    public static void CompositeFrame(
        Color32[] canvas,
        byte[] colorIndices,
        Color32[] colorTable,
        int left,
        int top,
        int width,
        int height,
        int screenWidth,
        int transparentIndex,
        bool interlaced)
    {
        int sourceRow = 0;
        if (interlaced)
        {
            for (int pass = 0; pass < InterlaceRowStarts.Length; pass++)
            {
                for (int targetRow = InterlaceRowStarts[pass];
                     targetRow < height;
                     targetRow += InterlaceRowSteps[pass])
                {
                    CompositeFrameRow(
                        canvas,
                        colorIndices,
                        colorTable,
                        left,
                        top,
                        width,
                        screenWidth,
                        sourceRow++,
                        targetRow,
                        transparentIndex);
                }
            }
        }
        else
        {
            for (int row = 0; row < height; row++)
            {
                CompositeFrameRow(
                    canvas,
                    colorIndices,
                    colorTable,
                    left,
                    top,
                    width,
                    screenWidth,
                    row,
                    row,
                    transparentIndex);
                sourceRow++;
            }
        }

        if (sourceRow != height)
        {
            throw new InvalidDataException(
                $"GIF interlace mapping consumed {sourceRow} rows; expected {height}.");
        }
    }

    private static void CompositeFrameRow(
        Color32[] canvas,
        byte[] colorIndices,
        Color32[] colorTable,
        int left,
        int top,
        int width,
        int screenWidth,
        int sourceRow,
        int targetRow,
        int transparentIndex)
    {
        int sourceOffset = sourceRow * width;
        int destinationOffset = ((top + targetRow) * screenWidth) + left;
        for (int x = 0; x < width; x++)
        {
            int colorIndex = colorIndices[sourceOffset + x];
            if (colorIndex == transparentIndex)
            {
                continue;
            }

            if (colorIndex >= colorTable.Length)
            {
                throw new InvalidDataException(
                    $"GIF frame references color {colorIndex}, but its table has " +
                    $"only {colorTable.Length} entries.");
            }

            canvas[destinationOffset + x] = colorTable[colorIndex];
        }
    }

    public static void ClearFrameRectangle(
        Color32[] canvas,
        int left,
        int top,
        int width,
        int height,
        int screenWidth,
        Color32 color)
    {
        for (int y = 0; y < height; y++)
        {
            int rowOffset = ((top + y) * screenWidth) + left;
            for (int x = 0; x < width; x++)
            {
                canvas[rowOffset + x] = color;
            }
        }
    }
}

#nullable enable

using System;
using MinesServer.Data;
using UnityEngine;

namespace Kern.UI.Map;

internal static class WorldMapMipSizing
{
    internal const long MaxCacheBytes = 64L * 1024L * 1024L;

    // Managed level data plus the readable texture and its weight plane.
    internal const int BytesPerMipTexel = sizeof(uint) + sizeof(float) + (2 * sizeof(uint));

    /// <summary>
    /// Finest power-of-two block divisor of <paramref name="chunkSize"/> whose
    /// full pyramid fits the per-session budget. Falls back to the storage chunk
    /// size when nothing smaller fits.
    /// </summary>
    public static int ChooseBlockSize(int worldWidth, int worldHeight, int chunkSize)
    {
        if (worldWidth <= 0 || worldHeight <= 0 || chunkSize <= 0)
        {
            return Math.Max(1, chunkSize);
        }

        for (int block = 1; block < chunkSize; block <<= 1)
        {
            if (chunkSize % block != 0)
            {
                break;
            }

            if (WorldMapMipSizing.FitsCacheBudget(worldWidth, worldHeight, block))
            {
                return block;
            }
        }

        return chunkSize;
    }

    internal static bool FitsCacheBudget(int worldWidth, int worldHeight, int blockSize)
    {
        int width = (worldWidth + blockSize - 1) / blockSize;
        int height = (worldHeight + blockSize - 1) / blockSize;
        long totalPixels = 0;
        while (true)
        {
            totalPixels += (long)width * height;
            if (width == 1 && height == 1)
            {
                break;
            }

            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
        }

        return totalPixels * WorldMapMipSizing.BytesPerMipTexel <= WorldMapMipSizing.MaxCacheBytes;
    }

}

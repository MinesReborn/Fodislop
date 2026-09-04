#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fodinae.World.Lighting
{
    /// <summary>
    /// Computes discrete probe resolutions, spacing, and interval layouts for Radiance Cascades.
    /// </summary>
    public static class CascadeLayoutBuilder
    {
        private const int MaximumCascadeDirections = 256;

        public static int GetMaximumCascadeCount(long atlasDimension)
        {
            return atlasDimension <= 256 ? 3 : 4;
        }

        public static long CalculateCascadeEntryCount(
            int width,
            int height,
            int maximumCascadeCount)
        {
            if (maximumCascadeCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumCascadeCount));
            }

            float requiredDistance = Mathf.Sqrt((width * width) + (height * height));
            long entryCount = 0;
            int spacing = 1;
            int directions = 4;
            float intervalEnd = 1f;

            while (true)
            {
                int probeWidth = Mathf.CeilToInt(width / (float)spacing);
                int probeHeight = Mathf.CeilToInt(height / (float)spacing);
                entryCount += (long)probeWidth * probeHeight * directions;

                if (intervalEnd >= requiredDistance || maximumCascadeCount == 1)
                {
                    return entryCount;
                }

                maximumCascadeCount--;
                spacing *= 2;
                directions = Mathf.Min(MaximumCascadeDirections, directions * 4);
                intervalEnd *= 4f;
            }
        }

        public static void BuildCascadeLayouts(
            int width,
            int height,
            long atlasDimension,
            List<CascadeLayout> cascades)
        {
            cascades.Clear();
            float requiredDistance = Mathf.Sqrt((width * width) + (height * height));
            int maxCascades = GetMaximumCascadeCount(atlasDimension);
            int offset = 0;
            int spacing = 1;
            int directions = 4;
            float intervalStart = 0f;
            float intervalEnd = 1f;

            while (true)
            {
                int probeWidth = Mathf.CeilToInt(width / (float)spacing);
                int probeHeight = Mathf.CeilToInt(height / (float)spacing);
                long entryCountLong = (long)probeWidth * probeHeight * directions;

                if (entryCountLong > int.MaxValue - offset)
                {
                    throw new InvalidOperationException("Radiance cascade atlas exceeds the supported buffer size.");
                }

                int entryCount = (int)entryCountLong;
                cascades.Add(new CascadeLayout(
                    offset,
                    entryCount,
                    probeWidth,
                    probeHeight,
                    spacing,
                    directions,
                    intervalStart,
                    intervalEnd));

                offset += entryCount;
                if (cascades.Count >= maxCascades || intervalEnd >= requiredDistance)
                {
                    break;
                }

                spacing *= 2;
                directions = Mathf.Min(MaximumCascadeDirections, directions * 4);
                intervalStart = intervalEnd;
                intervalEnd *= 4f;
            }
        }

        public static int SelectStablePixelsPerCell(
            int gridWidth,
            int gridHeight,
            int requestedScale,
            int maximumTextureDimension,
            long atlasDimension)
        {
            long maximumEntryCount = atlasDimension * atlasDimension * 4;

            for (int scale = requestedScale; scale >= 1; scale--)
            {
                int width = checked(gridWidth * scale);
                int height = checked(gridHeight * scale);

                if (width > maximumTextureDimension ||
                    height > maximumTextureDimension)
                {
                    continue;
                }

                int maximumCascadeCount = GetMaximumCascadeCount(atlasDimension);
                long requiredEntryCount = CalculateCascadeEntryCount(
                    width,
                    height,
                    maximumCascadeCount);

                if (requiredEntryCount <= maximumEntryCount)
                {
                    return scale;
                }
            }

            throw new InvalidOperationException(
                $"Radiance cascade region {gridWidth}x{gridHeight} cannot fit at " +
                $"one texel per cell within texture limit {maximumTextureDimension} " +
                $"and atlas limit {atlasDimension}.");
        }
    }
}

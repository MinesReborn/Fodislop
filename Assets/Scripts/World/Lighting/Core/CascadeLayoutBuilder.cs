#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kern.World.Lighting;
public static class CascadeLayoutBuilder
{
    // Safe default for every caller. A larger angular cap must be an explicit
    // experiment; an omitted optional argument must never create a frame-sized
    // transport burst by accident.
    public const int DefaultMaximumCascadeDirections = 64;

    public static int SelectMaximumCascadeDirections(
        int width,
        int height,
        long atlasDimension,
        int directionCeiling,
        long maximumRayWorkUnits)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        int maximumDirections = Mathf.Clamp(
            directionCeiling,
            4,
            DefaultMaximumCascadeDirections);
        maximumDirections = HighestPowerOfTwoAtMost(maximumDirections);

        for (int candidate = maximumDirections; candidate >= 4; candidate /= 2)
        {
            if (EstimateCascadeRayWorkUnits(width, height, atlasDimension, candidate) <=
                maximumRayWorkUnits)
            {
                return candidate;
            }
        }

        return 4;
    }

    public static int GetMaximumCascadeCount(long atlasDimension)
    {
        // A fourth interval is cheaper than extending the last cascade over
        // the whole remaining diagonal. Entry-count and ray-work governors
        // still reject it when the selected atlas cannot fit it.
        return 4;
    }

    public static long EstimateCascadeRayWorkUnits(
        int width,
        int height,
        long atlasDimension,
        int maximumDirections)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        var cascades = new List<CascadeLayout>();
        BuildCascadeLayouts(width, height, atlasDimension, cascades, maximumDirections);
        return CascadeCostCalculator.EstimateRayWorkUnits(cascades);
    }

    private static int HighestPowerOfTwoAtMost(int value)
    {
        int result = 4;
        while (result <= value / 2)
        {
            result *= 2;
        }

        return result;
    }

    public static long CalculateCascadeEntryCount(
        int width,
        int height,
        int maximumCascadeCount,
        int maximumDirections = DefaultMaximumCascadeDirections)
    {
        if (maximumCascadeCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCascadeCount));
        }

        float requiredDistance = (float)Math.Sqrt(
            (double)width * width + (double)height * height);
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
            directions = Mathf.Min(maximumDirections, directions * 4);
            intervalEnd *= 4f;
        }
    }

    public static void BuildCascadeLayouts(
        int width,
        int height,
        long atlasDimension,
        List<CascadeLayout> cascades,
        int maximumDirections = DefaultMaximumCascadeDirections)
    {
        cascades.Clear();
        float requiredDistance = (float)Math.Sqrt(
            (double)width * width + (double)height * height);
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
            // The atlas budget limits angular/spatial resolution, not the
            // distance light can travel. The last interval covers the field.
            float traceEnd = cascades.Count + 1 >= maxCascades
                ? Mathf.Max(intervalEnd, requiredDistance)
                : intervalEnd;
            cascades.Add(new CascadeLayout(
                offset,
                entryCount,
                probeWidth,
                probeHeight,
                spacing,
                directions,
                intervalStart,
                traceEnd));

            offset += entryCount;
            if (cascades.Count >= maxCascades || intervalEnd >= requiredDistance)
            {
                break;
            }

            spacing *= 2;
            directions = Mathf.Min(maximumDirections, directions * 4);
            intervalStart = intervalEnd;
            intervalEnd *= 4f;
        }
    }

    public static int SelectStablePixelsPerCell(
        int gridWidth,
        int gridHeight,
        int requestedScale,
        int maximumTextureDimension,
        long atlasDimension,
        int maximumDirections = DefaultMaximumCascadeDirections,
        long maximumRayWorkUnits = long.MaxValue)
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
            int selectedDirections = SelectMaximumCascadeDirections(
                width,
                height,
                atlasDimension,
                maximumDirections,
                maximumRayWorkUnits);
            long requiredEntryCount = CalculateCascadeEntryCount(
                width,
                height,
                maximumCascadeCount,
                selectedDirections);

            if (requiredEntryCount <= maximumEntryCount &&
                EstimateCascadeRayWorkUnits(
                    width,
                    height,
                    atlasDimension,
                    selectedDirections) <= maximumRayWorkUnits)
            {
                return scale;
            }
        }

        throw new InvalidOperationException(
            $"Radiance cascade region {gridWidth}x{gridHeight} cannot fit at " +
            $"one texel per cell within texture limit {maximumTextureDimension}, " +
            $"atlas limit {atlasDimension}, and ray budget {maximumRayWorkUnits}.");
    }
}

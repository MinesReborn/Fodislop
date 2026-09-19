#nullable enable

using UnityEngine;

namespace Kern.World.Streaming;

public readonly record struct StreamingPolicy(
    int AllocationQuantumCells,
    int MinimumWindowDimension,
    int MaximumWindowDimension,
    int ShrinkHysteresisQuanta)
{
    public const int DefaultLightingPaddingCells = 16;
    public const int DefaultAllocationQuantumCells = 32;
    // Covers the widest supported camera viewport plus one governor quantum
    // of arrival headroom. Map streaming uses this as its resident window;
    // terrain and lighting may allocate their own tighter windows.
    public const int DefaultMapWindowDimensionCells = DefaultAllocationQuantumCells * 4;
    public const int DefaultMinimumWindowDimension = 2;
    public const int DefaultMaximumWindowDimension = 384;
    public const int DefaultShrinkHysteresisQuanta = 2;

    public static StreamingPolicy Default => new(
        DefaultAllocationQuantumCells,
        DefaultMinimumWindowDimension,
        DefaultMaximumWindowDimension,
        DefaultShrinkHysteresisQuanta);

    public int ResolveEffectivePadding(
        int baseViewportPadding,
        int requiredLightingPadding,
        int stableRegionPadding)
    {
        return System.Math.Max(
            baseViewportPadding,
            requiredLightingPadding + stableRegionPadding);
    }

    public int QuantizeDimension(int requestedDimension)
    {
        int safeDimension = System.Math.Max(MinimumWindowDimension, requestedDimension);
        int quantum = System.Math.Max(1, AllocationQuantumCells);
        long quantized = ((long)safeDimension + quantum - 1) / quantum * quantum;
        return (int)System.Math.Clamp(
            quantized,
            (long)MinimumWindowDimension,
            (long)MaximumWindowDimension);
    }

    public int QuantizeDimensionWithHeadroom(int requestedDimension)
    {
        int quantum = System.Math.Max(1, AllocationQuantumCells);
        long headroomRequest = (long)requestedDimension + quantum;
        return QuantizeDimension(
            headroomRequest >= int.MaxValue
                ? int.MaxValue
                : (int)headroomRequest);
    }

    public int AlignOrigin(int coordinate)
    {
        long quantum = System.Math.Max(1, AllocationQuantumCells);
        long value = coordinate;
        long quotient = value >= 0
            ? value / quantum
            : -(((-value) + quantum - 1) / quantum);
        return checked((int)(quotient * quantum));
    }

    public Vector2Int AlignOrigin(Vector2Int origin) =>
        new(AlignOrigin(origin.x), AlignOrigin(origin.y));

    public int SelectWindowDimension(
        int requestedDimension,
        int currentDimension,
        bool isInitialized)
    {
        int quantized = QuantizeDimension(requestedDimension);
        if (!isInitialized || currentDimension <= 0 || quantized > currentDimension)
        {
            return quantized;
        }

        long shrinkThreshold = (long)System.Math.Max(0, ShrinkHysteresisQuanta) *
            System.Math.Max(1, AllocationQuantumCells);
        return (long)requestedDimension + shrinkThreshold <= currentDimension
            ? quantized
            : currentDimension;
    }

    public bool ContainsViewport(
        Vector2Int windowSize,
        Vector2Int viewportOrigin,
        Vector2Int viewportSize)
    {
        return ContainsViewportWithMargin(
            windowSize,
            viewportOrigin,
            viewportSize,
            marginCells: 0);
    }

    public bool ContainsViewportWithMargin(
        Vector2Int windowSize,
        Vector2Int viewportOrigin,
        Vector2Int viewportSize,
        int marginCells)
    {
        int margin = System.Math.Max(0, marginCells);
        return viewportOrigin.x >= margin &&
            viewportOrigin.y >= margin &&
            viewportOrigin.x + viewportSize.x <= windowSize.x - margin &&
            viewportOrigin.y + viewportSize.y <= windowSize.y - margin;
    }

    public int ResolvePrefetchMarginCells(int windowDimension)
    {
        int quantum = System.Math.Max(1, AllocationQuantumCells);
        int halfWindow = System.Math.Max(0, (windowDimension - 1) / 2);
        return System.Math.Min(quantum - 1, halfWindow);
    }
}

#nullable enable

using UnityEngine;

namespace Kern.World.Streaming;

public sealed class StreamingGovernor
{
    public StreamingGovernor(StreamingPolicy policy)
    {
        Policy = policy;
    }

    public StreamingPolicy Policy { get; }

    public StreamingPlan Plan(
        StreamingWindow current,
        Vector2Int desiredOrigin,
        Vector2Int targetSize,
        bool dimensionsChanged)
    {
        var target = new StreamingWindow(
            desiredOrigin,
            targetSize);

        if (!current.IsValid)
        {
            return new StreamingPlan(
                StreamingPlanKind.FullRebuild,
                current,
                target,
                Vector2Int.zero);
        }

        if (dimensionsChanged || current.Size != target.Size)
        {
            return new StreamingPlan(
                StreamingPlanKind.Resize,
                current,
                target,
                target.Origin - current.Origin);
        }

        Vector2Int delta = target.Origin - current.Origin;
        if (delta == Vector2Int.zero)
        {
            return new StreamingPlan(
                StreamingPlanKind.Keep,
                current,
                target,
                delta);
        }

        bool canScroll =
            Mathf.Abs(delta.x) < current.Size.x &&
            Mathf.Abs(delta.y) < current.Size.y;
        return new StreamingPlan(
            canScroll ? StreamingPlanKind.ScrollTerrain : StreamingPlanKind.FullRebuild,
            current,
            target,
            delta);
    }

    public Vector2Int SelectTargetOrigin(
        Vector2Int currentOrigin,
        Vector2Int desiredOrigin,
        Vector2Int viewportOrigin,
        Vector2Int viewportSize,
        Vector2Int windowSize,
        bool dimensionsChanged,
        int reanchorMarginCells = 0)
    {
        if (dimensionsChanged || currentOrigin.x == int.MinValue || currentOrigin.y == int.MinValue)
        {
            return desiredOrigin;
        }

        Vector2Int viewportOffset = viewportOrigin - currentOrigin;
        bool outsideWindow = !Policy.ContainsViewportWithMargin(
            windowSize,
            viewportOffset,
            viewportSize,
            reanchorMarginCells);

        if (!outsideWindow)
        {
            return currentOrigin;
        }

        // Prefetch windows advance by one policy quantum. The target is never
        // recentered on the current cell, so a request made on every movement
        // step cannot turn into a one-cell scroll loop.
        if (reanchorMarginCells > 0)
        {
            int quantum = System.Math.Max(1, Policy.AllocationQuantumCells);
            if (Mathf.Abs(desiredOrigin.x - currentOrigin.x) >= windowSize.x ||
                Mathf.Abs(desiredOrigin.y - currentOrigin.y) >= windowSize.y)
            {
                return Policy.AlignOrigin(desiredOrigin);
            }

            Vector2Int target = currentOrigin;
            if (viewportOffset.x < reanchorMarginCells &&
                desiredOrigin.x <= currentOrigin.x - quantum)
            {
                target.x -= quantum;
            }
            else if (viewportOffset.x + viewportSize.x > windowSize.x - reanchorMarginCells &&
                desiredOrigin.x > currentOrigin.x)
            {
                target.x += quantum;
            }

            if (viewportOffset.y < reanchorMarginCells &&
                desiredOrigin.y <= currentOrigin.y - quantum)
            {
                target.y -= quantum;
            }
            else if (viewportOffset.y + viewportSize.y > windowSize.y - reanchorMarginCells &&
                desiredOrigin.y > currentOrigin.y)
            {
                target.y += quantum;
            }

            return target;
        }

        // Callers without a prefetch margin still get a policy-aligned
        // replacement instead of an arbitrary cell-by-cell origin.
        return Policy.AlignOrigin(viewportOrigin);
    }

}

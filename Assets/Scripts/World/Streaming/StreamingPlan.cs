#nullable enable

using UnityEngine;

namespace Kern.World.Streaming;

public enum StreamingPlanKind
{
    Keep = 0,
    ScrollTerrain = 1,
    BuildPrefetch = 2,
    SwapPrefetch = 3,
    FullRebuild = 4,
    Resize = 5,
}

public readonly record struct StreamingWindow(Vector2Int Origin, Vector2Int Size)
{
    public bool IsValid =>
        Origin.x != int.MinValue &&
        Origin.y != int.MinValue &&
        Size.x > 0 &&
        Size.y > 0;
}

public readonly record struct StreamingPlan(
    StreamingPlanKind Kind,
    StreamingWindow Current,
    StreamingWindow Target,
    Vector2Int Delta);

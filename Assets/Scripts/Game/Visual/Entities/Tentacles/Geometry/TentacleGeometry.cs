#nullable enable

using UnityEngine;

using Kern.Game.Visual.Entities.Tails;

namespace Kern.Game.Visual.Entities.Tentacles;

/// <summary>
///     Constant-width ribbon layout of one LineRenderer strand.
/// </summary>
/// <remarks>
///     The four source LineRenderers use their full texture along each path. Adjacent ribbon
///     segments share vertices in the world-entity batch mesh.
/// </remarks>
internal static class TentacleGeometry
{
    /// <summary>Point count of one strand: the robot's own point plus four chain nodes.</summary>
    public const int PointCount = TailChain.PointCount;

    public const int SectorCount = PointCount - 1;

    public const int VerticesPerPoint = 2;

    /// <summary>
    ///     Total ribbon width is <c>2 / HalfWidthDivisor</c>. The legacy divisor of 16 assumed a
    ///     body one world unit wide; the batched body is 0.32.
    /// </summary>
    public const float HalfWidthDivisor = 25f;

    public const float RibbonHalfWidth = 1f / HalfWidthDivisor;

    public const int RibbonVertexCount = PointCount * VerticesPerPoint;

    public const int RibbonIndexCount = SectorCount * 6;

    public const int VerticesPerTentacle = RibbonVertexCount;

    public const int IndicesPerTentacle = RibbonIndexCount;

    /// <summary>Cap on the mitre, so a joint that folds back cannot spike the silhouette.</summary>
    private const float MaxMiterScale = 2f;

    private const float DegenerateDirectionSquared = 1e-10f;

    /// <summary>Smallest cosine treated as a real angle; below it the mitre divides by ~zero.</summary>
    private const float MinCosHalfTurn = 1e-4f;

    /// <summary>Writes one strand's vertices and UVs as a constant-width ribbon.</summary>
    /// <param name="points">The rendered strip points, root first; <see cref="PointCount" /> of them.</param>
    /// <param name="atlasRect">Region of the shared atlas holding this tentacle's texture.</param>
    /// <param name="verts">Destination vertex buffer.</param>
    /// <param name="uvs">Destination UV buffer.</param>
    /// <param name="vertBase">First vertex slot to write.</param>
    /// <param name="originOffset">
    ///     Translation the simulation ran in, subtracted back out here. Without it the strip
    ///     detaches from the body, because point zero is the chain's root.
    /// </param>
    public static void WriteVertices(
        Vector3[] points,
        Rect atlasRect,
        Vector3[] verts,
        Vector2[] uvs,
        int vertBase,
        Vector3 originOffset = default)
    {
        float totalLength = 0f;
        for (int point = 1; point < PointCount; point++)
        {
            totalLength += Vector3.Distance(points[point - 1], points[point]);
        }

        float accumulatedLength = 0f;
        for (int point = 0; point < PointCount; point++)
        {
            Vector3 centre = points[point] - originOffset;
            Vector3 previous = FindSegmentDirection(points, point - 1, -1);
            Vector3 next = FindSegmentDirection(points, point, 1);
            if (previous == Vector3.zero)
            {
                previous = next;
            }

            if (next == Vector3.zero)
            {
                next = previous;
            }

            Vector3 offset = MitredOffset(previous, next, RibbonHalfWidth);
            int vertex = vertBase + (point * VerticesPerPoint);

            verts[vertex] = centre + offset;
            verts[vertex + 1] = centre - offset;

            // The source LineRenderer uses Stretch texture mode: U follows distance along the
            // path, while V spans the full texture across the ribbon.
            float pathFraction = totalLength > 1e-6f
                ? accumulatedLength / totalLength
                : (float)point / (PointCount - 1);
            float u = atlasRect.xMin + (atlasRect.width * pathFraction);
            uvs[vertex] = new Vector2(u, atlasRect.yMax);
            uvs[vertex + 1] = new Vector2(u, atlasRect.yMin);

            if (point + 1 < PointCount)
            {
                accumulatedLength += Vector3.Distance(points[point], points[point + 1]);
            }
        }
    }

    /// <summary>
    ///     Writes two triangles per ribbon segment, so every quad is closed and no vertex is
    ///     left unreferenced.
    /// </summary>
    /// <param name="tris">Destination index buffer.</param>
    /// <param name="indexBase">First index slot to write.</param>
    /// <param name="vertexBase">First vertex slot the strand occupies.</param>
    public static void WriteIndices(int[] tris, int indexBase, int vertexBase)
    {
        WriteStripIndices(tris, indexBase, vertexBase, SectorCount);
    }

    /// <summary>Two triangles per segment of a two-vertices-per-point strip.</summary>
    private static void WriteStripIndices(int[] tris, int indexBase, int vertexBase, int segmentCount)
    {
        for (int segment = 0; segment < segmentCount; segment++)
        {
            int vertex = vertexBase + (segment * VerticesPerPoint);
            int index = indexBase + (segment * 6);

            tris[index] = vertex;
            tris[index + 1] = vertex + 1;
            tris[index + 2] = vertex + 2;
            tris[index + 3] = vertex + 2;
            tris[index + 4] = vertex + 1;
            tris[index + 5] = vertex + 3;
        }
    }

    /// <summary>
    ///     Finds the nearest non-degenerate segment on one side of a point. A fully collapsed
    ///     chain returns zero so its geometry collapses with it instead of choosing an arbitrary
    ///     visible direction.
    /// </summary>
    private static Vector3 FindSegmentDirection(
        Vector3[] points,
        int segment,
        int step)
    {
        while (segment >= 0 && segment < SectorCount)
        {
            Vector3 delta = points[segment + 1] - points[segment];
            Vector2 flat = new(delta.x, delta.y);
            if (flat.sqrMagnitude >= DegenerateDirectionSquared)
            {
                return new Vector3(flat.x, flat.y, 0f).normalized;
            }

            segment += step;
        }

        return Vector3.zero;
    }

    /// <summary>Offset of one point from the centre line, sized to half the ribbon width.</summary>
    /// <remarks>
    ///     Averaging two adjacent directions bisects the turn, so the length has to be mitred or
    ///     the ribbon pinches at every bend.
    /// </remarks>
    private static Vector3 MitredOffset(
        Vector3 previousDirection,
        Vector3 nextDirection,
        float offsetLength)
    {
        if (previousDirection == Vector3.zero)
        {
            return Perpendicular(nextDirection, offsetLength);
        }

        if (nextDirection == Vector3.zero)
        {
            return Perpendicular(previousDirection, offsetLength);
        }

        Vector3 bisector = previousDirection + nextDirection;
        if (bisector.sqrMagnitude < DegenerateDirectionSquared)
        {
            // A hard fold: the directions cancel, so there is no bisector to speak of.
            return Perpendicular(nextDirection, offsetLength);
        }

        Vector3 direction = bisector.normalized;
        float cosHalfTurn = Mathf.Clamp(Vector3.Dot(direction, nextDirection), 0f, 1f);
        float mitre = cosHalfTurn > MinCosHalfTurn
            ? Mathf.Min(1f / cosHalfTurn, MaxMiterScale)
            : MaxMiterScale;

        return Perpendicular(direction, offsetLength) * mitre;
    }

    /// <summary>Unit left normal of a direction.</summary>
    private static Vector3 PerpendicularUnit(Vector3 direction) => new(-direction.y, direction.x, 0f);

    /// <summary>Left normal of a direction, scaled to the given offset length.</summary>
    private static Vector3 Perpendicular(Vector3 direction, float offsetLength) =>
        PerpendicularUnit(direction) * offsetLength;
}

#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace Kern.World.Lighting;

internal sealed class LightingRuntimeState
{
    public bool FieldDirty { get; set; } = true;
    public bool CompositeDirty { get; set; } = true;
    public bool HasRenderedLightState { get; set; }
    public bool HasStaticRadianceState { get; set; }
    public bool HasDynamicRadianceState { get; set; }
    public RectInt LastDynamicReceiverRect { get; set; }
    public bool WasLightingBypassed { get; set; }
    public Vector4 LastVisibleRegion { get; set; } = new(float.NaN, float.NaN, float.NaN, float.NaN);
    public ulong LastTerrainGeometryRevision { get; set; }
    // Latest revision transferred through the contiguous terrain journal.
    // A matching revision has complete spatial invalidation records; an
    // unjournaled revision still requires the conservative full rebuild.
    public ulong StagedTerrainGeometryRevision { get; set; }
    public ulong LastContributorGeometryRevision { get; set; }
    public ulong SolveCount { get; set; }
    public float RequestedPixelsPerCell { get; set; }
    public float EffectivePixelsPerCell { get; set; }
    public bool TextureDimensionLimited { get; set; }
    public bool CascadeBudgetLimited { get; set; }

    // Geometry in the stable padding can affect visible rays immediately.
    // The coordinator activates the complete transport region plus its halo.
    private readonly List<RectInt> _pendingRegionInvalidations = new(8);
    private readonly List<RectInt> _activeRegionInvalidations = new(8);

    public IReadOnlyList<RectInt> ActiveRegionInvalidations => _activeRegionInvalidations;

    public void QueueRegionInvalidation(RectInt region)
    {
        if (region.width <= 0 || region.height <= 0)
        {
            return;
        }

        for (int index = 0; index < _pendingRegionInvalidations.Count; index++)
        {
            RectInt existing = _pendingRegionInvalidations[index];
            if (!existing.Equals(region))
            {
                continue;
            }

            return;
        }

        _pendingRegionInvalidations.Add(region);
    }

    public bool ActivatePendingRegionIfVisible(RectInt visibleRegion)
    {
        return ActivatePendingRegionsBudgeted(visibleRegion, maxAreaCells: int.MaxValue);
    }

    public bool ActivatePendingRegionsBudgeted(RectInt visibleRegion, int maxAreaCells)
    {
        bool activated = false;
        long allocatedArea = 0;
        if (_pendingRegionInvalidations.Count == 0)
        {
            return false;
        }
        if (_pendingRegionInvalidations.Count > 1)
        {
            SortPendingRegions(visibleRegion);
        }

        for (int index = _pendingRegionInvalidations.Count - 1; index >= 0; index--)
        {
            RectInt pending = _pendingRegionInvalidations[index];
            if (!Intersects(pending, visibleRegion))
            {
                continue;
            }

            long regionArea = (long)pending.width * pending.height;
            if (allocatedArea > 0 && allocatedArea + regionArea > maxAreaCells)
            {
                // Defer further regions to subsequent frames to preserve the frame budget.
                continue;
            }

            _pendingRegionInvalidations.RemoveAt(index);
            _activeRegionInvalidations.Add(pending);
            allocatedArea += regionArea;
            activated = true;
        }

        if (activated)
        {
            FieldDirty = true;
        }

        return activated;
    }

    private void SortPendingRegions(RectInt visibleRegion)
    {
        Vector2 viewportCenter = new(
            visibleRegion.xMin + visibleRegion.width * 0.5f,
            visibleRegion.yMin + visibleRegion.height * 0.5f);

        // Sort pending regions descending by distance so that the nearest regions are at the end,
        // allowing efficient RemoveAt(Count - 1) in O(1).
        _pendingRegionInvalidations.Sort((a, b) =>
        {
            Vector2 centerA = new(a.xMin + a.width * 0.5f, a.yMin + a.height * 0.5f);
            Vector2 centerB = new(b.xMin + b.width * 0.5f, b.yMin + b.height * 0.5f);
            float distSqA = (centerA.x - viewportCenter.x) * (centerA.x - viewportCenter.x) +
                (centerA.y - viewportCenter.y) * (centerA.y - viewportCenter.y);
            float distSqB = (centerB.x - viewportCenter.x) * (centerB.x - viewportCenter.x) +
                (centerB.y - viewportCenter.y) * (centerB.y - viewportCenter.y);
            return distSqB.CompareTo(distSqA);
        });

    }

    public void ClearPendingRegionInvalidation()
    {
        _pendingRegionInvalidations.Clear();
        _activeRegionInvalidations.Clear();
    }

    // Rebuilding a moved material field makes every pending overlapping edit
    // current at once. Transfer all of them to this solve before publishing;
    // deferring them would reuse old intervals against the new geometry.
    public void RetainPendingRegionsForReuse(RectInt field)
    {
        for (int index = _pendingRegionInvalidations.Count - 1; index >= 0; index--)
        {
            RectInt pending = _pendingRegionInvalidations[index];
            _pendingRegionInvalidations.RemoveAt(index);
            if (Intersects(pending, field))
            {
                _activeRegionInvalidations.Add(pending);
                FieldDirty = true;
            }
        }
    }

    public void CompleteActiveRegionInvalidation()
    {
        _activeRegionInvalidations.Clear();
    }

    private static bool Intersects(RectInt left, RectInt right)
    {
        return left.xMin < right.xMax &&
            left.xMax > right.xMin &&
            left.yMin < right.yMax &&
            left.yMax > right.yMin;
    }
}

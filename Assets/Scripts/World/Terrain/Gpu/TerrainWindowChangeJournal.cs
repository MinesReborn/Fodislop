#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Kern.Core;
using Kern.World.Streaming;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain;

internal enum TerrainWindowWorldChangeResult
{
    None,
    RefreshRequired,
    RegionRecorded,
}

/// <summary>Owns terrain edit invalidations and their publication acknowledgements.</summary>
internal sealed class TerrainWindowChangeJournal
{
    private readonly TerrainDirtyTracker _dirty = new();
    private readonly DirtyRectSet _publishedChangedRegions = new();
    private HashSet<CellType> _pendingTextureCellTypes = [];
    private HashSet<CellType> _buildTextureCellTypes = [];
    private List<RectInt> _changedRegions = [];
    private List<RectInt> _buildChangedRegions = [];
    private long _oldestChangeTimestamp;
    private bool? _requestedDistortion;
    private TerrainDistortionStyle? _requestedDistortionStyle;

    public TerrainDirtyTracker Dirty => _dirty;

    public bool NeedsRefresh { get; set; }

    public bool RebuildAllCells { get; set; }

    public HashSet<CellType> PendingTextureCellTypes => _pendingTextureCellTypes;

    public HashSet<CellType> BuildTextureCellTypes => _buildTextureCellTypes;

    public List<RectInt> ChangedRegions => _changedRegions;

    public List<RectInt> BuildChangedRegions => _buildChangedRegions;

    private bool HasPublishedChangedRegions => _publishedChangedRegions.Count > 0;

    private int PublishedChangedRegionCount => _publishedChangedRegions.Count;

    private RectInt GetPublishedChangedRegion(int index) => _publishedChangedRegions[index];

    public void AddChangedRegion(RectInt region) => _changedRegions.Add(region);

    public void ClearDirty() => _dirty.Clear();

    public void SwapTextureTypes()
    {
        (_buildTextureCellTypes, _pendingTextureCellTypes) =
            (_pendingTextureCellTypes, _buildTextureCellTypes);
        _pendingTextureCellTypes.Clear();
    }

    public void ClearBuildTextureCellTypes() => _buildTextureCellTypes.Clear();

    public void ClearChangedRegions() => _changedRegions.Clear();

    public void AddPublishedChangedRegion(RectInt region, RectInt publishedBounds) =>
        _publishedChangedRegions.Add(region, publishedBounds);

    private void ClearPublishedChangedRegions() => _publishedChangedRegions.Clear();

    public void SwapBuildChanges()
    {
        (_buildChangedRegions, _changedRegions) = (_changedRegions, _buildChangedRegions);
        _changedRegions.Clear();
    }

    public void TakePublishedChangedRegions(List<RectInt> destination)
    {
        const int MaximumSeparateRegions = 16;
        if (!HasPublishedChangedRegions)
        {
            return;
        }

        int count = PublishedChangedRegionCount;
        if (count == 0)
        {
            return;
        }

        if (count <= MaximumSeparateRegions)
        {
            for (int index = 0; index < count; index++)
            {
                destination.Add(GetPublishedChangedRegion(index));
            }
        }
        else
        {
            RectInt bounds = GetPublishedChangedRegion(0);
            for (int index = 1; index < count; index++)
            {
                RectInt rect = GetPublishedChangedRegion(index);
                int minX = Math.Min(bounds.xMin, rect.xMin);
                int minY = Math.Min(bounds.yMin, rect.yMin);
                int maxX = Math.Max(bounds.xMax, rect.xMax);
                int maxY = Math.Max(bounds.yMax, rect.yMax);
                bounds = new RectInt(minX, minY, maxX - minX, maxY - minY);
            }

            destination.Add(bounds);
        }

        ClearPublishedChangedRegions();
    }

    public TerrainWindowWorldChangeResult RecordWorldChange(
        int serverX,
        int serverY,
        int width,
        int height,
        int worldHeight,
        bool hasOrigin,
        bool buildInFlight,
        Vector2Int buildOrigin,
        int windowWidth,
        int windowHeight)
    {
        if (!hasOrigin && !buildInFlight)
        {
            NeedsRefresh = true;
            return TerrainWindowWorldChangeResult.RefreshRequired;
        }

        if (_dirty.Add(
            serverX,
            serverY,
            width,
            height,
            buildOrigin,
            windowWidth,
            windowHeight,
            worldHeight) is not { } region)
        {
            return TerrainWindowWorldChangeResult.None;
        }

        AddChangedRegion(region);
        if (_oldestChangeTimestamp == 0)
        {
            _oldestChangeTimestamp = Stopwatch.GetTimestamp();
        }

        return TerrainWindowWorldChangeResult.RegionRecorded;
    }

    public long TakeOldestChangeTimestamp()
    {
        long timestamp = _oldestChangeTimestamp;
        _oldestChangeTimestamp = 0;
        return timestamp;
    }

    public void RestoreOldestChangeTimestamp(long timestamp)
    {
        if (timestamp != 0 && (_oldestChangeTimestamp == 0 || timestamp < _oldestChangeTimestamp))
        {
            _oldestChangeTimestamp = timestamp;
        }
    }

    public void ClearWorldChanges()
    {
        _dirty.Clear();
        _oldestChangeTimestamp = 0;
    }

    public void RequestDistortion(bool enabled)
    {
        _requestedDistortion = enabled;
        NeedsRefresh = true;
    }

    public void RequestDistortionStyle(TerrainDistortionStyle style)
    {
        _requestedDistortionStyle = style;
        NeedsRefresh = true;
    }

    public bool TryTakeRequestedDistortion(out bool enabled)
    {
        if (_requestedDistortion is not { } requested)
        {
            enabled = false;
            return false;
        }

        enabled = requested;
        _requestedDistortion = null;
        return true;
    }

    public bool TryTakeRequestedDistortionStyle(out TerrainDistortionStyle style)
    {
        if (_requestedDistortionStyle is not { } requested)
        {
            style = default;
            return false;
        }

        style = requested;
        _requestedDistortionStyle = null;
        return true;
    }

    public bool IsNearBuildWindow(
        RectInt unityRect,
        int marginCells,
        bool isInitialized,
        bool hasOrigin,
        bool buildInFlight,
        Vector2Int buildOrigin,
        int width,
        int height)
    {
        if (!isInitialized || (!hasOrigin && !buildInFlight))
        {
            return true;
        }

        return unityRect.xMax > buildOrigin.x - marginCells &&
            unityRect.xMin < buildOrigin.x + width + marginCells &&
            unityRect.yMax > buildOrigin.y - marginCells &&
            unityRect.yMin < buildOrigin.y + height + marginCells;
    }

    public bool ShouldCoalesceDirtyRects(Vector2Int buildOrigin, int width, int height) =>
        RebuildAllCells || !_dirty.IsEmpty && _dirty.PrefersFullRebuild(buildOrigin, width, height);
}

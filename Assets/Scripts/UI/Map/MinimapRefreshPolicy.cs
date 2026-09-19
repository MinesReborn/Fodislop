#nullable enable

using UnityEngine;

namespace Kern.UI;

public sealed class MinimapRefreshPolicy
{
    public const float UpdateDelaySeconds = 0.1f;

    private float _lastUpdateTime = -1f;
    private Vector2Int _lastUpdatePos;
    private long _lastRenderedStorageRevision = -1;
    private bool _pendingPlayerMoveRefresh;
    private bool _chunkLoadRefreshRequested;
    private bool _initialRefreshDone;
    private bool _lastRefreshHadLoadedCells;

    public bool InitialRefreshDone => _initialRefreshDone;

    public bool CanRefresh(float currentTime)
    {
        return _lastUpdateTime < 0f || currentTime - _lastUpdateTime >= UpdateDelaySeconds - 0.0001f;
    }

    public void NotifyPlayerMoved(Vector2Int newPos, float currentTime, out bool shouldRefreshNow)
    {
        if (CanRefresh(currentTime))
        {
            _pendingPlayerMoveRefresh = false;
            _lastUpdatePos = newPos;
            shouldRefreshNow = true;
        }
        else
        {
            _pendingPlayerMoveRefresh = true;
            shouldRefreshNow = false;
        }
    }

    public void NotifyChunkLoaded()
    {
        _chunkLoadRefreshRequested = true;
    }

    public bool ShouldRefreshOnStorageOrMove(
        float currentTime,
        long currentStorageRevision,
        bool isReady,
        bool isVisible,
        bool hasServerPosition)
    {
        if (!isReady || !_initialRefreshDone || !isVisible || !hasServerPosition)
        {
            return false;
        }

        bool hasRevisionChange = currentStorageRevision != _lastRenderedStorageRevision;
        if ((_pendingPlayerMoveRefresh || hasRevisionChange) && CanRefresh(currentTime))
        {
            _pendingPlayerMoveRefresh = false;
            return true;
        }

        return false;
    }

    public bool ShouldRefreshOnChunkLoad(
        float currentTime,
        bool isReady,
        bool isVisible,
        bool hasServerPosition)
    {
        if (_chunkLoadRefreshRequested && isReady && isVisible && hasServerPosition && CanRefresh(currentTime))
        {
            _chunkLoadRefreshRequested = false;
            return true;
        }

        return false;
    }

    public void RecordRefresh(float currentTime, long storageRevision, bool hadLoadedCells)
    {
        _lastUpdateTime = currentTime;
        _lastRenderedStorageRevision = storageRevision;
        _lastRefreshHadLoadedCells = hadLoadedCells;
        if (hadLoadedCells)
        {
            _initialRefreshDone = true;
        }
    }

    public void RecordChunkLoadRefresh(float currentTime, long storageRevision, bool hadLoadedCells)
    {
        _lastUpdateTime = currentTime;
        _lastRefreshHadLoadedCells = hadLoadedCells;
        _initialRefreshDone = hadLoadedCells;
        if (_initialRefreshDone)
        {
            _lastRenderedStorageRevision = storageRevision;
        }
    }

    public void RecordInitialRefresh(float currentTime, Vector2Int playerPos, long storageRevision, bool isVisible, bool hadLoadedCells)
    {
        _lastUpdatePos = playerPos;
        _lastUpdateTime = currentTime;
        _lastRefreshHadLoadedCells = hadLoadedCells;
        _initialRefreshDone = !isVisible || hadLoadedCells;
        if (_initialRefreshDone)
        {
            _lastRenderedStorageRevision = storageRevision;
        }
    }

    public void Reset()
    {
        _lastUpdateTime = -1f;
        _lastRenderedStorageRevision = -1;
        _pendingPlayerMoveRefresh = false;
        _chunkLoadRefreshRequested = false;
        _initialRefreshDone = false;
        _lastRefreshHadLoadedCells = false;
    }

    public void InvalidateStorageRevision()
    {
        _lastRenderedStorageRevision = -1;
        _chunkLoadRefreshRequested = true;
    }
}

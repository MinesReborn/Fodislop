#nullable enable

using System;
using Kern.Core.Interfaces;
using UnityEngine;

namespace Kern.World.Streaming;

/// <summary>
/// Owns the presentation lifecycle shared by world entry, camera, terrain,
/// entities, and server effects. Terrain readiness is versioned so an older
/// asynchronous build cannot release a newer teleport.
/// </summary>
public sealed class WorldViewTransition : IWorldPresentationState
{
    private RectInt _readyCells;
    private bool _hasReadyCells;
    private ulong _nextTransitionId;
    private bool _hasPublishedView;

    public WorldPresentationSnapshot Current { get; private set; } = new(
        0,
        WorldPresentationKind.WorldEntry,
        WorldPresentationPhase.Preparing,
        Vector3.zero);

    public bool IsStable => Current.Phase == WorldPresentationPhase.Stable;

    public bool IsTeleportPreparing =>
        Current.Kind == WorldPresentationKind.Teleport &&
        Current.Phase != WorldPresentationPhase.Stable;

    public bool IsTeleportAwaitingDestination =>
        Current.Kind == WorldPresentationKind.Teleport &&
        Current.Phase == WorldPresentationPhase.AwaitingDestination;

    public bool HasPublishedView => _hasPublishedView;

    public event Action<WorldPresentationSnapshot>? Changed;

    public event Action<ulong>? TeleportCommitting;

    public ulong BeginWorldEntry()
    {
        ulong transitionId = checked(++_nextTransitionId);
        _hasPublishedView = false;
        _hasReadyCells = false;
        SetCurrent(new WorldPresentationSnapshot(
            transitionId,
            WorldPresentationKind.WorldEntry,
            WorldPresentationPhase.Preparing,
            Vector3.zero));
        return transitionId;
    }

    public bool PublishWorldEntry(ulong transitionId)
    {
        if (transitionId == 0 || Current.TransitionId != transitionId ||
            Current.Kind != WorldPresentationKind.WorldEntry ||
            Current.Phase != WorldPresentationPhase.Preparing)
        {
            return false;
        }

        _hasPublishedView = true;
        SetCurrent(Current with { Phase = WorldPresentationPhase.Stable });
        return true;
    }

    public bool TryBeginTeleport(Vector3 destination, out ulong transitionId)
    {
        transitionId = 0;
        if (!IsStable && !IsTeleportPreparing)
        {
            return false;
        }

        transitionId = checked(++_nextTransitionId);
        _hasReadyCells = false;
        SetCurrent(new WorldPresentationSnapshot(
            transitionId,
            WorldPresentationKind.Teleport,
            WorldPresentationPhase.Preparing,
            destination));
        return true;
    }

    public bool TryBeginTeleportRequest(Vector3 currentView, out ulong transitionId)
    {
        transitionId = 0;
        if (!IsStable)
        {
            return false;
        }

        transitionId = checked(++_nextTransitionId);
        _hasReadyCells = false;
        SetCurrent(new WorldPresentationSnapshot(
            transitionId,
            WorldPresentationKind.Teleport,
            WorldPresentationPhase.AwaitingDestination,
            currentView));
        return true;
    }

    public bool ConfirmTeleportRequest(ulong transitionId, Vector3 destination)
    {
        if (transitionId == 0 || transitionId != Current.TransitionId ||
            !IsTeleportAwaitingDestination)
        {
            return false;
        }

        SetCurrent(Current with
        {
            Phase = WorldPresentationPhase.Preparing,
            Destination = destination,
        });
        return true;
    }

    public bool CancelTeleportRequest(ulong transitionId)
    {
        if (transitionId == 0 || transitionId != Current.TransitionId ||
            !IsTeleportAwaitingDestination)
        {
            return false;
        }

        SetCurrent(Current with { Phase = WorldPresentationPhase.Stable });
        return true;
    }

    public bool UpdateTeleportDestination(ulong transitionId, Vector3 destination)
    {
        if (transitionId == 0 || Current.TransitionId != transitionId ||
            Current.Phase is not (WorldPresentationPhase.Preparing or WorldPresentationPhase.ReadyToCommit))
        {
            return false;
        }

        if (Current.Destination == destination)
        {
            return true;
        }

        _hasReadyCells = false;
        SetCurrent(Current with
        {
            Phase = WorldPresentationPhase.Preparing,
            Destination = destination,
        });
        return true;
    }

    public bool MarkTeleportReady(ulong transitionId, RectInt readyCells)
    {
        if (readyCells.width <= 0 || readyCells.height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(readyCells), "Teleport readiness requires a non-empty world-cell region.");
        }

        if (transitionId == 0 || Current.TransitionId != transitionId ||
            Current.Phase is not (WorldPresentationPhase.Preparing or WorldPresentationPhase.ReadyToCommit))
        {
            return false;
        }

        _readyCells = readyCells;
        _hasReadyCells = true;
        SetCurrent(Current with { Phase = WorldPresentationPhase.ReadyToCommit });
        return true;
    }

    public bool ClearTeleportReady(ulong transitionId)
    {
        if (transitionId == 0 || Current.TransitionId != transitionId ||
            Current.Phase is not (WorldPresentationPhase.Preparing or WorldPresentationPhase.ReadyToCommit))
        {
            return false;
        }

        if (!_hasReadyCells && Current.Phase == WorldPresentationPhase.Preparing)
        {
            return true;
        }

        _hasReadyCells = false;
        SetCurrent(Current with { Phase = WorldPresentationPhase.Preparing });
        return true;
    }

    public bool IsTeleportReadyFor(
        ulong transitionId,
        Vector3 position,
        float halfWidthCells,
        float halfHeightCells,
        float cellSize)
    {
        if (transitionId == 0 || transitionId != Current.TransitionId ||
            Current.Phase != WorldPresentationPhase.ReadyToCommit || !_hasReadyCells)
        {
            return false;
        }

        if (cellSize <= 0f || halfWidthCells < 0f || halfHeightCells < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize));
        }

        int minX = Mathf.FloorToInt((position.x / cellSize) - halfWidthCells);
        int minY = Mathf.FloorToInt((position.y / cellSize) - halfHeightCells);
        int maxX = Mathf.CeilToInt((position.x / cellSize) + halfWidthCells);
        int maxY = Mathf.CeilToInt((position.y / cellSize) + halfHeightCells);
        return minX >= _readyCells.xMin && minY >= _readyCells.yMin &&
            maxX <= _readyCells.xMax && maxY <= _readyCells.yMax;
    }

    public bool CommitTeleport(
        ulong transitionId,
        Vector3 position,
        float halfWidthCells,
        float halfHeightCells,
        float cellSize)
    {
        if (transitionId == 0 || transitionId != Current.TransitionId ||
            Current.Kind != WorldPresentationKind.Teleport ||
            Current.Phase != WorldPresentationPhase.ReadyToCommit || !_hasReadyCells ||
            !IsTeleportReadyFor(transitionId, position, halfWidthCells, halfHeightCells, cellSize))
        {
            return false;
        }

        TeleportCommitting?.Invoke(transitionId);
        _hasPublishedView = true;
        _hasReadyCells = false;
        SetCurrent(Current with { Phase = WorldPresentationPhase.Stable });
        return true;
    }

    public bool FailTransition(ulong transitionId)
    {
        if (transitionId == 0 || transitionId != Current.TransitionId ||
            Current.Phase == WorldPresentationPhase.Stable ||
            Current.Phase == WorldPresentationPhase.Failed)
        {
            return false;
        }

        _hasReadyCells = false;
        SetCurrent(Current with { Phase = WorldPresentationPhase.Failed });
        return true;
    }

    private void SetCurrent(WorldPresentationSnapshot snapshot)
    {
        Current = snapshot;
        Changed?.Invoke(snapshot);
    }
}

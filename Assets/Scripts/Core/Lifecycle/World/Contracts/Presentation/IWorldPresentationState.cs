#nullable enable

using System;
using UnityEngine;

namespace Kern.Core.Interfaces;

public enum WorldPresentationKind
{
    WorldEntry,
    Teleport,
}

public enum WorldPresentationPhase
{
    AwaitingDestination,
    Preparing,
    ReadyToCommit,
    Stable,
    Failed,
}

/// <summary>
/// Immutable identity and state of the currently prepared world presentation.
/// TransitionId increases for every world entry or teleport and rejects stale
/// readiness reports from superseded work.
/// </summary>
public readonly record struct WorldPresentationSnapshot(
    ulong TransitionId,
    WorldPresentationKind Kind,
    WorldPresentationPhase Phase,
    Vector3 Destination);

/// <summary>
/// Shared lifecycle contract for world entry and in-world presentation changes.
/// Positions are Unity world units (Y up); ready regions are half-open world-cell
/// rectangles. Calls and notifications occur on Unity's main thread. A teleport
/// remains unpublished until the current transition is committed.
/// </summary>
public interface IWorldPresentationState
{
    WorldPresentationSnapshot Current { get; }

    bool IsStable { get; }

    bool IsTeleportPreparing { get; }

    bool IsTeleportAwaitingDestination { get; }

    bool HasPublishedView { get; }

    event Action<WorldPresentationSnapshot>? Changed;

    /// <summary>Raised before a prepared teleport changes the current presentation.</summary>
    event Action<ulong>? TeleportCommitting;

    ulong BeginWorldEntry();

    bool PublishWorldEntry(ulong transitionId);

    bool TryBeginTeleport(Vector3 destination, out ulong transitionId);

    bool TryBeginTeleportRequest(Vector3 currentView, out ulong transitionId);

    bool ConfirmTeleportRequest(ulong transitionId, Vector3 destination);

    bool CancelTeleportRequest(ulong transitionId);

    bool UpdateTeleportDestination(ulong transitionId, Vector3 destination);

    bool MarkTeleportReady(ulong transitionId, RectInt readyCells);

    bool ClearTeleportReady(ulong transitionId);

    bool IsTeleportReadyFor(
        ulong transitionId,
        Vector3 position,
        float halfWidthCells,
        float halfHeightCells,
        float cellSize);

    bool CommitTeleport(
        ulong transitionId,
        Vector3 position,
        float halfWidthCells,
        float halfHeightCells,
        float cellSize);

    bool FailTransition(ulong transitionId);
}

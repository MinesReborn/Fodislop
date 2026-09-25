#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Lifecycle;
using Kern.Core.Interfaces;
using Kern.Core.Interfaces.Diagnostics;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain;

public enum TerrainBuildState
{
    WaitingForData,
    CpuPreparing,
    WaitingForPublication,
    Published,
    Canceled,
    Error,
}

/// <summary>Stable public API for the terrain window lifecycle owner.</summary>
public sealed class TerrainWindow : IDisposable
{
    private readonly TerrainWindowBuildLifecycle _lifecycle = new();

    public TerrainBuildDriver Driver => _lifecycle.Driver;

    public TerrainDirtyTracker Dirty => _lifecycle.Dirty;

    public HashSet<CellType> PendingTextureCellTypes => _lifecycle.PendingTextureCellTypes;

    public Mesh? CellIDMesh => _lifecycle.CellIDMesh;

    public Vector2Int Origin => _lifecycle.Origin;

    public int Width => _lifecycle.Width;

    public int Height => _lifecycle.Height;

    public bool IsInitialized => _lifecycle.IsInitialized;

    public bool CellsCommitted => _lifecycle.CellsCommitted;

    public ulong PublishedContentRevision => _lifecycle.PublishedContentRevision;

    public bool NeedsRefresh
    {
        get => _lifecycle.NeedsRefresh;
        set => _lifecycle.NeedsRefresh = value;
    }

    public bool HasOrigin => _lifecycle.HasOrigin;

    public bool HasCpuBuildInFlight => _lifecycle.HasCpuBuildInFlight;

    public bool HasUnpublishedTextureRefresh => _lifecycle.HasUnpublishedTextureRefresh;

    public TerrainBuildState BuildState => _lifecycle.BuildState;

    public float EstimatedPreparationSeconds => _lifecycle.EstimatedPreparationSeconds;

    public bool HoldPublication
    {
        get => _lifecycle.HoldPublication;
        set => _lifecycle.HoldPublication = value;
    }

    public Vector2Int? HeldOrigin => _lifecycle.HeldOrigin;

    public Vector2Int ProspectiveOrigin => _lifecycle.ProspectiveOrigin;

    public void Attach(
        Transform transform,
        ISceneObjectFactory sceneObjects,
        string sortingLayerName,
        int doorOverlaySortingOrder,
        float cellSize) =>
        _lifecycle.Attach(transform, sceneObjects, sortingLayerName, doorOverlaySortingOrder, cellSize);

    public void ApplyDimensions(Vector2Int size, bool dimensionsChanged) =>
        _lifecycle.ApplyDimensions(size, dimensionsChanged);

    public void CoalesceDirtyRects() => _lifecycle.CoalesceDirtyRects();

    public bool RecordWorldChange(int serverX, int serverY, int width, int height, int worldHeight) =>
        _lifecycle.RecordWorldChange(serverX, serverY, width, height, worldHeight);

    public bool IsNearBuildWindow(RectInt unityRect, int marginCells) =>
        _lifecycle.IsNearBuildWindow(unityRect, marginCells);

    public void InvalidateWorld() => _lifecycle.InvalidateWorld();

    public void RequestDistortion(bool enabled) => _lifecycle.RequestDistortion(enabled);

    public void RequestDistortionStyle(TerrainDistortionStyle style) =>
        _lifecycle.RequestDistortionStyle(style);

    public void TakePublishedChangedRegions(List<RectInt> destination) =>
        _lifecycle.TakePublishedChangedRegions(destination);

    public bool TryPublishCompleted(in TerrainBuildServices services, out Exception? failure) =>
        _lifecycle.TryPublishCompleted(services, out failure);

    public bool Process(
        in TerrainBuildServices services,
        IClientConfigManager clientConfigManager,
        Vector2Int requestedOrigin,
        bool dimensionsChanged,
        bool bypassCpuMeshRebuild,
        MeshRenderer? meshRenderer,
        ulong contentRevision,
        out Exception? failure) =>
        _lifecycle.Process(
            services,
            clientConfigManager,
            requestedOrigin,
            dimensionsChanged,
            bypassCpuMeshRebuild,
            meshRenderer,
            contentRevision,
            out failure);

    public float Commit() => _lifecycle.Commit();

    public void Dispose() => _lifecycle.Dispose();
}

#nullable enable

using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Interfaces.Diagnostics;
using Kern.Core.Lifecycle;
using UnityEngine;

using Kern.Diagnostics.Frames;

namespace Kern.World.Terrain;

/// <summary>Owns the terrain geometry snapshot that is committed for rendering.</summary>
internal sealed class TerrainWindowPublishedView : System.IDisposable
{
    private readonly TerrainCellIdMesh _cellIdMesh = new();
    internal TerrainVolumeTopology VolumeTopology { get; } = new();
    private Transform? _transform;
    private float _cellSize = 1f;
    private bool _cellTexturesDirty;

    public Vector2Int Origin { get; private set; } = new(int.MinValue, int.MinValue);

    public int Width { get; private set; }

    public int Height { get; private set; }

    public bool IsInitialized { get; private set; }

    public bool CellsCommitted { get; private set; }

    // Почему последняя опубликованная сетка снята с экрана. Живёт до
    // успешной публикации: по нему видно, из-за чего пропал террейн.
    public string? LastWithdrawalReason { get; private set; }

    public ulong PublishedContentRevision { get; private set; }

    public Mesh? CellIdMesh => _cellIdMesh.Mesh;

    public bool HasOrigin => Origin.x != int.MinValue;

    public void Attach(
        Transform transform,
        ISceneObjectFactory sceneObjects,
        string sortingLayerName,
        int doorOverlaySortingOrder,
        float cellSize,
        TerrainBuildDriver driver)
    {
        _transform = transform;
        _cellSize = cellSize;
        driver.Attach(transform, sceneObjects, sortingLayerName, doorOverlaySortingOrder, cellSize);
    }

    public void ApplyDimensions(Vector2Int size, TerrainBuildDriver driver)
    {
        Width = size.x;
        Height = size.y;
        IsInitialized = true;
        Origin = new Vector2Int(int.MinValue, int.MinValue);
        driver.EnsureCapacity(Width, Height);
        _cellIdMesh.EnsureSize(Width, Height, _cellSize);
        FrameEventLog.Record($"террейн: окно пересоздано {Width}×{Height}");
    }

    public void WithdrawPublication(TerrainBuildDriver driver, string reason)
    {
        bool wasVisible = CellsCommitted;
        LastWithdrawalReason = reason;
        CellsCommitted = false;
        _cellTexturesDirty = false;
        driver.HideDoorOverlay();

        // Снятие уже показанного террейна - это пропавший экран, а не штатный
        // шаг: без этой строки чёрный кадр не оставляет в журнале ни следа.
        if (wasVisible)
        {
            Debug.LogWarning($"[TerrainWindow] Published terrain withdrawn: {reason}.");
        }
    }

    public RectInt Publish(TerrainCPUBuildRequest request)
    {
        Origin = request.Origin;
        if (_transform != null)
        {
            _transform.position = new Vector3(
                request.Origin.x * _cellSize,
                request.Origin.y * _cellSize,
                0f);
        }

        _cellTexturesDirty = true;
        PublishedContentRevision = request.ContentRevision;
        return new RectInt(Origin.x - 1, Origin.y - 1, Width + 2, Height + 2);
    }

    public float Commit(TerrainBuildDriver driver)
    {
        if (!_cellTexturesDirty || !HasOrigin || _cellIdMesh.Mesh == null)
        {
            return 0f;
        }

        CellsCommitted = false;
        VolumeTopology.Publish(driver.Pipeline.CellBuilder.Buffers, Origin, Width, Height, driver.Pipeline.EnableDistortion);
        float uploadMs = driver.Commit(Origin.x, Origin.y);
        _cellTexturesDirty = false;
        CellsCommitted = true;
        LastWithdrawalReason = null;
        return uploadMs;
    }

    public void Dispose() => _cellIdMesh.Dispose();
}

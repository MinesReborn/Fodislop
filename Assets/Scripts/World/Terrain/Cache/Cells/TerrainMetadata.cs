#nullable enable

using Kern.World;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain;
public enum TerrainCellState
{
    Loaded,
    Unloaded,
    OutsideWorld,
}

public struct CachedCellData
{
    public TerrainCellState State;
    public CellType Type;
    public bool IsBackground;
    public CellOutline Outline;
    public int AtlasIndex;
    public Vector4 AtlasRect;
    public bool HasTileGroup;
    public int TileGroupId;
    public byte ReliefGroup;
    public int AnimationFrameCount;
    public bool IsTextureReady;
}

public struct CellMetadata
{
    public bool IsPopulated;
    public CellOutline Outline;
    public int AtlasIndex;
    public Vector4 AtlasRect;
    public bool HasTileGroup;
    public int TileGroupId;
    public byte DecalTextureIndex;
    public byte ReliefGroup;
    public int AnimationFrameCount;
    public bool IsTextureReady;
}

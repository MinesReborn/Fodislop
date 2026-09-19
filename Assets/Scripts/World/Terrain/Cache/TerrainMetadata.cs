#nullable enable

using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
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
    public CellConfigProperties Properties;
    public byte ReliefGroup;
    public CellDistortionType Distortion;
    public bool HasTileGroup;
    public int TileGroupID;
    public Color32 MinimapColor; // was Color (16 bytes) — Color32 (4 bytes) sufficient for minimap
    public CellAnimationType Animation;
    public float AnimationSpeed;
    public Vector4 AtlasRect;
    public int AtlasIndex;
    public float UVTileSize;
    public int AnimationFrameCount;
    public float FrameHeightTiles;
    public bool IsTextureReady;
}

public struct CellMetadata
{
    public CellConfigProperties Properties;
    public byte ReliefGroup;
    public CellDistortionType Distortion;
    public bool HasTileGroup;
    public int TileGroupID;
    public Color32 MinimapColor; // was Color (16 bytes) — Color32 (4 bytes) sufficient for minimap
    public CellAnimationType Animation;
    public float AnimationSpeed;
    public Vector4 AtlasRect;
    public int AtlasIndex;
    public float UVTileSize;
    public int AnimationFrameCount;
    public float FrameHeightTiles;
    public bool IsTextureReady;
    public bool IsPopulated;
}

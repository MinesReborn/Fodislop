#nullable enable

using Kern.Core;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>Read-only inputs shared by the geometry and cell-mask stages.</summary>
public readonly record struct TerrainPrecalculationInput(
    ITerrainCellDataSource CellData,
    Vector2Int WindowSize,
    Vector2Int WorldSize);

public class TerrainPrecalculator
{
    private readonly TerrainVertexDistortionCalculator _distortion = new();
    private readonly TerrainCellMaskCalculator _cellMask = new();

    public TerrainRingGrid<TerrainVertexOffset> GridVertexOffsets => _distortion.GridVertexOffsets;

    public TerrainRingGrid<int> CellTilingDescriptors => _cellMask.CellTilingDescriptors;

    public TerrainRingGrid<int> CellCornerVariants => _cellMask.CellCornerVariants;

    public TerrainRingGrid<byte> CellReliefMasks => _cellMask.CellReliefMasks;

    public TerrainRingGrid<byte> CellReliefCornerMasks => _cellMask.CellReliefCornerMasks;

    public TerrainRingGrid<byte> CellSolidBoundaryMasks => _cellMask.CellSolidBoundaryMasks;

    public bool EnableDistortion
    {
        get => _distortion.EnableDistortion;
        set => _distortion.EnableDistortion = value;
    }

    public TerrainDistortionStyle DistortionStyle
    {
        get => _distortion.DistortionStyle;
        set => _distortion.DistortionStyle = value;
    }

    public void EnsureCapacity(int meshWidth, int meshHeight)
    {
        _distortion.EnsureCapacity(meshWidth, meshHeight);
        _cellMask.EnsureCapacity(meshWidth, meshHeight);
    }

    public void PrecalculateFull(in TerrainPrecalculationInput input)
    {
        _distortion.PrecalculateFull(
            input.CellData,
            input.WindowSize.x,
            input.WindowSize.y,
            input.WorldSize.x,
            input.WorldSize.y);
        _cellMask.PrecalculateFull(input.CellData, input.WindowSize.x, input.WindowSize.y);
    }

    public void PrecalculateRegion(
        in TerrainPrecalculationInput input,
        TerrainWindowCellRegion region)
    {
        _distortion.PrecalculateRegion(
            input.CellData,
            input.WindowSize.x,
            input.WindowSize.y,
            region.StartX,
            region.StartY,
            region.CountX,
            region.CountY,
            input.WorldSize.x,
            input.WorldSize.y);
        _cellMask.PrecalculateRegion(
            input.CellData,
            input.WindowSize.x,
            input.WindowSize.y,
            region.StartX,
            region.StartY,
            region.CountX,
            region.CountY);
    }

    public void PrecalculateIncremental(
        in TerrainPrecalculationInput input,
        Vector2Int scrollDelta)
    {
        _distortion.PrecalculateIncremental(
            input.CellData,
            input.WindowSize.x,
            input.WindowSize.y,
            scrollDelta.x,
            scrollDelta.y,
            input.WorldSize.x,
            input.WorldSize.y);
        _cellMask.PrecalculateIncremental(
            input.CellData,
            input.WindowSize.x,
            input.WindowSize.y,
            scrollDelta.x,
            scrollDelta.y);
    }
}

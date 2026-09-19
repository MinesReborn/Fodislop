#nullable enable

using System.Collections.Generic;
using MinesServer.Data;
using UnityEngine;

namespace Kern.Networking.Buildings;
public sealed class Gun : PackBuilding
{
    public override PackType Type => PackType.Gun;

    public override Vector2 RoofCenterOffsetCells => new(0f, 0f);

    public override IEnumerable<((int X, int Y) Pos, CellType Cell)> CellsToPlace()
    {
        yield return ((0, 0), CellType.BuildingRoad);
        yield return ((1, 0), CellType.BuildingRoad);
        yield return ((2, 0), CellType.BuildingRoad);
        yield return ((-1, 0), CellType.BuildingRoad);
        yield return ((-2, 0), CellType.BuildingRoad);
        yield return ((0, -1), CellType.BuildingRoad);
        yield return ((0, -2), CellType.BuildingRoad);
        yield return ((0, 1), CellType.BuildingRoad);
        yield return ((0, 2), CellType.BuildingRoad);
        yield return ((1, 1), CellType.BuildingWall);
        yield return ((-1, 1), CellType.BuildingWall);
        yield return ((1, -1), CellType.BuildingWall);
        yield return ((-1, -1), CellType.BuildingWall);
    }
}

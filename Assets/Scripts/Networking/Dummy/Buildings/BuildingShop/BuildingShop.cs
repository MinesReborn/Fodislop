#nullable enable

using System.Collections.Generic;
using MinesServer.Data;

namespace Kern.Networking.Dummy;
public sealed class BuildingShop : PackBuilding
{
    public override BuildingType Type => BuildingType.BombShop;


    public override IEnumerable<((int X, int Y) Pos, CellType Cell)> CellsToPlace()
    {
        yield return ((0, 0), CellType.BuildingDoor);
        yield return ((0, 1), CellType.BuildingDoor);
        yield return ((0, 2), CellType.BuildingRoad);
        yield return ((0, 3), CellType.BuildingRoad);
        yield return ((-2, -1), CellType.BuildingCorner);
        yield return ((-1, -1), CellType.BuildingWall);
        yield return ((0, -1), CellType.BuildingWall);
        yield return ((1, -1), CellType.BuildingWall);
        yield return ((2, -1), CellType.BuildingCorner);
        yield return ((-2, 0), CellType.BuildingWall);
        yield return ((-1, 0), CellType.BuildingWall);
        yield return ((1, 0), CellType.BuildingWall);
        yield return ((2, 0), CellType.BuildingWall);
        yield return ((-2, 1), CellType.BuildingCorner);
        yield return ((-1, 1), CellType.BuildingWall);
        yield return ((1, 1), CellType.BuildingWall);
        yield return ((2, 1), CellType.BuildingCorner);
    }
}

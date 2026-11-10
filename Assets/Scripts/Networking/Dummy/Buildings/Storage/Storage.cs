#nullable enable

using System.Collections.Generic;
using MinesServer.Data;

namespace Kern.Networking.Dummy;
public sealed class Storage : PackBuilding
{
    public override BuildingType Type => BuildingType.Storage;


    public override IEnumerable<((int X, int Y) Pos, CellType Cell)> CellsToPlace()
    {
        yield return ((0, 0), CellType.BuildingDoor);
        yield return ((1, 0), CellType.BuildingWall);
        yield return ((1, -1), CellType.BuildingWall);
        yield return ((-1, -1), CellType.BuildingWall);
        yield return ((0, -1), CellType.BuildingWall);
        yield return ((-1, 0), CellType.BuildingWall);
        yield return ((0, 1), CellType.BuildingRoad);
        yield return ((0, 2), CellType.BuildingRoad);
    }
}

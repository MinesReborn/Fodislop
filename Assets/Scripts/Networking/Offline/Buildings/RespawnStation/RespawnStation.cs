#nullable enable

using System.Collections.Generic;
using MinesServer.Data;

namespace Kern.Networking.Offline;
public sealed class RespawnStation : PackBuilding
{
    public override BuildingType Type => BuildingType.Resp;


    public override IEnumerable<((int X, int Y) Pos, CellType Cell)> CellsToPlace()
    {
        yield return ((0, 0), CellType.BuildingDoor);
        yield return ((1, 0), CellType.BuildingDoor);
        yield return ((-1, 0), CellType.BuildingWall);
        yield return ((0, -1), CellType.BuildingWall);
        yield return ((0, 1), CellType.BuildingWall);
        yield return ((1, 1), CellType.BuildingWall);
        yield return ((-1, 1), CellType.BuildingWall);
        yield return ((1, -1), CellType.BuildingWall);
        yield return ((-1, -1), CellType.BuildingWall);
        yield return ((1, 2), CellType.BuildingWall);
        yield return ((-1, 2), CellType.BuildingWall);
        yield return ((0, 2), CellType.BuildingDoor);
        yield return ((0, 3), CellType.BuildingRoad);
        yield return ((0, 4), CellType.BuildingRoad);
        for (int xx = 2; xx < 6; xx++)
        {
            for (int yy = -1; yy < 3; yy++)
            {
                yield return ((xx, yy), CellType.BuildingRoad);
            }
        }
    }
}

#nullable enable

using System.Collections.Generic;
using MinesServer.Data;

namespace Kern.Networking.Dummy;
public sealed class Teleport : PackBuilding
{
    public override BuildingType Type => BuildingType.Teleport;


    public override IEnumerable<((int X, int Y) Pos, CellType Cell)> CellsToPlace()
    {
        yield return ((0, 0), CellType.BuildingDoor);
        yield return ((0, 1), CellType.BuildingDoor);
        yield return ((1, 0), CellType.BuildingWall);
        yield return ((1, -1), CellType.BuildingWall);
        yield return ((1, 1), CellType.BuildingWall);
        yield return ((-1, -1), CellType.BuildingWall);
        yield return ((-1, 1), CellType.BuildingWall);
        yield return ((0, -1), CellType.BuildingWall);
        yield return ((-1, 0), CellType.BuildingWall);
        for (int x = -1; x <= 1; x++)
        {
            for (int y = 2; y <= 4; y++)
            {
                yield return ((x, y), CellType.BuildingRoad);
            }
        }
    }
}

#nullable enable

using System.Collections.Generic;
using MinesServer.Data;

namespace Kern.Networking.Dummy;
public sealed class Gun : PackBuilding
{
    public override BuildingType Type => BuildingType.Gun;


    public override IEnumerable<((int X, int Y) Pos, CellType Cell)> CellsToPlace()
    {
        // Зеркало сервера (MinesServer: Game/Buildings/Gun.cs): крест из
        // дорог 5x3 без стен.
        yield return ((0, 0), CellType.BuildingRoad);
        yield return ((1, 0), CellType.BuildingRoad);
        yield return ((2, 0), CellType.BuildingRoad);
        yield return ((-1, 0), CellType.BuildingRoad);
        yield return ((-2, 0), CellType.BuildingRoad);
        yield return ((0, -1), CellType.BuildingRoad);
        yield return ((0, -2), CellType.BuildingRoad);
        yield return ((0, 1), CellType.BuildingRoad);
        yield return ((0, 2), CellType.BuildingRoad);
    }
}

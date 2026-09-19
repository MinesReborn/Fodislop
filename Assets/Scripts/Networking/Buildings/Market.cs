#nullable enable

using System.Collections.Generic;
using MinesServer.Data;
using UnityEngine;

namespace Kern.Networking.Buildings;
public sealed class Market : PackBuilding
{
    public override PackType Type => PackType.Market;

    public override Vector2 RoofCenterOffsetCells => new(0f, 0f);

    public override IEnumerable<((int X, int Y) Pos, CellType Cell)> CellsToPlace()
    {
        yield return ((0, 0), CellType.BuildingDoor);
        yield return ((0, 3), CellType.BuildingRoad);
        yield return ((0, 4), CellType.BuildingRoad);
        yield return ((0, -3), CellType.BuildingRoad);
        yield return ((0, -4), CellType.BuildingRoad);
        yield return ((3, 0), CellType.BuildingRoad);
        yield return ((4, 0), CellType.BuildingRoad);
        yield return ((-3, 0), CellType.BuildingRoad);
        yield return ((-4, 0), CellType.BuildingRoad);
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                if (dx == 0 || dy == 0)
                {
                    yield return ((dx, dy), CellType.BuildingDoor);
                    continue;
                }
                else if ((dx * dx) + (dy * dy) == 8)
                {
                    yield return ((dx, dy), CellType.BuildingCorner);
                }
                else
                {
                    yield return ((dx, dy), CellType.BuildingWall);
                }
            }
        }
    }
}

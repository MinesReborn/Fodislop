#nullable enable

using System.Collections.Generic;
using MinesServer.Data;
using UnityEngine;

namespace Kern.Networking.Buildings;
public sealed class ClansPack : PackBuilding
{
    public override PackType Type => PackType.Clans;

    public override Vector2 RoofCenterOffsetCells => new(0f, -0.5f);

    public override IEnumerable<((int X, int Y) Pos, CellType Cell)> CellsToPlace()
    {
        // Зеркало сервера (MinesServer: Game/Buildings/ClansPack.cs): стены
        // рядами y=-1..2, двери (0,0)/(0,1), дорога вниз (0,2..4). (0,2)
        // сервер прокладывает дорогой поверх стены — берём финальный слой.
        yield return ((0, 0), CellType.BuildingDoor);
        yield return ((0, 1), CellType.BuildingDoor);
        yield return ((-2, -1), CellType.BuildingCorner);
        yield return ((-1, -1), CellType.BuildingWall);
        yield return ((0, -1), CellType.BuildingWall);
        yield return ((1, -1), CellType.BuildingWall);
        yield return ((2, -1), CellType.BuildingCorner);
        yield return ((-2, 0), CellType.BuildingWall);
        yield return ((-1, 0), CellType.BuildingWall);
        yield return ((1, 0), CellType.BuildingWall);
        yield return ((2, 0), CellType.BuildingWall);
        yield return ((-2, 1), CellType.BuildingWall);
        yield return ((-1, 1), CellType.BuildingWall);
        yield return ((1, 1), CellType.BuildingWall);
        yield return ((2, 1), CellType.BuildingWall);
        yield return ((-2, 2), CellType.BuildingCorner);
        yield return ((-1, 2), CellType.BuildingWall);
        yield return ((0, 2), CellType.BuildingRoad);
        yield return ((1, 2), CellType.BuildingWall);
        yield return ((2, 2), CellType.BuildingCorner);
        yield return ((0, 3), CellType.BuildingRoad);
        yield return ((0, 4), CellType.BuildingRoad);
    }
}

#nullable enable

using System.Collections.Generic;
using MinesServer.Data;
using UnityEngine;

namespace Kern.Networking.Buildings
{
    public sealed class NC : PackBuilding
    {
        public override PackType Type => PackType.Science;

        public override Vector2 RoofCenterOffsetCells => new(0f, 1f);

        public override IEnumerable<((int X, int Y) Pos, CellType Cell)> CellsToPlace()
        {
            // Зеркало сервера (MinesServer: Game/Buildings/NC.cs). Пирамида
            // стен без угловых, двери (0,0)/(0,1), дорога 3x3 внизу (y=2..4).
            yield return ((0, 0), CellType.BuildingDoor);
            yield return ((0, 1), CellType.BuildingDoor);

            yield return ((-2, -1), CellType.BuildingWall);
            yield return ((-1, -1), CellType.BuildingWall);
            yield return ((0, -1), CellType.BuildingWall);
            yield return ((1, -1), CellType.BuildingWall);
            yield return ((2, -1), CellType.BuildingWall);

            yield return ((-3, 0), CellType.BuildingWall);
            yield return ((-2, 0), CellType.BuildingWall);
            yield return ((-1, 0), CellType.BuildingWall);
            yield return ((1, 0), CellType.BuildingWall);
            yield return ((2, 0), CellType.BuildingWall);
            yield return ((3, 0), CellType.BuildingWall);

            yield return ((-4, 1), CellType.BuildingWall);
            yield return ((-3, 1), CellType.BuildingWall);
            yield return ((-2, 1), CellType.BuildingWall);
            yield return ((-1, 1), CellType.BuildingWall);
            yield return ((1, 1), CellType.BuildingWall);
            yield return ((2, 1), CellType.BuildingWall);
            yield return ((3, 1), CellType.BuildingWall);
            yield return ((4, 1), CellType.BuildingWall);

            yield return ((-4, 2), CellType.BuildingWall);
            yield return ((-3, 2), CellType.BuildingWall);
            yield return ((-2, 2), CellType.BuildingWall);
            yield return ((2, 2), CellType.BuildingWall);
            yield return ((3, 2), CellType.BuildingWall);
            yield return ((4, 2), CellType.BuildingWall);

            yield return ((-4, 3), CellType.BuildingWall);
            yield return ((-3, 3), CellType.BuildingWall);
            yield return ((-2, 3), CellType.BuildingWall);
            yield return ((2, 3), CellType.BuildingWall);
            yield return ((3, 3), CellType.BuildingWall);
            yield return ((4, 3), CellType.BuildingWall);

            yield return ((-1, 2), CellType.BuildingRoad);
            yield return ((0, 2), CellType.BuildingRoad);
            yield return ((1, 2), CellType.BuildingRoad);
            yield return ((-1, 3), CellType.BuildingRoad);
            yield return ((0, 3), CellType.BuildingRoad);
            yield return ((1, 3), CellType.BuildingRoad);
            yield return ((-1, 4), CellType.BuildingRoad);
            yield return ((0, 4), CellType.BuildingRoad);
            yield return ((1, 4), CellType.BuildingRoad);
        }
    }
}

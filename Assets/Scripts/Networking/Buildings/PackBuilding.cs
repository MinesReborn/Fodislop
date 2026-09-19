#nullable enable

using System.Collections.Generic;
using MinesServer.Data;
using UnityEngine;

namespace Kern.Networking.Buildings;
public abstract class PackBuilding
{
    public abstract PackType Type { get; }

    public abstract Vector2 RoofCenterOffsetCells { get; }

    public abstract IEnumerable<((int X, int Y) Pos, CellType Cell)> CellsToPlace();
}

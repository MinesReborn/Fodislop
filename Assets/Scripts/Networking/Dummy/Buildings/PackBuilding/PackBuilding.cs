#nullable enable

using System.Collections.Generic;
using MinesServer.Data;

namespace Kern.Networking.Dummy;
public abstract class PackBuilding
{
    public abstract BuildingType Type { get; }

    public abstract IEnumerable<((int X, int Y) Pos, CellType Cell)> CellsToPlace();
}

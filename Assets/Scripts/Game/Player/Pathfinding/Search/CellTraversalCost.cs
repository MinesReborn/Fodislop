#nullable enable

using Kern.Core.Interfaces;
using Kern.Game.Player.Movement;
using MinesServer.Data;
using UnityEngine;

namespace Kern.Game.Player.Pathfinding;

// Цена входа в клетку для поисков пути по сетке: время хода по ней в мс
// (movement cooldown от сервера). -1 — клетка непроходима или не получена.
// Общая для клик-маршрута и обхода препятствий при ручном движении.
public static class CellTraversalCost
{
    public static int EnterCostMs(IWorldDataStorage storage, IMapDataProvider mapDataProvider, int x, int y)
    {
        if (!storage.TryGetCell(x, y, out CellType cell))
        {
            return -1;
        }

        if (!PlayerMovementValidator.IsPassable(cell, mapDataProvider.GetCellConfig(cell)))
        {
            return -1;
        }

        return Mathf.Max(1, Mathf.RoundToInt(mapDataProvider.GetMoveCooldown(cell) * 1000f));
    }

    public static int MinStepMs(IMapDataProvider mapDataProvider) =>
        Mathf.Max(1, Mathf.RoundToInt(mapDataProvider.GetMinMoveCooldown() * 1000f));
}

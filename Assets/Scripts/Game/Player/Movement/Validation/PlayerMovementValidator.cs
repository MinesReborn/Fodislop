#nullable enable

using Kern.Core.Interfaces;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Kern.Game.Player.Movement;

public static class PlayerMovementValidator
{
    public static bool IsWithinWorldBounds(Vector2Int position, int worldWidth, int worldHeight)
    {
        return worldWidth > 0 && worldHeight > 0 &&
               position.x >= 0 && position.x < worldWidth &&
               position.y >= 0 && position.y < worldHeight;
    }

    public static float CalculateMoveCooldown(
        IMapDataProvider mapDataProvider,
        CellType currentCellType,
        bool isCtrlPressed,
        bool ignoreCollision)
    {
        float cooldown = isCtrlPressed || currentCellType is CellType.Unloaded or CellType.Pregener
            ? mapDataProvider.GetMinMoveCooldown()
            : mapDataProvider.GetMoveCooldown(currentCellType);

        if (ignoreCollision)
        {
            cooldown = Mathf.Max(0.01f, cooldown / 10f);
        }

        return cooldown;
    }

    public static bool IsPassable(CellType cellType, in CellConfigurationPacket cellConfig)
    {
        return cellType is not (CellType.Unloaded or CellType.Pregener) &&
               cellConfig.Properties.HasFlag(CellConfigProperties.Passable);
    }

    public static bool TryEvaluateStep(
        Vector2Int currentPosition,
        Vector2Int direction,
        IMapDataProvider mapDataProvider,
        IWorldDataStorage storage,
        out Vector2Int targetPosition,
        out CellType cellType,
        out bool isPassable)
    {
        Vector2Int deltaServer = PlayerDirectionMapping.MovementToDeltaServer(direction);
        targetPosition = new Vector2Int(currentPosition.x + deltaServer.x, currentPosition.y + deltaServer.y);
        cellType = CellType.Empty;
        isPassable = false;

        if (storage.CellLayer == null)
        {
            return false;
        }

        if (!IsWithinWorldBounds(targetPosition, mapDataProvider.WorldWidth, mapDataProvider.WorldHeight))
        {
            return false;
        }

        ushort targetServerX = (ushort)targetPosition.x;
        ushort targetServerY = (ushort)targetPosition.y;

        if (!storage.TryGetCell(targetServerX, targetServerY, out cellType) ||
            cellType is CellType.Unloaded or CellType.Pregener)
        {
            cellType = CellType.Unloaded;
            return false;
        }

        CellConfigurationPacket cellConfig = mapDataProvider.GetCellConfig(cellType);
        isPassable = IsPassable(cellType, cellConfig);
        return true;
    }
}

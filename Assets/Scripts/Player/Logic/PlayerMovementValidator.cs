#nullable enable

using Kern.Core.Interfaces;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Kern.Player.Logic;

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
        float cooldown = isCtrlPressed || currentCellType == CellType.Unloaded
            ? mapDataProvider.GetMoveCooldown(CellType.Empty)
            : mapDataProvider.GetMoveCooldown(currentCellType);

        if (ignoreCollision)
        {
            cooldown = Mathf.Max(0.01f, cooldown / 10f);
        }

        return cooldown;
    }

    public static bool IsPassable(CellType cellType, in CellConfigurationPacket cellConfig)
    {
        return cellType != CellType.Unloaded &&
               (cellType == CellType.Empty ||
                ((CellConfigProperties)cellConfig.Properties).HasFlag(CellConfigProperties.Passable));
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
        Vector2Int deltaServer = PlayerMovementMath.MovementToDeltaServer(direction);
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
            cellType == CellType.Unloaded)
        {
            cellType = CellType.Unloaded;
            return false;
        }

        CellConfigurationPacket cellConfig = mapDataProvider.GetCellConfig(cellType);
        isPassable = IsPassable(cellType, cellConfig);
        return true;
    }
}

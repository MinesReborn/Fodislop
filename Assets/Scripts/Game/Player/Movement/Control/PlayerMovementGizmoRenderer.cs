#nullable enable

using Kern.Core.Interfaces;
using Kern.Diagnostics.World;
using Kern.Game.Entities.Robots;
using Kern.World;
using UnityEngine;

namespace Kern.Game.Player.Movement;

internal static class PlayerMovementGizmoRenderer
{
    public static void Draw(
        Transform owner,
        IMapDataProvider? mapDataProvider,
        bool hasServerPosition,
        Vector2Int position,
        Robot? robot)
    {
        if (mapDataProvider == null || mapDataProvider.WorldHeight <= 0 || !hasServerPosition)
        {
            return;
        }

        Gizmos.color = Color.cyan;
        int worldHeight = mapDataProvider.WorldHeight;
        Vector3 gridPos = WorldCoordinateConverter.ServerToUnityPos(position.x, position.y, worldHeight, owner.position.z);
        Gizmos.DrawWireCube(gridPos, new Vector3(1f, 1f, 0.1f));

        if (Application.isPlaying && robot != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(owner.position, robot.TargetPosition);
            Gizmos.DrawWireSphere(robot.TargetPosition, 0.2f);
            KernGizmos.DrawLabel(gridPos + (Vector3.down * 0.7f), $"Grid: {position.x}, {position.y}", Color.cyan);
        }
    }
}

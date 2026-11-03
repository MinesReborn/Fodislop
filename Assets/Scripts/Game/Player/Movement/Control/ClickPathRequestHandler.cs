#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Interfaces;
using Kern.Game.Player.Pathfinding;
using Kern.World;
using UnityEngine;

namespace Kern.Game.Player.Movement;

internal static class ClickPathRequestHandler
{
    public static bool TryFindPath(
        Vector2Int target,
        bool movementLocked,
        bool hasServerPosition,
        IWorldDataStorage? storage,
        IMapDataProvider? mapDataProvider,
        Vector2Int position,
        Action cancelPath,
        Action<Vector2Int> rotate,
        out List<Vector2Int>? path)
    {
        path = null;
        if (movementLocked || !hasServerPosition || storage == null || !storage.IsReady || mapDataProvider == null)
        {
            return false;
        }

        if (!PlayerMovementValidator.IsWithinWorldBounds(
                target, mapDataProvider.WorldWidth, mapDataProvider.WorldHeight))
        {
            Vector2Int delta = target - position;
            Vector2Int direction = Mathf.Abs(delta.x) > Mathf.Abs(delta.y)
                ? new Vector2Int(Math.Sign(delta.x), 0)
                : new Vector2Int(0, -Math.Sign(delta.y));
            cancelPath();
            rotate(direction);
            return false;
        }

        try
        {
            path = ClickPathfinder.FindPath(storage, mapDataProvider, position, target);
        }
        catch (Exception)
        {
            return false;
        }

        return path is { Count: > 0 };
    }
}

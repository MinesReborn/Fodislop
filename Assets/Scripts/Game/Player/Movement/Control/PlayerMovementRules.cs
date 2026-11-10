#nullable enable

using Kern.Core;
using Kern.Game.Player.Actions;
using Kern.Game.Player.Pathfinding;
using UnityEngine;

namespace Kern.Game.Player.Movement;

internal static class PlayerMovementRules
{
    public static bool IsWithinWorldBounds(Vector2Int position, int worldWidth, int worldHeight) =>
        PlayerMovementValidator.IsWithinWorldBounds(position, worldWidth, worldHeight);

    public static bool IsDigCooldownActive(
        float currentTime,
        float lastDigTime,
        float cooldown = GameplayActionDefaults.DefaultDigCooldown) =>
        currentTime - lastDigTime < cooldown;
}

#nullable enable

using Fodinae.World.Lighting;
using UnityEngine;

namespace Fodinae.Game;

/// <summary>
/// Manages offscreen distance-based culling for remote robots.
/// </summary>
public sealed class RobotCuller
{
    private const float OffscreenCullDistance = 35f;
    private const float OffscreenCullSqrDistance = OffscreenCullDistance * OffscreenCullDistance;

    private bool _isCulled;

    public bool IsCulled => _isCulled;

    public bool CheckAndApply(
        Transform transform,
        Camera? camera,
        RobotVisuals visuals,
        RobotNameplate nameplate,
        RobotLighting lighting,
        RobotMovement movement,
        LightingEngine lightingEngine)
    {
        Vector2 diff = camera != null
            ? new Vector2(transform.position.x - camera.transform.position.x, transform.position.y - camera.transform.position.y)
            : Vector2.zero;
        bool shouldCull = diff.sqrMagnitude > OffscreenCullSqrDistance;

        if (shouldCull)
        {
            if (!_isCulled)
            {
                _isCulled = true;
                visuals.SetBodyVisible(false);
                nameplate.SetEnabled(false);
                visuals.SetTentaclesActive(false);
                lighting.Remove(lightingEngine);
            }

            transform.position = movement.TargetPosition;
            movement.TeleportToTarget();
            transform.rotation = Quaternion.Euler(0, 0, movement.TargetAngle);
            return true;
        }

        if (_isCulled)
        {
            _isCulled = false;
            visuals.SetBodyVisible(true);
            nameplate.SetEnabled(true);
            visuals.SetTentaclesActive(true);
            visuals.SnapTentacles(transform.position);
        }

        return false;
    }
}

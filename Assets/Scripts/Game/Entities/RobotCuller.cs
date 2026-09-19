#nullable enable

using Kern.World.Lighting;
using UnityEngine;

namespace Kern.Game;

public sealed class RobotCuller
{
    private const float BaseCullMargin = 36f;

    private bool _isCulled;
    public bool CheckAndApply(
        Transform transform,
        Camera? camera,
        RobotVisuals visuals,
        RobotNameplate nameplate,
        RobotLighting lighting,
        RobotMovement movement,
        LightingEngine lightingEngine)
    {
        if (camera == null)
        {
            return false;
        }

        float halfHeight = camera.orthographic
            ? camera.orthographicSize
            : (Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * Mathf.Abs(camera.transform.position.z));
        float halfWidth = halfHeight * camera.aspect;

        float maxExtentX = halfWidth + BaseCullMargin;
        float maxExtentY = halfHeight + BaseCullMargin;

        Vector3 camPos = camera.transform.position;
        Vector3 myPos = transform.position;
        float diffX = Mathf.Abs(myPos.x - camPos.x);
        float diffY = Mathf.Abs(myPos.y - camPos.y);

        bool shouldCull = diffX > maxExtentX || diffY > maxExtentY;

        if (shouldCull)
        {
            nameplate.SetEnabled(false);
            if (!_isCulled)
            {
                _isCulled = true;
                visuals.SetBodyVisible(false);
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
            nameplate.InvalidatePosition();
            visuals.SetTentaclesActive(true);
            visuals.SnapTentacles(transform.position);
        }

        return false;
    }
}

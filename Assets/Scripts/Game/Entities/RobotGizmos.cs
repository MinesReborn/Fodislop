#nullable enable

#if UNITY_EDITOR
using Fodinae.World;
using UnityEngine;

namespace Fodinae.Game;

/// <summary>
/// Editor gizmo visualization for robot debugging.
/// </summary>
public static class RobotGizmos
{
    private const float VISUAL_ROTATION_OFFSET = -90f;

    public static void DrawGizmos(
        Transform transform,
        uint botId,
        bool isLocalPlayer,
        bool isMetadataLoaded,
        float moveSpeed,
        Vector3 serverPosition,
        Vector3 targetPosition)
    {
        FodinaeGizmos.DrawBounds(serverPosition, Vector2.one * 1.0f, Color.red);
        FodinaeGizmos.DrawBounds(targetPosition, Vector2.one * 0.9f, Color.blue);
        FodinaeGizmos.DrawBounds(transform.position, Vector2.one * 0.8f, Color.cyan);

        float angleRad = (transform.eulerAngles.z + VISUAL_ROTATION_OFFSET) * Mathf.Deg2Rad;
        Vector3 direction = new Vector3(Mathf.Cos(angleRad), Mathf.Sin(angleRad), 0);
        FodinaeGizmos.DrawArrow(transform.position, direction, Color.yellow, 1.2f);

        string status = $"ID: {botId}\n{(isLocalPlayer ? "LOCAL PLAYER" : "REMOTE ROBOT")}\n" +
                        $"Meta: {(isMetadataLoaded ? "OK" : "PENDING")}\n" +
                        $"Speed: {moveSpeed:F1}";
        FodinaeGizmos.DrawLabel(transform.position + (Vector3.up * 1.5f), status, isMetadataLoaded ? Color.green : Color.orange);

        if (!isLocalPlayer)
        {
            float lag = Vector3.Distance(serverPosition, transform.position);
            if (lag > 0.5f)
            {
                FodinaeGizmos.DrawDottedLine(transform.position, serverPosition, Color.red, 4f);
            }
        }
    }
}
#endif

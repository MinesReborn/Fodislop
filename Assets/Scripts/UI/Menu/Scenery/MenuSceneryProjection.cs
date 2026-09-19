#nullable enable

using UnityEngine;

namespace Kern.UI;

internal static class MenuSceneryProjection
{
    private const float OrbitRadius = 1.72f;
    private static readonly Vector3 _orbitTilt = new(72f, 0f, -19f);

    public static bool TryGetStationViewportPosition(
        MenuSceneryViewpoint viewpoint,
        OrbitalStationMotion? station,
        Transform? occluder,
        out Vector2 viewportPosition)
    {
        viewportPosition = default;
        if (station == null)
        {
            return false;
        }

        Vector3 stationPosition = station.transform.position;
        if (IsOccluded(viewpoint, stationPosition, occluder))
        {
            return false;
        }

        return TryProject(viewpoint, stationPosition, out viewportPosition);
    }

    public static bool TryGetOrbitPointViewportPosition(
        MenuSceneryViewpoint viewpoint,
        Transform center,
        float angleDegrees,
        out Vector2 viewportPosition)
    {
        var localOffset = new Vector3(
            Mathf.Cos(angleDegrees * Mathf.Deg2Rad),
            0f,
            Mathf.Sin(angleDegrees * Mathf.Deg2Rad)) * OrbitRadius;
        Vector3 point = center.position + (Quaternion.Euler(_orbitTilt) * localOffset);
        return TryProject(viewpoint, point, out viewportPosition);
    }

    public static bool TryGetSurfaceViewportPosition(
        MenuSceneryViewpoint viewpoint,
        Transform? planet,
        Vector3 localSurfaceDirection,
        out Vector2 viewportPosition)
    {
        viewportPosition = default;
        if (planet == null)
        {
            return false;
        }

        float radius = 0.5f * planet.lossyScale.x;
        Vector3 point = planet.position + (localSurfaceDirection.normalized * radius);
        return TryProject(viewpoint, point, out viewportPosition);
    }

    private static bool TryProject(
        MenuSceneryViewpoint viewpoint,
        Vector3 worldPosition,
        out Vector2 viewportPosition)
    {
        viewportPosition = default;
        Vector3 viewport = viewpoint.WorldToViewport(worldPosition);
        if (viewport.z <= 0f)
        {
            return false;
        }

        viewportPosition = new Vector2(viewport.x, viewport.y);
        return true;
    }

    private static bool IsOccluded(MenuSceneryViewpoint viewpoint, Vector3 point, Transform? occluder)
    {
        if (occluder == null)
        {
            return false;
        }

        Vector3 cameraPosition = viewpoint.Position;
        Vector3 toOccluder = occluder.position - cameraPosition;
        Vector3 toPoint = point - cameraPosition;
        if (toPoint.magnitude <= toOccluder.magnitude)
        {
            return false;
        }

        float radius = occluder.lossyScale.x * 0.5f;
        float offAxis = Vector3.ProjectOnPlane(toPoint, toOccluder.normalized).magnitude;
        return offAxis < radius;
    }
}

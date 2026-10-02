#nullable enable

using System.Reflection;
using Kern.Player;
using Kern.World.Streaming;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.Player;

public sealed class CameraFollowTests
{
    [TestCase(0f, 17.5f, 16f / 9f, false)]
    [TestCase(0f, 17.51f, 16f / 9f, true)]
    [TestCase(0f, -17.51f, 16f / 9f, true)]
    [TestCase(31f, 0f, 16f / 9f, false)]
    [TestCase(31.2f, 0f, 16f / 9f, true)]
    [TestCase(-31.2f, 0f, 16f / 9f, true)]
    [TestCase(9f, 0f, 0.5f, true)]
    public void TeleportViewportUsesReferenceRectangle(float x, float y, float aspect, bool expected)
    {
        Assert.That(CameraFollow.IsOutsideTeleportViewport(new Vector3(x, y, 100f), aspect), Is.EqualTo(expected));
    }

    [TestCase(5f, 18f, true)]
    [TestCase(30f, 18f, true)]
    [TestCase(5f, 10f, false)]
    [TestCase(30f, 10f, false)]
    public void TeleportEventUsesSameThresholdAtEveryZoom(float zoom, float displacement, bool shouldSnap)
    {
        var cameraOwner = new GameObject("camera teleport test");
        var followOwner = new GameObject("camera follow test");
        var targetOwner = new GameObject("camera target test");
        try
        {
            Camera camera = cameraOwner.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = zoom;
            camera.aspect = 16f / 9f;
            Vector3 initial = new(84.5f, 39804.5f, -10f);
            camera.transform.position = initial;
            Vector3 destination = initial + new Vector3(0f, -displacement, 10f);
            targetOwner.transform.position = destination;
            CameraFollow follow = followOwner.AddComponent<CameraFollow>();
            SetField(follow, "_camera", camera);
            SetField(follow, "_target", targetOwner.transform);
            SetField(follow, "_originalZ", -10f);
            SetField(follow, "_followVelocity", new Vector3(3f, -40f, 0f));
            var transition = new WorldViewTransition { CanHold = true };
            SetField(follow, "_viewTransition", transition);

            typeof(CameraFollow).GetMethod("HandlePlayerTeleported", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(follow, null);

            Vector3 expected = shouldSnap ? new Vector3(destination.x, destination.y, -10f) : initial;
            Assert.That(camera.transform.position, Is.EqualTo(expected));
            Assert.That(transition.IsHolding, Is.False, "Terrain readiness must not delay the teleport.");
            if (shouldSnap)
            {
                Assert.That(GetField(follow, "_followVelocity"), Is.EqualTo(Vector3.zero));
            }
        }
        finally
        {
            Object.DestroyImmediate(followOwner);
            Object.DestroyImmediate(targetOwner);
            Object.DestroyImmediate(cameraOwner);
        }
    }

    private static void SetField(CameraFollow follow, string name, object value) =>
        typeof(CameraFollow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(follow, value);

    private static object? GetField(CameraFollow follow, string name) =>
        typeof(CameraFollow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(follow);
}

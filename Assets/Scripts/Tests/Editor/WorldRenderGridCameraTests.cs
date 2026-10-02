#nullable enable

using Kern.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests;

public sealed class WorldRenderGridCameraTests
{
    [Test]
    public void GuardedProjectionAndOriginalViewportShareWorldPoint()
    {
        GameObject owner = UnityEditor.EditorUtility.CreateGameObjectWithHideFlags(
            "WorldRenderGridCameraTest", HideFlags.HideAndDontSave);
        try
        {
            var camera = owner.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 1;
            camera.aspect = 2;
            camera.transform.position = new Vector3(-0.01f, -1.01f, -10);
            Matrix4x4 original = camera.projectionMatrix;
            var binding = WorldRenderGridCamera.Capture(camera);
            Assert.That(binding.Grid.Width, Is.EqualTo(131));
            Assert.That(binding.Grid.Height, Is.EqualTo(67));
            Assert.That(camera.projectionMatrix, Is.EqualTo(original));
            Assert.That(camera.targetTexture, Is.Null);

            Vector3 point = camera.transform.position + new Vector3(0.5f, 0.25f, 10);
            Vector3 clip = (binding.ProjectionMatrix * binding.ViewMatrix).MultiplyPoint(point);
            Assert.That((clip.x + 1) / 2, Is.EqualTo((point.x + 2.0625) / (131.0 / 32)).Within(1e-6));
            Assert.That((clip.y + 1) / 2, Is.EqualTo((point.y + 2.0625) / (67.0 / 32)).Within(1e-6));
            Vector3 viewport = camera.WorldToViewportPoint(point);
            Assert.That(viewport.x, Is.EqualTo(0.625).Within(1e-6));
            Assert.That(viewport.y, Is.EqualTo(0.625).Within(1e-6));
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }
}

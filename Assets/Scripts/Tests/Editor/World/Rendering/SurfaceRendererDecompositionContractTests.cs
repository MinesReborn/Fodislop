#nullable enable

using System.IO;
using Kern.World;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World;

public sealed class SurfaceRendererDecompositionContractTests
{
    [Test]
    public void Renderer_DelegatesMeshAndLightingLifecycle()
    {
        string renderer = ReadRenderingSource("SurfaceRenderer.cs");

        Assert.That(renderer, Does.Contain("SurfaceMeshUtilities.CreateDynamic("));
        Assert.That(renderer, Does.Contain("SurfaceMeshUtilities.DrawLightingField("));
        Assert.That(renderer, Does.Contain("SurfaceMeshUtilities.DestroyOwned("));
        Assert.That(renderer, Does.Not.Contain("private static Mesh CreateMesh("));
        Assert.That(renderer, Does.Not.Contain("private static void DrawLightingMesh("));
        Assert.That(renderer, Does.Not.Contain("private static void DestroyOwnedObject("));
        Assert.That(renderer, Does.Not.Contain("showNearSurface"), "Surface meshes must never be arbitrarily deactivated based on zoom/PPU.");
        Assert.That(File.ReadAllLines(RenderingSourcePath("SurfaceRenderer.cs")), Has.Length.LessThanOrEqualTo(500));
    }

    [Test]
    public void SurfaceGeometryBuilder_UpdateSurfaceMeshes_CoversWideSpanAndBounds()
    {
        var builder = new SurfaceGeometryBuilder();
        var transitMesh = new Mesh();
        var perspectiveMesh = new Mesh();

        try
        {
            // Simulate WXGA 1280x800 and 1366x768 at max camera zoom (ortho = 30)
            float maxOrtho = 30f;
            float wxgaAspect = 1366f / 768f;
            float visibleHalfWidth = (maxOrtho * wxgaAspect) + SurfaceGeometryBuilder.BoundaryOverscan;
            float span = Mathf.Max(visibleHalfWidth + 16f, 1366f / 30f, 64f);
            float cx = 100f;
            float surfaceY = 256f;

            builder.UpdateSurfaceMeshes(transitMesh, perspectiveMesh, cx, surfaceY, span);

            Assert.That(transitMesh.vertexCount, Is.EqualTo(4));
            Assert.That(perspectiveMesh.vertexCount, Is.EqualTo(4));

            Vector3[] transitVertices = transitMesh.vertices;
            Assert.That(transitVertices[0].x, Is.LessThanOrEqualTo(cx - visibleHalfWidth));
            Assert.That(transitVertices[2].x, Is.GreaterThanOrEqualTo(cx + visibleHalfWidth));

            Vector3[] persVertices = perspectiveMesh.vertices;
            Assert.That(persVertices[0].x, Is.LessThanOrEqualTo(cx - visibleHalfWidth));
            Assert.That(persVertices[2].x, Is.GreaterThanOrEqualTo(cx + visibleHalfWidth));

            // Verify bounds extend well beyond the visible width so frustum culling doesn't drop them
            Assert.That(transitMesh.bounds.size.x, Is.GreaterThanOrEqualTo(visibleHalfWidth * 2f));
            Assert.That(perspectiveMesh.bounds.size.x, Is.GreaterThanOrEqualTo(visibleHalfWidth * 2f));
        }
        finally
        {
            Object.DestroyImmediate(transitMesh);
            Object.DestroyImmediate(perspectiveMesh);
        }
    }

    private static string ReadRenderingSource(string fileName)
    {
        return File.ReadAllText(RenderingSourcePath(fileName));
    }

    private static string RenderingSourcePath(string fileName)
    {
        return Path.Combine(Application.dataPath, "Scripts/World/Rendering", fileName);
    }
}

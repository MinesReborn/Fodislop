#nullable enable

using System.IO;
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
        Assert.That(File.ReadAllLines(RenderingSourcePath("SurfaceRenderer.cs")), Has.Length.LessThanOrEqualTo(500));
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

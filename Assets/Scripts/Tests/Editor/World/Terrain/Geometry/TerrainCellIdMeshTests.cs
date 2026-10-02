#nullable enable

using Kern.World.Terrain;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World;

[TestFixture]
public class TerrainCellIdMeshTests
{
    [Test]
    public void EnsureSizeBuildsCorrectMeshGeometry()
    {
        using var meshManager = new TerrainCellIdMesh();
        const int width = 16;
        const int height = 16;
        const float cellSize = 1f;

        bool changed = meshManager.EnsureSize(width, height, cellSize);
        Assert.That(changed, Is.True);
        Assert.That(meshManager.Mesh, Is.Not.Null);

        Mesh mesh = meshManager.Mesh!;
        int expectedQuads = width * height * TerrainCellDataPacker.LayersPerCell;
        int expectedVerts = expectedQuads * 4;
        int expectedIndices = expectedQuads * 6;

        Assert.That(mesh.vertexCount, Is.EqualTo(expectedVerts));
        Assert.That(mesh.GetIndexCount(0), Is.EqualTo(expectedIndices));

        // Re-call with same parameters should return false and not reallocate
        bool recheck = meshManager.EnsureSize(width, height, cellSize);
        Assert.That(recheck, Is.False);
    }
}

using System.Runtime.InteropServices;
using Kern.World.Terrain;
using NUnit.Framework;
using UnityEngine;

namespace Kern.TerrainTests.Volume;

public sealed class VolumeMeshTests
{
    [Test]
    public void EqualWallCountUpdatesOnlyWallVertices()
    {
        var buffers = new TerrainCellBuffers();
        buffers.Type(1, 0);
        buffers.Set(2, 2, 1);
        var topology = new TerrainVolumeTopology();
        topology.Publish(buffers, Vector2Int.zero, 8, 8, false);
        using var volume = new TerrainVolumeMesh();
        volume.EnsureSize(4, 4, 1, 8, 8, topology);
        Mesh mesh = volume.Mesh!;
        var oldVertices = (TerrainVolumeVertex[])mesh.Vertices.Clone();
        int indexUploads = mesh.IndexUploadCount;
        buffers.Set(2, 2, 2);
        buffers.Set(3, 2, 1);
        topology.Publish(buffers, Vector2Int.zero, 8, 8, false);
        volume.EnsureSize(4, 4, 1, 8, 8, topology);
        Assert.That(mesh.LastUploadDestination, Is.EqualTo(4 * 4 * 4));
        Assert.That(mesh.LastUploadCount, Is.EqualTo(4 * 4));
        Assert.That(mesh.IndexUploadCount, Is.EqualTo(indexUploads));
        var vertices = (TerrainVolumeVertex[])mesh.Vertices;
        Assert.That(vertices[0].Address, Is.EqualTo(oldVertices[0].Address));
        Assert.That(vertices[vertices.Length - 1].Address, Is.EqualTo(oldVertices[oldVertices.Length - 1].Address));
        Assert.That(vertices[4 * 4 * 4].Address, Is.Not.EqualTo(oldVertices[4 * 4 * 4].Address));
    }

    [Test]
    public void MeshHasRealWallQuadsBetweenBaseAndRaisedFaceAndDoesNotUploadOnStableFrame()
    {
        var buffers = new TerrainCellBuffers();
        buffers.Type(1, 0);
        buffers.Set(2, 2, 1);
        var topology = new TerrainVolumeTopology();
        topology.Publish(buffers, Vector2Int.zero, 8, 8, false);
        using var volume = new TerrainVolumeMesh();
        volume.EnsureSize(4, 4, 1, 8, 8, topology);
        Mesh mesh = volume.Mesh!;
        Assert.That(Marshal.SizeOf<TerrainVolumeVertex>(), Is.EqualTo(16));
        Assert.That(volume.WallQuadCount, Is.EqualTo(4));
        Assert.That(mesh.vertexCount, Is.EqualTo(16 * 8 + 4 * 4));
        Assert.That(mesh.GetSubMesh(0).indexCount, Is.EqualTo(16 * 6));
        Assert.That(mesh.GetSubMesh(1).indexCount, Is.EqualTo(4 * 6));
        Assert.That(mesh.GetSubMesh(2).indexStart, Is.EqualTo(16 * 6 + 4 * 6));
        var vertices = (TerrainVolumeVertex[])mesh.Vertices;
        int wallStart = 16 * 4;
        for (int quad = 0; quad < 4; quad++)
        {
            for (int corner = 0; corner < 4; corner++)
            {
                TerrainVolumeVertex vertex = vertices[wallStart + quad * 4 + corner];
                Assert.That(Mathf.FloorToInt(vertex.Address.z), Is.EqualTo(3));
                Assert.That(vertex.Address.w, Is.EqualTo(corner));
                Assert.That(vertex.Address.x, Is.GreaterThanOrEqualTo(2 * 128));
            }
        }
        // Wall addresses remain relative to the committed window even when
        // outside the smaller screen mesh. Camera cropping is a vertex test.
        buffers.Set(7, 7, 1);
        topology.Publish(buffers, Vector2Int.zero, 8, 8, false);
        volume.EnsureSize(4, 4, 1, 8, 8, topology);
        Assert.That(volume.WallQuadCount, Is.EqualTo(8));
        vertices = (TerrainVolumeVertex[])mesh.Vertices;
        Assert.That(Mathf.FloorToInt(vertices[wallStart + 4 * 4].Address.x / 128), Is.EqualTo(7));
        int uploads = mesh.UploadCount;
        volume.EnsureSize(4, 4, 1, 8, 8, topology);
        Assert.That(mesh.UploadCount, Is.EqualTo(uploads));
        buffers.Set(3, 2, 1);
        topology.Publish(buffers, Vector2Int.zero, 8, 8, false);
        volume.EnsureSize(4, 4, 1, 8, 8, topology);
        Assert.That(mesh.UploadCount, Is.EqualTo(uploads + 1));
        Assert.That(volume.WallQuadCount, Is.EqualTo(10));
        volume.Dispose();
        volume.EnsureSize(4, 4, 1, 8, 8, topology);
        Assert.That(volume.Mesh, Is.Not.SameAs(mesh));
        Assert.That(volume.Mesh!.Indices.Length, Is.GreaterThan(0));
    }
    [Test]
    public void PositionOnlyPackingHasIndependentKnownValuesIncludingNegativeContourCoordinates()
    {
        var vertex = new TerrainVolumeVertex(7, 5, 3, 2, new Vector2(-0.125f, 1.125f), new Vector2(1, 0));
        Assert.That(vertex.Address.x, Is.EqualTo(924f));
        Assert.That(vertex.Address.y, Is.EqualTo(708f));
        Assert.That(vertex.Address.z, Is.EqualTo(3.501953125f));
        Assert.That(vertex.Address.w, Is.EqualTo(2f));
        Assert.That(TerrainVolumeMesh.VertexLayout.Length, Is.EqualTo(1));
        Assert.That(TerrainVolumeMesh.VertexLayout[0].Attribute, Is.EqualTo(UnityEngine.Rendering.VertexAttribute.Position));
        Assert.That(TerrainVolumeMesh.VertexLayout[0].Format, Is.EqualTo(UnityEngine.Rendering.VertexAttributeFormat.Float32));
    }

}

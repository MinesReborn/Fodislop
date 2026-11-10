#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Kern.Diagnostics.Frames;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Terrain;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct TerrainVolumeVertex
{
    public readonly Vector4 Address;

    public TerrainVolumeVertex(int x, int y, int layer, int corner, Vector2 start, Vector2 end)
    {
        if (layer != 3)
        {
            Address = new Vector4(x, y, layer, corner);
            return;
        }
        int startX = PixelCode(start.x);
        int startY = PixelCode(start.y);
        int endX = PixelCode(end.x);
        int endY = PixelCode(end.y);
        // All values are exact float32 integers/fractions. POSITION remains the
        // only required attribute, including old recorded warmup declarations.
        Address = new Vector4((x * 128) + startX, (y * 128) + startY,
            3f + ((endX * 128 + endY) / 16384f), corner);
    }

    private static int PixelCode(float cells)
    {
        int code = Mathf.RoundToInt(cells * 32f) + 32;
        if (code < 0 || code >= 128)
        {
            throw new ArgumentOutOfRangeException(nameof(cells), "Volume contour exceeds its exact 7-bit pixel coordinate.");
        }
        return code;
    }
}

// Screen-only mesh: actual side quads join z=0 to the raised face. Vertices
// retain physical coordinates; the shader projects height from the active eye.
internal sealed class TerrainVolumeMesh : IDisposable
{
    internal static readonly VertexAttributeDescriptor[] VertexLayout =
    {
        new(VertexAttribute.Position, VertexAttributeFormat.Float32, 4),
    };
    private readonly List<TerrainVolumeVertex> _vertices = new();
    private readonly List<int> _indices = new();
    private readonly List<TerrainVolumeVertex> _blockVertices = new();
    private readonly List<int> _blockIndices = new();
    private int _width;
    private int _height;
    private int _boundsWidth;
    private int _boundsHeight;
    private float _cellSize;
    private ulong _version;
    private Mesh? _mesh;
    private Mesh? _blockMesh;

    // Фон (пол) рисуется под сущностями мира, а стенки и передний план
    // блоков — отдельным мешем над ними. Один меш с тремя сабмешами этого
    // не даёт: все сабмеши рендерера выводятся вместе, и другой рендерер
    // (роботы, хвосты) не может вклиниться между фоном и блоками.
    public Mesh? Mesh => _mesh;

    public Mesh? BlockMesh => _blockMesh;

    public int WallQuadCount { get; private set; }

    public void EnsureSize(int width, int height, float cellSize, int boundsWidth, int boundsHeight,
        TerrainVolumeTopology topology)
    {
        bool sameLayout = _width == width && _height == height &&
            _boundsWidth == boundsWidth && _boundsHeight == boundsHeight && _cellSize == cellSize;
        if (_mesh != null && _blockMesh != null && sameLayout &&
            _version == topology.Version)
        {
            return;
        }

        // Faces and indices are identical when the presentation dimensions and
        // number of wall quads are unchanged. Update only wall vertices; the
        // ground and foreground buffers remain intact. Walls are the first
        // block vertices, so they are uploaded at offset zero.
        if (_mesh != null && _blockMesh != null && sameLayout &&
            WallQuadCount == topology.Edges.Count)
        {
            _blockVertices.Clear();
            foreach (TerrainVolumeEdge edge in topology.Edges)
            {
                for (int corner = 0; corner < 4; corner++)
                {
                    _blockVertices.Add(new TerrainVolumeVertex(edge.X, edge.Y, 3, corner, edge.Start, edge.End));
                }
            }
            if (_blockVertices.Count > 0)
            {
                _blockMesh.SetVertexBufferData(_blockVertices, 0, 0, _blockVertices.Count, 0,
                    MeshUpdateFlags.DontRecalculateBounds);
            }
            _version = topology.Version;
            FrameEventLog.Record($"меш объёма: обновлены {_blockVertices.Count} вершин стенок");
            return;
        }

        _vertices.Clear();
        _indices.Clear();
        _blockVertices.Clear();
        _blockIndices.Clear();

        // Фон: один квад на клетку, слой 0.
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                AddQuad(_vertices, _indices, x, y, 0, Vector2.zero, Vector2.zero);
            }
        }

        // Блоки: сначала боковые стенки (слой 3), затем верхние грани (слой 1).
        WallQuadCount = 0;
        foreach (TerrainVolumeEdge edge in topology.Edges)
        {
            // Wall addresses belong to the committed window, like door overlays.
            // The vertex shader rejects cells outside the visible presentation
            // before decoding them. Smooth camera travel never rewrites this mesh.
            AddQuad(_blockVertices, _blockIndices, edge.X, edge.Y, 3, edge.Start, edge.End);
            WallQuadCount++;
        }
        int blockWallIndices = _blockIndices.Count;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                AddQuad(_blockVertices, _blockIndices, x, y, 1, Vector2.zero, Vector2.zero);
            }
        }

        var bounds = new Bounds(new Vector3(boundsWidth * cellSize * 0.5f, boundsHeight * cellSize * 0.5f, 0f),
            new Vector3((boundsWidth + 2) * cellSize, (boundsHeight + 2) * cellSize, 2f));

        RebuildMesh(ref _mesh, "TerrainVolumeMesh", _vertices, _indices,
            new[] { _indices.Count }, bounds);
        RebuildMesh(ref _blockMesh, "TerrainBlockMesh", _blockVertices, _blockIndices,
            new[] { blockWallIndices, _blockIndices.Count - blockWallIndices }, bounds);

        _width = width;
        _height = height;
        _boundsWidth = boundsWidth;
        _boundsHeight = boundsHeight;
        _cellSize = cellSize;
        _version = topology.Version;
        FrameEventLog.Record(
            $"меш объёма: фон {_vertices.Count} вершин, блоки {_blockVertices.Count} вершин, " +
            $"{WallQuadCount} боковых граней, " +
            $"{(int)(((_vertices.Count + _blockVertices.Count) * (long)Marshal.SizeOf<TerrainVolumeVertex>()) + ((_indices.Count + _blockIndices.Count) * 4L))} Б");
    }

    private static void RebuildMesh(
        ref Mesh? mesh,
        string name,
        List<TerrainVolumeVertex> vertices,
        List<int> indices,
        int[] subMeshIndexCounts,
        Bounds bounds)
    {
        if (mesh == null)
        {
            mesh = new Mesh
            {
                name = name,
                indexFormat = IndexFormat.UInt32,
                hideFlags = HideFlags.DontSave,
            };
        }
        else
        {
            mesh.Clear();
        }

        mesh.SetVertexBufferParams(vertices.Count, VertexLayout);
        mesh.SetVertexBufferData(vertices, 0, 0, vertices.Count, 0, MeshUpdateFlags.DontRecalculateBounds);
        mesh.SetIndexBufferParams(indices.Count, IndexFormat.UInt32);
        mesh.SetIndexBufferData(indices, 0, 0, indices.Count, MeshUpdateFlags.DontRecalculateBounds);
        mesh.bounds = bounds;
        mesh.subMeshCount = subMeshIndexCounts.Length;
        int start = 0;
        for (int draw = 0; draw < subMeshIndexCounts.Length; draw++)
        {
            int count = subMeshIndexCounts[draw];
            mesh.SetSubMesh(draw, new SubMeshDescriptor(start, count, MeshTopology.Triangles)
            {
                bounds = bounds,
            }, MeshUpdateFlags.DontRecalculateBounds);
            start += count;
        }
    }

    private static void AddQuad(
        List<TerrainVolumeVertex> vertices,
        List<int> indices,
        int x,
        int y,
        int layer,
        Vector2 start,
        Vector2 end)
    {
        int vertex = vertices.Count;
        for (int corner = 0; corner < 4; corner++)
        {
            vertices.Add(new TerrainVolumeVertex(x, y, layer, corner, start, end));
        }
        indices.Add(vertex);
        indices.Add(vertex + 1);
        indices.Add(vertex + 2);
        indices.Add(vertex);
        indices.Add(vertex + 2);
        indices.Add(vertex + 3);
    }

    public void Dispose()
    {
        DestroyMesh(ref _mesh);
        DestroyMesh(ref _blockMesh);
        _version = 0;
        _width = 0;
        _height = 0;
        _boundsWidth = 0;
        _boundsHeight = 0;
        _cellSize = 0f;
    }

    private static void DestroyMesh(ref Mesh? mesh)
    {
        if (mesh == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(mesh);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(mesh);
        }

        mesh = null;
    }
}

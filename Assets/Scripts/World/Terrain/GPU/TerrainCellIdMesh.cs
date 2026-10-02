#nullable enable

using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using Kern.Core.Interfaces.Diagnostics;

namespace Kern.World.Terrain;

[StructLayout(LayoutKind.Sequential)]
public struct TerrainCellIdVertex
{
    public Vector3 Position;
    public Vector2 Corner;
}

[BurstCompile]
public struct GenerateCellIdMeshJob : IJobParallelFor
{
    public int Width;
    public int Height;
    [NativeDisableParallelForRestriction]
    [WriteOnly] public NativeArray<TerrainCellIdVertex> Vertices;
    [NativeDisableParallelForRestriction]
    [WriteOnly] public NativeArray<int> Indices;

    public void Execute(int quad)
    {
        int layerQuads = Width * Height;
        int layer = quad / layerQuads;
        int rem = quad % layerQuads;
        int y = rem / Width;
        int x = rem % Width;

        int vertex = quad * 4;
        Vertices[vertex] = new TerrainCellIdVertex { Position = new Vector3(x, y, layer), Corner = new Vector2(0f, 0f) };
        Vertices[vertex + 1] = new TerrainCellIdVertex { Position = new Vector3(x, y, layer), Corner = new Vector2(1f, 0f) };
        Vertices[vertex + 2] = new TerrainCellIdVertex { Position = new Vector3(x, y, layer), Corner = new Vector2(1f, 1f) };
        Vertices[vertex + 3] = new TerrainCellIdVertex { Position = new Vector3(x, y, layer), Corner = new Vector2(0f, 1f) };

        int index = quad * 6;
        Indices[index] = vertex;
        Indices[index + 1] = vertex + 1;
        Indices[index + 2] = vertex + 2;
        Indices[index + 3] = vertex;
        Indices[index + 4] = vertex + 2;
        Indices[index + 5] = vertex + 3;
    }
}

// Меш идентификаторов квадов террейна.
//
// Вершина несёт только адрес: POSITION = (x, y, слой), TEXCOORD0 = угол квада
// (0 или 1 по каждой оси). Всё остальное шейдер читает из текстур данных
// клетки. Меш зависит только от размера сетки: при сдвиге камеры и при
// изменении клеток он не пересобирается и не выгружается.
//
// Остаётся мешем под MeshRenderer, а не процедурным вызовом: так террейн
// сохраняет слой сортировки 2D-рендерера, материалы по атласам и проход
// поля материалов без изменений.
public sealed class TerrainCellIdMesh : IDisposable
{
    private static readonly VertexAttributeDescriptor[] s_vertexLayout =
    [
        new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
        new(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
    ];

    private Mesh? _mesh;
    private int _width;
    private int _height;
    private int _boundsWidth;
    private int _boundsHeight;
    private float _cellSize;

    public Mesh? Mesh => _mesh;

    // boundsWidth/boundsHeight — вся сетка: меш видимого окна рисуется со
    // смещением внутри неё, и границы по самому окну отсекали бы террейн.
    public bool EnsureSize(int meshWidth, int meshHeight, float cellSize, int boundsWidth = 0, int boundsHeight = 0)
    {
        boundsWidth = Math.Max(boundsWidth, meshWidth);
        boundsHeight = Math.Max(boundsHeight, meshHeight);
        if (_mesh != null && _width == meshWidth && _height == meshHeight)
        {
            if (_boundsWidth == boundsWidth && _boundsHeight == boundsHeight &&
                _cellSize.Equals(cellSize))
            {
                return false;
            }

            // The presentation mesh topology depends on its visible size.
            // Backing-grid bounds only affect culling, so changing the terrain
            // window must not allocate vertex/index arrays or upload a new mesh.
            _boundsWidth = boundsWidth;
            _boundsHeight = boundsHeight;
            _cellSize = cellSize;
            UpdateBounds(_mesh, boundsWidth, boundsHeight, cellSize);
            return true;
        }

        bool hadMesh = _mesh != null;
        int previousWidth = _width;
        int previousHeight = _height;
        int previousBoundsWidth = _boundsWidth;
        int previousBoundsHeight = _boundsHeight;
        Dispose();
        _width = meshWidth;
        _height = meshHeight;
        _boundsWidth = boundsWidth;
        _boundsHeight = boundsHeight;
        _cellSize = cellSize;

        int quads = meshWidth * meshHeight * TerrainCellDataPacker.LayersPerCell;
        int vertexCount = quads * 4;
        int indexCount = quads * 6;

        var vertices = new NativeArray<TerrainCellIdVertex>(vertexCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        var indices = new NativeArray<int>(indexCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

        try
        {
            var job = new GenerateCellIdMeshJob
            {
                Width = meshWidth,
                Height = meshHeight,
                Vertices = vertices,
                Indices = indices,
            };

            JobHandle handle = job.Schedule(quads, 64);
            handle.Complete();

            _mesh = new Mesh
            {
                name = "TerrainCellIdMesh",
                indexFormat = IndexFormat.UInt32,
                hideFlags = HideFlags.DontSave,
            };

            _mesh.SetVertexBufferParams(vertexCount, s_vertexLayout);
            _mesh.SetVertexBufferData(vertices, 0, 0, vertexCount, 0, MeshUpdateFlags.DontRecalculateBounds);

            _mesh.SetIndexBufferParams(indexCount, IndexFormat.UInt32);
            _mesh.SetIndexBufferData(indices, 0, 0, indexCount, MeshUpdateFlags.DontRecalculateBounds);

            var subMeshBounds = new Bounds(
                new Vector3(boundsWidth * cellSize * 0.5f, boundsHeight * cellSize * 0.5f, 0f),
                new Vector3((boundsWidth + 2) * cellSize, (boundsHeight + 2) * cellSize, 2f));
            _mesh.subMeshCount = 1;
            _mesh.SetSubMesh(0, new SubMeshDescriptor(0, indexCount, MeshTopology.Triangles) { bounds = subMeshBounds }, MeshUpdateFlags.DontRecalculateBounds);
        }
        finally
        {
            vertices.Dispose();
            indices.Dispose();
        }

        // Позиции в меше — адреса, а не координаты: границы задаются
        // по реальному прямоугольнику сетки, как у меша вершин.
        UpdateBounds(_mesh, boundsWidth, boundsHeight, cellSize);
        _mesh.UploadMeshData(markNoLongerReadable: true);
        long nativeBytes = (vertexCount * 20L) + (indexCount * 4L);
        string reason = hadMesh
            ? $"геометрия {previousWidth}×{previousHeight}→{meshWidth}×{meshHeight}, " +
                $"границы {previousBoundsWidth}×{previousBoundsHeight}→{boundsWidth}×{boundsHeight}"
            : $"первая геометрия, границы {boundsWidth}×{boundsHeight}";
        FrameEventLog.Record(
            $"меш клеток {meshWidth}×{meshHeight} собран через Burst-Job ({reason}; " +
            $"буфер {nativeBytes} Б, 0 GC)");
        return true;
    }

    private static void UpdateBounds(Mesh mesh, int boundsWidth, int boundsHeight, float cellSize)
    {
        mesh.bounds = new Bounds(
            new Vector3(boundsWidth * cellSize * 0.5f, boundsHeight * cellSize * 0.5f, 0f),
            new Vector3((boundsWidth + 2) * cellSize, (boundsHeight + 2) * cellSize, 2f));
    }

    public void Dispose()
    {
        if (_mesh != null)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(_mesh);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(_mesh);
            }
        }

        _mesh = null;
        _width = 0;
        _height = 0;
    }
}

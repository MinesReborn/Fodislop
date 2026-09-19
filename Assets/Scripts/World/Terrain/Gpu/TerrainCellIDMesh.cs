#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Terrain;

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
public sealed class TerrainCellIDMesh : IDisposable
{
    private static readonly Vector2[] _Corners =
    [
        new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f),
    ];

    private Mesh? _mesh;
    private int _width;
    private int _height;
    private int _boundsWidth;
    private int _boundsHeight;

    public Mesh? Mesh => _mesh;

    // boundsWidth/boundsHeight — вся сетка: меш видимого окна рисуется со
    // смещением внутри неё, и границы по самому окну отсекали бы террейн.
    public bool EnsureSize(int meshWidth, int meshHeight, float cellSize, int boundsWidth = 0, int boundsHeight = 0)
    {
        boundsWidth = Math.Max(boundsWidth, meshWidth);
        boundsHeight = Math.Max(boundsHeight, meshHeight);
        if (_mesh != null && _width == meshWidth && _height == meshHeight &&
            _boundsWidth == boundsWidth && _boundsHeight == boundsHeight)
        {
            return false;
        }

        Dispose();
        _width = meshWidth;
        _height = meshHeight;
        _boundsWidth = boundsWidth;
        _boundsHeight = boundsHeight;

        int quads = meshWidth * meshHeight * TerrainCellDataPacker.LayersPerCell;
        var positions = new Vector3[quads * 4];
        var corners = new Vector2[quads * 4];
        var indices = new int[quads * 6];
        int quad = 0;
        for (int y = 0; y < meshHeight; y++)
        {
            for (int x = 0; x < meshWidth; x++)
            {
                for (int layer = 0; layer < TerrainCellDataPacker.LayersPerCell; layer++)
                {
                    int vertex = quad * 4;
                    for (int corner = 0; corner < 4; corner++)
                    {
                        positions[vertex + corner] = new Vector3(x, y, layer);
                        corners[vertex + corner] = _Corners[corner];
                    }

                    int index = quad * 6;
                    indices[index] = vertex;
                    indices[index + 1] = vertex + 1;
                    indices[index + 2] = vertex + 2;
                    indices[index + 3] = vertex;
                    indices[index + 4] = vertex + 2;
                    indices[index + 5] = vertex + 3;
                    quad++;
                }
            }
        }

        _mesh = new Mesh
        {
            name = "TerrainCellIDMesh",
            indexFormat = IndexFormat.UInt32,
            hideFlags = HideFlags.DontSave,
        };
        _mesh.SetVertices(positions);
        _mesh.SetUVs(0, corners);
        _mesh.SetIndices(indices, MeshTopology.Triangles, 0, calculateBounds: false);

        // Позиции в меше — адреса, а не координаты: границы задаются
        // по реальному прямоугольнику сетки, как у меша вершин.
        _mesh.bounds = new Bounds(
            new Vector3(boundsWidth * cellSize * 0.5f, boundsHeight * cellSize * 0.5f, 0f),
            new Vector3((boundsWidth + 2) * cellSize, (boundsHeight + 2) * cellSize, 2f));
        _mesh.UploadMeshData(markNoLongerReadable: true);
        return true;
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

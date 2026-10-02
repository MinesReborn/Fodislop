#nullable enable

using System;
using Kern.World.Streaming;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>
/// Меш, которым террейн рисуется на экране, внутри сетки, которой он живёт.
/// </summary>
///
/// Сетка больше кадра: её размер задан освещением и сдвигом, а не камерой.
/// Рисовать её целиком значит гонять квады, которых на экране нет. Поэтому
/// экран рисует свой меш поменьше, а его положение внутри сетки едет смещением
/// в шейдере — сами тексели при этом не двигаются, у них кольцевой адрес.
///
/// Запас в четыре клетки по краям — физический: камера стоит между границами
/// клеток непрерывно, а адрес текселя целочисленный.
public sealed class TerrainPresentationWindow : IDisposable
{
    private const int PresentationMarginCells = 4;

    private readonly TerrainCellIdMesh _mesh = new();
    private Vector4 _viewOffset;

    public Mesh? Mesh => _mesh.Mesh;

    public Vector4 ViewOffset => _viewOffset;

    public void Update(
        StreamingPolicy policy,
        RectInt cameraViewport,
        Vector2Int windowOrigin,
        int meshWidth,
        int meshHeight,
        float cellSize,
        MeshFilter? meshFilter)
    {
        // Backing grid is sized for the maximum supported zoom, while the
        // presentation mesh must match the current viewport. Retaining a
        // high-water size here submits off-screen quads forever after zoom-out.
        int wantedWidth = policy.QuantizeDimension(
            cameraViewport.width + (PresentationMarginCells * 2));
        int wantedHeight = policy.QuantizeDimension(
            cameraViewport.height + (PresentationMarginCells * 2));
        int width = Mathf.Clamp(wantedWidth, 1, meshWidth);
        int height = Mathf.Clamp(wantedHeight, 1, meshHeight);
        int extraWidth = Mathf.Max(0, width - cameraViewport.width - (PresentationMarginCells * 2));
        int extraHeight = Mathf.Max(0, height - cameraViewport.height - (PresentationMarginCells * 2));
        int presentationMinX = cameraViewport.xMin - PresentationMarginCells - (extraWidth / 2);
        int presentationMinY = cameraViewport.yMin - PresentationMarginCells - (extraHeight / 2);
        int offsetX = Mathf.Clamp(presentationMinX - windowOrigin.x, 0, meshWidth - width);
        int offsetY = Mathf.Clamp(presentationMinY - windowOrigin.y, 0, meshHeight - height);

        _mesh.EnsureSize(width, height, cellSize, meshWidth, meshHeight);
        var offset = new Vector4(offsetX, offsetY, 0f, 0f);
        if (offset != _viewOffset)
        {
            _viewOffset = offset;
            Shader.SetGlobalVector(TerrainCellDataTextures.ViewOffsetId, offset);
        }

        if (meshFilter != null && meshFilter.sharedMesh != _mesh.Mesh)
        {
            meshFilter.sharedMesh = _mesh.Mesh;
            Shader.SetGlobalVector(TerrainCellDataTextures.ViewOffsetId, _viewOffset);
        }
    }

    public void Dispose() => _mesh.Dispose();
}

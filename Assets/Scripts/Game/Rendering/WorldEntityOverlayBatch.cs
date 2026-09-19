#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Lifecycle;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.Game;

public sealed class WorldEntityOverlayBatch : IDisposable
{
    private readonly Mesh _mesh;
    private Vector3[] _vertices = [];
    private Vector2[] _uvs = [];
    private Color32[] _colors = [];
    private int[] _indices = [];
    private int _uploadedSpriteCount = -1;

    public WorldEntityOverlayBatch(
        ISceneObjectFactory sceneObjects,
        Material material,
        int sortingOrder)
    {
        GameObject renderObject = sceneObjects.Create("WorldEntityOverlayBatch");
        _mesh = new Mesh
        {
            name = "WorldEntityOverlayBatch",
            indexFormat = IndexFormat.UInt32,
        };
        _mesh.MarkDynamic();
        var filter = renderObject.AddComponent<MeshFilter>();
        filter.sharedMesh = _mesh;
        var renderer = renderObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.sortingOrder = sortingOrder;
    }

    public void Rebuild(
        IReadOnlyList<WorldEntityBatchRenderer.SpriteHandle> sprites,
        Func<Texture2D, Rect> getAtlasRect,
        Bounds? bounds)
    {
        int spriteCount = sprites.Count;
        int vertexCount = spriteCount * 4;
        int indexCount = spriteCount * 6;
        EnsureCapacity(vertexCount, indexCount);
        int vertexCursor = 0;
        int indexCursor = 0;
        for (int i = 0; i < sprites.Count; i++)
        {
            WorldEntityBatchRenderer.SpriteHandle handle = sprites[i];
            Sprite sprite = handle.Sprite ?? throw new InvalidOperationException(
                "An enabled overlay sprite requires a Sprite.");
            WorldEntityGeometry.WriteSprite(
                _vertices,
                _uvs,
                _colors,
                _indices,
                handle,
                getAtlasRect(sprite.texture),
                vertexCursor,
                indexCursor);
            vertexCursor += 4;
            indexCursor += 6;
        }

        vertexCount = vertexCursor;
        indexCount = indexCursor;

        bool topologyChanged = _uploadedSpriteCount != spriteCount;
        if (topologyChanged)
        {
            _mesh.Clear(keepVertexLayout: true);
        }

        if (vertexCount > 0)
        {
            _mesh.SetVertices(_vertices, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
            _mesh.SetUVs(0, _uvs, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
            _mesh.SetColors(_colors, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
            if (topologyChanged)
            {
                _mesh.SetIndices(
                    _indices,
                    0,
                    indexCount,
                    MeshTopology.Triangles,
                    0,
                    calculateBounds: false);
            }

            if (bounds.HasValue)
            {
                _mesh.bounds = bounds.Value;
            }
            else
            {
                _mesh.RecalculateBounds();
            }
        }

        _uploadedSpriteCount = spriteCount;
    }

    public void Dispose()
    {
        UnityEngine.Object.Destroy(_mesh);
    }

    private void EnsureCapacity(int vertexCount, int indexCount)
    {
        if (_vertices.Length < vertexCount)
        {
            Array.Resize(ref _vertices, vertexCount);
            Array.Resize(ref _uvs, vertexCount);
            Array.Resize(ref _colors, vertexCount);
        }

        if (_indices.Length < indexCount)
        {
            Array.Resize(ref _indices, indexCount);
        }
    }
}

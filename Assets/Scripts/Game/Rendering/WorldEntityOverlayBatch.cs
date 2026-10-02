#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Lifecycle;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.Game;

public sealed class WorldEntityOverlayBatch : IDisposable
{
    private readonly Mesh _mesh;
    private NativeArray<WorldEntityVertex> _vertices;
    private NativeArray<int> _indices;
    private int _allocatedCapacity;
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

        EnsureCapacity(64);
    }

    public void Rebuild(
        IReadOnlyList<WorldEntityBatchRenderer.SpriteHandle> sprites,
        Func<Texture2D, Rect> getAtlasRect,
        Bounds? bounds)
    {
        int spriteCount = sprites.Count;
        if (spriteCount > _allocatedCapacity)
        {
            EnsureCapacity(Math.Max(spriteCount, _allocatedCapacity * 2));
        }

        int vertexCursor = 0;
        var vertexSpan = _vertices.AsSpan();
        for (int i = 0; i < sprites.Count; i++)
        {
            WorldEntityBatchRenderer.SpriteHandle handle = sprites[i];
            Sprite sprite = handle.Sprite ?? throw new InvalidOperationException(
                "An enabled overlay sprite requires a Sprite.");
            WorldEntityGeometry.WriteSpriteInterleaved(
                vertexSpan,
                handle,
                getAtlasRect(sprite.texture),
                vertexCursor);
            vertexCursor += 4;
        }

        int vertexCount = vertexCursor;
        int indexCount = spriteCount * 6;

        if (vertexCount > 0)
        {
            _mesh.SetVertexBufferParams(vertexCount, WorldEntityGeometry.VertexLayout);
            _mesh.SetVertexBufferData(_vertices, 0, 0, vertexCount, 0, MeshUpdateFlags.DontRecalculateBounds);

            if (_uploadedSpriteCount != spriteCount)
            {
                _mesh.SetIndexBufferParams(indexCount, IndexFormat.UInt32);
                _mesh.SetIndexBufferData(_indices, 0, 0, indexCount, MeshUpdateFlags.DontRecalculateBounds);
            }

            _mesh.subMeshCount = 1;
            _mesh.SetSubMesh(0, new SubMeshDescriptor(0, indexCount, MeshTopology.Triangles), MeshUpdateFlags.DontRecalculateBounds);

            if (bounds.HasValue)
            {
                _mesh.bounds = bounds.Value;
            }
            else
            {
                _mesh.RecalculateBounds();
            }
        }
        else if (_uploadedSpriteCount > 0)
        {
            _mesh.subMeshCount = 1;
            _mesh.SetSubMesh(0, new SubMeshDescriptor(0, 0, MeshTopology.Triangles), MeshUpdateFlags.DontRecalculateBounds);
        }

        _uploadedSpriteCount = spriteCount;
    }

    public void Dispose()
    {
        if (_vertices.IsCreated)
        {
            _vertices.Dispose();
        }

        if (_indices.IsCreated)
        {
            _indices.Dispose();
        }

        UnityEngine.Object.Destroy(_mesh);
    }

    private void EnsureCapacity(int spriteCapacity)
    {
        if (_allocatedCapacity >= spriteCapacity)
        {
            return;
        }

        int vertexCount = spriteCapacity * 4;
        int indexCount = spriteCapacity * 6;

        if (_vertices.IsCreated)
        {
            _vertices.Dispose();
        }

        if (_indices.IsCreated)
        {
            _indices.Dispose();
        }

        _vertices = new NativeArray<WorldEntityVertex>(vertexCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        _indices = new NativeArray<int>(indexCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

        for (int i = 0; i < spriteCapacity; i++)
        {
            int v = i * 4;
            int idx = i * 6;
            _indices[idx] = v;
            _indices[idx + 1] = v + 1;
            _indices[idx + 2] = v + 2;
            _indices[idx + 3] = v + 2;
            _indices[idx + 4] = v + 1;
            _indices[idx + 5] = v + 3;
        }

        _uploadedSpriteCount = -1;
        _allocatedCapacity = spriteCapacity;
    }
}

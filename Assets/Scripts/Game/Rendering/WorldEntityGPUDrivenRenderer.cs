#nullable enable

using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.Game;

/// <summary>
/// GPU-driven batch renderer for dynamic world entities (mobs, crystals, drops, particles).
/// Uploads instance data to a StructuredBuffer and draws all visible entities in a single indirect draw call.
/// </summary>
public sealed class WorldEntityGPUDrivenRenderer : IDisposable
{
    private static readonly int s_entityInstancesId = Shader.PropertyToID("_EntityInstances");
    private const string InstancingKeyword = "KERN_GPU_INSTANCING";

    private readonly Mesh _quadMesh;
    private GraphicsBuffer? _instanceBuffer;
    private GraphicsBuffer? _argsBuffer;
    private NativeArray<WorldEntityGPUInstance> _instanceData;
    private readonly uint[] _argsData = new uint[5];
    private MaterialPropertyBlock? _propertyBlock;
    private int _capacity;
    private int _activeInstanceCount;

    public WorldEntityGPUDrivenRenderer(int initialCapacity = 256)
    {
        _quadMesh = CreateQuadMesh();
        EnsureCapacity(initialCapacity);
    }

    public int ActiveInstanceCount => _activeInstanceCount;

    public void UploadInstances(
        IReadOnlyList<WorldEntityBatchRenderer.SpriteHandle> sprites,
        Func<Texture2D, Rect> getAtlasRect)
    {
        int count = sprites.Count;
        if (count > _capacity)
        {
            EnsureCapacity(Math.Max(count, _capacity * 2));
        }

        int writeIndex = 0;
        for (int i = 0; i < count; i++)
        {
            WorldEntityBatchRenderer.SpriteHandle handle = sprites[i];
            Sprite? sprite = handle.Sprite;
            if (sprite == null)
            {
                continue;
            }

            Rect source = sprite.rect;
            float ppu = sprite.pixelsPerUnit;
            float width = source.width / ppu;
            float height = source.height / ppu;
            Vector2 pivot = new(
                sprite.pivot.x / source.width,
                sprite.pivot.y / source.height);

            Rect atlasRect = getAtlasRect(sprite.texture);
            float uMin = atlasRect.xMin + ((source.xMin / sprite.texture.width) * atlasRect.width);
            float uMax = atlasRect.xMin + ((source.xMax / sprite.texture.width) * atlasRect.width);
            float vMin = atlasRect.yMin + ((source.yMin / sprite.texture.height) * atlasRect.height);
            float vMax = atlasRect.yMin + ((source.yMax / sprite.texture.height) * atlasRect.height);

            Matrix4x4 localToWorld = handle.FrameLocalToWorld;
            Vector3 position = localToWorld.GetPosition();

            // Extract 2D rotation (cos, sin) from local-to-world matrix
            Vector3 right = localToWorld.MultiplyVector(Vector3.right);
            float rightLen = right.magnitude;
            float cos = rightLen > 0.0001f ? right.x / rightLen : 1f;
            float sin = rightLen > 0.0001f ? right.y / rightLen : 0f;

            _instanceData[writeIndex++] = new WorldEntityGPUInstance
            {
                PositionAndScale = new Vector4(position.x, position.y, width, height),
                UvRect = new Vector4(uMin, vMin, uMax, vMax),
                Color = handle.Color,
                RotationAndPivot = new Vector4(cos, sin, pivot.x, pivot.y),
            };
        }

        _activeInstanceCount = writeIndex;

        if (_activeInstanceCount > 0 && _instanceBuffer != null && _argsBuffer != null)
        {
            _instanceBuffer.SetData(_instanceData, 0, 0, _activeInstanceCount);

            _argsData[0] = 6;                          // Index count per instance (1 quad = 6 indices)
            _argsData[1] = (uint)_activeInstanceCount; // Instance count
            _argsData[2] = 0;                          // Start index
            _argsData[3] = 0;                          // Base vertex
            _argsData[4] = 0;                          // Start instance
            _argsBuffer.SetData(_argsData);
        }
    }

    public void Render(Material material, Bounds bounds)
    {
        if (_activeInstanceCount <= 0 || _instanceBuffer == null || _argsBuffer == null)
        {
            return;
        }

        _propertyBlock ??= new MaterialPropertyBlock();
        _propertyBlock.SetBuffer(s_entityInstancesId, _instanceBuffer);

        material.EnableKeyword(InstancingKeyword);
        Graphics.DrawMeshInstancedIndirect(
            _quadMesh,
            0,
            material,
            bounds,
            _argsBuffer,
            0,
            _propertyBlock);
    }

    public void RenderIndirect(
        CommandBuffer cmd,
        Material material,
        int passIndex)
    {
        if (_activeInstanceCount <= 0 || _instanceBuffer == null || _argsBuffer == null)
        {
            return;
        }

        _propertyBlock ??= new MaterialPropertyBlock();
        _propertyBlock.SetBuffer(s_entityInstancesId, _instanceBuffer);

        material.EnableKeyword(InstancingKeyword);
        cmd.DrawMeshInstancedIndirect(
            _quadMesh,
            0,
            material,
            passIndex,
            _argsBuffer,
            0,
            _propertyBlock);
    }

    public void Dispose()
    {
        if (_instanceData.IsCreated)
        {
            _instanceData.Dispose();
        }

        _instanceBuffer?.Release();
        _instanceBuffer = null;

        _argsBuffer?.Release();
        _argsBuffer = null;

        if (_quadMesh != null)
        {
            UnityEngine.Object.Destroy(_quadMesh);
        }
    }

    private void EnsureCapacity(int capacity)
    {
        if (_capacity >= capacity)
        {
            return;
        }

        if (_instanceData.IsCreated)
        {
            _instanceData.Dispose();
        }

        _instanceBuffer?.Release();
        _argsBuffer?.Release();

        _capacity = capacity;
        _instanceData = new NativeArray<WorldEntityGPUInstance>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        _instanceBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, System.Runtime.InteropServices.Marshal.SizeOf<WorldEntityGPUInstance>());
        _argsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, 5 * sizeof(uint));
    }

    private static Mesh CreateQuadMesh()
    {
        var mesh = new Mesh
        {
            name = "WorldEntityQuadInstanceMesh",
        };

        var positions = new Vector3[4]
        {
            new(0f, 0f, 0f),
            new(0f, 1f, 0f),
            new(1f, 0f, 0f),
            new(1f, 1f, 0f),
        };

        var uvs = new Vector2[4]
        {
            new(0f, 0f),
            new(0f, 1f),
            new(1f, 0f),
            new(1f, 1f),
        };

        var indices = new int[6] { 0, 1, 2, 2, 1, 3 };

        mesh.SetVertices(positions);
        mesh.SetUVs(0, uvs);
        mesh.SetIndices(indices, MeshTopology.Triangles, 0, calculateBounds: false);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
        mesh.UploadMeshData(markNoLongerReadable: true);
        return mesh;
    }
}

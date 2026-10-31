#nullable enable

using Kern.Core.Interfaces.Diagnostics;
using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.World;
using Kern.World.Lighting;
using Kern.World.Streaming;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using VContainer;
using Kern.Core.Rendering;
using Kern.Diagnostics.Memory;
using Kern.Game.Visual.Entities.Tentacles;

namespace Kern.Game.Rendering;

internal sealed class WorldEntityGeometryBuffers
{
    private readonly Func<Texture2D, Rect> _atlasRect;

    internal WorldEntityGeometryBuffers(int vertexCapacity, int indexCapacity, Func<Texture2D, Rect> atlasRect)
    {
        _verts = new Vector3[vertexCapacity];
        _uvs = new Vector2[vertexCapacity];
        _colors = new Color32[vertexCapacity];
        _tris = new int[indexCapacity];
        _atlasRect = atlasRect;
    }

    internal Vector3[] Vertices => _verts;
    internal Vector2[] Uvs => _uvs;
    internal Color32[] Colors => _colors;
    internal int[] Indices => _tris;

        internal Vector3[] _verts = Array.Empty<Vector3>();
        internal Vector2[] _uvs = Array.Empty<Vector2>();
        internal Color32[] _colors = Array.Empty<Color32>();
        internal int[] _tris = Array.Empty<int>();

        internal void WriteSprites(
            IReadOnlyList<WorldEntitySpriteHandle> handles,
            ref int vertexCursor,
            ref int indexCursor)
        {
            for (int i = 0; i < handles.Count; i++)
            {
                WorldEntitySpriteHandle handle = handles[i];
                Sprite sprite = handle.Sprite ?? throw new InvalidOperationException(
                    "An enabled batched sprite requires a Sprite.");
                WorldEntityGeometry.WriteSprite(
                    _verts,
                    _uvs,
                    _colors,
                    _tris,
                    handle,
                    _atlasRect(sprite.texture),
                    vertexCursor,
                    indexCursor);
                vertexCursor += 4;
                indexCursor += 6;
            }
        }

        internal void EnsureGeometryCapacity(int vertexCount, int indexCount)
        {
            int vertexCapacity = Mathf.Max(1, vertexCount);
            if (_verts.Length < vertexCapacity)
            {
                Array.Resize(ref _verts, vertexCapacity);
                Array.Resize(ref _uvs, vertexCapacity);
                Array.Resize(ref _colors, vertexCapacity);
            }

            int indexCapacity = Mathf.Max(1, indexCount);
            if (_tris.Length < indexCapacity)
            {
                Array.Resize(ref _tris, indexCapacity);
            }
        }

}

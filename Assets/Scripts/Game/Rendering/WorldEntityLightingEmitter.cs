#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.World.Lighting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.Game
{
    /// <summary>
    /// Builds the emissive-only mesh that light-emitting batch sprites contribute
    /// to the lighting material fields. The revision follows emissive state only —
    /// camera motion rebuilds the visible batch every frame and must not re-solve light.
    /// </summary>
    internal sealed class WorldEntityLightingEmitter : IDisposable
    {
        private const int EmptyEmissiveStateHash = 17;

        private readonly List<WorldEntityBatchRenderer.SpriteHandle> _sprites;
        private readonly Func<Material?> _batchMaterial;
        private readonly Func<Texture2D, Rect> _atlasRect;

        private Mesh? _lightingMesh;
        private Vector3[] _lightingVerts = new Vector3[4];
        private Vector2[] _lightingUvs = new Vector2[4];
        private Color32[] _lightingColors = new Color32[4];
        private int[] _lightingTris = new int[6];
        private ulong _revision = 1;
        private int _emissiveStateHash = EmptyEmissiveStateHash;

        public WorldEntityLightingEmitter(
            List<WorldEntityBatchRenderer.SpriteHandle> sprites,
            Func<Material?> batchMaterial,
            Func<Texture2D, Rect> atlasRect)
        {
            _sprites = sprites;
            _batchMaterial = batchMaterial;
            _atlasRect = atlasRect;
        }

        public ulong Revision => _revision;

        public void UpdateRevision()
        {
            int hash = EmptyEmissiveStateHash;
            for (int i = 0; i < _sprites.Count; i++)
            {
                WorldEntityBatchRenderer.SpriteHandle handle = _sprites[i];
                if (!handle.EmitsLight || !IsRenderable(handle))
                {
                    continue;
                }

                hash = HashCode.Combine(
                    hash,
                    handle.Sprite!,
                    handle.FrameLocalToWorld,
                    handle.Color);
            }

            if (hash != _emissiveStateHash)
            {
                _emissiveStateHash = hash;
                _revision++;
            }
        }

        public void RenderFields(CommandBuffer commandBuffer, in LightingFieldContext context)
        {
            Material? batchMaterial = _batchMaterial();
            if (batchMaterial == null)
            {
                return;
            }

            int emissiveCount = 0;
            for (int i = 0; i < _sprites.Count; i++)
            {
                if (_sprites[i].EmitsLight && IsRenderable(_sprites[i]))
                {
                    emissiveCount++;
                }
            }

            if (emissiveCount == 0)
            {
                return;
            }

            int pass = batchMaterial.FindPass(ProjectRuntimeContracts.ShaderPassNames.LightingMaterialField);
            if (pass < 0)
            {
                throw new InvalidOperationException(
                    $"World-entity material '{batchMaterial.name}' is missing the LightingMaterialField pass.");
            }

            int vertexCount = emissiveCount * 4;
            int indexCount = emissiveCount * 6;
            if (_lightingVerts.Length < vertexCount)
            {
                Array.Resize(ref _lightingVerts, vertexCount);
                Array.Resize(ref _lightingUvs, vertexCount);
                Array.Resize(ref _lightingColors, vertexCount);
            }

            if (_lightingTris.Length < indexCount)
            {
                Array.Resize(ref _lightingTris, indexCount);
            }

            int vertexCursor = 0;
            int indexCursor = 0;
            for (int i = 0; i < _sprites.Count; i++)
            {
                WorldEntityBatchRenderer.SpriteHandle handle = _sprites[i];
                if (!handle.EmitsLight || !IsRenderable(handle))
                {
                    continue;
                }

                WorldEntityGeometry.WriteSprite(
                    _lightingVerts,
                    _lightingUvs,
                    _lightingColors,
                    _lightingTris,
                    handle,
                    _atlasRect(handle.Sprite!.texture),
                    vertexCursor,
                    indexCursor);
                vertexCursor += 4;
                indexCursor += 6;
            }

            if (_lightingMesh == null)
            {
                _lightingMesh = new Mesh
                {
                    name = "WorldEntityLightingField",
                    indexFormat = IndexFormat.UInt32,
                };
                _lightingMesh.MarkDynamic();
            }

            _lightingMesh.Clear(keepVertexLayout: true);
            _lightingMesh.SetVertices(_lightingVerts, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
            _lightingMesh.SetUVs(0, _lightingUvs, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
            _lightingMesh.SetColors(_lightingColors, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
            _lightingMesh.SetIndices(_lightingTris, 0, indexCount, MeshTopology.Triangles, 0, calculateBounds: false);
            commandBuffer.DrawMesh(_lightingMesh, Matrix4x4.identity, batchMaterial, 0, pass);
        }

        public void Dispose()
        {
            if (_lightingMesh != null)
            {
                UnityEngine.Object.Destroy(_lightingMesh);
                _lightingMesh = null;
            }
        }

        private static bool IsRenderable(WorldEntityBatchRenderer.SpriteHandle handle)
        {
            return handle.Enabled && handle.FrameAlive && handle.Sprite != null;
        }
    }
}

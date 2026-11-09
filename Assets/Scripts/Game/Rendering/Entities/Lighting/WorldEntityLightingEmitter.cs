#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.Game.Rendering
{
    /// <summary>
    /// Builds the glowing-only mesh that light-emitting batch sprites contribute
    /// to the lighting material fields. The revision follows glowing state only —
    /// camera motion rebuilds the visible batch every frame and must not re-solve light.
    /// </summary>
    internal sealed class WorldEntityLightingEmitter : IDisposable
    {
        private const int EmptyGlowStateHash = 17;
        private const int GlowRectStride = 16;

        private static readonly int s_entityGlowRectsId = Shader.PropertyToID("_EntityGlowRects");

        private readonly List<WorldEntityBatchRenderer.SpriteHandle> _sprites;
        private readonly Func<Material?> _batchMaterial;
        private readonly Func<Texture2D, Rect> _atlasRect;

        private Mesh? _lightingMesh;
        private ComputeBuffer? _glowRects;
        private Vector3[] _lightingVerts = new Vector3[4];
        private Vector2[] _lightingUvs = new Vector2[4];
        private Vector4[] _lightingRects = new Vector4[4];
        private Color32[] _lightingColors = new Color32[4];
        private int[] _lightingTris = new int[6];
        private ulong _revision = 1;
        private int _glowStateHash = EmptyGlowStateHash;

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
            int hash = EmptyGlowStateHash;
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

            if (hash != _glowStateHash)
            {
                _glowStateHash = hash;
                _revision++;
            }
        }

        public void RenderMaterialGlowFields(
            CommandBuffer commandBuffer,
            in Kern.Core.Interfaces.WorldLighting.LightingMaterialGlowContext context) =>
            RenderField(commandBuffer);

        public void RenderAmbientOcclusionField(
            CommandBuffer commandBuffer,
            in Kern.Core.Interfaces.WorldLighting.LightingAmbientOcclusionContext context)
        {
            // This contributor contains glow-only sprites. Its material
            // pass writes zero physical occupancy, so drawing it into the AO
            // target only rebuilds the mesh and issues an MRT pass against a
            // single render target.
        }

        private void RenderField(CommandBuffer commandBuffer)
        {
            Material? batchMaterial = _batchMaterial();
            if (batchMaterial == null)
            {
                return;
            }

            int glowCount = 0;
            for (int i = 0; i < _sprites.Count; i++)
            {
                if (_sprites[i].EmitsLight && IsRenderable(_sprites[i]))
                {
                    glowCount++;
                }
            }

            if (glowCount == 0)
            {
                return;
            }

            int pass = batchMaterial.FindPass(ShaderPassNames.LightingMaterialField);
            if (pass < 0)
            {
                throw new InvalidOperationException(
                    $"World-entity material '{batchMaterial.name}' is missing the {ShaderPassNames.LightingMaterialField} pass.");
            }

            int vertexCount = glowCount * 4;
            int indexCount = glowCount * 6;
            if (_lightingVerts.Length < vertexCount)
            {
                Array.Resize(ref _lightingVerts, vertexCount);
                Array.Resize(ref _lightingUvs, vertexCount);
                Array.Resize(ref _lightingRects, glowCount);
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

                Rect atlasRect = _atlasRect(handle.Sprite!.texture);
                WorldEntityGeometry.WriteSprite(
                    _lightingVerts,
                    _lightingUvs,
                    _lightingColors,
                    _lightingTris,
                    handle,
                    atlasRect,
                    vertexCursor,
                    indexCursor);

                // Glow-пас усредняет цвет непрозрачных пикселей спрайта по
                // этому прямоугольнику: поле в два текселя на клетку не может
                // показать окна и надписи, и точечная выборка давала мозаику.
                // Прямоугольник уходит в буфер, а индекс спрайта — в COLOR:
                // отдельный поток вершин сломал бы записанную warmup-коллекцию.
                int glowIndex = vertexCursor / 4;
                _lightingRects[glowIndex] = new Vector4(
                    atlasRect.xMin,
                    atlasRect.yMin,
                    atlasRect.xMax,
                    atlasRect.yMax);

                var indexColor = new Color32(
                    (byte)(glowIndex & 0xFF),
                    (byte)((glowIndex >> 8) & 0xFF),
                    0,
                    255);
                _lightingColors[vertexCursor] = indexColor;
                _lightingColors[vertexCursor + 1] = indexColor;
                _lightingColors[vertexCursor + 2] = indexColor;
                _lightingColors[vertexCursor + 3] = indexColor;

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

            if (_glowRects == null || _glowRects.count < glowCount)
            {
                _glowRects?.Release();
                _glowRects = new ComputeBuffer(
                    Mathf.NextPowerOfTwo(Mathf.Max(glowCount, 8)),
                    GlowRectStride,
                    ComputeBufferType.Structured);
            }

            _glowRects.SetData(_lightingRects, 0, 0, glowCount);
            // Глобальный буфер объявляется так же, как массивы клеток террейна:
            // command-buffer-биндинг до растрового пасса не доезжает.
            Shader.SetGlobalBuffer(s_entityGlowRectsId, _glowRects);

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

            _glowRects?.Release();
            _glowRects = null;
        }

        private static bool IsRenderable(WorldEntityBatchRenderer.SpriteHandle handle)
        {
            return handle.Enabled && handle.FrameAlive && handle.Sprite != null;
        }
    }
}

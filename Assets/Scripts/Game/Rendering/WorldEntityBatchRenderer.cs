#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Lifecycle;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using VContainer;

namespace Fodinae.Game
{
    /// <summary>
    /// Renders compatible world sprites and robot tentacles through one mesh,
    /// one material and one runtime texture atlas.
    /// </summary>
    public class WorldEntityBatchRenderer : MonoBehaviour
    {
        // Matches the five-point tail used by the stable June implementation.
        public const int POINT_COUNT = 5;
        private const int VERTS_PER_TENTACLE = POINT_COUNT * 2;
        private const int TRIS_PER_TENTACLE = (POINT_COUNT - 1) * 6;
        private const int INITIAL_CAPACITY = 64;
        private const int BATCH_SORTING_ORDER = -1;
        private const int OVERLAY_BATCH_SORTING_ORDER = 600;
        private const int TENTACLE_SORTING_ORDER = -1;

        private static readonly ProfilerMarker LateUpdateMarker =
            new("Fodinae.WorldEntities.LateUpdate");

        private readonly List<Tentacle> _tentacles = [];
        private readonly List<SpriteHandle> _sprites = [];
        private Vector3[] _verts = new Vector3[VERTS_PER_TENTACLE * INITIAL_CAPACITY];
        private Vector2[] _uvs = new Vector2[VERTS_PER_TENTACLE * INITIAL_CAPACITY];
        private Color32[] _colors = new Color32[VERTS_PER_TENTACLE * INITIAL_CAPACITY];
        private int[] _tris = new int[TRIS_PER_TENTACLE * INITIAL_CAPACITY];
        private Mesh? _mesh;
        private WorldEntityOverlayBatch? _overlayBatch;
        private WorldEntityTextureAtlas? _atlas;
        private int _uploadedTentacleCount = -1;
        private int _uploadedSpriteCount = -1;
        private bool _geometryDirty = true;
        [Inject]
        private ISceneObjectFactory _sceneObjects = null!;
        [Inject]
        private ISharedMaterialCache _sharedMaterials = null!;

        public sealed class SpriteHandle
        {
            internal SpriteHandle(Transform transform, int sortingOrder)
            {
                Transform = transform;
                SortingOrder = sortingOrder;
            }

            internal Transform Transform { get; }
            internal int SortingOrder { get; }
            internal Sprite? Sprite { get; private set; }
            internal Color Color { get; private set; } = Color.white;
            internal bool Enabled { get; private set; }
            private Vector3 _lastPosition;
            private Quaternion _lastRotation;
            private Vector3 _lastScale;
            private Sprite? _lastSprite;
            private Color _lastColor;
            private bool _lastEnabled;
            private bool _hasSnapshot;

            public void SetSprite(Sprite? sprite)
            {
                Sprite = sprite;
                if (sprite == null)
                {
                    Enabled = false;
                }
            }

            public void SetColor(Color color)
            {
                Color = color;
            }

            public void SetEnabled(bool enabled)
            {
                Enabled = enabled && Sprite != null;
            }

            internal bool HasChanged()
            {
                if (Transform == null)
                {
                    return _hasSnapshot;
                }

                return !_hasSnapshot ||
                    _lastPosition != Transform.position ||
                    _lastRotation != Transform.rotation ||
                    _lastScale != Transform.lossyScale ||
                    _lastSprite != Sprite ||
                    _lastColor != Color ||
                    _lastEnabled != Enabled;
            }

            internal void CaptureState()
            {
                if (Transform == null)
                {
                    _lastPosition = Vector3.zero;
                    _lastRotation = Quaternion.identity;
                    _lastScale = Vector3.one;
                }
                else
                {
                    _lastPosition = Transform.position;
                    _lastRotation = Transform.rotation;
                    _lastScale = Transform.lossyScale;
                }

                _lastSprite = Sprite;
                _lastColor = Color;
                _lastEnabled = Enabled;
                _hasSnapshot = true;
            }
        }

        public SpriteHandle RegisterSprite(Transform spriteTransform, int sortingOrder)
        {
            var handle = new SpriteHandle(spriteTransform, sortingOrder);
            _sprites.Add(handle);
            _sprites.Sort(static (left, right) => left.SortingOrder.CompareTo(right.SortingOrder));
            _geometryDirty = true;
            return handle;
        }

        public void SetSprite(SpriteHandle handle, Sprite? sprite)
        {
            if (sprite != null)
            {
                EnsureRenderer();
                EnsureTextureInAtlas(sprite.texture);
            }

            handle.SetSprite(sprite);
            _geometryDirty = true;
        }

        public void UnregisterSprite(SpriteHandle? handle)
        {
            if (handle != null && _sprites.Remove(handle))
            {
                _geometryDirty = true;
            }
        }

        public void Register(Tentacle tentacle, Texture2D texture)
        {
            if (tentacle == null || texture == null)
            {
                return;
            }

            EnsureRenderer();
            EnsureTextureInAtlas(texture);
            if (!_tentacles.Contains(tentacle))
            {
                _tentacles.Add(tentacle);
                _geometryDirty = true;
            }
        }

        public void Unregister(Tentacle tentacle, Texture2D texture)
        {
            if (tentacle != null && _tentacles.Remove(tentacle))
            {
                _geometryDirty = true;
            }
        }

        public void MarkDirty(Texture2D texture)
        {
            _geometryDirty = true;
        }

        internal Rect GetAtlasRect(Texture2D texture)
        {
            return _atlas?.GetRect(texture) ?? throw new InvalidOperationException(
                "World-entity atlas is not initialized.");
        }

        protected void LateUpdate()
        {
            using var marker = LateUpdateMarker.Auto();
            if (!_geometryDirty)
            {
                for (int i = 0; i < _sprites.Count; i++)
                {
                    if (_sprites[i].HasChanged())
                    {
                        _geometryDirty = true;
                        break;
                    }
                }
            }

            if (_geometryDirty && _mesh != null)
            {
                RebuildMesh();
                _overlayBatch?.Rebuild(_sprites, GetAtlasRect);
            }
        }

        private void EnsureRenderer()
        {
            if (_mesh != null)
            {
                return;
            }

            _atlas = new WorldEntityTextureAtlas();

            GameObject renderObject = _sceneObjects.Create("WorldEntityBatch");

            _mesh = new Mesh
            {
                name = "WorldEntityBatch",
                indexFormat = IndexFormat.UInt32,
            };
            _mesh.MarkDynamic();

            var filter = renderObject.AddComponent<MeshFilter>();
            filter.sharedMesh = _mesh;

            var renderer = renderObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _sharedMaterials.GetForTexture(_atlas.Texture);
            renderer.sortingOrder = BATCH_SORTING_ORDER;

            _overlayBatch = new WorldEntityOverlayBatch(
                _sceneObjects,
                renderer.sharedMaterial,
                OVERLAY_BATCH_SORTING_ORDER);
        }

        private void EnsureTextureInAtlas(Texture2D texture)
        {
            WorldEntityTextureAtlas atlas = _atlas ?? throw new InvalidOperationException(
                "World-entity atlas must exist before a texture is registered.");
            atlas.EnsureTexture(texture);
        }

        private void RebuildMesh()
        {
            Mesh mesh = _mesh ?? throw new InvalidOperationException(
                "Tentacle mesh must exist before geometry is rebuilt.");
            int activeCount = 0;
            for (int i = 0; i < _tentacles.Count; i++)
            {
                if (_tentacles[i].IsActive)
                {
                    activeCount++;
                }
            }

            int activeSpriteCount = 0;
            for (int i = 0; i < _sprites.Count; i++)
            {
                if (_sprites[i].Enabled &&
                    _sprites[i].Transform != null &&
                    _sprites[i].SortingOrder < OVERLAY_BATCH_SORTING_ORDER)
                {
                    activeSpriteCount++;
                }
            }

            int vertexCount = (activeCount * VERTS_PER_TENTACLE) + (activeSpriteCount * 4);
            int indexCount = (activeCount * TRIS_PER_TENTACLE) + (activeSpriteCount * 6);
            EnsureGeometryCapacity(vertexCount, indexCount);

            int vertexCursor = 0;
            int indexCursor = 0;
            for (int i = 0; i < _sprites.Count; i++)
            {
                SpriteHandle handle = _sprites[i];
                if (!IsRenderable(handle) ||
                    handle.SortingOrder >= TENTACLE_SORTING_ORDER ||
                    handle.SortingOrder >= OVERLAY_BATCH_SORTING_ORDER)
                {
                    continue;
                }

                WriteSpriteGeometry(handle, vertexCursor, indexCursor);
                vertexCursor += 4;
                indexCursor += 6;
            }

            for (int i = 0; i < _tentacles.Count; i++)
            {
                Tentacle tentacle = _tentacles[i];
                if (!tentacle.IsActive)
                {
                    continue;
                }

                int vertexOffset = vertexCursor;
                tentacle.WriteGeometry(
                    _verts,
                    _uvs,
                    vertexOffset,
                    GetAtlasRect(tentacle.Texture));
                for (int vertex = 0; vertex < VERTS_PER_TENTACLE; vertex++)
                {
                    _colors[vertexOffset + vertex] = Color.white;
                }
                int indexOffset = indexCursor;
                for (int segment = 0; segment < POINT_COUNT - 1; segment++)
                {
                    int baseVertex = vertexOffset + (segment * 2);
                    int triangle = indexOffset + (segment * 6);
                    _tris[triangle] = baseVertex;
                    _tris[triangle + 1] = baseVertex + 1;
                    _tris[triangle + 2] = baseVertex + 2;
                    _tris[triangle + 3] = baseVertex + 2;
                    _tris[triangle + 4] = baseVertex + 1;
                    _tris[triangle + 5] = baseVertex + 3;
                }

                vertexCursor += VERTS_PER_TENTACLE;
                indexCursor += TRIS_PER_TENTACLE;
            }

            for (int i = 0; i < _sprites.Count; i++)
            {
                SpriteHandle handle = _sprites[i];
                if (!IsRenderable(handle) ||
                    handle.SortingOrder < TENTACLE_SORTING_ORDER ||
                    handle.SortingOrder >= OVERLAY_BATCH_SORTING_ORDER)
                {
                    continue;
                }

                WriteSpriteGeometry(handle, vertexCursor, indexCursor);
                vertexCursor += 4;
                indexCursor += 6;
            }

            bool topologyChanged =
                _uploadedTentacleCount != activeCount ||
                _uploadedSpriteCount != activeSpriteCount;
            if (topologyChanged)
            {
                mesh.Clear(keepVertexLayout: true);
            }

            if (vertexCount > 0)
            {
                mesh.SetVertices(_verts, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
                mesh.SetUVs(0, _uvs, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
                mesh.SetColors(_colors, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
                if (topologyChanged)
                {
                    mesh.SetIndices(
                        _tris,
                        0,
                        indexCount,
                        MeshTopology.Triangles,
                        0,
                        calculateBounds: false);
                }

                Vector3 minimum = _verts[0];
                Vector3 maximum = minimum;
                for (int i = 1; i < vertexCount; i++)
                {
                    minimum = Vector3.Min(minimum, _verts[i]);
                    maximum = Vector3.Max(maximum, _verts[i]);
                }

                mesh.bounds = new Bounds(
                    (minimum + maximum) * 0.5f,
                    maximum - minimum + new Vector3(0.1f, 0.1f, 0.1f));
            }

            _uploadedTentacleCount = activeCount;
            _uploadedSpriteCount = activeSpriteCount;
            for (int i = 0; i < _sprites.Count; i++)
            {
                _sprites[i].CaptureState();
            }

            _geometryDirty = false;
        }

        private static bool IsRenderable(SpriteHandle handle)
        {
            return handle.Enabled && handle.Transform != null && handle.Sprite != null;
        }

        private void WriteSpriteGeometry(SpriteHandle handle, int vertexOffset, int indexOffset)
        {
            Sprite sprite = handle.Sprite ?? throw new InvalidOperationException(
                "An enabled batched sprite requires a Sprite.");
            Rect textureAtlasRect = GetAtlasRect(sprite.texture);
            Rect source = sprite.rect;
            float pixelsPerUnit = sprite.pixelsPerUnit;
            Vector2 pivot = new(
                sprite.pivot.x / source.width,
                sprite.pivot.y / source.height);
            float width = source.width / pixelsPerUnit;
            float height = source.height / pixelsPerUnit;
            float left = -pivot.x * width;
            float right = left + width;
            float bottom = -pivot.y * height;
            float top = bottom + height;
            Transform spriteTransform = handle.Transform;

            _verts[vertexOffset] = spriteTransform.TransformPoint(new Vector3(left, bottom, 0f));
            _verts[vertexOffset + 1] = spriteTransform.TransformPoint(new Vector3(left, top, 0f));
            _verts[vertexOffset + 2] = spriteTransform.TransformPoint(new Vector3(right, bottom, 0f));
            _verts[vertexOffset + 3] = spriteTransform.TransformPoint(new Vector3(right, top, 0f));

            float uMin = textureAtlasRect.xMin + ((source.xMin / sprite.texture.width) * textureAtlasRect.width);
            float uMax = textureAtlasRect.xMin + ((source.xMax / sprite.texture.width) * textureAtlasRect.width);
            float vMin = textureAtlasRect.yMin + ((source.yMin / sprite.texture.height) * textureAtlasRect.height);
            float vMax = textureAtlasRect.yMin + ((source.yMax / sprite.texture.height) * textureAtlasRect.height);
            _uvs[vertexOffset] = new Vector2(uMin, vMin);
            _uvs[vertexOffset + 1] = new Vector2(uMin, vMax);
            _uvs[vertexOffset + 2] = new Vector2(uMax, vMin);
            _uvs[vertexOffset + 3] = new Vector2(uMax, vMax);

            Color32 color = handle.Color;
            _colors[vertexOffset] = color;
            _colors[vertexOffset + 1] = color;
            _colors[vertexOffset + 2] = color;
            _colors[vertexOffset + 3] = color;

            _tris[indexOffset] = vertexOffset;
            _tris[indexOffset + 1] = vertexOffset + 1;
            _tris[indexOffset + 2] = vertexOffset + 2;
            _tris[indexOffset + 3] = vertexOffset + 2;
            _tris[indexOffset + 4] = vertexOffset + 1;
            _tris[indexOffset + 5] = vertexOffset + 3;
        }

        private void EnsureGeometryCapacity(int vertexCount, int indexCount)
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

        protected void OnDestroy()
        {
            if (_mesh != null)
            {
                Destroy(_mesh);
                _mesh = null;
            }

            if (_atlas != null)
            {
                _atlas.Dispose();
                _atlas = null;
            }

            _overlayBatch?.Dispose();
            _overlayBatch = null;

            _tentacles.Clear();
            _sprites.Clear();
        }
    }
}

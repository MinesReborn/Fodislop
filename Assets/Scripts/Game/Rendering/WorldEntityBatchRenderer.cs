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

namespace Kern.Game
{
    public class WorldEntityBatchRenderer : MonoBehaviour, ILightingGeometryContributor
    {
        // Matches the five-point tail used by the stable June implementation.
        public const int POINT_COUNT = 5;
        private const int VERTS_PER_TENTACLE = POINT_COUNT * 2;
        private const int TRIS_PER_TENTACLE = (POINT_COUNT - 1) * 6;
        private const int INITIAL_CAPACITY = 64;
        private const int BATCH_SORTING_ORDER = -1;
        private const int OVERLAY_BATCH_SORTING_ORDER = 600;
        private const int TENTACLE_SORTING_ORDER = -1;
        private static readonly float _visibilityPrefetchMargin =
            StreamingPolicy.Default.AllocationQuantumCells;

        private static readonly ProfilerMarker _LateUpdateMarker =
            new("Kern.WorldEntities.LateUpdate");

        private static readonly AllocationLedger.Entry _AllocationEntry =
            AllocationLedger.Register("Сущности мира — LateUpdate");

        private readonly List<Tentacle> _tentacles = [];
        private readonly List<SpriteHandle> _sprites = [];
        private readonly SpatialShardGrid<SpriteHandle> _spatialGrid = new();
        private readonly WorldEntityVisibility _visibility;
        private readonly WorldEntityLightingEmitter _lightingEmitter;
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
        [Inject]
        private IGameplayCamera? _gameplayCamera;
        [Inject]
        private LightingGeometryRegistry? _lightingGeometryRegistry;

        // Light-emitting sprites are drawn into the lighting fields from their
        // own mesh; see WorldEntityLightingEmitter. The revision follows only
        // their state — camera motion rebuilds the visible batch every frame
        // and must not re-solve light.
        private Material? _batchMaterial;
        private bool _lightingContributorRegistered;

        public WorldEntityBatchRenderer()
        {
            _visibility = new WorldEntityVisibility(_spatialGrid, _visibilityPrefetchMargin);
            _lightingEmitter = new WorldEntityLightingEmitter(_sprites, () => _batchMaterial, GetAtlasRect);
        }

        public ulong LightingGeometryRevision => _lightingEmitter.Revision;

        public sealed class SpriteHandle : WorldEntitySpriteHandle
        {
            internal SpriteHandle(Transform transform, int sortingOrder, bool isStatic, bool emitsLight)
                : base(transform, sortingOrder, isStatic, emitsLight)
            {
            }
        }

        public SpriteHandle RegisterSprite(
            Transform spriteTransform,
            int sortingOrder,
            bool isStatic = false,
            bool emitsLight = false)
        {
            var handle = new SpriteHandle(spriteTransform, sortingOrder, isStatic, emitsLight);
            _sprites.Add(handle);
            _sprites.Sort(static (left, right) => left.SortingOrder.CompareTo(right.SortingOrder));
            _spatialGrid.Insert(handle, spriteTransform.position);
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
            if (handle != null)
            {
                _spatialGrid.Remove(handle);
                if (_sprites.Remove(handle))
                {
                    _geometryDirty = true;
                }
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

        protected void Start()
        {
            if (_lightingGeometryRegistry != null && !_lightingContributorRegistered)
            {
                _lightingGeometryRegistry.Register(this);
                _lightingContributorRegistered = true;
            }
        }

        protected void LateUpdate()
        {
            using var marker = _LateUpdateMarker.Auto();
            using var allocationScope = AllocationLedger.Measure(_AllocationEntry);
            for (int i = 0; i < _sprites.Count; i++)
            {
                SpriteHandle handle = _sprites[i];
                handle.RefreshFrameState();
                if (!handle.IsStatic)
                {
                    _spatialGrid.Update(handle, handle.FramePosition);
                }
            }

            _lightingEmitter.UpdateRevision();

            Camera? camera = _gameplayCamera?.Camera;
            if (_visibility.UpdateCameraState(camera))
            {
                _geometryDirty = true;
            }

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

            if (!_geometryDirty || _mesh == null)
            {
                return;
            }

            bool hasCamera = _visibility.TryGetVisibleRect(camera, out Rect visibleRect);
            _visibility.Collect(_sprites, hasCamera, visibleRect, OVERLAY_BATCH_SORTING_ORDER, TENTACLE_SORTING_ORDER);
            RebuildMesh(hasCamera, visibleRect);
            _overlayBatch?.Rebuild(_visibility.Overlay, GetAtlasRect, _mesh.bounds);

            for (int i = 0; i < _sprites.Count; i++)
            {
                _sprites[i].CaptureState();
            }

            _geometryDirty = false;
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
            _batchMaterial = renderer.sharedMaterial;
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

        private void RebuildMesh(bool hasCamera, in Rect visibleRect)
        {
            Mesh mesh = _mesh ?? throw new InvalidOperationException(
                "Tentacle mesh must exist before geometry is rebuilt.");
            int activeCount = 0;
            for (int i = 0; i < _tentacles.Count; i++)
            {
                Tentacle tentacle = _tentacles[i];
                if (tentacle.IsActive && WorldEntityVisibility.IsTentacleInView(tentacle, hasCamera, visibleRect))
                {
                    activeCount++;
                }
            }

            int activeSpriteCount = _visibility.UnderTentacles.Count + _visibility.OverTentacles.Count;
            int vertexCount = (activeCount * VERTS_PER_TENTACLE) + (activeSpriteCount * 4);
            int indexCount = (activeCount * TRIS_PER_TENTACLE) + (activeSpriteCount * 6);
            EnsureGeometryCapacity(vertexCount, indexCount);

            int vertexCursor = 0;
            int indexCursor = 0;
            WriteSprites(_visibility.UnderTentacles, ref vertexCursor, ref indexCursor);

            for (int i = 0; i < _tentacles.Count; i++)
            {
                Tentacle tentacle = _tentacles[i];
                if (!tentacle.IsActive || !WorldEntityVisibility.IsTentacleInView(tentacle, hasCamera, visibleRect))
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

            WriteSprites(_visibility.OverTentacles, ref vertexCursor, ref indexCursor);

            vertexCount = vertexCursor;
            indexCount = indexCursor;

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

                if (hasCamera)
                {
                    mesh.bounds = new Bounds(
                        new Vector3(visibleRect.center.x, visibleRect.center.y, 0f),
                        new Vector3(visibleRect.size.x + 8f, visibleRect.size.y + 8f, 20f));
                }
                else
                {
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
            }

            _uploadedTentacleCount = activeCount;
            _uploadedSpriteCount = activeSpriteCount;
        }

        public void RenderLightingFields(CommandBuffer commandBuffer, in LightingFieldContext context)
        {
            if (_atlas == null)
            {
                return;
            }

            _lightingEmitter.RenderFields(commandBuffer, context);
        }

        private void WriteSprites(
            List<SpriteHandle> handles,
            ref int vertexCursor,
            ref int indexCursor)
        {
            for (int i = 0; i < handles.Count; i++)
            {
                SpriteHandle handle = handles[i];
                Sprite sprite = handle.Sprite ?? throw new InvalidOperationException(
                    "An enabled batched sprite requires a Sprite.");
                WorldEntityGeometry.WriteSprite(
                    _verts,
                    _uvs,
                    _colors,
                    _tris,
                    handle,
                    GetAtlasRect(sprite.texture),
                    vertexCursor,
                    indexCursor);
                vertexCursor += 4;
                indexCursor += 6;
            }
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
            if (_lightingContributorRegistered)
            {
                _lightingGeometryRegistry?.Unregister(this);
                _lightingContributorRegistered = false;
            }

            _lightingEmitter.Dispose();

            if (_mesh != null)
            {
                Destroy(_mesh);
                _mesh = null;
            }

            _atlas?.Dispose();
            _atlas = null;
            _overlayBatch?.Dispose();
            _overlayBatch = null;
            _tentacles.Clear();
            _sprites.Clear();
        }
    }
}

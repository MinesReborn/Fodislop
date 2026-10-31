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

namespace Kern.Game.Rendering
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Kern.Game", sourceAssembly: null, sourceClassName: "WorldEntityBatchRenderer")]
    [DefaultExecutionOrder(150)]
    public class WorldEntityBatchRenderer : MonoBehaviour, Kern.Core.Interfaces.WorldLighting.ILightingGeometryContributor
    {
        // Matches the five-point tail used by the stable June implementation:
        // the robot plus four chain nodes, laid out as the legacy client's four
        // four-vertex sectors.
        public const int POINT_COUNT = 5;
        private const int VERTS_PER_TENTACLE = TentacleGeometry.VerticesPerTentacle;
        private const int INDICES_PER_TENTACLE = TentacleGeometry.IndicesPerTentacle;
        private const int INITIAL_CAPACITY = 64;
        private const int BATCH_SORTING_ORDER = -1;
        private const int OVERLAY_BATCH_SORTING_ORDER = 600;
        private const int TENTACLE_SORTING_ORDER = -1;
        private static readonly float s_visibilityPrefetchMargin =
            StreamingPolicy.Default.AllocationQuantumCells;

        private static readonly ProfilerMarker s_lateUpdateMarker =
            new("Kern.WorldEntities.LateUpdate");

        private static readonly AllocationLedger.Entry s_allocationEntry =
            AllocationLedger.Register("Сущности мира — LateUpdate");

        private static readonly int s_spriteAlphaCullId =
            Shader.PropertyToID("_SpriteAlphaCull");

        private static readonly int s_glowFieldThresholdId =
            Shader.PropertyToID("_GlowFieldThreshold");

        private static bool s_tuningGlobalsApplied;

        private readonly List<Tentacle> _tentacles = new();
        private readonly List<SpriteHandle> _sprites = new();
        private readonly Dictionary<string, Sprite> _ownedSpritesByKey = new(StringComparer.Ordinal);
        private readonly SpatialShardGrid<SpriteHandle> _spatialGrid = new();
        private readonly WorldEntityVisibility _visibility;
        private readonly WorldEntityLightingEmitter _lightingEmitter;
        private readonly WorldEntityGeometryBuffers _geometry;
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
        private IGameplayCamera? _gameplayCamera = null;
        [Inject]
        private IWorldPresentationState _presentationState = null!;
        [Inject]
        private LightingGeometryRegistry? _lightingGeometryRegistry = null;

        // Light-emitting sprites are drawn into the lighting fields from their
        // own mesh; see WorldEntityLightingEmitter. The revision follows only
        // their state — camera motion may rebuild the visible batch when it
        // leaves the cached coverage and must not re-solve light.
        private Material? _batchMaterial;
        private bool _lightingContributorRegistered;

        public WorldEntityBatchRenderer()
        {
            _geometry = new WorldEntityGeometryBuffers(VERTS_PER_TENTACLE * INITIAL_CAPACITY,
                INDICES_PER_TENTACLE * INITIAL_CAPACITY, GetAtlasRect);
            _visibility = new WorldEntityVisibility(_spatialGrid, s_visibilityPrefetchMargin);
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

        internal Sprite GetOrCreateOwnedSprite(
            string key,
            Func<Texture2D> createTexture,
            int width,
            int height)
        {
            if (_ownedSpritesByKey.TryGetValue(key, out Sprite? sprite) && sprite != null)
            {
                return sprite;
            }

            Texture2D texture = createTexture();
            try
            {
                sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, width, height),
                    new Vector2(0.5f, 0.5f),
                    RenderingConstants.PIXELS_PER_UNIT);
                _ownedSpritesByKey[key] = sprite;
                return sprite;
            }
            catch
            {
                Destroy(texture);
                throw;
            }
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
            using var marker = s_lateUpdateMarker.Auto();
            using var allocationScope = AllocationLedger.Measure(s_allocationEntry);
            if (_presentationState.IsTeleportPreparing)
            {
                return;
            }

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
            ApplyTuningGlobals();
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

        // Пороги отсечения одинаковы для всех материалов мира сущностей, поэтому
        // уходят глобальными юниформами, а не в каждый материал по отдельности.
        private static void ApplyTuningGlobals()
        {
            if (s_tuningGlobalsApplied)
            {
                return;
            }

            Shader.SetGlobalFloat(s_spriteAlphaCullId, WorldRenderConfigHolder.SpriteAlphaCull);
            Shader.SetGlobalFloat(
                s_glowFieldThresholdId,
                WorldRenderConfigHolder.GlowFieldThreshold);
            s_tuningGlobalsApplied = true;
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
            int indexCount = (activeCount * INDICES_PER_TENTACLE) + (activeSpriteCount * 6);
            _geometry.EnsureGeometryCapacity(vertexCount, indexCount);

            int vertexCursor = 0;
            int indexCursor = 0;
            _geometry.WriteSprites(_visibility.UnderTentacles, ref vertexCursor, ref indexCursor);

            for (int i = 0; i < _tentacles.Count; i++)
            {
                Tentacle tentacle = _tentacles[i];
                if (!tentacle.IsActive || !WorldEntityVisibility.IsTentacleInView(tentacle, hasCamera, visibleRect))
                {
                    continue;
                }

                int vertexOffset = vertexCursor;
                tentacle.WriteGeometry(
                    _geometry.Vertices,
                    _geometry.Uvs,
                    vertexOffset,
                    GetAtlasRect(tentacle.Texture));
                for (int vertex = 0; vertex < VERTS_PER_TENTACLE; vertex++)
                {
                    _geometry.Colors[vertexOffset + vertex] = Color.white;
                }

                int indexOffset = indexCursor;
                TentacleGeometry.WriteIndices(_geometry.Indices, indexOffset, vertexOffset);

                vertexCursor += VERTS_PER_TENTACLE;
                indexCursor += INDICES_PER_TENTACLE;
            }

            _geometry.WriteSprites(_visibility.OverTentacles, ref vertexCursor, ref indexCursor);

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
                mesh.SetVertices(_geometry.Vertices, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
                mesh.SetUVs(0, _geometry.Uvs, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
                mesh.SetColors(_geometry.Colors, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
                if (topologyChanged)
                {
                    mesh.SetIndices(
                        _geometry.Indices,
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
                    Vector3 minimum = _geometry.Vertices[0];
                    Vector3 maximum = minimum;
                    for (int i = 1; i < vertexCount; i++)
                    {
                        minimum = Vector3.Min(minimum, _geometry.Vertices[i]);
                        maximum = Vector3.Max(maximum, _geometry.Vertices[i]);
                    }

                    mesh.bounds = new Bounds(
                        (minimum + maximum) * 0.5f,
                        maximum - minimum + new Vector3(0.1f, 0.1f, 0.1f));
                }
            }

            _uploadedTentacleCount = activeCount;
            _uploadedSpriteCount = activeSpriteCount;
        }

        public void RenderMaterialGlowFields(
            CommandBuffer commandBuffer,
            in Kern.Core.Interfaces.WorldLighting.LightingMaterialGlowContext context)
        {
            if (_atlas == null)
            {
                return;
            }

            _lightingEmitter.RenderMaterialGlowFields(commandBuffer, context);
        }

        public void RenderAmbientOcclusionField(
            CommandBuffer commandBuffer,
            in Kern.Core.Interfaces.WorldLighting.LightingAmbientOcclusionContext context)
        {
            if (_atlas == null)
            {
                return;
            }

            _lightingEmitter.RenderAmbientOcclusionField(commandBuffer, context);
        }

        protected void OnDestroy()
        {
            if (_lightingContributorRegistered)
            {
                _lightingGeometryRegistry?.Unregister(this);
                _lightingContributorRegistered = false;
            }

            _lightingEmitter.Dispose();

            foreach (Sprite sprite in _ownedSpritesByKey.Values)
            {
                if (sprite != null)
                {
                    Texture2D texture = sprite.texture;
                    Destroy(sprite);
                    if (texture != null)
                    {
                        Destroy(texture);
                    }
                }
            }

            _ownedSpritesByKey.Clear();

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

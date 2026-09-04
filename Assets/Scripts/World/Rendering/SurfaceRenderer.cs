#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Lifecycle;
using Fodinae.World.Lighting;
using UnityEngine;
using UnityEngine.Rendering;
using VContainer;
using Unity.Profiling;

namespace Fodinae.World
{
    [DisallowMultipleComponent]
    public class SurfaceRenderer : MonoBehaviour, ILightingGeometryContributor
    {
        private static readonly ProfilerMarker SurfaceLateUpdateMarker =
            new("Fodinae.Surface.LateUpdate");

        private const string TransitObjectName = "SurfaceTransit";
        private const string PerspectiveObjectName = "SurfacePerspective";
        private const string RedRockObjectName = "SurfaceRedrock";

        [Header("Local Assets")]
        [SerializeField]
        private Texture2D? _transitTexture;
        [SerializeField]
        private Texture2D? _perspectiveTexture;
        [SerializeField]
        private Texture2D? _redRockTexture;

        [Header("Rendering")]
        [SerializeField]
        private int _transitSortingOrder = -501;
        [SerializeField]
        private int _perspectiveSortingOrder = -502;

        [Inject]
        private MapManager _mapManager = null!;
        [Inject]
        private LightingGeometryRegistry _lightingGeometryRegistry = null!;
        [Inject]
        private IClientConfigManager _clientConfigManager = null!;
        [Inject]
        private ISceneObjectFactory _sceneObjects = null!;
        [Inject]
        private IGameplayCamera _gameplayCamera = null!;

        private readonly SurfaceGeometryBuilder _geometry = new();
        private readonly SurfaceMaterialManager _materialManager = new();

        private Camera? _mainCamera;
        private Mesh? _transitMesh;
        private Mesh? _perspectiveMesh;
        private Mesh? _redRockMesh;
        private Mesh? _transitLightingMesh;
        private Mesh? _perspectiveLightingMesh;
        private Mesh? _redRockLightingMesh;
        private Material? _transitMaterial;
        private Material? _perspectiveMaterial;
        private Material? _redRockMaterial;
        private ulong _lightingGeometryRevision = 1;
        private int _lastWorldWidth = int.MinValue;
        private int _lastWorldHeight = int.MinValue;
        private Rect _cachedCoverageRect;
        private bool _hasCachedCoverage;
        private bool _initialized;
        private bool _registered;

        public ulong LightingGeometryRevision => _lightingGeometryRevision;
        public bool IsInitialized => _initialized;

        public void ApplyClientConfig()
        {
            if (!_initialized)
            {
                return;
            }

            ClientConfig config = _clientConfigManager.Config ??
                throw new InvalidOperationException(
                    "SurfaceRenderer requires an initialized ClientConfig.");
            Material transitMaterial = _transitMaterial ??
                throw new InvalidOperationException(
                    "SurfaceRenderer transit material is not initialized.");
            Material perspectiveMaterial = _perspectiveMaterial ??
                throw new InvalidOperationException(
                    "SurfaceRenderer perspective material is not initialized.");
            Material redRockMaterial = _redRockMaterial ??
                throw new InvalidOperationException(
                    "SurfaceRenderer redrock material is not initialized.");

            _materialManager.ApplyMaterialConfig(
                transitMaterial,
                config.Terrain.TransitEmissionColor,
                config.Terrain.TransitEmissionStrength,
                config.Terrain.SurfaceOccupancy);
            _materialManager.ApplyMaterialConfig(
                perspectiveMaterial,
                config.Terrain.PerspectiveEmissionColor,
                config.Terrain.PerspectiveEmissionStrength,
                occupancy: 0f);
            _materialManager.ApplyMaterialConfig(
                redRockMaterial,
                Color.clear,
                emissionStrength: 0f,
                occupancy: 1f);
            _lightingGeometryRevision++;
            Debug.Log($"[SurfaceRenderer] ApplyClientConfig: revision={_lightingGeometryRevision}");
        }

        public void SetLocalAssets(
            Texture2D? transitTexture,
            Texture2D? perspectiveTexture,
            Texture2D? redRockTexture)
        {
            if (_initialized)
            {
                if (_transitTexture == transitTexture &&
                    _perspectiveTexture == perspectiveTexture &&
                    _redRockTexture == redRockTexture)
                {
                    return;
                }

                throw new InvalidOperationException(
                    "Surface assets cannot be replaced after SurfaceRenderer initialization.");
            }

            _transitTexture = transitTexture;
            _perspectiveTexture = perspectiveTexture;
            _redRockTexture = redRockTexture;

            if (_mapManager != null && _mapManager.IsWorldInitialized)
            {
                EnsureInitialized();
            }
        }

        public void RenderLightingFields(
            CommandBuffer commandBuffer,
            in LightingFieldContext context)
        {
            if (!_initialized || _transitLightingMesh == null ||
                _perspectiveLightingMesh == null || _redRockLightingMesh == null ||
                _transitMaterial == null || _perspectiveMaterial == null ||
                _redRockMaterial == null)
            {
                throw new InvalidOperationException(
                    "Surface lighting fields cannot be rendered before surface initialization.");
            }

            Rect lightingRect = Rect.MinMaxRect(
                context.WorldRect.x,
                context.WorldRect.y,
                context.WorldRect.x + context.WorldRect.z,
                context.WorldRect.y + context.WorldRect.w);
            _geometry.UpdateBoundaryMesh(
                _redRockLightingMesh,
                lightingRect,
                _mapManager.WorldWidth,
                _mapManager.WorldHeight);
            _geometry.UpdateTransitMesh(
                _transitLightingMesh,
                lightingRect,
                _mapManager.WorldHeight);
            _geometry.UpdatePerspectiveMesh(
                _perspectiveLightingMesh,
                lightingRect,
                _mapManager.WorldHeight);

            SurfaceMeshUtilities.DrawLightingField(
                commandBuffer,
                _redRockLightingMesh,
                _redRockMaterial);
            SurfaceMeshUtilities.DrawLightingField(
                commandBuffer,
                _perspectiveLightingMesh,
                _perspectiveMaterial);
            SurfaceMeshUtilities.DrawLightingField(
                commandBuffer,
                _transitLightingMesh,
                _transitMaterial);
        }

        protected void OnEnable()
        {
            if (_mapManager != null)
            {
                _mapManager.OnWorldInitialized -= OnWorldInitialized;
                _mapManager.OnWorldInitialized += OnWorldInitialized;
            }

            if (_initialized && !_registered && _lightingGeometryRegistry != null)
            {
                _lightingGeometryRegistry.Register(this);
                _registered = true;
            }
        }

        protected void Start()
        {
            if (!_initialized && _mapManager != null && _mapManager.IsWorldInitialized)
            {
                EnsureInitialized();
            }
        }

        private void OnWorldInitialized()
        {
            if (!_initialized)
            {
                EnsureInitialized();
            }
        }

        protected void LateUpdate()
        {
            using var marker = SurfaceLateUpdateMarker.Auto();
            if (_mapManager == null || !_mapManager.IsWorldInitialized)
            {
                return;
            }

            if (!EnsureInitialized())
            {
                return;
            }

            Camera? resolvedCam = _gameplayCamera?.Camera;
            if (resolvedCam != null)
            {
                _mainCamera = resolvedCam;
            }

            if (_mainCamera == null)
            {
                return;
            }

            Camera mainCamera = _mainCamera;
            Rect visibleRect = SurfaceGeometryBuilder.GetVisibleRect(mainCamera);
            if (_lastWorldWidth == _mapManager.WorldWidth &&
                _lastWorldHeight == _mapManager.WorldHeight &&
                _hasCachedCoverage &&
                SurfaceGeometryBuilder.Contains(_cachedCoverageRect, visibleRect))
            {
                return;
            }

            RebuildVisibleGeometry(
                _mapManager.WorldWidth,
                _mapManager.WorldHeight,
                visibleRect);
        }

        protected void OnDisable()
        {
            if (_mapManager != null)
            {
                _mapManager.OnWorldInitialized -= OnWorldInitialized;
            }

            UnregisterLightingContributor();
        }

        protected void OnDestroy()
        {
            UnregisterLightingContributor();
            SurfaceMeshUtilities.DestroyOwned(_transitMesh);
            SurfaceMeshUtilities.DestroyOwned(_perspectiveMesh);
            SurfaceMeshUtilities.DestroyOwned(_redRockMesh);
            SurfaceMeshUtilities.DestroyOwned(_transitLightingMesh);
            SurfaceMeshUtilities.DestroyOwned(_perspectiveLightingMesh);
            SurfaceMeshUtilities.DestroyOwned(_redRockLightingMesh);
            SurfaceMeshUtilities.DestroyOwned(_transitMaterial);
            SurfaceMeshUtilities.DestroyOwned(_perspectiveMaterial);
            SurfaceMeshUtilities.DestroyOwned(_redRockMaterial);

            if (!Application.isPlaying)
            {
                DestroyOwnedChild(TransitObjectName);
                DestroyOwnedChild(PerspectiveObjectName);
                DestroyOwnedChild(RedRockObjectName);
            }
        }

        private bool EnsureInitialized()
        {
            if (_initialized)
            {
                return true;
            }

            if (_transitTexture == null || _perspectiveTexture == null || _redRockTexture == null)
            {
                return false;
            }

            Texture2D transitTexture = _transitTexture;
            Texture2D perspectiveTexture = _perspectiveTexture;
            Texture2D redRockTexture = _redRockTexture;
            ClientConfig? clientConfig = _clientConfigManager?.Config;
            if (clientConfig == null || _mapManager == null || _mapManager.WorldWidth <= 0 || _mapManager.WorldHeight <= 0)
            {
                return false;
            }

            _transitMaterial = _materialManager.CreateSurfaceMaterial(
                transitTexture,
                clientConfig.Terrain.TransitEmissionColor,
                clientConfig.Terrain.TransitEmissionStrength,
                clientConfig.Terrain.SurfaceOccupancy,
                Vector2.one,
                new Vector2(_mapManager.WorldWidth, _mapManager.WorldHeight),
                SurfaceMaterialManager.SurfaceKind.Transit,
                "World Surface Transit");

            _perspectiveMaterial = _materialManager.CreateSurfaceMaterial(
                perspectiveTexture,
                clientConfig.Terrain.PerspectiveEmissionColor,
                clientConfig.Terrain.PerspectiveEmissionStrength,
                occupancy: 0f,
                baseMapTileCount: Vector2.one,
                worldSize: new Vector2(_mapManager.WorldWidth, _mapManager.WorldHeight),
                kind: SurfaceMaterialManager.SurfaceKind.Perspective,
                materialName: "World Surface Perspective");

            _redRockMaterial = _materialManager.CreateSurfaceMaterial(
                redRockTexture,
                Color.clear,
                emissionStrength: 0f,
                occupancy: 1f,
                baseMapTileCount: _materialManager.GetTerrainSheetTileCount(redRockTexture),
                worldSize: new Vector2(_mapManager.WorldWidth, _mapManager.WorldHeight),
                kind: SurfaceMaterialManager.SurfaceKind.RedRock,
                materialName: "World Surface Redrock");

            _transitMesh = SurfaceMeshUtilities.CreateDynamic("World Surface Transit Mesh");
            _perspectiveMesh = SurfaceMeshUtilities.CreateDynamic("World Surface Perspective Mesh");
            _redRockMesh = SurfaceMeshUtilities.CreateDynamic("World Surface Redrock Mesh");
            _transitLightingMesh = SurfaceMeshUtilities.CreateDynamic("World Surface Transit Lighting Mesh");
            _perspectiveLightingMesh = SurfaceMeshUtilities.CreateDynamic("World Surface Perspective Lighting Mesh");
            _redRockLightingMesh = SurfaceMeshUtilities.CreateDynamic("World Surface Redrock Lighting Mesh");

            BindBandObject(
                TransitObjectName,
                _transitMesh,
                _transitMaterial,
                _transitSortingOrder);
            BindBandObject(
                PerspectiveObjectName,
                _perspectiveMesh,
                _perspectiveMaterial,
                _perspectiveSortingOrder);
            BindBandObject(
                RedRockObjectName,
                _redRockMesh,
                _redRockMaterial,
                _transitSortingOrder);

            _lightingGeometryRegistry.Register(this);
            _registered = true;
            _initialized = true;
            return true;
        }

        private void RebuildVisibleGeometry(
            int worldWidth,
            int worldHeight,
            Rect visibleRect)
        {
            if (worldWidth <= 0 || worldHeight <= 0)
            {
                throw new InvalidOperationException(
                    $"SurfaceRenderer received invalid world dimensions {worldWidth}x{worldHeight}.");
            }

            Rect coverageRect = SurfaceGeometryBuilder.BuildCoverageRect(visibleRect);

            _geometry.UpdateBoundaryMesh(_redRockMesh!, coverageRect, worldWidth, worldHeight);
            _geometry.UpdateTransitMesh(_transitMesh!, coverageRect, worldHeight);
            _geometry.UpdatePerspectiveMesh(_perspectiveMesh!, coverageRect, worldHeight);

            if (_lastWorldWidth != worldWidth || _lastWorldHeight != worldHeight)
            {
                _materialManager.SetMaterialWorldSize(
                    _transitMaterial,
                    _perspectiveMaterial,
                    _redRockMaterial,
                    worldWidth,
                    worldHeight);
                _lightingGeometryRevision++;
            }

            _lastWorldWidth = worldWidth;
            _lastWorldHeight = worldHeight;
            _cachedCoverageRect = coverageRect;
            _hasCachedCoverage = true;
        }

        private void BindBandObject(
            string objectName,
            Mesh mesh,
            Material material,
            int sortingOrder)
        {
            Transform? existingTransform = transform.Find(objectName);
            GameObject bandObject;
            if (existingTransform == null)
            {
                bandObject = _sceneObjects.Create(objectName, RuntimeOwner.General);
                bandObject.transform.SetParent(transform, worldPositionStays: false);
            }
            else
            {
                bandObject = existingTransform.gameObject;
            }

            bandObject.layer = gameObject.layer;
            bandObject.transform.SetLocalPositionAndRotation(
                Vector3.zero,
                Quaternion.identity);
            bandObject.transform.localScale = Vector3.one;
            MeshFilter meshFilter = SurfaceMeshUtilities.GetOrAddComponent<MeshFilter>(bandObject);
            MeshRenderer meshRenderer = SurfaceMeshUtilities.GetOrAddComponent<MeshRenderer>(bandObject);
            meshFilter.sharedMesh = mesh;
            meshRenderer.sharedMaterial = material;
            meshRenderer.sortingOrder = sortingOrder;
            bandObject.SetActive(true);
        }

        private void UnregisterLightingContributor()
        {
            if (!_registered)
            {
                return;
            }

            _lightingGeometryRegistry?.Unregister(this);
            _registered = false;
        }

        private void DestroyOwnedChild(string objectName)
        {
            Transform? ownedChild = transform.Find(objectName);
            if (ownedChild != null)
            {
                SurfaceMeshUtilities.DestroyOwned(ownedChild.gameObject);
            }
        }

    }
}

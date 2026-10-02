#nullable enable

using Kern.Core.Interfaces.Diagnostics;
using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.World.Lighting;
using UnityEngine;
using UnityEngine.Rendering;
using VContainer;
using Unity.Profiling;

namespace Kern.World
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(50)]
    public class SurfaceRenderer : MonoBehaviour, Kern.Core.Interfaces.WorldLighting.ILightingGeometryContributor
    {
        private static readonly ProfilerMarker s_surfaceLateUpdateMarker =
            new("Kern.Surface.LateUpdate");
        private static readonly ProfilerMarker s_surfaceLightingMeshBuildMarker =
            new("Kern.Surface.RebuildLightingMeshes");

        private static readonly AllocationLedger.Entry s_allocationEntry =
            AllocationLedger.Register("Поверхность — LateUpdate");

        private const string TransitObjectName = "SurfaceTransit";
        private const string PerspectiveObjectName = "SurfacePerspective";
        private const string HorizonObjectName = "SurfaceHorizon";
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
        private Mesh? _horizonMesh;
        private Mesh? _transitLightingMesh;
        private Mesh? _redRockLightingMesh;
        private Material? _transitMaterial;
        private Material? _perspectiveMaterial;
        private Material? _redRockMaterial;
        private Material? _horizonMaterial;
        private MeshRenderer? _transitRenderer;
        private MeshRenderer? _perspectiveRenderer;
        private MeshRenderer? _horizonRenderer;
        private ulong _lightingGeometryRevision = 1;
        private int _lastWorldWidth = int.MinValue;
        private int _lastWorldHeight = int.MinValue;
        private Rect _cachedCoverageRect;
        private bool _hasCachedCoverage;
        private Vector4 _lastLightingMeshRect;
        private int _lastLightingMeshWorldWidth = int.MinValue;
        private int _lastLightingMeshWorldHeight = int.MinValue;
        private bool _hasLightingMeshes;
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

        public void RenderMaterialEmissionFields(
            CommandBuffer commandBuffer,
            in Kern.Core.Interfaces.WorldLighting.LightingMaterialEmissionContext context) =>
            RenderLightingMeshes(
                commandBuffer,
                context.WorldRect,
                ProjectRuntimeContracts.ShaderPassNames.LightingMaterialField);

        public void RenderAmbientOcclusionField(
            CommandBuffer commandBuffer,
            in Kern.Core.Interfaces.WorldLighting.LightingAmbientOcclusionContext context) =>
            RenderLightingMeshes(
                commandBuffer,
                context.WorldRect,
                ProjectRuntimeContracts.ShaderPassNames.LightingAmbientOcclusionField);

        private void RenderLightingMeshes(
            CommandBuffer commandBuffer,
            Vector4 worldRect,
            string shaderPassName)
        {
            if (!_initialized || _transitLightingMesh == null ||
                _redRockLightingMesh == null ||
                _transitMaterial == null || _redRockMaterial == null)
            {
                throw new InvalidOperationException(
                    "Surface lighting fields cannot be rendered before surface initialization.");
            }

            int worldWidth = _mapManager.WorldWidth;
            int worldHeight = _mapManager.WorldHeight;
            if (!_hasLightingMeshes || _lastLightingMeshRect != worldRect ||
                _lastLightingMeshWorldWidth != worldWidth ||
                _lastLightingMeshWorldHeight != worldHeight)
            {
                using var buildMarker = s_surfaceLightingMeshBuildMarker.Auto();
                Rect lightingRect = Rect.MinMaxRect(
                    worldRect.x,
                    worldRect.y,
                    worldRect.x + worldRect.z,
                    worldRect.y + worldRect.w);
                _geometry.UpdateBoundaryMesh(
                    _redRockLightingMesh,
                    lightingRect,
                    worldWidth,
                    worldHeight);
                _geometry.UpdateTransitMesh(
                    _transitLightingMesh,
                    lightingRect,
                    worldHeight);
                _lastLightingMeshRect = worldRect;
                _lastLightingMeshWorldWidth = worldWidth;
                _lastLightingMeshWorldHeight = worldHeight;
                _hasLightingMeshes = true;
            }

            SurfaceMeshUtilities.DrawLightingField(
                commandBuffer,
                _redRockLightingMesh,
                _redRockMaterial,
                shaderPassName);
            SurfaceMeshUtilities.DrawLightingField(
                commandBuffer,
                _transitLightingMesh,
                _transitMaterial,
                shaderPassName);
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
            _hasCachedCoverage = false;
            _hasLightingMeshes = false;
            _lastWorldWidth = 0;
            _lastWorldHeight = 0;
            if (!_initialized)
            {
                EnsureInitialized();
            }
        }

        protected void LateUpdate()
        {
            using var marker = s_surfaceLateUpdateMarker.Auto();
            using var allocationScope = AllocationLedger.Measure(s_allocationEntry);
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
            float cx = mainCamera.transform.position.x;
            float surfaceY = _mapManager.WorldHeight;

            float visibleHalfWidth = (mainCamera.orthographicSize * mainCamera.aspect) +
                SurfaceGeometryBuilder.BoundaryOverscan;
            float span = Mathf.Max(visibleHalfWidth + 16f, Screen.width / 30f, 64f);

            _geometry.UpdateSurfaceMeshes(
                _transitMesh!,
                _perspectiveMesh!,
                cx,
                surfaceY,
                span);

            _horizonRenderer!.transform.localPosition = new Vector3(
                cx,
                surfaceY,
                -0.02f);

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
            SurfaceMeshUtilities.DestroyOwned(_horizonMesh);
            SurfaceMeshUtilities.DestroyOwned(_transitLightingMesh);
            SurfaceMeshUtilities.DestroyOwned(_redRockLightingMesh);
            SurfaceMeshUtilities.DestroyOwned(_transitMaterial);
            SurfaceMeshUtilities.DestroyOwned(_perspectiveMaterial);
            SurfaceMeshUtilities.DestroyOwned(_redRockMaterial);
            SurfaceMeshUtilities.DestroyOwned(_horizonMaterial);

            if (!Application.isPlaying)
            {
                DestroyOwnedChild(TransitObjectName);
                DestroyOwnedChild(PerspectiveObjectName);
                DestroyOwnedChild(RedRockObjectName);
                DestroyOwnedChild(HorizonObjectName);
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

            Vector2 worldSize = new(_mapManager.WorldWidth, _mapManager.WorldHeight);
            _transitMaterial = _materialManager.CreateSurfaceMaterial(
                transitTexture, clientConfig.Terrain.TransitEmissionColor,
                clientConfig.Terrain.TransitEmissionStrength, clientConfig.Terrain.SurfaceOccupancy,
                Vector2.one, worldSize, SurfaceMaterialManager.SurfaceKind.Transit, "World Surface Transit");

            _perspectiveMaterial = _materialManager.CreateSurfaceMaterial(
                perspectiveTexture, clientConfig.Terrain.PerspectiveEmissionColor,
                clientConfig.Terrain.PerspectiveEmissionStrength, occupancy: 0f,
                baseMapTileCount: Vector2.one, worldSize: worldSize,
                kind: SurfaceMaterialManager.SurfaceKind.Perspective, materialName: "World Surface Perspective");

            _redRockMaterial = _materialManager.CreateSurfaceMaterial(
                redRockTexture, Color.clear, emissionStrength: 0f, occupancy: 1f,
                baseMapTileCount: _materialManager.GetTerrainSheetTileCount(redRockTexture),
                worldSize: worldSize, kind: SurfaceMaterialManager.SurfaceKind.RedRock,
                materialName: "World Surface Redrock");

            _horizonMaterial = _materialManager.CreateSurfaceMaterial(
                perspectiveTexture, Color.clear, emissionStrength: 0f, occupancy: 0f,
                baseMapTileCount: Vector2.one, worldSize: worldSize,
                kind: SurfaceMaterialManager.SurfaceKind.Horizon, materialName: "World Surface Horizon");
            _materialManager.SetHorizonSkyColor(_horizonMaterial, SurfaceMaterialManager.HorizonSkyColor);
            _materialManager.SetPerspectiveProjection(_perspectiveMaterial, _gameplayCamera.Camera);

            _transitMesh = SurfaceMeshUtilities.CreateDynamic("World Surface Transit Mesh");
            _perspectiveMesh = SurfaceMeshUtilities.CreateDynamic("World Surface Perspective Mesh");
            _redRockMesh = SurfaceMeshUtilities.CreateDynamic("World Surface Redrock Mesh");
            _horizonMesh = SurfaceMeshUtilities.CreateDynamic("World Surface Horizon Mesh");
            _geometry.UpdateHorizonMesh(_horizonMesh);
            _geometry.InitializePerspectiveMesh(_perspectiveMesh);
            _geometry.InitializeTransitMesh(_transitMesh);
            _transitLightingMesh = SurfaceMeshUtilities.CreateDynamic("World Surface Transit Lighting Mesh");
            _redRockLightingMesh = SurfaceMeshUtilities.CreateDynamic("World Surface Redrock Lighting Mesh");

            _transitRenderer = BindBandObject(
                TransitObjectName,
                _transitMesh,
                _transitMaterial,
                _transitSortingOrder);
            _perspectiveRenderer = BindBandObject(
                PerspectiveObjectName,
                _perspectiveMesh,
                _perspectiveMaterial,
                _perspectiveSortingOrder);
            BindBandObject(
                RedRockObjectName,
                _redRockMesh,
                _redRockMaterial,
                _transitSortingOrder);
            _horizonRenderer = BindBandObject(
                HorizonObjectName,
                _horizonMesh,
                _horizonMaterial,
                _perspectiveSortingOrder - 1);

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

        private MeshRenderer BindBandObject(
            string objectName,
            Mesh mesh,
            Material material,
            int sortingOrder) =>
            SurfaceMeshUtilities.BindBandObject(
                _sceneObjects,
                transform,
                gameObject.layer,
                objectName,
                mesh,
                material,
                sortingOrder);

        private void UnregisterLightingContributor()
        {
            if (!_registered)
            {
                return;
            }

            _lightingGeometryRegistry?.Unregister(this);
            _registered = false;
        }

        private void DestroyOwnedChild(string objectName) =>
            SurfaceMeshUtilities.DestroyOwnedChild(transform, objectName);

    }
}

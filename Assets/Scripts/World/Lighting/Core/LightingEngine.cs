#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Rendering;
using Kern.World.Lighting.Diagnostics;
using Kern.World.Lighting.Quality;
using Kern.World.Streaming;
using Kern.World.Terrain;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VContainer;

namespace Kern.World.Lighting
{
    [DisallowMultipleComponent]
    public class LightingEngine : MonoBehaviour
    {
        public enum DebugView
        {
            FinalLighting = 0,
            Occupancy = 1,
            Albedo = 2,
            Emission = 3,
            Transmission = 4,
            StaticDirect = 5,
            DynamicDirect = 6,
            DiffuseBounce = 7,
            Exposure = 8,

            AmbientOcclusion = 9,
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForDomainReload()
        {
            Shader.DisableKeyword(LightingPresentation.WorldLightingKeyword);
        }

        [Header("Quality")]

        // Quality is selected by ClientConfig.GraphicsPreset at runtime.
        private GraphicsPreset _graphicsPreset;
        private LightingQualityMode _lightingQualityMode = LightingQualityMode.PerBlock;


        [Header("Diagnostics")]
        [SerializeField]
        [Tooltip("Debug view для проверки отдельных lighting-слоёв без скрытого AO/exposure влияния.")]
        private DebugView _debugView;

        private readonly LightingResourceManager _resources = new();
        private readonly LightingRuntimeState _runtimeState = new();
        private StaticLightingSolver? _staticLightingSolver;
        private DynamicLightingSolver? _dynamicLightingSolver;
        private IndirectLightingSolver? _indirectLightingSolver;
        private GeometryLightingSolver? _geometryLightingSolver;
        private LightingFrameExecutor? _lightingFrameExecutor;
        private LightingGPULifecycle? _lightingGPULifecycle;
        private LightingPresentation? _lightingPresentation;
        private LightingUpdateCoordinator? _lightingUpdateCoordinator;
        private readonly LightingInvalidationJournal _journal = new();
        private readonly DynamicLightManager _dynamicLightManager = new();
        private GraphicsQualitySettings _qualitySettings;

        private StaticLightingSolver StaticSolver =>
            _staticLightingSolver ??= new StaticLightingSolver(_resources, _telemetry);
        private DynamicLightingSolver DynamicSolver =>
            _dynamicLightingSolver ??= new DynamicLightingSolver(
                _resources,
                _dynamicLightManager,
                new DynamicLightTileCache());
        private IndirectLightingSolver IndirectSolver =>
            _indirectLightingSolver ??= new IndirectLightingSolver(_resources);
        private GeometryLightingSolver GeometrySolver =>
            _geometryLightingSolver ??= new GeometryLightingSolver(_resources);
        private LightingFrameExecutor FrameExecutor =>
            _lightingFrameExecutor ??= new LightingFrameExecutor(
                _resources,
                GeometrySolver,
                StaticSolver,
                DynamicSolver,
                IndirectSolver,
                _dynamicLightManager,
                _lightingGeometryRegistry,
                _telemetry);

        private LightingGPULifecycle GPULifecycle =>
            _lightingGPULifecycle ??= new LightingGPULifecycle(
                _resources,
                FrameExecutor);

        private LightingPresentation Presentation =>
            _lightingPresentation ??= new LightingPresentation(_resources);

        private LightingUpdateCoordinator UpdateCoordinator =>
            _lightingUpdateCoordinator ??= new LightingUpdateCoordinator(
                _resources,
                _runtimeState,
                GPULifecycle,
                FrameExecutor,
                Presentation,
                _lightingGeometryRegistry,
                _dynamicLightManager,
                _telemetry,
                _journal);

        private List<CascadeLayout> _cascades => _resources.Cascades;
        private int _fieldWidth => _resources.FieldWidth;
        private int _fieldHeight => _resources.FieldHeight;
        private int _bounceWidth => _resources.BounceWidth;
        private int _bounceHeight => _resources.BounceHeight;
        private int _atlasEntryCount => _resources.AtlasEntryCount;

        // Для интеграционных тестов жизненного цикла GPU-ресурсов.
        internal bool IsGPUPipelineInitialized => _resources.GPUPipelineInitialized;

        internal LightingResources GPUResources => _resources.Registry;

        [Inject]
        private LightingGeometryRegistry _lightingGeometryRegistry = null!;
        [Inject]
        private IClientConfigManager _clientConfig = null!;
        [Inject]
        private IFrameTelemetry _telemetry = null!;
        [Inject]
        private IRuntimeDebugSettings _debugSettings = null!;

        private bool _initialized;
        private bool _budgetViolationCaptured;

        public bool IsInitialized => _initialized;

        public event Action? OnInitialized;
        private bool _forceBypassLighting;

        public bool BypassLightingCompute
        {
            get => _forceBypassLighting || (_debugSettings != null && _debugSettings.BypassLightingCompute);
            set
            {
                _forceBypassLighting = value;
                if (_debugSettings != null)
                {
                    _debugSettings.BypassLightingCompute = value;
                }
            }
        }

        public GraphicsPreset ActiveGraphicsPreset => _graphicsPreset;

        public LightingQualityMode ActiveLightingQuality => _lightingQualityMode;

        public DebugView ActiveDebugView => _debugView;

        public bool DiffuseBounceEnabled =>
            LightingConfigHolder.BounceEnabled && LightingConfigHolder.BounceStrength > 0f;

        public float AmbientIntensity => LightingConfigHolder.AmbientIntensity;

        public Color AmbientColor => LightingConfigHolder.AmbientColor;

        public float EmissionScale => LightingConfigHolder.EmissionScale;

        public Color EmptyExtinctionRGB => LightingConfigHolder.EmptyExtinctionRGB;

        public Color SolidExtinctionRGB => LightingConfigHolder.SolidExtinctionRGB;

        public float EmptyExtinctionMultiplier => LightingConfigHolder.EmptyExtinctionMultiplier;

        public float SolidExtinctionMultiplier => LightingConfigHolder.SolidExtinctionMultiplier;

        public float BounceStrength => LightingConfigHolder.BounceStrength;

        public float MaximumLightMultiplier => LightingConfigHolder.MaximumLightMultiplier;

        public float TransmittanceDebugDistanceCells =>
            LightingComputeBinder.ResolveTransmittanceDebugDistance();

        public float DynamicLightIntensity => LightingConfigHolder.DynamicLightIntensity;

        public Color DynamicLightColor => LightingConfigHolder.DynamicLightColor;

        public bool IsRuntimeConfigReady => true;

        public string RuntimeConfigFilePath => "constants";

        public int LightSafeBorder => 2;

        public int DynamicLightCount => _dynamicLightManager.Count;

        public uint DynamicLightGeneration => _dynamicLightManager.Generation;

        public int UploadedDynamicLightCount => _dynamicLightManager.UploadedCount;

        public int DroppedDynamicLightCount => _dynamicLightManager.DroppedCount;

        public IReadOnlyList<int> DroppedDynamicLightIDs => _dynamicLightManager.DroppedLightIDs;

        public ulong SolveCount => _runtimeState.SolveCount;

        public int FieldWidth => _fieldWidth;

        public int FieldHeight => _fieldHeight;

        public float RequestedPixelsPerCell => _runtimeState.RequestedPixelsPerCell;

        public float EffectivePixelsPerCell => _runtimeState.EffectivePixelsPerCell;

        public bool TextureDimensionLimited => _runtimeState.TextureDimensionLimited;

        public bool CascadeBudgetLimited => _runtimeState.CascadeBudgetLimited;

        public int BounceWidth => _bounceWidth;

        public int BounceHeight => _bounceHeight;

        public int CascadeCount => _cascades.Count;

        // One clipped DDA path crosses at most every row and column once.
        public int MaximumIntervalSteps => _fieldWidth + _fieldHeight + 1;

        public void CollectCascadeCosts(List<CascadeCostSample> destination)
        {
            if (_lightingQualityMode == LightingQualityMode.PerBlock)
            {
                destination.Clear();
                return;
            }

            CascadeCostCalculator.CollectCascadeCosts(_cascades, MaximumIntervalSteps, destination);
        }

        public LightingInvalidationJournal Journal => _journal;

        public string? DumpCurrentFrame(string? targetDirectory = null)
        {
            if (!_initialized || _resources.Registry.Compute == null)
            {
                return null;
            }

            return LightingFrameDumper.DumpCurrentFrame(
                _resources.Registry,
                WorldRect,
                CellSize,
                _lightingQualityMode,
                _telemetry,
                _resources.LightingCounters,
                targetDirectory);
        }

        public void CaptureBudgetViolationIfNeeded()
        {
            long estimatedRayWork = CascadeCostCalculator.EstimateRayWorkUnits(_cascades);
            bool staticRayBudgetViolation =
                estimatedRayWork > LightingPerformanceBudget.MaximumStaticCascadeRayWorkUnits;
            bool measuredBudgetViolation =
                !LightingPerformanceBudget.CheckBudget(_telemetry, out _) ||
                !LightingPerformanceBudget.CheckFrameBudget(_telemetry, out _);
            bool repeatedStaticSolve =
                _runtimeState.SolveCount > 1 &&
                _telemetry.LightingStaticSolveCount > 0 &&
                (_telemetry.LightingRegionChangeCount > 0 ||
                    _telemetry.LightingFieldRebuildCount > 0);
            bool repeatedTerrainFullRebuild =
                _runtimeState.SolveCount > 1 &&
                _telemetry.TerrainFullPopulateCount > 1 &&
                (_telemetry.StreamingPlanKind == (int)StreamingPlanKind.FullRebuild ||
                    _telemetry.StreamingPlanKind == (int)StreamingPlanKind.Resize);
            if (_budgetViolationCaptured ||
                !_initialized ||
                _resources.Registry.Compute == null ||
                !measuredBudgetViolation &&
                !staticRayBudgetViolation &&
                !repeatedStaticSolve &&
                !repeatedTerrainFullRebuild)
            {
                return;
            }

            _budgetViolationCaptured = true;
            Debug.LogWarning(
                $"[Lighting] Budget violation captured: " +
                $"estimatedStaticRayWork={estimatedRayWork}, " +
                $"limit={LightingPerformanceBudget.MaximumStaticCascadeRayWorkUnits}, " +
                $"staticSolves={_telemetry.LightingStaticSolveCount}, " +
                $"terrainFullPopulates={_telemetry.TerrainFullPopulateCount}.");
            try
            {
                // A violation is already the expensive frame. Full ReadPixels
                // plus PNG encoding here would add seven synchronous GPU/CPU
                // readbacks and turn one bad frame into a visible freeze.
                LightingFrameDumper.DumpCurrentFrame(
                    _resources.Registry,
                    WorldRect,
                    CellSize,
                    _lightingQualityMode,
                    _telemetry,
                    lightingCounters: null,
                    targetDirectory: null,
                    includeTextures: false);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Lighting] Budget violation capture failed: {exception.Message}");
            }
        }

        public int MaterialYFlip => SystemInfo.graphicsUVStartsAtTop ? 1 : 0;

        public float CellSize => ProjectRuntimeContracts.World.CellSize;

        public Vector4 WorldRect => new(
            _runtimeState.LastVisibleRegion.x * ProjectRuntimeContracts.World.CellSize,
            _runtimeState.LastVisibleRegion.y * ProjectRuntimeContracts.World.CellSize,
            _runtimeState.LastVisibleRegion.z * ProjectRuntimeContracts.World.CellSize,
            _runtimeState.LastVisibleRegion.w * ProjectRuntimeContracts.World.CellSize);

        public IReadOnlyList<string> GetCascadeUniformSummaries()
        {
            var summaries = new List<string>(_cascades.Count);
            for (int index = 0; index < _cascades.Count; index++)
            {
                CascadeLayout cascade = _cascades[index];
                summaries.Add(
                    $"Cascade {index}: offset={cascade.Offset}, entries={cascade.EntryCount}, " +
                    $"probe={cascade.ProbeWidth}x{cascade.ProbeHeight}, spacing={cascade.ProbeSpacing}, " +
                    $"directions={cascade.DirectionCount}, interval={cascade.IntervalStart:F2}..{cascade.IntervalEnd:F2}");
            }

            return summaries;
        }

        public int AtlasEntryCount => _atlasEntryCount;

        public Color ComputeAmbientColor => LightingConfigHolder.AmbientColor * LightingConfigHolder.AmbientIntensity;

        public Color ComputeEmptyExtinction =>
            LightingConfigHolder.EmptyExtinctionRGB * LightingConfigHolder.EmptyExtinctionMultiplier;

        public Color ComputeSolidExtinction =>
            LightingConfigHolder.SolidExtinctionRGB * LightingConfigHolder.SolidExtinctionMultiplier;

        public int StableRegionPaddingCells => LightingRegionCalculator.LightingRegionPaddingCells;

        public int RequiredTerrainPadding
        {
            get
            {
                // Dynamic sources are rasterized as one-cell emitters. Their
                // propagation distance is solved by the same extinction and
                // cascade intervals as terrain emission, not by a source halo.
                return 3;
            }
        }

        private void Start()
        {
            // Scene instances run Start before GameBootstrap injects them. The
            // explicit PostStart resolution below performs the authoritative
            // initialization; do not throw every frame while that hand-off is
            // still pending.
            if (_DependenciesReady)
            {
                TryInitialize();
            }
        }

        private bool _DependenciesReady =>
            _clientConfig?.Config != null &&
            _lightingGeometryRegistry != null;

        public void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            if (!_DependenciesReady)
            {
                throw new InvalidOperationException(
                    "LightingEngine requires all DI dependencies before initialization.");
            }

            ApplyQualitySettings(
                _clientConfig.Config.GraphicsPreset,
                _clientConfig.Config.GraphicsQualitySettings);

            _initialized = true;
            OnInitialized?.Invoke();

            if (_lightingQualityMode == LightingQualityMode.Off)
            {
                DisableGPULighting();
            }
        }

        private void TryInitialize()
        {
            if (_initialized)
            {
                return;
            }

            EnsureInitialized();
        }

        private void OnDestroy()
        {

            ReleaseGPUPipeline();
            Shader.DisableKeyword(LightingPresentation.WorldLightingKeyword);
        }

        private void Update()
        {
            if (!_initialized)
            {
                if (_DependenciesReady)
                {
                    TryInitialize();
                }

                return;
            }

        }

        public void SetDynamicLight(
            int id,
            Vector2 position,
            Color color,
            float intensity)
        {
            _dynamicLightManager.SetDynamicLight(id, position, color, intensity);
            if (_dynamicLightManager.IsDirty)
            {
                _runtimeState.CompositeDirty = true;
            }
        }

        public void RemoveDynamicLight(int id)
        {
            _dynamicLightManager.RemoveDynamicLight(id);
        }

        public void ClearDynamicLights()
        {
            _dynamicLightManager.ClearDynamicLights();
        }

        public void InvalidateStaticCache()
        {
            _runtimeState.FieldDirty = true;
        }

        public void InvalidateRegion(int worldX, int worldY, int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            int regionMaxX = worldX + width - 1;
            int regionMaxY = worldY + height - 1;
            Vector4 stableRegion = _runtimeState.LastVisibleRegion;
            if (float.IsNaN(stableRegion.x) ||
                (regionMaxX >= stableRegion.x - 1f &&
                worldX <= stableRegion.x + stableRegion.z + 1f &&
                regionMaxY >= stableRegion.y - 1f &&
                worldY <= stableRegion.y + stableRegion.w + 1f))
            {
                _telemetry.LightingRegionInvalidationCount++;
                _telemetry.LightingRegionInvalidationFrameCount++;
                _runtimeState.QueueRegionInvalidation(
                    new RectInt(worldX, worldY, width, height));
            }
        }
        public void ApplyClientConfig()
        {
            ApplyQualitySettings(
                _clientConfig.Config.GraphicsPreset,
                _clientConfig.Config.GraphicsQualitySettings);
            _runtimeState.FieldDirty = true;
            _runtimeState.BounceDirty = true;
            _runtimeState.CompositeDirty = true;
            _runtimeState.HasStaticRadianceState = false;
            _runtimeState.HasDynamicRadianceState = false;
            _runtimeState.HasRenderedLightState = false;
            _dynamicLightManager.IncrementGeneration();
            _dynamicLightManager.MarkDirty();
            Debug.Log($"[LightingEngine] Applied client config (Preset={_clientConfig.Config.GraphicsPreset})");
        }

        public void SetDebugView(DebugView debugView)
        {
            if (_debugView == debugView)
            {
                return;
            }

            _debugView = debugView;
            _runtimeState.HasRenderedLightState = false;
            _runtimeState.HasStaticRadianceState = false;
            _runtimeState.HasDynamicRadianceState = false;
            _runtimeState.CompositeDirty = true;
            _runtimeState.BounceDirty = true;
            Debug.Log($"[LightingEngine] SetDebugView: {debugView}");
        }


        public void ResetRuntimeLightingPreferences()
        {
            ApplyQualitySettings(
                _clientConfig.Config.GraphicsPreset,
                _clientConfig.Config.GraphicsQualitySettings);
            _runtimeState.FieldDirty = true;
            _runtimeState.CompositeDirty = true;
            _runtimeState.BounceDirty = true;
            _runtimeState.HasRenderedLightState = false;
            _runtimeState.HasStaticRadianceState = false;
            _runtimeState.HasDynamicRadianceState = false;
        }

        public void UpdateLighting(
            int visibleMinX,
            int visibleMinY,
            int visibleWidth,
            int visibleHeight,
            Camera camera,
            IWorldDataStorage? storage,
            MapManager? mapManager,
            TerrainRenderer terrainRenderer)
        {
            UpdateCoordinator.Update(
                visibleMinX,
                visibleMinY,
                visibleWidth,
                visibleHeight,
                camera,
                storage,
                mapManager,
                terrainRenderer,
                _qualitySettings,
                _lightingQualityMode,
                _debugView,
                BypassLightingCompute);
        }

        private void DisableGPULighting()
        {
            ReleaseGPUPipeline();
            Presentation.PublishDisabled();
        }

        private void ReleaseGPUPipeline()
        {
            if (_lightingGPULifecycle != null)
            {
                _lightingGPULifecycle.ReleasePipeline();
                return;
            }

            _resources.ReleaseGPUPipeline();
            _dynamicLightManager.ResetUploadState();
        }

        private void ApplyQualitySettings(
            GraphicsPreset preset,
            GraphicsQualitySettings settings)
        {
            GraphicsQualityProfile.ValidateSettings(settings, preset.ToString());
            bool technicalSettingsChanged = _qualitySettings != settings;
            LightingQualityMode previousQuality = _lightingQualityMode;
            if (technicalSettingsChanged && _resources.GPUPipelineInitialized)
            {
                ReleaseResources();
            }

            _graphicsPreset = preset;
            ApplyUnityQualityLevel(preset);
            _qualitySettings = settings;
            LightingQualityMode resolvedQuality = LightingQualityResolver.Resolve(
                preset,
                settings.LightingQuality);
            if (resolvedQuality != _lightingQualityMode)
            {
                _lightingQualityMode = resolvedQuality;
            }

            if (resolvedQuality == LightingQualityMode.Off)
            {
                DisableGPULighting();
            }
            else
            {
                Presentation.MarkEnabled();
                Shader.EnableKeyword(LightingPresentation.WorldLightingKeyword);
            }

            ApplyUnityRenderingSettings(_qualitySettings);
            if (!technicalSettingsChanged && previousQuality == resolvedQuality)
            {
                return;
            }

            _runtimeState.LastVisibleRegion = new Vector4(float.NaN, float.NaN, float.NaN, float.NaN);
            _runtimeState.FieldDirty = true;
            _runtimeState.CompositeDirty = true;
            _runtimeState.BounceDirty = true;
            _runtimeState.HasRenderedLightState = false;
            _runtimeState.HasStaticRadianceState = false;
            _runtimeState.HasDynamicRadianceState = false;
        }

        // Владение глобальным качеством Unity. Это единственное место, где
        // присваиваются уровень качества, сглаживание и масштаб рендера URP:
        // у глобального тумблера обязан быть ровно один владелец, иначе
        // «кто выставил текущее значение» становится неустановимым. Стережёт
        // KERN-PATTERN. Поиск уровня и квантование масштаба остались в
        // LightingUnityQuality — они чистая арифметика и тестируются без сцены.
        private void ApplyUnityQualityLevel(GraphicsPreset preset)
        {
            int qualityIndex = LightingUnityQuality.ResolveQualityLevelIndex(preset);
            if (qualityIndex < 0)
            {
                return;
            }

            QualitySettings.SetQualityLevel(qualityIndex, applyExpensiveChanges: true);
            Debug.Log(
                "[LightingEngine] Applied Unity QualityLevel: " +
                $"{LightingUnityQuality.DescribeQualityLevel(qualityIndex)} ({qualityIndex})");
        }

        private void ApplyUnityRenderingSettings(GraphicsQualitySettings settings)
        {
            LightingUnityQuality.RenderingPlan plan =
                LightingUnityQuality.ResolveRenderingPlan(settings);
            QualitySettings.antiAliasing = plan.AntiAliasing;
            if (plan.RenderScaleWasQuantized)
            {
                Debug.Log(
                    $"[LightingEngine] Масштаб рендера {plan.RequestedRenderScale:F2} приведён " +
                    $"к {plan.RenderScale:F2}: промежуточные значения дают дробный апскейл " +
                    "и муар на пиксель-арте.");
            }

            float appliedScale = plan.RequestedRenderScale;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.renderScale = plan.RenderScale;
                urp.msaaSampleCount = plan.MsaaSampleCount;
                appliedScale = urp.renderScale;
            }

            Debug.Log(
                $"[LightingEngine] ApplyUnityRenderingSettings: AA={settings.AntiAliasing}, " +
                $"RenderScale={appliedScale} (запрошено {settings.RenderScale})");
        }

        private void ReleaseResources()
        {
            if (_lightingGPULifecycle != null)
            {
                GPULifecycle.ReleaseResources();
            }
            else
            {
                _resources.ReleaseResources();
                _dynamicLightManager.ResetUploadState();
            }

            _runtimeState.HasStaticRadianceState = false;
            _runtimeState.HasDynamicRadianceState = false;
        }
    }
}

#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Interfaces.WorldLighting;
using Kern.Rendering;
using Kern.World.Lighting.Diagnostics;
using Kern.World.Lighting.Quality;
using UnityEngine;
using VContainer;

namespace Kern.World.Lighting
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
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
        [Header("Diagnostics")]
        [SerializeField]
        [Tooltip("Debug view для проверки отдельных lighting-слоёв без скрытого AO/exposure влияния.")]
        private DebugView _debugView;

        private readonly LightingResourceManager _resources = new();
        private readonly LightingRuntimeState _runtimeState = new();
        private LightingComposition? _composition;
        private LightingQualityController? _qualityController;
        private LightingDiagnosticsReporter? _diagnostics;
        private readonly LightingInvalidationJournal _journal = new();
        private readonly LightingTerrainExchangeState _terrainExchangeState = new();
        private readonly DynamicLightManager _dynamicLightManager = new();

        // Граф строится по первому требованию: его вход — внедрённые
        // зависимости, а их у MonoBehaviour на момент инициализации полей ещё
        // нет. Отсутствие графа означает, что GPU-ресурсы не создавались.
        private LightingComposition Composition =>
            _composition ??= new LightingComposition(
                _resources,
                _runtimeState,
                _dynamicLightManager,
                _lightingGeometryRegistry,
                _telemetry,
                _journal);

        private LightingQualityController QualityController =>
            _qualityController ??= new LightingQualityController(
                _resources,
                _runtimeState,
                _dynamicLightManager,
                () => _composition,
                () => Composition);

        private LightingDiagnosticsReporter Diagnostics =>
            _diagnostics ??= new LightingDiagnosticsReporter(
                _resources, _runtimeState, _telemetry);

        private LightingDiagnosticsContext DiagnosticsContext => new(
            _initialized,
            QualityController.QualityMode,
            WorldRect,
            CellSize,
            MaximumIntervalSteps);

        private List<CascadeLayout> _cascades => _resources.Cascades;
        private int _fieldWidth => _resources.FieldWidth;
        private int _fieldHeight => _resources.FieldHeight;
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
        [Inject]
        private ITerrainLightingExchange _terrainLightingExchange = null!;

        private bool _initialized;

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

        public GraphicsPreset ActiveGraphicsPreset => QualityController.ActivePreset;

        public DebugView ActiveDebugView => _debugView;

        public float AmbientIntensity => LightingConfigHolder.AmbientIntensity;

        public Color AmbientColor => LightingConfigHolder.AmbientColor;

        public float EmissionScale => LightingConfigHolder.EmissionScale;

        public Color EmptyExtinctionRGB => LightingConfigHolder.EmptyExtinctionRGB;

        public Color SolidExtinctionRGB => LightingConfigHolder.SolidExtinctionRGB;

        public float EmptyExtinctionMultiplier => LightingConfigHolder.EmptyExtinctionMultiplier;

        public float SolidExtinctionMultiplier => LightingConfigHolder.SolidExtinctionMultiplier;

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



        public int CascadeCount => _cascades.Count;

        // One clipped DDA path crosses at most every row and column once.
        public int MaximumIntervalSteps => _fieldWidth + _fieldHeight + 1;

        public void CollectCascadeCosts(List<CascadeCostSample> destination) =>
            Diagnostics.CollectCascadeCosts(destination, DiagnosticsContext);

        public LightingInvalidationJournal Journal => _journal;

        public string? DumpCurrentFrame(string? targetDirectory = null) =>
            Diagnostics.DumpCurrentFrame(DiagnosticsContext, targetDirectory);

        private void CaptureBudgetViolationIfNeeded() =>
            Diagnostics.CaptureBudgetViolationIfNeeded(DiagnosticsContext);

        public int MaterialYFlip => SystemInfo.graphicsUVStartsAtTop ? 1 : 0;

        public float CellSize => ProjectRuntimeContracts.World.CellSize;

        public Vector4 WorldRect => new(
            _runtimeState.LastVisibleRegion.x * ProjectRuntimeContracts.World.CellSize,
            _runtimeState.LastVisibleRegion.y * ProjectRuntimeContracts.World.CellSize,
            _runtimeState.LastVisibleRegion.z * ProjectRuntimeContracts.World.CellSize,
            _runtimeState.LastVisibleRegion.w * ProjectRuntimeContracts.World.CellSize);

        public IReadOnlyList<string> GetCascadeUniformSummaries() =>
            LightingDiagnosticsReporter.DescribeCascadeUniforms(_cascades);

        public int AtlasEntryCount => _atlasEntryCount;

        public Color ComputeAmbientColor => LightingConfigHolder.AmbientColor * LightingConfigHolder.AmbientIntensity;

        public Color ComputeEmptyExtinction =>
            LightingConfigHolder.EmptyExtinctionRGB * LightingConfigHolder.EmptyExtinctionMultiplier;

        public Color ComputeSolidExtinction =>
            LightingConfigHolder.SolidExtinctionRGB * LightingConfigHolder.SolidExtinctionMultiplier;

        public int StableRegionPaddingCells => LightingRegionCalculator.LightingRegionPaddingCells;

        public int RequiredTerrainPadding => LightingRegionCalculator.TerrainPaddingCells;

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
            PublishTerrainRequirementsIfChanged();

            _initialized = true;
            OnInitialized?.Invoke();

            if (QualityController.QualityMode == LightingQualityMode.Off &&
                QualityController.ActivePreset != GraphicsPreset.Standard)
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

            LightingGpuTeardown.ReleasePipeline(
                _composition, _resources, _dynamicLightManager);
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

        public void ApplyClientConfig()
        {
            ApplyQualitySettings(
                _clientConfig.Config.GraphicsPreset,
                _clientConfig.Config.GraphicsQualitySettings);
            PublishTerrainRequirementsIfChanged();
            LightingRuntimeInvalidation.ResetFieldAndRadiance(_runtimeState);
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
            Debug.Log($"[LightingEngine] SetDebugView: {debugView}");
        }

        // Пересчитать свет теми же полями. Нужно, когда изменилась величина,
        // входящая в решение, но не его размерность: экспозиция сцены, флаг
        // прохода. Без этого новое значение не доехало бы до экрана — свет
        // считается не каждый кадр, — а при следующем движении в мире кадр
        // собрался бы из кусков, посчитанных до и после правки.
        //
        // Отдельно от ResetRuntimeLightingPreferences: тот заново применяет
        // настройки качества и метит поле грязным, то есть переаллоцирует
        // текстуры. На каждый кадр перетаскивания ползунка это недопустимо,
        // да и размерность при смене экспозиции та же самая.
        public void InvalidateRadiance()
        {
            LightingRuntimeInvalidation.ResetRadiance(_runtimeState);
        }


        public void ResetRuntimeLightingPreferences()
        {
            ApplyQualitySettings(
                _clientConfig.Config.GraphicsPreset,
                _clientConfig.Config.GraphicsQualitySettings);
            PublishTerrainRequirementsIfChanged();
            LightingRuntimeInvalidation.ResetFieldAndRadiance(_runtimeState);
        }

        private void PublishTerrainRequirementsIfChanged()
        {
            ITerrainLightingExchange exchange = _terrainLightingExchange ??
                throw new InvalidOperationException(
                    "LightingEngine requires ITerrainLightingExchange before publishing terrain requirements.");
            _terrainExchangeState.PublishRequirements(
                exchange,
                RequiredTerrainPadding,
                StableRegionPaddingCells);
        }

        private void LateUpdate()
        {
            if (!_initialized)
            {
                return;
            }

            ITerrainLightingExchange terrainLightingExchange = _terrainLightingExchange ??
                throw new InvalidOperationException(
                    "LightingEngine requires ITerrainLightingExchange injection before its frame tick.");
            _terrainExchangeState.ProcessLatestFrame(terrainLightingExchange, ProcessTerrainFrame);
        }

        private bool ProcessTerrainFrame(TerrainLightingFrameSnapshot frame)
        {
            ITerrainLightingExchange terrainLightingExchange = _terrainLightingExchange ??
                throw new InvalidOperationException(
                    "LightingEngine requires ITerrainLightingExchange injection before its frame tick.");

            if (frame.State is not TerrainLightingFrameState.Ready and
                not TerrainLightingFrameState.HoldingPublishedView ||
                frame.Camera == null ||
                frame.GeometryContributor == null ||
                frame.GeometryContributor.LightingGeometryRevision != frame.TerrainGeometryRevision)
            {
                throw new InvalidOperationException(
                    "Lighting received a terrain frame without a valid committed presentation.");
            }

            if ((QualityController.QualityMode == LightingQualityMode.Off &&
                 QualityController.ActivePreset != GraphicsPreset.Standard) ||
                BypassLightingCompute)
            {
                UpdateLightingCoordinator(frame);
                _terrainExchangeState.PublishOutput(
                    terrainLightingExchange,
                    frame.WorldGeneration,
                    LightingOutputState.Disabled,
                    default);
                return false;
            }

            _terrainExchangeState.StageTerrainChanges(
                terrainLightingExchange,
                frame.WorldGeneration,
                ApplyTerrainLightingChange);
            UpdateLightingCoordinator(frame);
            _terrainExchangeState.AcknowledgeStagedChanges(terrainLightingExchange);
            if (QualityController.QualityMode != LightingQualityMode.Off)
            {
                CaptureBudgetViolationIfNeeded();
            }
            _terrainExchangeState.PublishOutput(
                terrainLightingExchange,
                frame.WorldGeneration,
                LightingOutputState.Published,
                CurrentLightingWorldRectCells());
            return true;
        }

        private void UpdateLightingCoordinator(TerrainLightingFrameSnapshot frame)
        {
            RectInt viewport = frame.LightingViewportCells;
            Composition.UpdateCoordinator.Update(
                viewport.x,
                viewport.y,
                viewport.width,
                viewport.height,
                frame.Camera,
                frame.GeometryContributor,
                QualityController.Settings,
                QualityController.QualityMode,
                _debugView,
                BypassLightingCompute,
                QualityController.ActivePreset == GraphicsPreset.Standard);
        }

        private RectInt CurrentLightingWorldRectCells()
        {
            Vector4 region = _runtimeState.LastVisibleRegion;
            return new RectInt(
                Mathf.RoundToInt(region.x),
                Mathf.RoundToInt(region.y),
                Mathf.RoundToInt(region.z),
                Mathf.RoundToInt(region.w));
        }

        private void ApplyTerrainLightingChange(TerrainLightingChange change)
        {
            bool regionQueued = TerrainLightingChangeApplier.Apply(change, _runtimeState);
            if (change.Kind == TerrainLightingChangeKind.Region && !regionQueued)
            {
                return;
            }

            if (regionQueued)
            {
                _telemetry.LightingRegionInvalidationCount++;
                _telemetry.LightingRegionInvalidationFrameCount++;
            }
        }

        private void DisableGPULighting()
        {
            QualityController.DisableGpuLighting();
        }

        private void ApplyQualitySettings(
            GraphicsPreset preset,
            GraphicsQualitySettings settings) =>
            QualityController.Apply(preset, settings);
    }
}

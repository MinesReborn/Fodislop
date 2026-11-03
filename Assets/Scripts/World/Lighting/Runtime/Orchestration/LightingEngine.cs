#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Interfaces.Diagnostics;
using Kern.Core.Interfaces.WorldLighting;
using Kern.Rendering;
using Kern.World.Lighting.Diagnostics;
using Kern.World.Lighting.Quality;
using UnityEngine;
using VContainer;

using Kern.Diagnostics.Frames;

using Kern.Diagnostics.World;

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
            Glow = 3,
            Transmission = 4,
            StaticDirect = 5,
            DynamicDirect = 6,
            Exposure = 8,

            AmbientOcclusion = 9,
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForDomainReload()
        {
            // Коherent-состояние глобалов освещения нужно до первого кадра:
            // перерисовка Game-вью при входе в Play Mode случается раньше
            // Start и раньше асинхронной DI-инициализации движка, а пустой
            // _WorldLightRect роняет bloom-проход вместе с render graph.
            LightingPresentation.PublishDisabledGlobals();
            LightingUpdateCoordinator.DiagnosticForceDenseReanchor = false;
            LightingComputeBinder.DiagnosticTransportCounters = false;
            LightingComputeBinder.DiagnosticTexelTraversalReference = false;
            LightingComputeBinder.DiagnosticUniformSourceTraversal = false;
            LightingComputeBinder.DiagnosticBatchedDynamicLights = false;
            LightingQualityTuningController.Apply(LightingConfigHolder.DefaultQuality);
            LightingQualityTuningController.SetDynamicTransportMode(DynamicLightingTransportMode.ExactDda);
            LightingQualityTuningController.SetBatchDynamicLights(true);
            LightingQualityTuningController.MarkDynamicExecutionModeApplied(
                LightingQualityTuningController.DynamicExecutionModeRevision);
            LightingComputeBinder.DiagnosticVectorPolarReference = false;
            LightingFrameExecutor.DiagnosticMaterialReadback = null;
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
        private LightingRuntimeControls? _runtimeControls;
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

        private LightingRuntimeControls RuntimeControls =>
            _runtimeControls ??= new LightingRuntimeControls(
                _clientConfig,
                QualityController,
                _runtimeState,
                _dynamicLightManager,
                PublishTerrainRequirementsIfChanged);

        private LightingDiagnosticsReporter Diagnostics =>
            _diagnostics ??= new LightingDiagnosticsReporter(
                _resources, _telemetry);

        private LightingDiagnosticsContext DiagnosticsContext => new(
            _initialized,
            QualityController.QualityMode,
            WorldRect,
            CellSize);

        private List<CascadeLayout> _cascades => _resources.Cascades;
        private int _fieldWidth => _resources.FieldWidth;
        private int _fieldHeight => _resources.FieldHeight;
        private int _atlasEntryCount => _resources.AtlasEntryCount;

        // Для интеграционных тестов жизненного цикла GPU-ресурсов.
        internal bool IsGPUPipelineInitialized => _resources.GPUPipelineInitialized;
        internal bool HasDiagnosticTransportCounterBuffer => _resources.LightingCounters != null;

        internal LightingResources GPUResources => _resources.Registry;
        // Borrowed for explicit production captures; caller must finish before scope teardown.
        internal ComputeBuffer DiagnosticTransportCounterBuffer => _resources.LightingCounters
            ?? throw new InvalidOperationException("Lighting transport counters are not allocated.");

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
        private bool _hasGameplayLightingOutput;
        private ulong _gameplayLightingWorldGeneration;
        private ulong _gameplayLightingFrameSequence;
        private LightingOutputState _gameplayLightingOutputState;
        private RectInt _gameplayLightingOutputRect;
        private RectInt _gameplayLightingViewport;
        private bool _orientationValidationStarted;
        private bool _orientationValidationRequired;
        private LightingQualityTuning _appliedTuning = LightingQualityTuningController.Current;
        private ulong _appliedDynamicExecutionModeRevision =
            LightingQualityTuningController.DynamicExecutionModeRevision;

        public bool IsInitialized => _initialized;

        public bool IsReadyForGameplay
        {
            get
            {
                if (!_initialized || !_hasGameplayLightingOutput || _terrainLightingExchange == null ||
                    !_terrainLightingExchange.TryReadLightingOutput(out LightingOutputSnapshot output) ||
                    output.WorldGeneration != _gameplayLightingWorldGeneration ||
                    output.State != _gameplayLightingOutputState ||
                    output.WorldRectCells != _gameplayLightingOutputRect ||
                    (_terrainLightingExchange.TryReadLatestTerrainFrame(
                        0,
                        out TerrainLightingFrameSnapshot pendingFrame) &&
                     (pendingFrame.WorldGeneration != _gameplayLightingWorldGeneration ||
                      pendingFrame.FrameSequence > _gameplayLightingFrameSequence)))
                {
                    return false;
                }

                if (output.State == LightingOutputState.Disabled)
                {
                    return true;
                }

                RectInt outputRect = output.WorldRectCells;
                if (output.State != LightingOutputState.Published ||
                    outputRect.xMin > _gameplayLightingViewport.xMin ||
                    outputRect.yMin > _gameplayLightingViewport.yMin ||
                    outputRect.xMax < _gameplayLightingViewport.xMax ||
                    outputRect.yMax < _gameplayLightingViewport.yMax)
                {
                    return false;
                }

                return true;
            }
        }

        public Cysharp.Threading.Tasks.UniTask BeginOrientationValidationAsync()
        {
            if (!DependenciesReady)
            {
                throw new InvalidOperationException(
                    "LightingEngine requires all DI dependencies before orientation validation.");
            }

            ApplyQualitySettings(
                _clientConfig.Config.GraphicsPreset,
                _clientConfig.Config.GraphicsQualitySettings);
            _orientationValidationStarted = true;
            _orientationValidationRequired = QualityController.QualityMode != LightingQualityMode.Off ||
                QualityController.ActivePreset == GraphicsPreset.Classic;
            if (!_orientationValidationRequired)
            {
                return Cysharp.Threading.Tasks.UniTask.CompletedTask;
            }

            return LightingFieldOrientationValidator.ValidateBeforeWorldEntryAsync();
        }

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

        public float GlowScale => LightingConfigHolder.GlowScale;

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

        public IReadOnlyList<int> DroppedDynamicLightIds => _dynamicLightManager.DroppedLightIds;

        public ulong SolveCount => _runtimeState.SolveCount;

        public int FieldWidth => _fieldWidth;

        public int FieldHeight => _fieldHeight;

        public int LightWidth => _resources.LightWidth;

        public int LightHeight => _resources.LightHeight;

        /// <summary>Validate a session quality change against this world's resource coverage before publishing it.</summary>
        public bool TryApplyQualityTuning(LightingQualityTuning quality, out string rejection)
        {
            if (LightingQualityTuningController.DynamicTransportMode ==
                DynamicLightingTransportMode.JumpFloodSdfSphereTracing &&
                _resources.CellGridWidth > 0 && _resources.CellGridHeight > 0)
            {
                try
                {
                    LightingResourceManager.ValidateDynamicDistanceFieldRequest(
                        checked(_resources.CellGridWidth * quality.FieldPixelsPerCell),
                        checked(_resources.CellGridHeight * quality.FieldPixelsPerCell));
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
                {
                    rejection = exception.Message;
                    return false;
                }
            }

            return _resources.TryApplyQualityTuning(
                quality, QualityController.Settings, out rejection);
        }

        public bool TrySetDynamicTransportMode(DynamicLightingTransportMode mode, out string rejection)
        {
            try
            {
                if (mode == DynamicLightingTransportMode.JumpFloodSdfSphereTracing)
                {
                    if (!SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.RFloat))
                    {
                        throw new NotSupportedException("JFA sphere tracing requires random-write RFloat support.");
                    }
                    if (_resources.CellGridWidth > 0 && _resources.CellGridHeight > 0)
                    {
                        LightingResourceManager.ValidateDynamicDistanceFieldRequest(
                            _resources.FieldWidth, _resources.FieldHeight);
                    }
                }

                LightingQualityTuningController.SetDynamicTransportMode(mode);
                rejection = string.Empty;
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                NotSupportedException or OverflowException)
            {
                rejection = exception.Message;
                return false;
            }
        }

        public RectInt DynamicReceiverRect => _runtimeState.LastDynamicReceiverRect;

        public float RequestedPixelsPerCell => _runtimeState.RequestedPixelsPerCell;

        public float EffectivePixelsPerCell => _runtimeState.EffectivePixelsPerCell;

        public float EffectiveCascadeProbesPerCell => _cascades.Count == 0
            ? 0f : EffectivePixelsPerCell / _cascades[0].ProbeSpacing;



        public int CascadeCount => _cascades.Count;

        // One clipped DDA path crosses at most every row and column once.
        public void CollectCascadeCosts(List<CascadeCostSample> destination) =>
            Diagnostics.CollectCascadeCosts(destination);

        public LightingInvalidationJournal Journal => _journal;

        public string? DumpCurrentFrame(string? targetDirectory = null) =>
            Diagnostics.DumpCurrentFrame(DiagnosticsContext, targetDirectory);

        public int MaterialYFlip => LightingFieldOrientation.RowsTopDown ? 1 : 0;

        public float CellSize => WorldGridDefaults.CellSize;

        public Vector4 WorldRect => new(
            _runtimeState.LastVisibleRegion.x * WorldGridDefaults.CellSize,
            _runtimeState.LastVisibleRegion.y * WorldGridDefaults.CellSize,
            _runtimeState.LastVisibleRegion.z * WorldGridDefaults.CellSize,
            _runtimeState.LastVisibleRegion.w * WorldGridDefaults.CellSize);

        public IReadOnlyList<string> GetCascadeUniformSummaries() =>
            LightingDiagnosticsReporter.DescribeCascadeUniforms(_cascades);

        public int AtlasEntryCount => _atlasEntryCount;
        public int AtlasCapacity => _resources.AtlasCapacity;

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
            if (DependenciesReady)
            {
                TryInitialize();
            }
        }

        private bool DependenciesReady =>
            _clientConfig?.Config != null &&
            _lightingGeometryRegistry != null;

        public void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            if (!DependenciesReady)
            {
                throw new InvalidOperationException(
                    "LightingEngine requires all DI dependencies before initialization.");
            }

            ApplyQualitySettings(
                _clientConfig.Config.GraphicsPreset,
                _clientConfig.Config.GraphicsQualitySettings);

            // GameBootstrap starts the asynchronous raster/readback probe
            // after scene-service activation, overlaps it with persistent-data
            // preparation, and awaits success before Initialize can connect.
            // Keep the validated-state check mandatory for every mode that
            // publishes a lighting or AO field; resource setup must never
            // proceed with unconfirmed row orientation.
            if (QualityController.QualityMode != LightingQualityMode.Off ||
                QualityController.ActivePreset == GraphicsPreset.Classic)
            {
                LightingFieldOrientationValidator.EnsureValidated();
            }

            PublishTerrainRequirementsIfChanged();

            _initialized = true;
            OnInitialized?.Invoke();

            if (QualityController.QualityMode == LightingQualityMode.Off &&
                QualityController.ActivePreset != GraphicsPreset.Classic)
            {
                DisableGPULighting();
            }
        }

        private void TryInitialize()
        {
            if (_initialized || !_orientationValidationStarted ||
                (_orientationValidationRequired && !LightingFieldOrientationValidator.IsValidated))
            {
                return;
            }

            EnsureInitialized();
        }

        private void OnDestroy()
        {

            LightingGPUTeardown.ReleasePipeline(
                _composition, _resources, _dynamicLightManager);
            Shader.DisableKeyword(LightingPresentation.WorldLightingKeyword);
        }

        private void Update()
        {
            if (!_initialized)
            {
                if (DependenciesReady)
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
                // Re-evaluate the uploaded set before requesting GPU work. A
                // changed source may be outside the field or beyond capacity.
                _runtimeState.HasRenderedLightState = false;
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
            => RuntimeControls.ApplyClientConfig();

        public void SetDebugView(DebugView debugView)
        {
            RuntimeControls.SetDebugView(ref _debugView, debugView);
            _runtimeState.CompositeDirty = true;
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
            => RuntimeControls.InvalidateRadiance();


        public void ResetRuntimeLightingPreferences()
            => RuntimeControls.ResetRuntimeLightingPreferences();

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
                 QualityController.ActivePreset != GraphicsPreset.Classic) ||
                BypassLightingCompute)
            {
                UpdateLightingCoordinator(frame);
                _terrainExchangeState.PublishOutput(
                    terrainLightingExchange,
                    frame.WorldGeneration,
                    LightingOutputState.Disabled,
                    default);
                RecordGameplayLightingOutput(frame, LightingOutputState.Disabled, default);
                return false;
            }

            _terrainExchangeState.StageTerrainChanges(
                terrainLightingExchange,
                frame.WorldGeneration,
                ApplyTerrainLightingChange);
            UpdateLightingCoordinator(frame);
            _terrainExchangeState.AcknowledgeStagedChanges(terrainLightingExchange);
            _terrainExchangeState.PublishOutput(
                terrainLightingExchange,
                frame.WorldGeneration,
                LightingOutputState.Published,
                CurrentLightingWorldRectCells());
            RecordGameplayLightingOutput(
                frame,
                LightingOutputState.Published,
                CurrentLightingWorldRectCells());
            return true;
        }

        private void RecordGameplayLightingOutput(
            TerrainLightingFrameSnapshot frame,
            LightingOutputState state,
            RectInt worldRect)
        {
            _gameplayLightingWorldGeneration = frame.WorldGeneration;
            _gameplayLightingFrameSequence = frame.FrameSequence;
            _gameplayLightingOutputState = state;
            _gameplayLightingOutputRect = worldRect;
            _gameplayLightingViewport = frame.LightingViewportCells;
            _hasGameplayLightingOutput = true;
        }

        private void UpdateLightingCoordinator(TerrainLightingFrameSnapshot frame)
        {
            ApplyVisualTuningIfChanged();
            GraphicsQualitySettings settings = QualityController.Settings;
            RectInt viewport = frame.LightingViewportCells;
            Composition.UpdateCoordinator.Update(
                viewport.x,
                viewport.y,
                viewport.width,
                viewport.height,
                frame.Camera,
                frame.GeometryContributor,
                settings,
                QualityController.QualityMode,
                _debugView,
                BypassLightingCompute,
                QualityController.ActivePreset == GraphicsPreset.Classic);
        }

        private void ApplyVisualTuningIfChanged()
        {
            LightingQualityTuning tuning = LightingQualityTuningController.Current;
            ulong executionModeRevision = LightingQualityTuningController.DynamicExecutionModeRevision;
            if (_appliedTuning == tuning &&
                _appliedDynamicExecutionModeRevision == executionModeRevision)
            {
                return;
            }

            bool staticOrFieldChanged = _appliedTuning.FieldPixelsPerCell != tuning.FieldPixelsPerCell ||
                _appliedTuning.LightPixelsPerCell != tuning.LightPixelsPerCell ||
                _appliedTuning.CascadeProbePixelsPerCell != tuning.CascadeProbePixelsPerCell ||
                _appliedTuning.MaximumStaticCascadeDirections != tuning.MaximumStaticCascadeDirections;
            if (staticOrFieldChanged)
            {
                Composition.Presentation.PublishDisabled();
                LightingGPUTeardown.ReleaseResources(
                    _composition, _resources, _dynamicLightManager, _runtimeState);
                LightingRuntimeInvalidation.ResetFieldAndRadiance(_runtimeState);
            }
            else
            {
                Composition.FrameExecutor.InvalidateDynamicQuality();
                _runtimeState.HasRenderedLightState = false;
                _runtimeState.HasDynamicRadianceState = false;
                _runtimeState.CompositeDirty = true;
            }

            FrameEventLog.Record(staticOrFieldChanged
                ? "свет: VisualTuning изменил поля или статику"
                : "свет: изменён режим динамической трассировки или батчинга");
            _appliedTuning = tuning;
            _appliedDynamicExecutionModeRevision = executionModeRevision;
            LightingQualityTuningController.MarkDynamicExecutionModeApplied(executionModeRevision);
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
                FrameEventLog.Record($"свет: изменение {change.Sequence}, ревизия {change.TerrainGeometryRevision} " +
                    $"вне поля транспорта {change.Region}");
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
            QualityController.DisableGPULighting();
        }

        private void ApplyQualitySettings(
            GraphicsPreset preset,
            GraphicsQualitySettings settings) =>
            QualityController.Apply(preset, settings);
    }
}

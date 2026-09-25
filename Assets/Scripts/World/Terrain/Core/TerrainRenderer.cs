#nullable enable

using Kern.Core.Interfaces.Diagnostics;
using Kern.Core.Interfaces.WorldLighting;
using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using MinesServer.Data;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using VContainer;

namespace Kern.World.Terrain
{
    /// <summary>
    /// Жизненный цикл террейна в сцене и порядок одного кадра.
    /// </summary>
    ///
    /// Здесь не считается ничего. Кадр — это последовательность вызовов:
    /// разрешить камеру → выбрать план кадра (<see cref="TerrainFramePlanner"/>)
    /// → довести окно до плана (<see cref="TerrainWindow"/>) → выгрузить
    /// тексели → поставить меш показа
    /// (<see cref="TerrainPresentationWindow"/>) → отдать окно освещению.
    /// Каждый шаг живёт отдельным типом, и искать его надо там.
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [DefaultExecutionOrder(100)]
    public class TerrainRenderer : MonoBehaviour, Kern.Core.Interfaces.WorldLighting.ILightingGeometryContributor
    {
        [Header("Configuration")]
        [SerializeField]
        private float _cellSize = ProjectRuntimeContracts.World.CellSize;
        [SerializeField]
        private Shader? _terrainShader = null;
        [SerializeField]
        private string _sortingLayerName = "Default";
        [SerializeField]
        private int _sortingOrder = ProjectRuntimeContracts.RequiredLayers.TerrainSortingOrder;
        [SerializeField]
        private int _doorOverlaySortingOrder = 500;
        [SerializeField]
        private int _viewportPadding = 2;

        [Inject]
        private Kern.World.Streaming.WorldViewTransition? _viewTransition = null;

        [Inject]
        private IWorldDataStorage _storage = null!;
        [Inject]
        private IConnectionService _connectionService = null!;
        [Inject]
        private MapManager _mapManager = null!;
        [Inject]
        private ITextureService _textureService = null!;
        [Inject]
        private IClientConfigManager _clientConfigManager = null!;
        [Inject]
        private IFrameTelemetry _telemetry = null!;
        [Inject]
        private IRuntimeDebugSettings _debugSettings = null!;
        [Inject]
        private ITerrainLightingExchange _terrainLightingExchange = null!;
        [Inject]
        private ILocalPlayerState _localPlayer = null!;
        [Inject]
        private IGameplayCamera _gameplayCamera = null!;
        [Inject]
        private ISceneObjectFactory _sceneObjects = null!;

        private static readonly ProfilerMarker _TerrainLateUpdateMarker =
            new("Kern.Terrain.LateUpdate.CPU");

        private static readonly AllocationLedger.Entry _AllocationEntry =
            AllocationLedger.Register("Террейн — LateUpdate");

        private readonly TerrainWindow _window = new();
        private readonly TerrainFramePlanner _planner = new();
        private readonly TerrainMeshManager _meshManager = new();
        private readonly TerrainPresentationWindow _presentation = new();
        private TerrainFrameDiagnostics? _diagnostics;
        private TerrainClientConfigApplier? _configApplier;
        private TerrainWorldChangeHandler? _worldChangeHandler;

        private MeshFilter? _meshFilter;
        private MeshRenderer? _meshRenderer;
        private readonly TerrainCameraFrameState _cameraFrame = new();
        private TerrainSubscriptions? _subscriptions;

        private RectInt _lightingViewport;
        private bool _fatalBuildError;
        private TerrainLightingFramePublisher? _lightingFramePublisher;
        private readonly List<RectInt> _publishedChangedRegions = [];

        private TerrainLightingFramePublisher LightingFramePublisher =>
            _lightingFramePublisher ??= new(_terrainLightingExchange);

        public bool BypassCpuMeshRebuild
        {
            get => _debugSettings.BypassCpuMeshRebuild;
            set => _debugSettings.BypassCpuMeshRebuild = value;
        }

        public bool BypassTerrainDraw
        {
            get => _debugSettings.BypassTerrainDraw;
            set => _debugSettings.BypassTerrainDraw = value;
        }

        // Ревизия ЗАФИКСИРОВАННОЙ геометрии — ровно та, что освещение читает
        // через ILightingGeometryContributor.LightingGeometryRevision.
        // _terrainContentRevision называет ЗАПРОШЕННУЮ сборку и опережает её:
        // загрузка текстуры, применение конфига или загрузка мира поднимают
        // счётчик до того, как шаг доедет до GPU. Кадр и запись изменения,
        // опубликованные с опережающей ревизией, описывали бы геометрию,
        // которой на экране ещё нет, — освещение отвергает такой кадр.
        private ulong CommittedContentRevision => _window.PublishedContentRevision;

        ulong Kern.Core.Interfaces.WorldLighting.ILightingGeometryContributor.LightingGeometryRevision =>
            CommittedContentRevision;

        public bool HasPublishedTerrain => _window.CellsCommitted;

        private TerrainFrameDiagnostics Diagnostics => _diagnostics ??= new(_window);

        private TerrainClientConfigApplier ConfigApplier => _configApplier ??= new(_window);

        private TerrainWorldChangeHandler WorldChanges =>
            _worldChangeHandler ??= new TerrainWorldChangeHandler(
                _window,
                _telemetry,
                () => _storage,
                () => _mapManager,
                () => _textureService,
                () => _terrainShader,
                () => LightingFramePublisher);

        // Exposes the production builder's geometry evidence to PlayMode
        // contract tests. A hand-authored cell-data texture can pass a shader
        // test while the live scene still renders a rectangular CPU path; the
        // count makes that divergence observable without a second renderer.
        internal int LastFullBuildAnchoredForegroundCellCount =>
            _window.Driver.Pipeline.CellBuilder.LastFullBuildAnchoredForegroundCellCount;

        public bool IsReadyForGameplay =>
            TerrainReadiness.IsReadyForGameplay(_window, _textureService);

        public void ApplyClientConfig()
        {
            IClientConfigManager clientConfigManager = _clientConfigManager ??
                throw new InvalidOperationException(
                    "TerrainRenderer requires IClientConfigManager injection.");
            ClientConfig config = clientConfigManager.Config ??
                throw new InvalidOperationException(
                    "TerrainRenderer requires an initialized ClientConfig.");

            ConfigApplier.Apply(config);
            WorldChanges.RecordConfigurationChange();
        }

        public void InitializeEditorPreview(
            IWorldDataStorage storage,
            MapManager mapManager,
            ITextureService textureService)
        {
            _storage = storage;
            _mapManager = mapManager;
            _textureService = textureService;
            InitializeSceneBindings();
            EnsureSubscriptions();
            _window.NeedsRefresh = true;
        }

        public void EnsureSubscriptions()
        {
            _subscriptions ??= new TerrainSubscriptions(
                WorldChanges.HandleCellChanged,
                WorldChanges.HandleRegionChanged,
                OnTextureLoaded,
                OnWorldDataLoaded,
                WorldChanges.HandleChunkLoaded);
            _subscriptions.Bind(_storage, _textureService, _mapManager);
        }

        public void RenderMaterialEmissionFields(
            CommandBuffer commandBuffer,
            in Kern.Core.Interfaces.WorldLighting.LightingMaterialEmissionContext context) =>
            _meshManager.RenderLightingMaterialFields(
                commandBuffer,
                context.MaterialField,
                context.EmissionField,
                context.WorldRect,
                transform.localToWorldMatrix,
                _window.Driver.Materials.CellMaterials,
                _window.CellIDMesh,
                _presentation.ViewOffset);

        public void RenderAmbientOcclusionField(
            CommandBuffer commandBuffer,
            in Kern.Core.Interfaces.WorldLighting.LightingAmbientOcclusionContext context) =>
            _meshManager.RenderLightingAmbientOcclusionField(
                commandBuffer,
                context.AmbientOcclusionField,
                context.WorldRect,
                transform.localToWorldMatrix,
                _window.Driver.Materials.CellMaterials,
                _window.CellIDMesh,
                _presentation.ViewOffset);

        protected void Awake()
        {
            InitializeSceneBindings();
        }

        protected void Start() => _cameraFrame.SetCamera(_gameplayCamera?.Camera);

        protected void OnDestroy()
        {
            _subscriptions?.Dispose();
            _subscriptions = null;
            _presentation.Dispose();
            _diagnostics?.Dispose();
            _diagnostics = null;
            _window.Dispose();
        }

        protected void LateUpdate()
        {
            if (_fatalBuildError)
            {
                return;
            }

            using var terrainLateUpdateMarker = _TerrainLateUpdateMarker.Auto();
            using var allocationScope = AllocationLedger.Measure(_AllocationEntry);
            long stallStart = TerrainStallReport.Begin();
            _telemetry.ResetFrameTimers();
            if (_mapManager == null || _storage == null || !_storage.IsReady)
            {
                return;
            }

            if (_localPlayer is not { Current: { HasServerPosition: true } })
            {
                return;
            }

            if (LightingFramePublisher.WorldGeneration == 0)
            {
                BeginTerrainWorldGeneration();
            }

            Diagnostics.Mark(1 << 1, "[TerrainDiag] gate passed: storage ready");
            if (!TryResolveCamera())
            {
                return;
            }

            _cameraFrame.UpdateSpeedEstimate(
                Time.unscaledDeltaTime,
                _cellSize,
                _window.Width,
                _window.Height);

            LightingTerrainRequirements lightingRequirements = LightingFramePublisher.ReadRequirements();
            LightingOutputSnapshot lightingOutput = default;
            bool hasLightingOutput = LightingFramePublisher.TryReadLightingOutput(out lightingOutput);

            // Готовый фоновый шаг забирается до плана и выгружается сразу:
            // так следующий шаг ставится уже в этом кадре по новому началу, а
            // тексели, начало окна и двери меняются вместе при любом раннем
            // выходе ниже.
            // Переход вида (телепорт): камера стоит на старом месте, окно
            // назначения собирается и не публикуется до кадра, в котором
            // камера на него встанет. CameraFollow обновляется раньше, поэтому
            // в кадре перестановки удержание здесь уже снято.
            bool holdingView = _viewTransition is { IsHolding: true };
            _window.HoldPublication = holdingView;
            if (_viewTransition != null)
            {
                _viewTransition.CanHold = _window.CellsCommitted;
            }

            long publishStart = TerrainStallReport.Begin();
            if (!_window.TryPublishCompleted(Services, out Exception? publishFailure))
            {
                _fatalBuildError = Diagnostics.ReportBuildFailure(
                    publishFailure,
                    _window.Origin,
                    _mapManager,
                    _textureService,
                    _storage);
                return;
            }

            float publishMs = TerrainStallReport.ElapsedMs(publishStart);
            float uploadMs = _window.Commit();
            if (uploadMs > 0f)
            {
                _telemetry.TerrainGpuUploadTimeMs = uploadMs;
            }

            long planStart = TerrainStallReport.Begin();
            Vector3 focusPosition = holdingView
                ? _viewTransition!.Destination
                : _cameraFrame.Camera!.transform.position;
            TerrainFramePlan framePlan = _planner.Plan(
                _cameraFrame.Camera!,
                focusPosition,
                _cellSize,
                _viewportPadding,
                lightingRequirements.RequiredTerrainPaddingCells,
                lightingRequirements.StableLightingPaddingCells,
                _window.ProspectiveOrigin,
                _window.Width,
                _window.Height,
                _window.IsInitialized,
                _window.CellsCommitted,
                _window.HasCpuBuildInFlight,
                _cameraFrame.SpeedCellsPerSecond,
                _window.EstimatedPreparationSeconds,
                _lightingViewport,
                allowPartialAdvance: !holdingView,
                holdingPublishedView: holdingView,
                _storage,
                _mapManager,
                _connectionService,
                _telemetry);
            float planMs = TerrainStallReport.ElapsedMs(planStart);
            if (_meshRenderer != null)
            {
                _meshRenderer.enabled = !BypassTerrainDraw && _window.CellsCommitted;
            }

            if (!framePlan.ShouldProcess)
            {
                if (!holdingView)
                {
                    _lightingViewport = framePlan.LightingViewport;
                }
                LightingFramePublisher.ValidateLightingOutput(hasLightingOutput, lightingOutput, _window);
                PublishTerrainFrameDemand(framePlan, holdingView);
                return;
            }

            long dimensionsStart = TerrainStallReport.Begin();
            _window.ApplyDimensions(framePlan.ActiveWindow.Size, framePlan.DimensionsChanged);
            _window.CoalesceDirtyRects();
            float dimensionsMs = TerrainStallReport.ElapsedMs(dimensionsStart);

            // Снимок до Process: он чистит набор заплаток, а в отчёт нужно
            // то, чем кадр был занят, а не то, что от него осталось.
            int dirtyRectCount = _window.Dirty.Rects.Count;
            long dirtyArea = _window.Dirty.Rects.TotalArea;
            long processStart = TerrainStallReport.Begin();
            if (!_window.Process(
                Services,
                _clientConfigManager,
                framePlan.ActiveWindow.Origin,
                framePlan.DimensionsChanged,
                BypassCpuMeshRebuild,
                _meshRenderer,
                WorldChanges.RequestedContentRevision,
                out Exception? failure))
            {
                _fatalBuildError = Diagnostics.ReportBuildFailure(
                    failure,
                    framePlan.ActiveWindow.Origin,
                    _mapManager,
                    _textureService,
                    _storage);
                return;
            }

            float processMs = publishMs + TerrainStallReport.ElapsedMs(processStart);

            // Окно рисуется только опубликованным: до первой публикации, после
            // смены мира или размера на GPU нет согласованной версии.
            if (_meshRenderer != null)
            {
                _meshRenderer.enabled = !BypassTerrainDraw && _window.CellsCommitted;
            }

            // Освещение узнаёт об изменённых клетках в кадре, когда их новая
            // геометрия действительно на экране, а не когда пришёл пакет.
            _publishedChangedRegions.Clear();
            _window.TakePublishedChangedRegions(_publishedChangedRegions);
            LightingFramePublisher.PublishCommittedChanges(
                CommittedContentRevision,
                _publishedChangedRegions);

            if (holdingView)
            {
                // Кадр по-прежнему показывает старое место: меш показа и
                // область освещения остаются прежними, план кадра считан для
                // места назначения.
                TerrainReadiness.PublishViewTransitionReadiness(
                    _window,
                    _textureService,
                    _viewTransition!);
                LightingFramePublisher.ValidateLightingOutput(hasLightingOutput, lightingOutput, _window);
                PublishTerrainFrameDemand(framePlan, holdingView: true);
                return;
            }

            // Меш показа ставится только по собранному окну: до первой
            // выгрузки текселей его размеры не с чем согласовывать.
            if (_window.CellsCommitted && _window.HasOrigin &&
                _window.Width > 0 && _window.Height > 0)
            {
                _presentation.Update(
                    _planner.Policy,
                    framePlan.CameraViewport,
                    _window.Origin,
                    _window.Width,
                    _window.Height,
                    _cellSize,
                    _meshFilter);
            }

            // Terrain cache и lighting cache имеют разные окна жизни. Terrain
            // может сдвинуться на выровненную границу, пока камера всё ещё
            // находится внутри стабильного lighting region.
            _lightingViewport = framePlan.LightingViewport;
            LightingFramePublisher.ValidateLightingOutput(hasLightingOutput, lightingOutput, _window);
            PublishTerrainFrameDemand(framePlan, holdingView: false);

            Diagnostics.Record(
                stallStart,
                _telemetry,
                new TerrainFrameTimings(
                    planMs,
                    dimensionsMs,
                    processMs,
                    uploadMs,
                    dirtyRectCount,
                    dirtyArea));
        }

        private TerrainBuildServices Services =>
            new(
                _storage,
                _mapManager,
                _textureService ?? throw new InvalidOperationException(
                    "TerrainRenderer requires ITextureService injection."),
                _telemetry);

        private void InitializeSceneBindings()
        {
            _meshFilter ??= GetComponent<MeshFilter>();
            _meshRenderer ??= GetComponent<MeshRenderer>();
            _cameraFrame.SetCameraIfMissing(_gameplayCamera?.Camera);

            _window.Driver.Materials.TerrainShader = _terrainShader;
            _window.Driver.Materials.InitializeShader();
            _window.Attach(
                transform,
                _sceneObjects,
                _sortingLayerName,
                _doorOverlaySortingOrder,
                _cellSize);

            if (_meshRenderer == null)
            {
                return;
            }

            _meshRenderer.enabled = true;
            _meshRenderer.sortingLayerName = _sortingLayerName;
            _meshRenderer.sortingOrder = _sortingOrder;
        }

        private void OnTextureLoaded(string filename, Texture2D texture)
        {
            Diagnostics.Mark(1 << 9, $"[TerrainDiag] first texture arrived: {filename}");
            WorldChanges.HandleTextureLoaded(filename, texture);
        }

        private void OnWorldDataLoaded() =>
            WorldChanges.HandleWorldDataLoaded(EnsureSubscriptions);

        private bool TryResolveCamera()
        {
            if (!_cameraFrame.UseCameraCandidate(_gameplayCamera?.Camera))
            {
                Diagnostics.Mark(1 << 2, "[TerrainDiag] camera NULL");
                return false;
            }

            Diagnostics.Mark(1 << 3,
                $"[TerrainDiag] camera ok: {_cameraFrame.Camera!.name} at {_cameraFrame.Camera.transform.position}");
            return true;
        }

        private void BeginTerrainWorldGeneration() => WorldChanges.BeginWorldGeneration();

        private void PublishTerrainFrameDemand(TerrainFramePlan framePlan, bool holdingView)
        {
            LightingFramePublisher.PublishFrameDemand(
                framePlan,
                holdingView,
                _lightingViewport,
                _window,
                _cameraFrame.Camera,
                _meshRenderer,
                CommittedContentRevision,
                this);
        }
    }
}

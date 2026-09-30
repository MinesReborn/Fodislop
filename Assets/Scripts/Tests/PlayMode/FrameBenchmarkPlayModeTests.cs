#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Lifecycle;
using Kern.Core.Interfaces.Diagnostics;
using Kern.Core.Interfaces;
using Kern.Networking;
using Kern.Networking.Auth;
using Kern.Player;
using Kern.Rendering;
using Kern.Rendering.PostProcessing;
using Kern.World.Terrain;
using MinesServer.Networking.Connection.Client;
using Newtonsoft.Json;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using VContainer;
using VContainer.Unity;
using Object = UnityEngine.Object;

namespace Kern.Tests.PlayMode;

// Бенчмарк кадра в настоящем мире.
//
// Поднимает Bootstrap → MainMenu → MainGame на заглушке сервера, ждёт готовый
// террейн и меряет одинаковые окна кадров в нескольких сценариях. Разница
// между «всё» и «без отрисовки террейна» — диагностическое наблюдение, не
// изолированная стоимость: зависимости и CPU/GPU overlap сохраняются. Отчёт
// разделяет CPU profiler markers, production CommandBuffer scopes и whole-frame
// CPU/GPU timings. Stage scopes — оконные агрегаты, не покадровое время; вложенные
// scopes не суммируются. Результат пишется в Logs/Diagnostics/Performance/benchmark_*.txt.
//
// Explicit: в обычный прогон тестов не входит, запускается руками.
[TestFixture]
[Explicit("Бенчмарк: запускается вручную из Test Runner.")]
[Category("Benchmark")]
public sealed class FrameBenchmarkPlayModeTests
{
    private const float UITimeoutSeconds = 20f;
    private const float WorldTimeoutSeconds = 60f;
    private const int WarmupFrames = 120;
    private const int MeasuredFrames = 300;
    private const string TestDummyToken = "playmode-benchmark-token";

    private static readonly string[] _Markers =
    [
        "PlayerLoop",
        "Kern.Terrain.LateUpdate.CPU",
        "Kern.Terrain.MeshBuild",
        "Kern.Terrain.MeshUpload",
        "Kern.Terrain.Cache",
        "Kern.Terrain.Precalculate",
        "Kern.World.Terrain.BackgroundFloodFill",
        "Gfx.WaitForPresentOnGfxThread",
        "Gfx.PresentFrame",
        "UIR.DrawChain",
        "GC.Collect",
    ];

    // These are existing production CommandBuffer GPU scopes, not CPU helpers.
    // Each marker is reported independently: several scopes are nested and must
    // not be summed into a fictitious total.
    private static readonly string[] _GpuStageMarkers =
    [
        "Kern.Terrain.RenderMaterialFields",
        "Kern.Terrain.RenderAmbientOcclusionField",
        "Kern.Lighting.MaterialField",
        "Kern.Lighting.AmbientOcclusionField",
        "Kern.Lighting.GeometryCaches",
        "Kern.Lighting.Cascade_0",
        "Kern.Lighting.Cascade_1",
        "Kern.Lighting.Cascade_2",
        "Kern.Lighting.Cascade_3",
        "Kern.Lighting.DynamicRadiance",
        "Kern.Lighting.Composite",
        "Kern.PostProcess.Composite",
        "Kern.PostProcess.BlitBack",
        "Kern.PostProcess.Bloom.Prefilter",
        "Kern.PostProcess.Bloom.Downsample",
        "Kern.PostProcess.Bloom.Upsample",
        "Kern.PostProcess.Bloom.UpsampleComposite",
    ];

    private static readonly FrameTiming[] _FrameTimingBuffer = new FrameTiming[1];

    private BootstrapLifetimeScope _bootstrap = null!;
    private string _originalClientToken = string.Empty;
    private HashSet<string> _originalDummyTokens = [];

    // Observation only: no JSON objects, file IO or strings are created in the sample loop.
    private readonly struct CaptureSample(IFrameTelemetry telemetry, long? allocatedBytes = null)
    {
        // Coroutine resumes after Update, before Terrain.LateUpdate resets the previous frame.
        public readonly int FrameId = Time.frameCount - 1;
        public readonly int? BloomDispatches = PostProcessRuntimeState.DiagnosticBloomFrame == Time.frameCount - 1
            ? PostProcessRuntimeState.DiagnosticBloomDispatches : null;
        public readonly TerrainTextureUploadSnapshot? TerrainTextureUpload =
            (telemetry as FrameTelemetry)?.CaptureTerrainTextureUploadSnapshot(Time.frameCount - 1);
        public readonly int? ProducerFrameId = (telemetry as IFrameTelemetryProducerStamp)?.ProducerFrameId;
        public readonly bool? ProducerLifecycleValid = (telemetry as IFrameTelemetryProducerStamp)?.ProducerLifecycleValid;
        public readonly float TerrainMesh = telemetry.TerrainMeshTimeMs;
        public readonly float TerrainCache = telemetry.TerrainCacheTimeMs;
        public readonly float TerrainFloodFill = telemetry.TerrainFloodFillTimeMs;
        public readonly float TerrainGpuUpload = telemetry.TerrainGpuUploadTimeMs;
        public readonly float TerrainAtlasUpload = telemetry.TerrainAtlasUploadTimeMs;
        public readonly float LightingBuildCommands = telemetry.LightingBuildCommandsTimeMs;
        public readonly float LightingExecuteCommands = telemetry.LightingExecuteCommandsTimeMs;
        public readonly float LightingCascadeTrace = telemetry.LightingCascadeTraceTimeMs;
        public readonly float LightingCascadeMerge = telemetry.LightingCascadeMergeTimeMs;
        public readonly float LightingDynamic = telemetry.LightingDynamicLightingTimeMs;
        public readonly float LightingComposite = telemetry.LightingCompositeTimeMs;
        public readonly int TerrainRebuilds = telemetry.TerrainRebuildCount;
        public readonly int TerrainFullPopulates = telemetry.TerrainFullPopulateCount;
        public readonly int TerrainDirtyPatches = telemetry.TerrainDirtyPatchCount;
        public readonly int TerrainChunkLoads = telemetry.TerrainChunkLoadCount;
        public readonly int TerrainMeshClears = telemetry.TerrainMeshClearCount;
        public readonly int TerrainBuildCancels = telemetry.TerrainBuildCancelCount;
        public readonly int LightingDynamicSolves = telemetry.LightingDynamicSolveCount;
        public readonly int LightingDynamicTraces = telemetry.LightingDynamicTraceCount;
        public readonly int LightingAtlasScrolls = telemetry.LightingAtlasScrollCount;
        public readonly int LightingFieldRebuilds = telemetry.LightingFieldRebuildCount;
        public readonly int LightingStaticSolves = telemetry.LightingStaticSolveFrameCount;
        public readonly int LightingStaticDependencyMaskSolves = telemetry.LightingStaticDependencyMaskSolveCount;
        public readonly int LightingStaticDenseFallbacks = telemetry.LightingStaticDenseFallbackCount;
        public readonly int LightingRegionInvalidations = telemetry.LightingRegionInvalidationFrameCount;
        public readonly int LightingRegionChanges = telemetry.LightingRegionChangeCount;
        public readonly int LightingGeometryChanges = telemetry.LightingGeometryChangeCount;
        public readonly int ActiveDynamicLights = telemetry.ActiveDynamicLights;
        public readonly int LightingCommandBufferBytes = telemetry.LightingCommandBufferBytes;
        public readonly int LightingDdaSegments = telemetry.LightingDdaSegments;
        public readonly long LightingDdaTexelVisits = telemetry.LightingDdaTexelVisits;
        public readonly int LightingCascadeMergeSamples = telemetry.LightingCascadeMergeSamples;
        public readonly long LightingDynamicDispatchPixels = telemetry.LightingDynamicDispatchPixels;
        public readonly long LightingDynamicComposePixels = telemetry.LightingDynamicComposePixels;
        public readonly long LightingCompositeDispatchPixels = telemetry.LightingCompositeDispatchPixels;
        public readonly long LightingPolarRayWorkUnits = telemetry.LightingPolarRayWorkUnits;
        public readonly long LightingEstimatedCascadeRayWorkUnits = telemetry.LightingEstimatedCascadeRayWorkUnits;
        public readonly long LightingEstimatedCascadeDispatchThreads = telemetry.LightingEstimatedCascadeDispatchThreads;
        public readonly long LightingCascadePartialEntries = telemetry.LightingCascadePartialEntriesFrame;
        public readonly long LightingCascadeFullEntries = telemetry.LightingCascadeFullEntriesFrame;
        public readonly long LightingCascadePartialEntriesTotal = telemetry.LightingCascadePartialEntries;
        public readonly long LightingCascadeFullEntriesTotal = telemetry.LightingCascadeFullEntries;
        public readonly long LightingAtlasReusedEntries = telemetry.LightingAtlasReusedEntries;
        public readonly long LightingAtlasClearedEntries = telemetry.LightingAtlasClearedEntries;
        public readonly long? GcAllocPerFrameBytes = allocatedBytes;
        public readonly int GcCollectionCount = telemetry.GcCollectionCount;
        public readonly int LightingRegionInvalidationTotal = telemetry.LightingRegionInvalidationCount;

        public object CumulativeJson()
        {
            var cumulative = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["terrainRebuilds"] = TerrainRebuilds,
                ["terrainFullPopulates"] = TerrainFullPopulates,
                ["terrainDirtyPatches"] = TerrainDirtyPatches,
                ["terrainChunkLoads"] = TerrainChunkLoads,
                ["terrainMeshClears"] = TerrainMeshClears,
                ["terrainBuildCancels"] = TerrainBuildCancels,
                ["lightingDynamicSolves"] = LightingDynamicSolves,
                ["lightingDynamicTraces"] = LightingDynamicTraces,
                ["lightingAtlasScrolls"] = LightingAtlasScrolls,
                ["lightingStaticDependencyMaskSolves"] = LightingStaticDependencyMaskSolves,
                ["lightingStaticDenseFallbacks"] = LightingStaticDenseFallbacks,
                ["lightingRegionInvalidations"] = LightingRegionInvalidationTotal,
                ["lightingCascadePartialEntries"] = LightingCascadePartialEntriesTotal,
                ["lightingCascadeFullEntries"] = LightingCascadeFullEntriesTotal,
                ["lightingAtlasReusedEntries"] = LightingAtlasReusedEntries,
                ["lightingAtlasClearedEntries"] = LightingAtlasClearedEntries,
                ["gcCollectionCount"] = GcCollectionCount,
            };
            foreach (KeyValuePair<string, object?> field in TerrainTextureUploadCaptureFields.Cumulative(TerrainTextureUpload))
            {
                cumulative.Add(field.Key, field.Value);
            }

            return cumulative;
        }

        public object FrameJson(double frameMs, TerrainTextureUploadSnapshot? previousUploadSnapshot)
        {
            var frameCounters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["terrainUploadCalls"] = null,
                ["terrainUploadBytes"] = null,
                ["terrainAtlasUploadCalls"] = null,
                ["terrainAtlasUploadBytes"] = null,
                ["lightingFieldRebuilds"] = LightingFieldRebuilds,
                ["lightingStaticSolves"] = LightingStaticSolves,
                ["lightingRegionInvalidations"] = LightingRegionInvalidations,
                ["lightingRegionChanges"] = LightingRegionChanges,
                ["lightingGeometryChanges"] = LightingGeometryChanges,
                ["lightingDdaSegments"] = LightingDdaSegments,
                ["lightingDdaTexelVisits"] = LightingDdaTexelVisits,
                ["lightingCascadeMergeSamples"] = LightingCascadeMergeSamples,
                ["lightingDynamicDispatchPixels"] = LightingDynamicDispatchPixels,
                ["lightingDynamicComposePixels"] = LightingDynamicComposePixels,
                ["lightingCompositeDispatchPixels"] = LightingCompositeDispatchPixels,
                ["bloomDispatches"] = BloomDispatches,
                ["lightingPolarRayWorkUnits"] = LightingPolarRayWorkUnits,
                ["lightingCascadePartialEntries"] = LightingCascadePartialEntries,
                ["lightingCascadeFullEntries"] = LightingCascadeFullEntries,
                ["gcAllocBytes"] = GcAllocPerFrameBytes,
            };
            foreach (KeyValuePair<string, object?> field in
                TerrainTextureUploadCaptureFields.FrameCounters(TerrainTextureUpload, previousUploadSnapshot))
            {
                frameCounters.Add(field.Key, field.Value);
            }

            return new
            {
            frameId = FrameId,
            producerFrameId = ProducerFrameId,
            producerLifecycleValid = ProducerLifecycleValid,
            @class = (string?)null,
            frameDurationMs = frameMs,
            gpuFrameMs = (double?)null,
            inputs = (object?)null,
            counterGeneration = 0,
            counterResetObserved = false,
            cumulative = CumulativeJson(),
            frameCounters,
            stageState = new
            {
                activeDynamicLights = ActiveDynamicLights,
                lastCommandBufferBytes = LightingCommandBufferBytes,
                estimatedCascadeRayWorkUnits = LightingEstimatedCascadeRayWorkUnits,
                estimatedCascadeDispatchThreads = LightingEstimatedCascadeDispatchThreads,
            },
            terrainCellDataUpload = TerrainTextureUploadCaptureFields.Observation(TerrainTextureUpload),
            cpuMs = new
            {
                terrainMesh = TerrainMesh,
                terrainCache = TerrainCache,
                terrainFloodFill = TerrainFloodFill,
                terrainGpuUpload = TerrainGpuUpload,
                terrainAtlasUpload = TerrainAtlasUpload,
                lightingBuildCommands = LightingBuildCommands,
                lightingExecuteCommands = LightingExecuteCommands,
                lightingCascadeTrace = LightingCascadeTrace,
                lightingCascadeMerge = LightingCascadeMerge,
                lightingDynamic = LightingDynamic,
                lightingComposite = LightingComposite,
            },
            };
        }
    }

    private sealed class CaptureWindow(string scenario, int count)
    {
        public readonly string Scenario = scenario;
        public readonly string CapturedAtUtc = DateTime.UtcNow.ToString("O");
        public readonly int Width = ResolveInScene<IGameplayCamera>(SceneManager.GetSceneByName("MainGame")).Camera.pixelWidth;
        public readonly int Height = ResolveInScene<IGameplayCamera>(SceneManager.GetSceneByName("MainGame")).Camera.pixelHeight;
        public readonly CaptureSample[] Samples = new CaptureSample[count];
        public readonly FrameTiming[] TimingObservations = new FrameTiming[count];
        public int TimingObservationCount;
        public string? QualityProfile;
        public CaptureSample Baseline;
        public double[] FrameMs = [];

        public void Write()
        {
            // MVIDs identify loaded code, not uncompiled working-tree files or all shader/assets.
            string loadedCode = string.Join(";", new[]
            {
                typeof(FrameBenchmarkPlayModeTests).Assembly,
                typeof(TerrainRenderer).Assembly,
                typeof(IFrameTelemetry).Assembly,
            }.Distinct().Select(assembly => $"{assembly.GetName().Name}:{assembly.ManifestModule.ModuleVersionId:D}"));
            string captureId = Guid.NewGuid().ToString("N");
            var capture = new
            {
                schemaVersion = 1,
                harnessVersion = "1",
                captureId,
                capturedAtUtc = CapturedAtUtc,
                manifest = new
                {
                    scenarioId = Scenario,
                    capturePhase = "coroutine-update-before-terrain-lateupdate:previous-frame",
                    workloadHash = (string?)null,
                    build = new
                    {
                        applicationVersion = Application.version,
                        loadedArtifactId = loadedCode,
                        artifactScope = "managed-code-only",
                    },
                    runtime = new
                    {
                        unityVersion = Application.unityVersion,
                        platform = $"{Application.platform}:{(Application.isEditor ? "editor" : "player")}",
                        graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                        graphicsDevice = SystemInfo.graphicsDeviceName,
                        width = Width,
                        height = Height,
                        qualityProfile = QualityProfile,
                    },
                    observationEvidence = "Coroutine observes previous frame before Terrain.LateUpdate; producer reset frame stamp and lifecycle validity exported; stamp alignment alone does not establish output commit/readiness or timing precision",
                    visualCoverage = (bool?)null,
                    visualEvidence = (string?)null,
                unavailableMetrics = new Dictionary<string, string>
                {
                        ["gpuFrameMs"] = "No frame-correlated GPU measurement; CPU command timings are not GPU time.",
                        ["inputs"] = "No deterministic replay, full source revisions, readiness or resource-generation observations.",
                        ["class"] = "Warmup length alone cannot establish steady/cold/reanchor classification.",
                        ["workloadHash"] = "Offline world is not pinned to a captured seed/snapshot/input schedule.",
                    ["uploadCountsAndBytes"] = "Cell-data Apply/CopyTexture estimates are exported; aggregate mesh and atlas upload coverage remains absent.",
                        ["visualCoverage"] = "No independent production visual oracle.",
                        ["fullArtifactIdentity"] = "Loaded assembly MVIDs exclude shaders/assets and current source-tree changes.",
                        ["frameCorrelation"] = "Producer reset frame and lifecycle-validity stamp are exported; commit/output readiness IDs are not represented.",
                    },
                },
                counterBaselineGeneration = 0,
                baselineObservationFrameId = Baseline.FrameId,
                baselineProducerFrameId = Baseline.ProducerFrameId,
                baselineProducerLifecycleValid = Baseline.ProducerLifecycleValid,
                counterBaseline = Baseline.CumulativeJson(),
                inputBaseline = (object?)null,
                frames = Samples.Select((sample, index) => sample.FrameJson(
                    FrameMs[index],
                    index == 0 ? Baseline.TerrainTextureUpload : Samples[index - 1].TerrainTextureUpload)).ToArray(),
            };
            string path = DiagnosticArtifactPaths.CreatePath("Performance", $"frame_capture_{captureId}", "json");
            // Fail the explicitly requested capture if export fails; never silently lose its evidence.
            File.WriteAllText(path, JsonConvert.SerializeObject(capture, Formatting.Indented));
            string timingPath = Path.ChangeExtension(path, ".timings.json");
            File.WriteAllText(timingPath, JsonConvert.SerializeObject(new
            {
                captureId,
                scenario = Scenario,
                width = Width,
                height = Height,
                qualityProfile = QualityProfile,
                semantics = "Unique FrameTiming timestamps observed during this window; delayed results have no proven Unity frameId mapping. Zero GPU time is unavailable, not zero cost.",
                requestedFrames = Samples.Length,
                observations = TimingObservations.Take(TimingObservationCount).Select(timing => new
                {
                    timestamp = timing.frameStartTimestamp,
                    cpuFrameMs = timing.cpuFrameTime > 0 ? (double?)timing.cpuFrameTime : null,
                    gpuFrameMs = timing.gpuFrameTime > 0 ? (double?)timing.gpuFrameTime : null,
                }).ToArray(),
            }, Formatting.Indented));
            DiagnosticReport.Announce("Frame harness capture (incomplete evidence)", path);
        }
    }

    // Без record: сборке PlayMode-тестов недоступен IsExternalInit.
    private readonly struct Result
    {
        public readonly string Scenario;
        public readonly double MeanMs;
        public readonly double P50Ms;
        public readonly double P95Ms;
        public readonly double P99Ms;
        public readonly double MaxMs;
        public readonly double CpuFrameP50Ms;
        public readonly double CpuMainThreadP50Ms;
        public readonly double CpuRenderThreadP50Ms;
        public readonly int FrameTimingSampleCount;
        public readonly double GpuP50Ms;
        public readonly int GpuSampleCount;
        public readonly Dictionary<string, double> Markers;
        public readonly Dictionary<string, double> GpuStageMarkers;
        public readonly Dictionary<string, int> GpuStageMarkerSampleCounts;

        public Result(string scenario, double meanMs, double p50Ms, double p95Ms, double p99Ms, double maxMs,
            double cpuFrameP50Ms, double cpuMainThreadP50Ms, double cpuRenderThreadP50Ms,
            int frameTimingSampleCount, double gpuP50Ms, int gpuSampleCount, Dictionary<string, double> markers,
            Dictionary<string, double> gpuStageMarkers, Dictionary<string, int> gpuStageMarkerSampleCounts)
        {
            Scenario = scenario;
            MeanMs = meanMs;
            P50Ms = p50Ms;
            P95Ms = p95Ms;
            P99Ms = p99Ms;
            MaxMs = maxMs;
            CpuFrameP50Ms = cpuFrameP50Ms;
            CpuMainThreadP50Ms = cpuMainThreadP50Ms;
            CpuRenderThreadP50Ms = cpuRenderThreadP50Ms;
            FrameTimingSampleCount = frameTimingSampleCount;
            GpuP50Ms = gpuP50Ms;
            GpuSampleCount = gpuSampleCount;
            Markers = markers;
            GpuStageMarkers = gpuStageMarkers;
            GpuStageMarkerSampleCounts = gpuStageMarkerSampleCounts;
        }
    }

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        BootstrapLifetimeScope? existing = FindBootstrap();
        if (existing != null)
        {
            Object.Destroy(existing.gameObject);
            yield return null;
            yield return null;
        }

        var gameTokenStore = new GameTokenStore();
        _originalClientToken = gameTokenStore.Load();
        DummyTokenStore tokenStore = new();
        _originalDummyTokens = tokenStore.Load();
        tokenStore.Save(new HashSet<string>(_originalDummyTokens) { TestDummyToken });
        gameTokenStore.Save(TestDummyToken);

        yield return SceneManager.LoadSceneAsync("Bootstrap", LoadSceneMode.Single);
        yield return WaitUntil(() => FindBootstrap() is { Container: not null }, UITimeoutSeconds, "Bootstrap container was not built.");
        _bootstrap = FindBootstrap()!;
        yield return WaitUntil(() => _bootstrap.CurrentSceneName == "Gateway", UITimeoutSeconds, "Gateway did not open.");
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        BootstrapLifetimeScope? bootstrap = FindBootstrap();
        if (bootstrap?.Container != null &&
            bootstrap.Container.TryResolve<IConnectionService>(out IConnectionService connection))
        {
            connection.Disconnect();
        }

        if (bootstrap != null)
        {
            Object.Destroy(bootstrap.gameObject);
            yield return null;
            yield return null;
        }

        var gameTokenStore = new GameTokenStore();
        new DummyTokenStore().Save(_originalDummyTokens);
        if (string.IsNullOrEmpty(_originalClientToken))
        {
            gameTokenStore.Clear();
        }
        else
        {
            gameTokenStore.Save(_originalClientToken);
        }
    }

    [UnityTest]
    [Timeout(300_000)]
    public IEnumerator MainGame_FrameCostByScenario()
    {
        string? resolution = Environment.GetEnvironmentVariable("KERN_BENCHMARK_RESOLUTION");
        if (resolution is not null)
        {
            string[] dimensions = resolution.Split('x');
            if (dimensions.Length != 2 || !int.TryParse(dimensions[0], out int width) ||
                !int.TryParse(dimensions[1], out int height) || width <= 0 || height <= 0)
            {
                throw new InvalidOperationException("KERN_BENCHMARK_RESOLUTION must be WIDTHxHEIGHT.");
            }

            // Resolution belongs to the explicit camera target below. DisplayManager
            // owns Screen settings; the benchmark must not change persisted display config.
        }

        yield return Await(_bootstrap.TransitionAsync("MainMenu"), UITimeoutSeconds);
        yield return Await(_bootstrap.TransitionAsync("MainGame"), WorldTimeoutSeconds);

        Scene game = SceneManager.GetSceneByName("MainGame");
        TerrainRenderer terrain = FindComponentInScene<TerrainRenderer>(game)
            ?? throw new InvalidOperationException("MainGame has no TerrainRenderer.");
        yield return WaitUntil(() => terrain.IsReadyForGameplay, WorldTimeoutSeconds, "Terrain never became ready.");
        MeshFilter terrainMeshFilter = terrain.GetComponentInChildren<MeshFilter>()
            ?? throw new InvalidOperationException("TerrainRenderer has no presentation MeshFilter.");

        Camera camera = ResolveInScene<IGameplayCamera>(game).Camera;
        Camera? originalDiagnosticCamera = PostProcessRuntimeState.DiagnosticOffscreenCamera;
        PostProcessRuntimeState.DiagnosticOffscreenCamera = camera;
        RenderTexture? benchmarkTarget = null;
        RenderTexture originalTarget = camera.targetTexture;
        if (resolution is not null)
        {
            string[] dimensions = resolution.Split('x');
            benchmarkTarget = new RenderTexture(int.Parse(dimensions[0]), int.Parse(dimensions[1]), 24,
                RenderTextureFormat.ARGBHalf)
            {
                name = "FrameBenchmarkProductionCameraTarget",
            };
            if (!benchmarkTarget.Create())
            {
                throw new InvalidOperationException("Cannot create requested benchmark camera target.");
            }

            camera.targetTexture = benchmarkTarget;
            Assert.That(camera.pixelWidth, Is.EqualTo(benchmarkTarget.width));
            Assert.That(camera.pixelHeight, Is.EqualTo(benchmarkTarget.height));
        }
        CameraFollow cameraFollow = camera.GetComponent<CameraFollow>() ??
            FindComponentInScene<CameraFollow>(game) ??
            throw new InvalidOperationException("MainGame has no CameraFollow for the zoom workload.");
        bool cameraFollowWasEnabled = cameraFollow.enabled;
        float originalZoom = camera.orthographicSize;

        IRuntimeDebugSettings debug = ResolveInScene<IRuntimeDebugSettings>(game);
        bool originalLightingBypass = debug.BypassLightingCompute;
        bool originalTerrainDrawBypass = debug.BypassTerrainDraw;
        bool originalCpuMeshRebuildBypass = debug.BypassCpuMeshRebuild;
        var results = new List<Result>();
        IFrameTelemetry telemetry = ResolveInScene<IFrameTelemetry>(game);
        var captures = new List<CaptureWindow>();
        int benchmarkStageId = Shader.PropertyToID("_KernTerrainBenchmarkStage");
        int originalBenchmarkStage = Shader.GetGlobalInt(benchmarkStageId);

        try
        {
            Shader.SetGlobalInt(benchmarkStageId, 0);
            debug.BypassLightingCompute = false;
            debug.BypassTerrainDraw = false;
            debug.BypassCpuMeshRebuild = false;
            yield return Skip(WarmupFrames);
            yield return Measure("всё включено", results, telemetry, captures, "observational/all-enabled");
            yield return MeasurePostProcessing(camera, cameraFollow, telemetry, results, captures);
            for (int stage = 1; stage <= 4; stage++)
            {
                Shader.SetGlobalInt(benchmarkStageId, stage);
                yield return Skip(30);
                yield return Measure($"террейн: fragment checkpoint {stage}", results, telemetry,
                    captures, $"diagnostic/terrain-fragment-checkpoint-{stage}");
            }

            Shader.SetGlobalInt(benchmarkStageId, 0);

            cameraFollow.enabled = false;

            camera.orthographicSize = ProjectRuntimeContracts.Camera.MaximumOrthographicSize;
            yield return Skip(60);
            Assert.That(
                camera.orthographicSize,
                Is.EqualTo(ProjectRuntimeContracts.Camera.MaximumOrthographicSize).Within(0.001f),
                "Camera did not reach the maximum zoom benchmark input.");
            int maximumZoomVertexCount = terrainMeshFilter.sharedMesh != null
                ? terrainMeshFilter.sharedMesh.vertexCount
                : throw new InvalidOperationException("Terrain presentation mesh is missing at maximum zoom.");
            yield return Measure(
                $"террейн: максимальный зум, {maximumZoomVertexCount} вершин",
                results,
                telemetry,
                captures,
                "terrain/maximum-zoom");
            camera.orthographicSize = ProjectRuntimeContracts.Camera.MinimumOrthographicSize;
            yield return Skip(60);
            Assert.That(
                camera.orthographicSize,
                Is.EqualTo(ProjectRuntimeContracts.Camera.MinimumOrthographicSize).Within(0.001f),
                "Camera did not reach the minimum zoom benchmark input.");
            int minimumAfterMaximumVertexCount = terrainMeshFilter.sharedMesh != null
                ? terrainMeshFilter.sharedMesh.vertexCount
                : throw new InvalidOperationException("Terrain presentation mesh is missing after zoom-in.");
            Assert.That(
                minimumAfterMaximumVertexCount,
                Is.LessThan(maximumZoomVertexCount),
                "Presentation mesh retained its maximum-zoom high-water size after zoom-in.");
            yield return Measure(
                $"террейн: минимум после максимума, {minimumAfterMaximumVertexCount} вершин",
                results,
                telemetry,
                captures,
                "terrain/minimum-after-maximum-zoom");
            camera.orthographicSize = originalZoom;
            cameraFollow.enabled = cameraFollowWasEnabled;

            yield return Skip(60);
            yield return MeasureBypassCombination(
                "только обход terrain draw",
                terrainDrawBypass: true,
                lightingBypass: false,
                "diagnostic/terrain-draw-bypass",
                debug,
                telemetry,
                results,
                captures);
            yield return MeasureBypassCombination(
                "только обход lighting compute",
                terrainDrawBypass: false,
                lightingBypass: true,
                "diagnostic/lighting-compute-bypass",
                debug,
                telemetry,
                results,
                captures);
            yield return MeasureBypassCombination(
                "обход terrain draw + lighting compute",
                terrainDrawBypass: true,
                lightingBypass: true,
                "diagnostic/terrain-and-lighting-bypass",
                debug,
                telemetry,
                results,
                captures);

            debug.BypassLightingCompute = false;
            debug.BypassTerrainDraw = false;
            yield return Skip(WarmupFrames);
            yield return Measure("всё включено (повтор)", results, telemetry, captures, "observational/all-enabled-repeat");

            debug.BypassCpuMeshRebuild = true;
            yield return Skip(30);
            yield return Measure("без пересборки террейна", results, telemetry, captures, "diagnostic/terrain-build-bypass");

            // Formatting and IO happen only after all measured windows.
            foreach (CaptureWindow capture in captures)
            {
                capture.Write();
            }

            string report = BuildReport(results, captures);
            DiagnosticReport.Write("Performance", "benchmark", "Бенчмарк кадра", report);
            Debug.Log($"[FrameBenchmark]\n{report}");

            Assert.That(results.All(r => r.MeanMs > 0), Is.True, "Frames were not measured.");
        }
        finally
        {
            PostProcessRuntimeState.DiagnosticOffscreenCamera = originalDiagnosticCamera;
            Shader.SetGlobalInt(benchmarkStageId, originalBenchmarkStage);
            camera.targetTexture = originalTarget;
            if (benchmarkTarget is not null)
            {
                benchmarkTarget.Release();
                Object.Destroy(benchmarkTarget);
            }

            camera.orthographicSize = originalZoom;
            cameraFollow.enabled = cameraFollowWasEnabled;
            debug.BypassLightingCompute = originalLightingBypass;
            debug.BypassTerrainDraw = originalTerrainDrawBypass;
            debug.BypassCpuMeshRebuild = originalCpuMeshRebuildBypass;
        }
    }

    private static IEnumerator MeasurePostProcessing(Camera camera, CameraFollow cameraFollow, IFrameTelemetry telemetry,
        List<Result> results, List<CaptureWindow> captures)
    {
        UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
        bool originalUrp = cameraData.renderPostProcessing;
        bool originalBypass = PostProcessRuntimeState.BypassPostProcessEffects;
        bool originalSkip = PostProcessRuntimeState.SkipPasses;
        bool originalTemporary = PostProcessRuntimeState.TemporaryBypass;
        bool originalUnfusedBloom = PostProcessRuntimeState.DiagnosticUnfusedBloom;
        GameObject fixture = ResolveInScene<ISceneObjectFactory>(SceneManager.GetSceneByName("MainGame"))
            .Create("FrameBenchmarkPostProcessOverrides");
        int mask = cameraData.volumeLayerMask.value;
        int layer = 0;
        while (layer < 32 && (mask & (1 << layer)) == 0)
        {
            layer++;
        }

        if (layer == 32)
        {
            Object.Destroy(fixture);
            throw new InvalidOperationException("Benchmark camera has no Volume layer mask.");
        }

        fixture.layer = layer;
        Volume volume = fixture.AddComponent<Volume>();
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        volume.sharedProfile = profile;
        volume.isGlobal = true;
        volume.priority = float.MaxValue;
        BloomComponent bloom = profile.Add<BloomComponent>();
        VignetteComponent vignette = profile.Add<VignetteComponent>();
        EigengrauComponent eigengrau = profile.Add<EigengrauComponent>();
        bloom.intensity.overrideState = true;
        vignette.intensity.overrideState = true;
        eigengrau.intensity.overrideState = true;
        try
        {
            PostProcessRuntimeState.SkipPasses = false;
            PostProcessRuntimeState.BypassPostProcessEffects = false;
            PostProcessRuntimeState.TemporaryBypass = false;
            bloom.intensity.value = PostProcessLook.Bloom.Intensity;
            vignette.intensity.value = PostProcessLook.Vignette.Intensity;
            eigengrau.intensity.value = PostProcessLook.FilmGrain.Intensity;
            cameraData.renderPostProcessing = true;
            yield return VerifyBloomCameraImage(camera, cameraFollow, eigengrau);
            int bloomLevels = 0;
            int smallestSide = Mathf.Min(camera.pixelWidth, camera.pixelHeight) / 2;
            while (bloomLevels < 4 && (smallestSide >> (bloomLevels + 1)) >= 8)
            {
                bloomLevels++;
            }

            foreach (bool unfused in new[] { true, false, false, true, true, false })
            {
                PostProcessRuntimeState.DiagnosticUnfusedBloom = unfused;
                yield return Skip(60);
                Assert.That(PostProcessRuntimeState.DiagnosticBloomFrame,
                    Is.GreaterThanOrEqualTo(Time.frameCount - 2));
                Assert.That(PostProcessRuntimeState.DiagnosticBloomDispatches, Is.EqualTo(2 * bloomLevels + (unfused ? 2 : 1)),
                    "Unexpected bloom dispatch count at benchmark resolution.");
                yield return Measure($"bloom fusion: {(unfused ? "reference" : "fused")}",
                    results, telemetry, captures, unfused ? "diagnostic/bloom-reference" : "diagnostic/bloom-fused");
            }

            PostProcessRuntimeState.DiagnosticUnfusedBloom = false;
            // Author intensities, fixed between windows; all other settings are inherited.
            foreach (int effects in new[] { 7, 6, 5, 3, 1, 2, 4, 0, 7 })
            {
                bloom.intensity.value = (effects & 1) != 0 ? PostProcessLook.Bloom.Intensity : 0f;
                vignette.intensity.value = (effects & 2) != 0 ? PostProcessLook.Vignette.Intensity : 0f;
                eigengrau.intensity.value = (effects & 4) != 0 ? PostProcessLook.FilmGrain.Intensity : 0f;
                cameraData.renderPostProcessing = true;
                yield return Skip(60);
                if ((effects & 1) != 0)
                {
                    Assert.That(PostProcessRuntimeState.DiagnosticSceneFrame,
                        Is.GreaterThanOrEqualTo(Time.frameCount - 2), "Bloom production pass was not recorded.");
                }

                if ((effects & 6) != 0)
                {
                    Assert.That(PostProcessRuntimeState.DiagnosticDisplayFrame,
                        Is.GreaterThanOrEqualTo(Time.frameCount - 2), "DisplayFinal production pass was not recorded.");
                }

                yield return Measure($"постпроцесс: mask {effects} (bloom=1, vignette=2, eigengrau=4)",
                    results, telemetry, captures, $"diagnostic/postprocess-mask-{effects}");
            }

            bloom.intensity.value = 0f;
            vignette.intensity.value = 0f;
            eigengrau.intensity.value = 0f;
            cameraData.renderPostProcessing = false;
            yield return Skip(60);
            yield return Measure("постпроцесс: эффекты 0, URP off", results, telemetry, captures,
                "diagnostic/postprocess-zero-urp-off");
        }
        finally
        {
            cameraData.renderPostProcessing = originalUrp;
            PostProcessRuntimeState.BypassPostProcessEffects = originalBypass;
            PostProcessRuntimeState.SkipPasses = originalSkip;
            PostProcessRuntimeState.TemporaryBypass = originalTemporary;
            PostProcessRuntimeState.DiagnosticUnfusedBloom = originalUnfusedBloom;
            Object.Destroy(fixture);
            foreach (VolumeComponent component in profile.components)
            {
                Object.Destroy(component);
            }

            Object.Destroy(profile);
        }

        yield return Skip(60);
    }

    private static IEnumerator VerifyBloomCameraImage(Camera camera, CameraFollow follow, EigengrauComponent eigengrau)
    {
        RenderTexture? originalTarget = camera.targetTexture;
        RenderTexture? ownedTarget = null;
        if (originalTarget == null)
        {
            ownedTarget = new RenderTexture(camera.pixelWidth, camera.pixelHeight, 24, RenderTextureFormat.ARGBHalf)
            {
                name = "BloomProductionImageOracleTarget",
            };
            Assert.That(ownedTarget.Create(), Is.True);
            camera.targetTexture = ownedTarget;
        }

        RenderTexture target = camera.targetTexture;
        float originalTimeScale = Time.timeScale;
        float originalEigengrau = eigengrau.intensity.value;
        bool originalReference = PostProcessRuntimeState.DiagnosticUnfusedBloom;
        UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
        bool originalDithering = cameraData.dithering;
        bool originalFollowEnabled = follow.enabled;
        Vector3 originalCameraPosition = camera.transform.position;
        Texture2D readback = RuntimeTextureFactory.CreateRGBAHalfNoMip(target.width, target.height,
            "BloomProductionCameraReadback", RuntimeTextureColorSpace.Linear, FilterMode.Point, TextureWrapMode.Clamp);
        try
        {
            // Freeze animation inputs, not lighting execution. Frame-index grain must
            // be disabled only for the image oracle; timing windows keep it enabled.
            Time.timeScale = 0f;
            follow.enabled = false;
            eigengrau.intensity.value = 0f;
            // ConfigureCamera enables URP blue-noise dithering; its texture/index
            // and random offset change even when Time.time is frozen. Disable it
            // only for the deterministic image comparison, never timing windows.
            cameraData.dithering = false;
            Color[][] images = new Color[3][];
            for (int pass = 0; pass < images.Length; pass++)
            {
                PostProcessRuntimeState.DiagnosticUnfusedBloom = pass != 1;
                yield return Skip(30);
                Assert.That(cameraData.dithering, Is.False, "Image oracle dithering was re-enabled.");
                Assert.That(PostProcessRuntimeState.DiagnosticSceneFrame,
                    Is.GreaterThanOrEqualTo(Time.frameCount - 2));
                RenderTexture previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                    readback.Apply();
                    images[pass] = readback.GetPixels();
                }
                finally
                {
                    RenderTexture.active = previous;
                }
            }

            float baselineDifference = 0f;
            float fusionDifference = 0f;
            int baselinePixel = 0;
            int baselineChannel = 0;
            for (int pixel = 0; pixel < images[0].Length; pixel++)
            {
                for (int channel = 0; channel < 4; channel++)
                {
                    float reference = images[0][pixel][channel];
                    float fused = images[1][pixel][channel];
                    if (float.IsNaN(fused) || float.IsInfinity(fused))
                    {
                        Assert.Fail($"Non-finite bloom camera pixel {pixel}, channel {channel}");
                    }
                    float delta = Mathf.Abs(reference - images[2][pixel][channel]);
                    if (delta > baselineDifference)
                    {
                        baselineDifference = delta;
                        baselinePixel = pixel;
                        baselineChannel = channel;
                    }
                    fusionDifference = Mathf.Max(fusionDifference, Mathf.Abs(reference - fused));
                }
            }

            TestContext.WriteLine($"Production bloom image: baseline max difference={baselineDifference:R}, fusion={fusionDifference:R}, baseline coordinate=({baselinePixel % target.width},{baselinePixel / target.width}), channel={baselineChannel}, camera delta={(camera.transform.position - originalCameraPosition).magnitude:R}, shaderTime={Shader.GetGlobalVector("_Time").y:R}, time={Time.time:R}");
            Assert.That(baselineDifference, Is.LessThanOrEqualTo(0.002f), "Image oracle inputs changed between reference frames.");
            Assert.That(fusionDifference, Is.LessThanOrEqualTo(0.01f), "Bloom fusion changed the production camera image.");
        }
        finally
        {
            Time.timeScale = originalTimeScale;
            follow.enabled = originalFollowEnabled;
            cameraData.dithering = originalDithering;
            eigengrau.intensity.value = originalEigengrau;
            PostProcessRuntimeState.DiagnosticUnfusedBloom = originalReference;
            Object.Destroy(readback);
            if (ownedTarget != null)
            {
                camera.targetTexture = originalTarget;
                ownedTarget.Release();
                Object.Destroy(ownedTarget);
            }
        }
    }

    private static IEnumerator MeasureBypassCombination(
        string scenario,
        bool terrainDrawBypass,
        bool lightingBypass,
        string captureScenario,
        IRuntimeDebugSettings debug,
        IFrameTelemetry telemetry,
        List<Result> results,
        List<CaptureWindow> captures)
    {
        debug.BypassTerrainDraw = terrainDrawBypass;
        debug.BypassLightingCompute = lightingBypass;

        // Lighting bypass releases production GPU resources. Allow its transition
        // and re-enable path to settle before recording a comparable steady window.
        yield return Skip(WarmupFrames);
        yield return Measure(scenario, results, telemetry, captures, captureScenario);
    }

    private static IEnumerator Measure(string scenario, List<Result> results,
        IFrameTelemetry? telemetry, List<CaptureWindow> captures, string captureScenario)
    {
        // Own the counter: telemetry tracking otherwise depends on an open diagnostics window.
        // LastValue observes the completed frame, like CaptureSample.FrameId.
        using var allocationRecorder = ProfilerRecorder.StartNew(
            ProfilerCategory.Memory, "GC Allocated In Frame");
        var recorders = new List<(string Name, bool IsGpuStage, ProfilerRecorder Recorder)>();
        foreach ((string marker, bool isGpuStage) in _Markers.Select(name => (name, false))
                     .Concat(_GpuStageMarkers.Select(name => (name, true))))
        {
            // Маркеры из разных категорий: конструктор по имени ищет во всех.
            var recorder = new ProfilerRecorder(
                marker,
                MeasuredFrames,
                ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.SumAllSamplesInFrame);
            recorders.Add((marker, isGpuStage, recorder));
        }

        var frameMs = new double[MeasuredFrames];
        var cpuFrameMs = new List<double>(MeasuredFrames);
        var cpuMainThreadMs = new List<double>(MeasuredFrames);
        var cpuRenderThreadMs = new List<double>(MeasuredFrames);
        var gpuFrameMs = new List<double>(MeasuredFrames);
        bool gpuTimingSupported = FrameTimingManager.IsFeatureEnabled();
        ulong lastFrameTimingTimestamp = 0;
        bool hasFrameTimingTimestamp = false;
        if (gpuTimingSupported)
        {
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _FrameTimingBuffer) > 0)
            {
                lastFrameTimingTimestamp = _FrameTimingBuffer[0].frameStartTimestamp;
                hasFrameTimingTimestamp = true;
            }
        }

        CaptureWindow? capture = telemetry is null ? null : new CaptureWindow(captureScenario, MeasuredFrames)
        {
            Baseline = new CaptureSample(telemetry),
            FrameMs = frameMs,
        };
        if (capture is not null)
        {
            ClientConfig config = FindBootstrap()!.Container.Resolve<IClientConfigManager>().Config;
            capture.QualityProfile = JsonConvert.SerializeObject(new
            {
                config.GraphicsPreset,
                config.GraphicsQualitySettings,
                terrain = JsonUtility.ToJson(config.Terrain),
                effects = JsonUtility.ToJson(config.Effects),
                display = JsonUtility.ToJson(config.Display),
                unityQuality = QualitySettings.GetQualityLevel(),
                activePipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline?.name,
                terrainFragmentCheckpoint = Shader.GetGlobalInt("_KernTerrainBenchmarkStage"),
                postprocess = new
                {
                    urp = ResolveInScene<IGameplayCamera>(SceneManager.GetSceneByName("MainGame"))
                        .Camera.GetUniversalAdditionalCameraData().renderPostProcessing,
                    bloomIntensity = VolumeManager.instance.stack.GetComponent<BloomComponent>().intensity.value,
                    vignetteIntensity = VolumeManager.instance.stack.GetComponent<VignetteComponent>().intensity.value,
                    eigengrauIntensity = VolumeManager.instance.stack.GetComponent<EigengrauComponent>().intensity.value,
                    PostProcessRuntimeState.SkipPasses,
                    PostProcessRuntimeState.BypassPostProcessEffects,
                    PostProcessRuntimeState.DiagnosticUnfusedBloom,
                },
            });
        }
        for (int frame = 0; frame < MeasuredFrames; frame++)
        {
            yield return null;
            frameMs[frame] = Time.unscaledDeltaTime * 1000.0;
            if (gpuTimingSupported)
            {
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, _FrameTimingBuffer) > 0)
                {
                    FrameTiming timing = _FrameTimingBuffer[0];
                    if (!hasFrameTimingTimestamp || timing.frameStartTimestamp != lastFrameTimingTimestamp)
                    {
                        if (capture is not null)
                        {
                            capture.TimingObservations[capture.TimingObservationCount++] = timing;
                        }
                        if (timing.cpuFrameTime > 0.0 && double.IsFinite(timing.cpuFrameTime))
                        {
                            cpuFrameMs.Add(timing.cpuFrameTime);
                        }

                        if (timing.cpuMainThreadFrameTime > 0.0 && double.IsFinite(timing.cpuMainThreadFrameTime))
                        {
                            cpuMainThreadMs.Add(timing.cpuMainThreadFrameTime);
                        }

                        if (timing.cpuRenderThreadFrameTime > 0.0 && double.IsFinite(timing.cpuRenderThreadFrameTime))
                        {
                            cpuRenderThreadMs.Add(timing.cpuRenderThreadFrameTime);
                        }

                        if (timing.gpuFrameTime > 0.0 && double.IsFinite(timing.gpuFrameTime))
                        {
                            gpuFrameMs.Add(timing.gpuFrameTime);
                        }

                        lastFrameTimingTimestamp = timing.frameStartTimestamp;
                        hasFrameTimingTimestamp = true;
                    }
                }
            }

            if (capture is not null && telemetry is not null)
            {
                capture.Samples[frame] = new CaptureSample(telemetry,
                    allocationRecorder.Valid && allocationRecorder.Count > 0
                        ? allocationRecorder.LastValue
                        : null);
            }
        }

        if (capture is not null)
        {
            captures.Add(capture);
        }

        var markers = new Dictionary<string, double>();
        var gpuStageMarkers = new Dictionary<string, double>();
        var gpuStageMarkerSampleCounts = new Dictionary<string, int>();
        foreach ((string name, bool isGpuStage, ProfilerRecorder recorder) in recorders)
        {
            if (recorder.Valid && recorder.Count > 0)
            {
                double sum = 0;
                for (int i = 0; i < recorder.Count; i++)
                {
                    sum += recorder.GetSample(i).Value;
                }

                if (isGpuStage)
                {
                    gpuStageMarkers[name] = sum / recorder.Count * 1e-6;
                    gpuStageMarkerSampleCounts[name] = recorder.Count;
                }
                else
                {
                    markers[name] = sum / recorder.Count * 1e-6;
                }
            }

            recorder.Dispose();
        }

        double[] sorted = frameMs.OrderBy(value => value).ToArray();
        double[] sortedCpuFrame = cpuFrameMs.OrderBy(value => value).ToArray();
        double[] sortedCpuMainThread = cpuMainThreadMs.OrderBy(value => value).ToArray();
        double[] sortedCpuRenderThread = cpuRenderThreadMs.OrderBy(value => value).ToArray();
        double[] sortedGpu = gpuFrameMs.OrderBy(value => value).ToArray();
        results.Add(new Result(
            scenario,
            frameMs.Average(),
            Percentile(sorted, 0.50),
            Percentile(sorted, 0.95),
            Percentile(sorted, 0.99),
            sorted[^1],
            sortedCpuFrame.Length > 0 ? Percentile(sortedCpuFrame, 0.50) : 0,
            sortedCpuMainThread.Length > 0 ? Percentile(sortedCpuMainThread, 0.50) : 0,
            sortedCpuRenderThread.Length > 0 ? Percentile(sortedCpuRenderThread, 0.50) : 0,
            sortedCpuFrame.Length,
            sortedGpu.Length > 0 ? Percentile(sortedGpu, 0.50) : 0,
            sortedGpu.Length,
            markers,
            gpuStageMarkers,
            gpuStageMarkerSampleCounts));
    }

    private static string BuildReport(List<Result> results, List<CaptureWindow> captures)
    {
        var report = new StringBuilder(4096);
        report.Append("Бенчмарк кадра, ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
            .Append(", render ").Append(captures[0].Width).Append('×').Append(captures[0].Height)
            .Append(", ").Append(SystemInfo.graphicsDeviceType)
            .Append(Application.isEditor ? ", редактор" : ", сборка").AppendLine()
            .Append("Окно: ").Append(MeasuredFrames).AppendLine(" кадров на сценарий").AppendLine();

        foreach (Result result in results)
        {
            report.Append("== ").Append(result.Scenario).AppendLine(" ==")
                .Append("кадр: среднее ").Append(result.MeanMs.ToString("F2"))
                .Append(" мс (").Append((1000.0 / result.MeanMs).ToString("F0")).Append(" fps), p50 ")
                .Append(result.P50Ms.ToString("F2")).Append(", p95 ").Append(result.P95Ms.ToString("F2"))
                .Append(", p99 ").Append(result.P99Ms.ToString("F2")).Append(", максимум ")
                .Append(result.MaxMs.ToString("F2"));
            if (result.FrameTimingSampleCount > 0)
            {
                report.Append(", FrameTiming p50 CPU ").Append(result.CpuFrameP50Ms.ToString("F2"))
                    .Append(" мс (main ").Append(result.CpuMainThreadP50Ms.ToString("F2"))
                    .Append(", render ").Append(result.CpuRenderThreadP50Ms.ToString("F2"))
                    .Append("), GPU ");
                if (result.GpuSampleCount > 0)
                {
                    report.Append("p50 ").Append(result.GpuP50Ms.ToString("F2"))
                        .Append(" мс (уникальных кадров ").Append(result.GpuSampleCount).Append(')');
                }
                else
                {
                    report.Append("нет данных (0 уникальных кадров)");
                }

                report.Append("; уникальных CPU timing кадров ").Append(result.FrameTimingSampleCount);
            }
            else
            {
                report.Append(", FrameTiming CPU/GPU нет данных (0 уникальных кадров)");
            }

            report.AppendLine();
            foreach (KeyValuePair<string, double> marker in result.Markers.OrderByDescending(entry => entry.Value))
            {
                report.Append("  ").Append(marker.Value.ToString("F3")).Append(" мс  ").AppendLine(marker.Key);
            }

            report.AppendLine("Command-buffer profiler scopes (GPU duration unverified; nested values are not additive):");
            foreach (string stage in _GpuStageMarkers)
            {
                if (result.GpuStageMarkers.TryGetValue(stage, out double stageMs))
                {
                    report.Append("  ").Append(stageMs.ToString("F3")).Append(" ms average over ")
                        .Append(result.GpuStageMarkerSampleCounts[stage]).Append(" marker samples  ")
                        .AppendLine(stage);
                }
                else
                {
                    report.Append("  NO PROFILER SAMPLES  ").AppendLine(stage);
                }
            }

            report.AppendLine("  Terrain visible MeshRenderer draw has no scoped GPU marker; draw-bypass delta is diagnostic only.")
                .AppendLine();
        }

        AppendBypassFactorial(report, results);
        Result[] postprocessBaselines = results.Where(result =>
            result.Scenario == "постпроцесс: mask 7 (bloom=1, vignette=2, eigengrau=4)").ToArray();
        if (postprocessBaselines.Length == 2)
        {
            double drift = Math.Abs(postprocessBaselines[1].P50Ms - postprocessBaselines[0].P50Ms) /
                postprocessBaselines[0].P50Ms;
            report.AppendLine().Append("Postprocess baseline repeat drift: ")
                .Append((drift * 100).ToString("F1")).AppendLine("%.");
            if (drift > 0.10)
            {
                report.AppendLine("PERFORMANCE INCOMPLETE: repeated baseline differs by >10%; sequential per-effect deltas do not establish isolated costs.");
            }
        }

        report.AppendLine().AppendLine("Покрытие измерений (отсутствие данных не означает нулевую стоимость):");
        foreach (CaptureWindow capture in captures)
        {
            long[] bytes = capture.Samples.Where(sample => sample.GcAllocPerFrameBytes.HasValue)
                .Select(sample => sample.GcAllocPerFrameBytes!.Value).OrderBy(value => value).ToArray();
            int gpuSamples = capture.TimingObservations.Take(capture.TimingObservationCount)
                .Count(timing => timing.gpuFrameTime > 0 && double.IsFinite(timing.gpuFrameTime));
            report.Append(capture.Scenario).Append(": allocations ").Append(bytes.Length)
                .Append('/').Append(capture.Samples.Length);
            if (bytes.Length > 0)
            {
                report.Append(", mean ").Append(bytes.Average().ToString("F0"))
                    .Append(" B/frame, max ").Append(bytes[^1]).Append(" B");
            }

            report.Append("; independent GPU timings ").Append(gpuSamples)
                .Append('/').Append(capture.Samples.Length)
                .AppendLine("; frame-correlated GPU, source revisions and sample classification: unavailable.");
        }

        return report.ToString();
    }

    private static void AppendBypassFactorial(StringBuilder report, List<Result> results)
    {
        Result? allOn = results.Where(result => result.Scenario == "всё включено").Select(result => (Result?)result).FirstOrDefault();
        Result? terrainBypass = results.Where(result => result.Scenario == "только обход terrain draw")
            .Select(result => (Result?)result).FirstOrDefault();
        Result? lightingBypass = results.Where(result => result.Scenario == "только обход lighting compute")
            .Select(result => (Result?)result).FirstOrDefault();
        Result? bothBypass = results.Where(result => result.Scenario == "обход terrain draw + lighting compute")
            .Select(result => (Result?)result).FirstOrDefault();
        if (allOn is null || terrainBypass is null || lightingBypass is null || bothBypass is null)
        {
            return;
        }

        double meanInteraction = bothBypass.Value.MeanMs - terrainBypass.Value.MeanMs -
                                 lightingBypass.Value.MeanMs + allOn.Value.MeanMs;
        report.AppendLine("Факторная диагностика bypass (Δ p50; последовательные, не парные окна):")
            .Append("  terrain draw bypass: ").Append((terrainBypass.Value.P50Ms - allOn.Value.P50Ms).ToString("+0.00;-0.00;0.00"))
            .AppendLine(" мс")
            .Append("  lighting compute bypass: ").Append((lightingBypass.Value.P50Ms - allOn.Value.P50Ms).ToString("+0.00;-0.00;0.00"))
            .AppendLine(" мс")
            .Append("  оба bypass: ").Append((bothBypass.Value.P50Ms - allOn.Value.P50Ms).ToString("+0.00;-0.00;0.00"))
            .AppendLine(" мс")
            .Append("  interaction среднего frame time (оба − terrain − lighting + all-on; только диагностика): ")
            .Append(meanInteraction.ToString("+0.00;-0.00;0.00"))
            .AppendLine(" мс");
    }

    private static double Percentile(double[] sorted, double fraction) =>
        sorted[Math.Clamp((int)Math.Round(fraction * (sorted.Length - 1)), 0, sorted.Length - 1)];

    private static IEnumerator Skip(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            yield return null;
        }
    }

    private static T ResolveInScene<T>(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (LifetimeScope scope in root.GetComponentsInChildren<LifetimeScope>(true))
            {
                if (scope.Container != null && scope.Container.TryResolve(out T resolved))
                {
                    return resolved;
                }
            }
        }

        throw new InvalidOperationException($"{typeof(T).Name} is not registered in {scene.name}.");
    }

    private static BootstrapLifetimeScope? FindBootstrap() =>
        Object.FindAnyObjectByType<BootstrapLifetimeScope>(FindObjectsInactive.Include);

    private static T? FindComponentInScene<T>(Scene scene)
        where T : Component
    {
        if (!scene.IsValid() || !scene.isLoaded)
        {
            return null;
        }

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T? component = root.GetComponentInChildren<T>(true);
            if (component != null)
            {
                return component;
            }
        }

        return null;
    }

    private static IEnumerator Await(UniTask task, float timeoutSeconds)
    {
        UniTask preserved = task.Preserve();
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (!preserved.Status.IsCompleted() && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        Assert.That(preserved.Status.IsCompleted(), Is.True, $"Operation timed out after {timeoutSeconds:F0}s.");
        preserved.GetAwaiter().GetResult();
    }

    private static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds, string failureMessage)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (condition())
            {
                yield break;
            }

            yield return null;
        }

        Assert.Fail(failureMessage);
    }
}

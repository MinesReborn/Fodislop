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
using Kern.Game;
using Kern.Networking;
using Kern.Networking.Auth;
using Kern.Player;
using Kern.Rendering;
using Kern.Rendering.PostProcessing;
using Kern.World;
using Kern.World.Terrain;
using Kern.World.Lighting;
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

    private static readonly string[] s_markers =
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
    private static readonly string[] s_GPUStageMarkers =
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
        "Kern.Lighting.DynamicPolar",
        "Kern.Lighting.DynamicReceiverTrace",
        "Kern.Lighting.DynamicCompose",
        "Kern.Lighting.Composite",
        "Kern.PostProcess.SceneComposite",
        "Kern.PostProcess.DisplayFinal",
        "Kern.PostProcess.BlitBack",
        "Kern.PostProcess.Bloom.Prefilter",
        "Kern.PostProcess.Bloom.Downsample",
        "Kern.PostProcess.Bloom.Upsample",
        "Kern.PostProcess.Bloom.UpsampleComposite",
        "Kern.PostProcess.Bloom.Add",
    ];

    private static readonly FrameTiming[] s_frameTimingBuffer = new FrameTiming[1];

    private sealed class RecorderLifetime(
        List<(string Name, bool IsGPUStage, ProfilerRecorder Recorder)> cpu,
        List<(string Name, ProfilerRecorder Recorder)> gpu) : IDisposable
    {
        public void Dispose()
        {
            foreach (var entry in cpu)
            {
                if (entry.Recorder.Valid)
                {
                    entry.Recorder.Dispose();
                }
            }

            foreach (var entry in gpu)
            {
                if (entry.Recorder.Valid)
                {
                    entry.Recorder.Dispose();
                }
            }
        }
    }

    private BootstrapLifetimeScope _bootstrap = null!;
    private string _originalClientToken = string.Empty;
    private HashSet<string> _originalDummyTokens = [];

    // Observation only: no JSON objects, file IO or strings are created in the sample loop.
    private readonly struct CaptureSample(IFrameTelemetry telemetry, long? allocatedBytes = null,
        PostProcessRendererFeature? postprocess = null)
    {
        public readonly PostProcessWorkloadSnapshot? SceneWork = CorrelatedWork(postprocess?.SceneWorkload);
        public readonly PostProcessWorkloadSnapshot? DisplayWork = CorrelatedWork(postprocess?.DisplayWorkload);
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
        public readonly float TerrainGPUUpload = telemetry.TerrainGPUUploadTimeMs;
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
                terrainGPUUpload = TerrainGPUUpload,
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

    private sealed class CaptureWindow(string scenario, int count, Camera? renderingCamera = null)
    {
        public readonly string Scenario = scenario;
        public readonly string CapturedAtUtc = DateTime.UtcNow.ToString("O");
        public readonly int Width = (renderingCamera ?? ResolveInScene<IGameplayCamera>(SceneManager.GetSceneByName("MainGame")).Camera).pixelWidth;
        public readonly int Height = (renderingCamera ?? ResolveInScene<IGameplayCamera>(SceneManager.GetSceneByName("MainGame")).Camera).pixelHeight;
        public readonly int NativeUIWidth = Screen.width;
        public readonly int NativeUIHeight = Screen.height;
        public readonly CaptureSample[] Samples = new CaptureSample[count];
        public readonly FrameTiming[] TimingObservations = new FrameTiming[count];
        public int TimingObservationCount;
        public string? QualityProfile;
        public CaptureSample Baseline;
        public double[] FrameMs = [];

        public void Write(string runDirectory)
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
                    observationEvidence = $"Coroutine observes previous frame before Terrain.LateUpdate; producer reset frame stamp and lifecycle validity exported; stamp alignment alone does not establish output commit/readiness or timing precision. Production output {Width}x{Height}; native UI {NativeUIWidth}x{NativeUIHeight}.",
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
            string path = Path.Combine(runDirectory, $"frame_capture_{captureId}.json");
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
            string workPath = Path.ChangeExtension(path, ".render-work.json");
            File.WriteAllText(workPath, JsonConvert.SerializeObject(new
            {
                captureId,
                scenario = Scenario,
                semantics = "Executed Kern postprocess passes only. CPU is command recording, GPU is unavailable. Texture bytes are created color-target payload, excluding imported resources, pooling and driver overhead. Missing or stale passes are null, never zero work.",
                frames = Samples.Select(sample => new
                {
                    frameId = sample.FrameId,
                    scene = WorkJson(sample.SceneWork),
                    display = WorkJson(sample.DisplayWork),
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
        public readonly double CPUFrameP50Ms;
        public readonly double CPUMainThreadP50Ms;
        public readonly double CPURenderThreadP50Ms;
        public readonly int FrameTimingSampleCount;
        public readonly double GPUP50Ms;
        public readonly int GPUSampleCount;
        public readonly Dictionary<string, double> Markers;
        public readonly Dictionary<string, double> GPUStageMarkers;
        public readonly Dictionary<string, int> GPUStageMarkerSampleCounts;
        public readonly Dictionary<string, double> PostprocessGPUTimes;
        public readonly Dictionary<string, int> PostprocessGPUCounts;

        public Result(string scenario, double meanMs, double p50Ms, double p95Ms, double p99Ms, double maxMs,
            double cpuFrameP50Ms, double cpuMainThreadP50Ms, double cpuRenderThreadP50Ms,
            int frameTimingSampleCount, double gpuP50Ms, int gpuSampleCount, Dictionary<string, double> markers,
            Dictionary<string, double> gpuStageMarkers, Dictionary<string, int> gpuStageMarkerSampleCounts,
            Dictionary<string, double> postprocessGPUTimes, Dictionary<string, int> postprocessGPUCounts)
        {
            Scenario = scenario;
            MeanMs = meanMs;
            P50Ms = p50Ms;
            P95Ms = p95Ms;
            P99Ms = p99Ms;
            MaxMs = maxMs;
            CPUFrameP50Ms = cpuFrameP50Ms;
            CPUMainThreadP50Ms = cpuMainThreadP50Ms;
            CPURenderThreadP50Ms = cpuRenderThreadP50Ms;
            FrameTimingSampleCount = frameTimingSampleCount;
            GPUP50Ms = gpuP50Ms;
            GPUSampleCount = gpuSampleCount;
            Markers = markers;
            GPUStageMarkers = gpuStageMarkers;
            GPUStageMarkerSampleCounts = gpuStageMarkerSampleCounts;
            PostprocessGPUTimes = postprocessGPUTimes;
            PostprocessGPUCounts = postprocessGPUCounts;
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
    [Timeout(120_000)]
    public IEnumerator MainGame_WorldBloomImage()
    {
        yield return Await(_bootstrap.TransitionAsync("MainMenu"), UITimeoutSeconds);
        yield return Await(_bootstrap.TransitionAsync("MainGame"), WorldTimeoutSeconds);
        Scene game = SceneManager.GetSceneByName("MainGame");
        TerrainRenderer terrain = FindComponentInScene<TerrainRenderer>(game)!;
        yield return WaitUntil(() => terrain.IsReadyForGameplay, WorldTimeoutSeconds, "Terrain not ready.");
        Camera screenCamera = ResolveInScene<IGameplayCamera>(game).Camera;
        ISceneObjectFactory objects = ResolveInScene<ISceneObjectFactory>(game);
        GameObject diagnosticObject = objects.Create("WorldBloomDiagnosticCamera");
        Camera camera = diagnosticObject.AddComponent<Camera>();
        camera.CopyFrom(screenCamera);
        camera.enabled = false;
        camera.transform.SetPositionAndRotation(screenCamera.transform.position, screenCamera.transform.rotation);
        camera.depth = screenCamera.depth - 1f;
        UniversalAdditionalCameraData sourceData = screenCamera.GetUniversalAdditionalCameraData();
        UniversalAdditionalCameraData diagnosticData = camera.GetUniversalAdditionalCameraData();
        diagnosticData.renderPostProcessing = sourceData.renderPostProcessing;
        diagnosticData.volumeLayerMask = sourceData.volumeLayerMask;
        Assert.That(diagnosticData.scriptableRenderer, Is.SameAs(sourceData.scriptableRenderer));
        int screenMask = screenCamera.cullingMask;
        const int FixtureLayer = 2;
        screenCamera.cullingMask &= ~(1 << FixtureLayer);
        camera.cullingMask |= 1 << FixtureLayer;
        CameraFollow follow = FindComponentInScene<CameraFollow>(game)!;
        LightingEngine lighting = ResolveInScene<LightingEngine>(game);
        LightingGeometryRegistry registry = ResolveInScene<LightingGeometryRegistry>(game);
        GraphicsSettingsController graphics = ResolveInScene<GraphicsSettingsController>(game);
        GraphicsPreset originalPreset = _bootstrap.Container.Resolve<IClientConfigManager>().Config.GraphicsPreset;
        graphics.SelectPreset(GraphicsPreset.Overdrive);
        yield return Skip(30);
        UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
        RenderTexture? originalTarget = camera.targetTexture;
        Camera? originalDiagnostic = PostProcessRuntimeState.DiagnosticOffscreenCamera;
        bool originalFollow = follow.enabled;
        bool originalDither = data.dithering;
        bool originalBypass = PostProcessRuntimeState.BypassPostProcessEffects;
        PostProcessDebugView originalDebugView = PostProcessRuntimeState.DebugView;
        float originalZoom = camera.orthographicSize;
        float originalAspect = camera.aspect;
        float originalTime = Time.timeScale;
        Vector3 originalPosition = camera.transform.position;
        var target = new RenderTexture(513, 511, 24, RenderTextureFormat.ARGBHalf)
        {
            name = "WorldBloomProductionOracle",
        };
        Assert.That(target.Create(), Is.True);
        GameObject fixture = objects.Create("WorldBloomProductionFixture");
        GameObject backdrop = objects.Create("WorldBloomBlackBackdrop");
        Texture2D texture = RuntimeTextureFactory.CreateRGBAFloatNoMip(32, 32, "WorldBloomHdrFixture",
            RuntimeTextureColorSpace.Linear, FilterMode.Point, TextureWrapMode.Clamp);
        Color[] pixels = Enumerable.Repeat(new Color(0f, 0f, 0f, 1f), 1024).ToArray();
        pixels[16 * 32 + 16] = new Color(64f, 64f, 64f, 1f);
        texture.SetPixels(pixels);
        texture.Apply(false);
        Shader surfaceShader = Shader.Find("Kern/World Surface")
            ?? throw new InvalidOperationException("Production world surface shader is missing.");
        var material = new Material(surfaceShader);
        material.EnableKeyword("KERN_SURFACE_TRANSIT");
        material.SetTexture("_BaseMap", texture);
        material.SetColor("_EmissionColor", Color.white);
        material.SetFloat("_EmissionStrength", 1f);
        material.SetFloat("_Occupancy", 0f);
        material.SetVector("_BaseMapTileCount", Vector4.one);
        Texture2D blackTexture = RuntimeTextureFactory.CreateRGBA32NoMip(1, 1, "WorldBloomBlackFixture",
            RuntimeTextureColorSpace.Linear, FilterMode.Point, TextureWrapMode.Clamp);
        blackTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 1f));
        blackTexture.Apply(false);
        var blackMaterial = new Material(material);
        blackMaterial.SetTexture("_BaseMap", blackTexture);
        Mesh sourceMesh = BloomFixtureQuad(1f);
        Mesh blackMesh = BloomFixtureQuad(32f);
        fixture.AddComponent<MeshFilter>().sharedMesh = sourceMesh;
        MeshRenderer renderer = fixture.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.sortingOrder = short.MaxValue;
        backdrop.AddComponent<MeshFilter>().sharedMesh = blackMesh;
        MeshRenderer blackRenderer = backdrop.AddComponent<MeshRenderer>();
        blackRenderer.sharedMaterial = blackMaterial;
        blackRenderer.sortingOrder = short.MaxValue - 1;
        fixture.layer = backdrop.layer = FixtureLayer;
        GameObject volumeObject = objects.Create("WorldBloomOracleVolume");
        int volumeMask = data.volumeLayerMask.value;
        int volumeLayer = 0;
        while (volumeLayer < 32 && (volumeMask & (1 << volumeLayer)) == 0) { volumeLayer++; }
        Assert.That(volumeLayer, Is.LessThan(32));
        volumeObject.layer = volumeLayer;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        Volume volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = float.MaxValue;
        volume.sharedProfile = profile;
        BloomComponent bloom = profile.Add<BloomComponent>();
        bloom.intensity.Override(1f);
        bloom.threshold.Override(1f);
        bloom.tint.Override(Color.white);
        profile.Add<VignetteComponent>().intensity.Override(0f);
        profile.Add<EigengrauComponent>().intensity.Override(0f);
        profile.Add<Tonemapping>().mode.Override(TonemappingMode.Neutral);
        ColorAdjustments exposure = profile.Add<ColorAdjustments>();
        exposure.postExposure.Override(PostProcessLook.Exposure.Stops);
        // The authored cell source is registered on the transport grid, while
        // its visible sprite contains one HDR pixel. Keep the transport mask
        // independent of whether a coarse texel center hits that sprite pixel.
        Texture2D emissionTexture = RuntimeTextureFactory.CreateRGBAFloatNoMip(1, 1, "WorldBloomEmissionFixture",
            RuntimeTextureColorSpace.Linear, FilterMode.Point, TextureWrapMode.Clamp);
        emissionTexture.SetPixel(0, 0, Color.white);
        emissionTexture.Apply(false);
        var emissionMaterial = new Material(material);
        emissionMaterial.SetTexture("_BaseMap", emissionTexture);
        var contributor = new BloomFixtureContributor(sourceMesh, material, fixture.transform)
        {
            FieldMaterial = emissionMaterial,
        };
        bool registered = false;
        string directory = DiagnosticArtifactPaths.CreateDirectory("Performance", "world_bloom_image");
        try
        {
            follow.enabled = false;
            data.dithering = false;
            camera.targetTexture = target;
            camera.enabled = true;
            camera.aspect = 259f / 257f;
            camera.orthographicSize = 257f / 64f;
            camera.transform.position = new Vector3(Mathf.Floor(originalPosition.x),
                Mathf.Floor(originalPosition.y), originalPosition.z);
            Time.timeScale = 0f;
            PostProcessRuntimeState.DiagnosticOffscreenCamera = camera;
            PostProcessRuntimeState.BypassPostProcessEffects = false;
            yield return Skip(30);
            float[] originalEmission = [];
            bool emissionRead = false;
            RenderTexture emission = lighting.GPUResources.Geometry.StaticEmission!;
            AsyncGPUReadback.Request(emission, 0, TextureFormat.RGBAFloat, readback =>
            {
                Assert.That(readback.hasError, Is.False);
                originalEmission = readback.GetData<float>().ToArray();
                emissionRead = true;
            });
            yield return WaitUntil(() => emissionRead, 10f, "Emission readback did not finish.");
            Vector4 field = lighting.WorldRect;
            Vector2Int sourceCell = default;
            bool found = false;
            for (int dy = -2; dy <= 2 && !found; dy++)
            {
                for (int dx = -2; dx <= 2 && !found; dx++)
                {
                    Vector2Int cell = new(Mathf.FloorToInt(camera.transform.position.x) + dx,
                        Mathf.FloorToInt(camera.transform.position.y) + dy);
                    int x = Mathf.FloorToInt((cell.x + 0.5f - field.x) / field.z * emission.width);
                    int y = Mathf.FloorToInt((cell.y + 0.5f - field.y) / field.w * emission.height);
                    if (SystemInfo.graphicsUVStartsAtTop) { y = emission.height - 1 - y; }
                    if (x < 0 || y < 0 || x >= emission.width || y >= emission.height) { continue; }
                    int index = (y * emission.width + x) * 4;
                    if (originalEmission[index] + originalEmission[index + 1] + originalEmission[index + 2] != 0f) { continue; }
                    sourceCell = cell;
                    found = true;
                }
            }
            Assert.That(found, Is.True, "Fixture requires a non-emissive production cell.");
            fixture.transform.position = new Vector3(sourceCell.x, sourceCell.y, 0f);
            backdrop.transform.position = camera.transform.position + new Vector3(-16f, -16f, 10f);
            registry.Register(contributor);
            registered = true;
            for (int scenario = 0; scenario < 4; scenario++)
            {
                contributor.Emits = scenario != 2;
                contributor.Revision++;
                if (scenario == 1)
                {
                    // Put the source pixel in the first visible column, next
                    // to the guarded frame edge; no synthetic texture blit.
                    camera.transform.position = new Vector3(sourceCell.x + 16.5f / 32f +
                        camera.orthographicSize * camera.aspect - 0.5f / 32f,
                        camera.transform.position.y, originalPosition.z);
                }
                if (scenario == 3)
                {
                    pixels[17 * 32 + 17] = new Color(32f, 16f, 8f, 1f);
                    pixels[15 * 32 + 15] = new Color(8f, 16f, 32f, 1f);
                    texture.SetPixels(pixels);
                    texture.Apply(false);
                }
                yield return Skip(30);
                Assert.That(screenCamera.targetTexture, Is.Null,
                    "The image fixture must keep the gameplay camera on the display.");
                Dictionary<string, float[]> images = new();
                Dictionary<string, Vector2Int> dimensions = new();
                int capturedFrame = -1;
                PostProcessRuntimeState.DiagnosticBloomImage = (cmd, stage, image, rect) =>
                {
                    if (capturedFrame < 0) { capturedFrame = Time.frameCount; }
                    if (capturedFrame != Time.frameCount) { return; }
                    dimensions[stage] = new Vector2Int(image.width, image.height);
                    cmd.RequestAsyncReadback(image, 0, TextureFormat.RGBAFloat, readback =>
                    {
                        Assert.That(readback.hasError, Is.False);
                        images[stage] = readback.GetData<float>().ToArray();
                    });
                    if (stage == "after") { PostProcessRuntimeState.DiagnosticBloomImage = null; }
                };
                yield return WaitUntil(() => images.Count == 3, 10f, "Production bloom observations did not finish.");
                float[] before = images["before"];
                float[] after = images["after"];
                float[] prefilter = images["prefilter"];
                File.WriteAllBytes(Path.Combine(directory, $"before_{scenario}.rgba32"),
                    System.Runtime.InteropServices.MemoryMarshal.AsBytes(before.AsSpan()).ToArray());
                File.WriteAllBytes(Path.Combine(directory, $"after_{scenario}.rgba32"),
                    System.Runtime.InteropServices.MemoryMarshal.AsBytes(after.AsSpan()).ToArray());
                File.WriteAllText(Path.Combine(directory, $"fixture_{scenario}.txt"),
                    $"camera={camera.transform.position}; source={fixture.transform.position}; " +
                    $"black={backdrop.transform.position}; sourceBounds={renderer.bounds}; blackBounds={blackRenderer.bounds}; " +
                    $"sourceLayer={renderer.sortingLayerName}/{renderer.sortingOrder}; blackLayer={blackRenderer.sortingLayerName}/{blackRenderer.sortingOrder}; " +
                    $"sourceActive={renderer.enabled}/{fixture.activeInHierarchy}; blackActive={blackRenderer.enabled}/{backdrop.activeInHierarchy}; " +
                    $"culling={camera.cullingMask}; field={lighting.WorldRect}; emits={contributor.Emits}");

                float peak = 0f;
                float bloomEnergy = 0f;
                float prefilterEnergy = 0f;
                float alphaError = 0f;
                bool finiteAndMonotone = true;
                for (int pixel = 0; pixel < before.Length; pixel += 4)
                {
                    peak = Mathf.Max(peak, before[pixel]);
                    alphaError = Mathf.Max(alphaError, Mathf.Abs(before[pixel + 3] - after[pixel + 3]));
                    for (int channel = 0; channel < 3; channel++)
                    {
                        finiteAndMonotone &= float.IsFinite(after[pixel + channel]) &&
                            after[pixel + channel] >= before[pixel + channel] - 0.002f;
                        bloomEnergy += after[pixel + channel] - before[pixel + channel];
                    }
                }
                for (int pixel = 0; pixel < prefilter.Length; pixel += 4)
                {
                    prefilterEnergy += prefilter[pixel] + prefilter[pixel + 1] + prefilter[pixel + 2];
                }
                File.AppendAllText(Path.Combine(directory, "oracle.txt"),
                    $"scenario={scenario}; world={dimensions["before"]}; quarter={dimensions["prefilter"]}; " +
                    $"peak={peak:R}; prefilterEnergy={prefilterEnergy:R}; bloomEnergy={bloomEnergy:R}; alphaError={alphaError:R}\n");
                Assert.That(peak, Is.GreaterThan(1f), "Production world lost scene HDR before bloom.");
                Assert.That(finiteAndMonotone, Is.True);
                Assert.That(alphaError, Is.EqualTo(0f), "Additive bloom modified scene alpha.");
                if (scenario == 0)
                {
                    Assert.That(prefilterEnergy, Is.EqualTo(11.25f).Within(0.01f),
                        "A 64-unit white pixel must contribute (64/4 - 1)/4 per RGB channel.");
                }
                if (scenario == 2)
                {
                    Assert.That(prefilterEnergy, Is.EqualTo(0f));
                    Assert.That(bloomEnergy, Is.EqualTo(0f));
                }
                else
                {
                    Assert.That(prefilterEnergy, Is.GreaterThan(0f), "Quarter prefilter lost a source pixel.");
                    Assert.That(bloomEnergy, Is.GreaterThan(0f), "Production additive bloom added no light.");
                }
            }
            // Real Renderer2D + native grading + DisplayFinal + FinalBlit.
            // Independent disabled-effect expectations: white vignette mask,
            // neutral signed-grain display, and zero bloom without emission.
            contributor.Emits = false;
            contributor.Revision++;
            PostProcessDebugView[] layerViews =
                [PostProcessDebugView.Vignette, PostProcessDebugView.FilmGrain, PostProcessDebugView.Bloom];
            float[] layerExpected = [1f, 0.5f, 0f];
            for (int layer = 0; layer < layerViews.Length; layer++)
            {
                PostProcessRuntimeState.DebugView = layerViews[layer];
                yield return Skip(10);
                bool received = false;
                float maximumError = 0f;
                float expected = layerExpected[layer];
                AsyncGPUReadback.Request(target, 0, TextureFormat.RGBAFloat, readback =>
                {
                    Assert.That(readback.hasError, Is.False);
                    var values = readback.GetData<float>();
                    for (int pixel = 0; pixel < values.Length; pixel += 4)
                    {
                        for (int channel = 0; channel < 3; channel++)
                        {
                            maximumError = Mathf.Max(maximumError, Mathf.Abs(values[pixel + channel] - expected));
                        }
                    }
                    received = true;
                });
                yield return WaitUntil(() => received, 10f, "Postprocess debug layer readback did not finish.");
                File.AppendAllText(Path.Combine(directory, "oracle.txt"),
                    $"debugLayer={layerViews[layer]}; expected={expected:R}; maxError={maximumError:R}\n");
                Assert.That(maximumError, Is.LessThan(0.002f), $"Incorrect {layerViews[layer]} production debug layer.");
            }
            PostProcessRuntimeState.DebugView = PostProcessDebugView.None;
            // Observe native URP exposure after its grading pass, with the
            // production DisplayFinal observer and no custom curve enabled.
            bloom.intensity.Override(0f);
            float[] exposurePeaks = new float[2];
            for (int stops = 0; stops < 2; stops++)
            {
                Color[] exposurePixels = new Color[32 * 32];
                Array.Fill(exposurePixels, new Color(0f, 0f, 0f, 1f));
                exposurePixels[16 * 32 + 16] = new Color(0.25f * (stops + 1), 0f, 0f, 1f);
                texture.SetPixels(exposurePixels);
                texture.Apply(false);
                contributor.Revision++;
                yield return Skip(10);
                bool received = false;
                int captured = stops;
                PostProcessRuntimeState.DiagnosticWorldImage = (cmd, image, rect) =>
                {
                    PostProcessRuntimeState.DiagnosticWorldImage = null;
                    cmd.RequestAsyncReadback(image, 0, TextureFormat.RGBAFloat, readback =>
                    {
                        Assert.That(readback.hasError, Is.False);
                        var values = readback.GetData<float>();
                        for (int index = 0; index < values.Length; index += 4)
                        {
                            exposurePeaks[captured] = Mathf.Max(exposurePeaks[captured], values[index]);
                        }
                        received = true;
                    });
                };
                yield return WaitUntil(() => received, 10f, "Native URP exposure observation did not finish.");
            }
            File.AppendAllText(Path.Combine(directory, "oracle.txt"),
                $"nativeExposure0={exposurePeaks[0]:R}; nativeExposure1={exposurePeaks[1]:R}\n");
            // Independent literal Neutral curve: input is exposed once by the
            // production calibration volume (+1 stop), before native tonemapping.
            static double Neutral(double x)
            {
                static double Curve(double v) => ((v * (0.2 * v + 0.24 * 0.29) + 0.272 * 0.02) /
                    (v * (0.2 * v + 0.29) + 0.272 * 0.3)) - 0.02 / 0.3;
                double whiteScale = 1.0 / Curve(5.3);
                return Curve(x * whiteScale) * whiteScale;
            }
            Assert.That(exposurePeaks[0], Is.EqualTo(Neutral(0.5)).Within(0.005));
            Assert.That(exposurePeaks[1], Is.EqualTo(Neutral(1.0)).Within(0.005),
                "Native exposure and Neutral tonemapping must each be applied exactly once.");
        }
        finally
        {
            PostProcessRuntimeState.DiagnosticBloomImage = null;
            PostProcessRuntimeState.DiagnosticWorldImage = null;
            PostProcessRuntimeState.DebugView = originalDebugView;
            if (registered) { registry.Unregister(contributor); }
            camera.targetTexture = originalTarget;
            camera.orthographicSize = originalZoom;
            camera.aspect = originalAspect;
            camera.transform.position = originalPosition;
            follow.enabled = originalFollow;
            data.dithering = originalDither;
            Time.timeScale = originalTime;
            PostProcessRuntimeState.DiagnosticOffscreenCamera = originalDiagnostic;
            PostProcessRuntimeState.BypassPostProcessEffects = originalBypass;
            screenCamera.cullingMask = screenMask;
            Object.Destroy(diagnosticObject);
            Object.Destroy(fixture);
            Object.Destroy(backdrop);
            Object.Destroy(volumeObject);
            Object.Destroy(profile);
            Object.Destroy(sourceMesh);
            Object.Destroy(blackMesh);
            Object.Destroy(material);
            Object.Destroy(blackMaterial);
            Object.Destroy(emissionMaterial);
            Object.Destroy(emissionTexture);
            Object.Destroy(blackTexture);
            Object.Destroy(texture);
            target.Release();
            Object.Destroy(target);
            graphics.SelectPreset(originalPreset);
        }
    }

    private static Mesh BloomFixtureQuad(float size)
    {
        var mesh = new Mesh
        {
            vertices = [Vector3.zero, new(size, 0f, 0f), new(size, size, 0f), new(0f, size, 0f)],
            uv = [Vector2.zero, Vector2.right, Vector2.one, Vector2.up],
            triangles = [0, 1, 2, 0, 2, 3],
        };
        mesh.SetUVs(1, new List<Vector4> { Vector4.one, Vector4.one, Vector4.one, Vector4.one });
        mesh.RecalculateBounds();
        return mesh;
    }

    private sealed class BloomFixtureContributor(Mesh mesh, Material material, Transform transform)
        : Kern.Core.Interfaces.WorldLighting.ILightingGeometryContributor
    {
        public bool Emits = true;
        public Material FieldMaterial { get; set; } = material;
        public ulong Revision = 1;
        public ulong LightingGeometryRevision => Revision;

        public void RenderMaterialEmissionFields(CommandBuffer cmd,
            in Kern.Core.Interfaces.WorldLighting.LightingMaterialEmissionContext context)
        {
            if (Emits)
            {
                cmd.DrawMesh(mesh, transform.localToWorldMatrix, FieldMaterial, 0,
                    FieldMaterial.FindPass("LightingMaterialField"));
            }
        }

        public void RenderAmbientOcclusionField(CommandBuffer cmd,
            in Kern.Core.Interfaces.WorldLighting.LightingAmbientOcclusionContext context)
        {
        }
    }

    [UnityTest]
    [Timeout(900_000)]
    public IEnumerator MainGame_RenderPathBeforeAfter()
    {
        yield return Await(_bootstrap.TransitionAsync("MainMenu"), UITimeoutSeconds);
        yield return Await(_bootstrap.TransitionAsync("MainGame"), WorldTimeoutSeconds);
        Scene game = SceneManager.GetSceneByName("MainGame");
        TerrainRenderer terrain = FindComponentInScene<TerrainRenderer>(game)!;
        yield return WaitUntil(() => terrain.IsReadyForGameplay, WorldTimeoutSeconds, "Terrain not ready.");
        Camera camera = ResolveInScene<IGameplayCamera>(game).Camera;
        CameraFollow follow = FindComponentInScene<CameraFollow>(game)!;
        LightingEngine lighting = ResolveInScene<LightingEngine>(game);
        LightingGeometryRegistry registry = ResolveInScene<LightingGeometryRegistry>(game);
        GraphicsSettingsController graphics = ResolveInScene<GraphicsSettingsController>(game);
        PostProcessController post = ResolveInScene<PostProcessController>(game);
        IFrameTelemetry telemetry = ResolveInScene<IFrameTelemetry>(game);
        GraphicsPreset originalPreset = _bootstrap.Container.Resolve<IClientConfigManager>().Config.GraphicsPreset;
        RenderTexture? originalTarget = camera.targetTexture;
        Camera? originalDiagnostic = PostProcessRuntimeState.DiagnosticOffscreenCamera;
        bool originalFollow = follow.enabled;
        bool originalFull = PostProcessRuntimeState.DiagnosticFullResolutionWorld;
        bool originalDense = LightingUpdateCoordinator.DiagnosticForceDenseReanchor;
        bool originalBypass = PostProcessRuntimeState.BypassPostProcessEffects;
        float originalZoom = camera.orthographicSize;
        float originalBloom = post.BloomIntensity;
        Vector3 origin = camera.transform.position;
        const int MovingLight = int.MinValue + 824;
        Assert.That(originalTarget, Is.Null, "Native-output benchmark requires the gameplay camera to render to the display.");
        using var resolution = new NativeBenchmarkResolution(3420, 2148);
        ISceneObjectFactory objects = ResolveInScene<ISceneObjectFactory>(game);
        GameObject fixture = objects.Create("RenderPathEmissionFixture");
        Texture2D texture = RuntimeTextureFactory.CreateRGBAFloatNoMip(64, 64, "RenderPathHdrFixture",
            RuntimeTextureColorSpace.Linear, FilterMode.Point, TextureWrapMode.Clamp);
        var material = new Material(Shader.Find("Kern/World Surface"));
        material.EnableKeyword("KERN_SURFACE_TRANSIT");
        material.SetTexture("_BaseMap", texture);
        material.SetColor("_EmissionColor", Color.white);
        material.SetFloat("_EmissionStrength", 1f);
        material.SetFloat("_Occupancy", 0f);
        material.SetVector("_BaseMapTileCount", Vector4.one);
        Mesh mesh = BloomFixtureQuad(64f);
        fixture.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = fixture.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.sortingOrder = 100;
        fixture.transform.position = new Vector3(Mathf.Floor(origin.x) - 32f, Mathf.Floor(origin.y) - 32f, 0f);
        var contributor = new BloomFixtureContributor(mesh, material, fixture.transform);
        registry.Register(contributor);
        List<Result> results = [];
        List<CaptureWindow> captures = [];
        string directory = DiagnosticArtifactPaths.CreateDirectory("Performance", "render_path_ab");
        try
        {
            graphics.SelectPreset(GraphicsPreset.Overdrive);
            yield return Skip(30);
            Camera outputCamera = PostProcessRuntimeState.DiagnosticOffscreenCamera ?? camera;
            Assert.That(outputCamera.pixelWidth, Is.EqualTo(3420));
            Assert.That(outputCamera.pixelHeight, Is.EqualTo(2148));
            TestContext.WriteLine($"productionOutput={outputCamera.pixelWidth}x{outputCamera.pixelHeight}; " +
                $"batch={Application.isBatchMode}; nativeUI={Screen.width}x{Screen.height}");
            PostProcessRuntimeState.DiagnosticOffscreenCamera = camera;
            PostProcessRuntimeState.BypassPostProcessEffects = false;
            follow.enabled = false;
            float[] zooms = [ProjectRuntimeContracts.Camera.MinimumOrthographicSize, originalZoom,
                ProjectRuntimeContracts.Camera.MaximumOrthographicSize];
            for (int repetition = 0; repetition < 3; repetition++)
            {
                for (int zoomIndex = 0; zoomIndex < zooms.Length; zoomIndex++)
                {
                    camera.orthographicSize = zooms[zoomIndex];
                    for (int density = 0; density < 2; density++)
                    {
                        Color[] emitters = new Color[64 * 64];
                        for (int y = 0; y < 64; y++)
                        {
                            for (int x = 0; x < 64; x++)
                            {
                                bool source = density == 0 ? x == 32 && y == 32 : (x + y) % 4 == 0;
                                emitters[y * 64 + x] = source ? new Color(8f, 4f, 2f, 1f) : Color.clear;
                            }
                        }
                        texture.SetPixels(emitters);
                        texture.Apply(false);
                        contributor.Revision++;
                        for (int moving = 0; moving < 2; moving++)
                        {
                            // Alternate reference order across repetitions to
                            // avoid attributing a monotonic drift to the path.
                            for (int order = 0; order < 2; order++)
                            {
                                bool reference = (order + repetition) % 2 == 0;
                                PostProcessRuntimeState.DiagnosticFullResolutionWorld = reference;
                                LightingUpdateCoordinator.DiagnosticForceDenseReanchor = reference;
                                post.BloomIntensity = 1f;
                                camera.transform.position = origin;
                                lighting.InvalidateRadiance();
                                Action<int> advance = frame =>
                                {
                                    float shift = moving == 0 ? 0f : frame * 0.125f;
                                    camera.transform.position = origin + new Vector3(shift, 0f, 0f);
                                    lighting.SetDynamicLight(MovingLight,
                                        new Vector2(origin.x + shift, origin.y), new Color(1f, 0.7f, 0.3f), 8f);
                                };
                                for (int warmup = -WarmupFrames; warmup < 0; warmup++)
                                {
                                    advance(warmup);
                                    yield return null;
                                }
                                string scenario = $"rep-{repetition}/zoom-{zoomIndex}/density-{density}/moving-{moving}/{(reference ? "reference" : "world32")}";
                                yield return Measure(scenario, results, telemetry, captures, scenario, advance);
                                captures[^1].Write(directory);
                                File.WriteAllText(Path.Combine(directory, "results.json"), JsonConvert.SerializeObject(results, Formatting.Indented));
                                File.WriteAllText(Path.Combine(directory, "progress.txt"),
                                    $"completed={results.Count}/78; last={scenario}; p50={results[^1].P50Ms:R}; p95={results[^1].P95Ms:R}");
                            }
                        }
                    }
                }
                // Same normal-zoom stationary dense scene, bloom off/on cost.
                PostProcessRuntimeState.DiagnosticFullResolutionWorld = false;
                LightingUpdateCoordinator.DiagnosticForceDenseReanchor = false;
                camera.orthographicSize = originalZoom;
                camera.transform.position = origin;
                lighting.SetDynamicLight(MovingLight, new Vector2(origin.x, origin.y), new Color(1f, 0.7f, 0.3f), 8f);
                for (int enabled = 0; enabled < 2; enabled++)
                {
                    post.BloomIntensity = enabled;
                    yield return Skip(WarmupFrames);
                    string scenario = $"rep-{repetition}/bloom-{enabled}/world32";
                    yield return Measure(scenario, results, telemetry, captures, scenario);
                    captures[^1].Write(directory);
                    File.WriteAllText(Path.Combine(directory, "results.json"), JsonConvert.SerializeObject(results, Formatting.Indented));
                }
            }
            foreach (CaptureWindow capture in captures) { capture.Write(directory); }
            File.WriteAllText(Path.Combine(directory, "report.txt"), BuildReport(results, captures));
            File.WriteAllText(Path.Combine(directory, "results.json"), JsonConvert.SerializeObject(results, Formatting.Indented));
            Assert.That(results.Count, Is.EqualTo(78));
        }
        finally
        {
            registry.Unregister(contributor);
            lighting.RemoveDynamicLight(MovingLight);
            camera.targetTexture = originalTarget;
            camera.orthographicSize = originalZoom;
            camera.transform.position = origin;
            follow.enabled = originalFollow;
            post.BloomIntensity = originalBloom;
            PostProcessRuntimeState.DiagnosticOffscreenCamera = originalDiagnostic;
            PostProcessRuntimeState.DiagnosticFullResolutionWorld = originalFull;
            PostProcessRuntimeState.BypassPostProcessEffects = originalBypass;
            LightingUpdateCoordinator.DiagnosticForceDenseReanchor = originalDense;
            Object.Destroy(fixture);
            Object.Destroy(mesh);
            Object.Destroy(material);
            Object.Destroy(texture);

            graphics.SelectPreset(originalPreset);
        }
    }

    // Own only a temporary Game View size entry. Rendering stays on the native
    // camera/output/UI path; directing gameplay to an offscreen target leaves
    // the display without its scene clear and makes overlay UI accumulate.
    internal sealed class DiagnosticCameraScope : IDisposable
    {
        private readonly Camera _gameplay;
        private readonly Vector3 _position;
        private readonly Quaternion _rotation;
        private readonly float _zoom;
        private readonly Camera? _previousDiagnostic;
        private readonly GameObject _owner;
        public Camera Camera { get; }

        public DiagnosticCameraScope(Camera gameplay, ISceneObjectFactory objects)
        {
            Assert.That(gameplay.targetTexture, Is.Null,
                "An image test must not redirect the gameplay camera away from the display.");
            _gameplay = gameplay;
            _position = gameplay.transform.position;
            _rotation = gameplay.transform.rotation;
            _zoom = gameplay.orthographicSize;
            _previousDiagnostic = PostProcessRuntimeState.DiagnosticOffscreenCamera;
            _owner = objects.Create("ProductionImageDiagnosticCamera");
            Camera = _owner.AddComponent<Camera>();
            Camera.CopyFrom(gameplay);
            Camera.enabled = false;
            Camera.transform.SetPositionAndRotation(_position, _rotation);
            Camera.depth = gameplay.depth - 1f;
            UniversalAdditionalCameraData source = gameplay.GetUniversalAdditionalCameraData();
            UniversalAdditionalCameraData data = Camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = source.renderPostProcessing;
            data.volumeLayerMask = source.volumeLayerMask;
            data.dithering = source.dithering;
            Assert.That(data.scriptableRenderer, Is.SameAs(source.scriptableRenderer));
            PostProcessRuntimeState.DiagnosticOffscreenCamera = Camera;
        }

        // Reanchor proofs must still move the authoritative gameplay viewport.
        // UI/input use its native aspect and projection throughout the test.
        public void ApplyPoseToGameplay()
        {
            _gameplay.transform.SetPositionAndRotation(Camera.transform.position, Camera.transform.rotation);
            _gameplay.orthographicSize = Camera.orthographicSize;
        }

        public void Dispose()
        {
            PostProcessRuntimeState.DiagnosticOffscreenCamera = _previousDiagnostic;
            if (_gameplay != null)
            {
                _gameplay.transform.SetPositionAndRotation(_position, _rotation);
                _gameplay.orthographicSize = _zoom;
            }
            Object.Destroy(_owner);
        }
    }

    internal sealed class NativeBenchmarkResolution : IDisposable
    {
#if UNITY_EDITOR
        private readonly UnityEditor.EditorWindow _view = null!;
        private readonly System.Reflection.PropertyInfo _selection = null!;
        private readonly object _group = null!;
        private readonly System.Reflection.MethodInfo _remove = null!;
        private readonly int _previousSelection;
        private readonly int _addedSelectionIndex;
        private readonly int _batchOriginalWidth;
        private readonly int _batchOriginalHeight;
        private bool _disposed;
#endif
        public NativeBenchmarkResolution(int width, int height)
        {
#if UNITY_EDITOR
            if (Application.isBatchMode)
            {
                _batchOriginalWidth = Screen.width;
                _batchOriginalHeight = Screen.height;
                DisplayManager.SetTransientResolution(width, height, FullScreenMode.Windowed);
                return;
            }
            const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            System.Reflection.Assembly editor = typeof(UnityEditor.EditorWindow).Assembly;
            Type viewType = editor.GetType("UnityEditor.GameView", throwOnError: true)!;
            _view = Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>().Single(window => window.GetType() == viewType);
            _selection = viewType.GetProperty("selectedSizeIndex", Flags)
                ?? throw new InvalidOperationException("Game View size selection API is unavailable.");
            _previousSelection = (int)_selection.GetValue(_view)!;
            Type sizesType = editor.GetType("UnityEditor.GameViewSizes", throwOnError: true)!;
            Type singleton = typeof(UnityEditor.ScriptableSingleton<>).MakeGenericType(sizesType);
            object sizes = singleton.GetProperty("instance", System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Static)!.GetValue(null)!;
            Type groupKind = editor.GetType("UnityEditor.GameViewSizeGroupType", throwOnError: true)!;
            _group = sizesType.GetMethod("GetGroup", Flags)!.Invoke(sizes,
                [Enum.Parse(groupKind, "Standalone")])!;
            Type groupType = _group.GetType();
            Type sizeType = editor.GetType("UnityEditor.GameViewSize", throwOnError: true)!;
            Type sizeKind = editor.GetType("UnityEditor.GameViewSizeType", throwOnError: true)!;
            object size = Activator.CreateInstance(sizeType, Flags, binder: null,
                args: [Enum.Parse(sizeKind, "FixedResolution"), width, height, "Kern temporary native benchmark"], culture: null)!;
            _remove = groupType.GetMethod("RemoveCustomSize", Flags)!;
            groupType.GetMethod("AddCustomSize", Flags)!.Invoke(_group, [size]);
            int total = (int)groupType.GetMethod("GetTotalCount", Flags)!.Invoke(_group, null)!;
            // RemoveCustomSize takes an index in the complete list, including
            // built-in sizes; a custom-list offset removes the wrong entry.
            _addedSelectionIndex = total - 1;
            _selection.SetValue(_view, _addedSelectionIndex);
            _view.Repaint();
            UnityEditor.EditorApplication.playModeStateChanged += RestoreAfterInterruptedRun;
#else
            if (Screen.width != width || Screen.height != height)
            {
                throw new InvalidOperationException("Native benchmark output does not match the required resolution.");
            }
#endif
        }

#if UNITY_EDITOR
        private void RestoreAfterInterruptedRun(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.EnteredEditMode)
            {
                Dispose();
            }
        }
#endif

        public void Dispose()
        {
#if UNITY_EDITOR
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            if (Application.isBatchMode)
            {
                DisplayManager.SetTransientResolution(_batchOriginalWidth, _batchOriginalHeight, FullScreenMode.Windowed);
                return;
            }
            UnityEditor.EditorApplication.playModeStateChanged -= RestoreAfterInterruptedRun;
            _selection.SetValue(_view, _previousSelection);
            _remove.Invoke(_group, [_addedSelectionIndex]);
            _view.Repaint();
#endif
        }
    }

    [UnityTest]
    [Timeout(120_000)]
    public IEnumerator MainGame_WorldGridZoomImage()
    {
        IClientConfigManager config = _bootstrap.Container.Resolve<IClientConfigManager>();
        GraphicsPreset originalPreset = config.Config.GraphicsPreset;
        yield return Await(_bootstrap.TransitionAsync("MainMenu"), UITimeoutSeconds);
        config.SelectGraphicsPreset(GraphicsPreset.Standard);
        yield return Await(_bootstrap.TransitionAsync("MainGame"), WorldTimeoutSeconds);
        Scene game = SceneManager.GetSceneByName("MainGame");
        TerrainRenderer terrain = FindComponentInScene<TerrainRenderer>(game)
            ?? throw new InvalidOperationException("MainGame has no terrain.");
        yield return WaitUntil(() => terrain.IsReadyForGameplay, WorldTimeoutSeconds, "Terrain not ready.");
        Camera screenCamera = ResolveInScene<IGameplayCamera>(game).Camera;
        using var diagnosticCamera = new DiagnosticCameraScope(screenCamera, ResolveInScene<ISceneObjectFactory>(game));
        Camera camera = diagnosticCamera.Camera;
        CameraFollow follow = FindComponentInScene<CameraFollow>(game)
            ?? throw new InvalidOperationException("MainGame has no camera follow.");
        UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
        var target = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGBHalf)
        {
            name = "WorldGridProductionZoomOracle",
            filterMode = FilterMode.Point,
        };
        Assert.That(target.Create(), Is.True);
        Texture2D readback = RuntimeTextureFactory.CreateRGBAHalfNoMip(1024, 1024, "WorldGridZoomReadback",
            RuntimeTextureColorSpace.Linear, FilterMode.Point, TextureWrapMode.Clamp);
        RenderTexture? originalTarget = camera.targetTexture;
        Camera? originalDiagnostic = PostProcessRuntimeState.DiagnosticOffscreenCamera;
        bool originalBypass = PostProcessRuntimeState.BypassPostProcessEffects;
        bool originalFollow = follow.enabled;
        bool originalDither = cameraData.dithering;
        float originalTimeScale = Time.timeScale;
        float originalZoom = camera.orthographicSize;
        Vector3 originalPosition = camera.transform.position;
        PostProcessController postprocessController = ResolveInScene<PostProcessController>(game);
        float originalBloomIntensity = postprocessController.BloomIntensity;
        string directory = DiagnosticArtifactPaths.CreateDirectory("Performance", "world_grid_zoom");
        LightingEngine zoomLighting = ResolveInScene<LightingEngine>(game);
        const int ZoomProbeId = int.MinValue + 826;
        Robot[] frozenRobots = Object.FindObjectsByType<Robot>();
        bool[] robotEnabled = frozenRobots.Select(robot => robot.enabled).ToArray();
        try
        {
            camera.targetTexture = target;
            camera.enabled = true;
            PostProcessRuntimeState.DiagnosticOffscreenCamera = camera;
            PostProcessRuntimeState.BypassPostProcessEffects = true;
            follow.enabled = false;
            cameraData.dithering = false;
            Time.timeScale = 0f;
            foreach (Robot robot in frozenRobots)
            {
                robot.enabled = false;
            }
            // Integer world bounds make the independent oracle exact: at size 8
            // one world raster pixel occupies 2x2 output pixels; at size 16, 1x1.
            camera.transform.position = new Vector3(Mathf.Floor(originalPosition.x),
                Mathf.Floor(originalPosition.y), originalPosition.z);
            camera.orthographicSize = 8f;
            diagnosticCamera.ApplyPoseToGameplay();
            yield return Skip(5);
            ResolveInScene<GraphicsSettingsController>(game).SelectPreset(GraphicsPreset.Overdrive);
            zoomLighting.SetDynamicLight(ZoomProbeId,
                new Vector2(originalPosition.x, originalPosition.y), Color.white, 8f);
            Color[][] images = new Color[3][];
            for (int index = 0; index < images.Length; index++)
            {
                camera.orthographicSize = index == 1 ? 16f : 8f;
                diagnosticCamera.ApplyPoseToGameplay();
                yield return Skip(60);
                Assert.That(zoomLighting.UploadedDynamicLightCount, Is.GreaterThan(0),
                    "Zoom oracle must include actual dynamic illumination.");
                Assert.That(cameraData.dithering, Is.False);
                bool receivedWorldImage = false;
                int capturedIndex = index;
                PostProcessRuntimeState.DiagnosticWorldImage = (cmd, worldTexture, worldRect) =>
                {
                    PostProcessRuntimeState.DiagnosticWorldImage = null;
                    int worldWidth = worldTexture.width;
                    int worldHeight = worldTexture.height;
                    Texture lightingTexture = Shader.GetGlobalTexture("_WorldLightTexture");
                    Vector4 lightingRect = Shader.GetGlobalVector("_WorldLightRect");
                    string metadata = $"rect={worldRect}; dimensions={worldWidth}x{worldHeight}; " +
                        $"lightRect={lightingRect}; lightDimensions={lightingTexture.width}x{lightingTexture.height}; " +
                        $"aspect={camera.aspect:R}; AA={cameraData.antialiasing}; bypass={PostProcessRuntimeState.BypassPostProcessEffects}; " +
                        $"screen={Shader.GetGlobalVector("_ScreenParams")}";
                    LightingEngine lighting = ResolveInScene<LightingEngine>(game);
                    metadata += $"; solve={lighting.SolveCount}; sources={lighting.UploadedDynamicLightCount}; " +
                        $"receiverRect={lighting.DynamicReceiverRect}";
                    File.WriteAllText(Path.Combine(directory, $"world_{capturedIndex}.txt"), metadata);
                    cmd.RequestAsyncReadback(lightingTexture, 0, TextureFormat.RGBAFloat, request =>
                    {
                        Assert.That(request.hasError, Is.False, "Production light field readback failed.");
                        File.WriteAllBytes(Path.Combine(directory, $"light_{capturedIndex}.rgba32"), request.GetData<byte>().ToArray());
                    });
                    cmd.RequestAsyncReadback(worldTexture, 0, TextureFormat.RGBAFloat, request =>
                    {
                        Assert.That(request.hasError, Is.False, "Production world readback failed.");
                        var values = request.GetData<byte>();
                        File.WriteAllBytes(Path.Combine(directory, $"world_{capturedIndex}.rgba32"), values.ToArray());
                        receivedWorldImage = true;
                    });
                };
                yield return WaitUntil(() => receivedWorldImage, UITimeoutSeconds, "World image readback did not complete.");
                RenderTexture previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0);
                    readback.Apply();
                    images[index] = readback.GetPixels();
                    File.WriteAllBytes(Path.Combine(directory, $"zoom_{index}.png"), readback.EncodeToPNG());
                    File.WriteAllBytes(Path.Combine(directory, $"zoom_{index}.rgba16"), readback.GetRawTextureData<byte>().ToArray());
                }
                finally
                {
                    RenderTexture.active = previous;
                }
            }

            TestContext.WriteLine($"Camera aspect={camera.aspect:R}; AA={cameraData.antialiasing}; bypass={PostProcessRuntimeState.BypassPostProcessEffects}; output={camera.pixelWidth}x{camera.pixelHeight}");
            float blockError = 0f;
            float roundTripError = 0f;
            float overlapError = 0f;
            int nonconstantBlocks = 0;
            for (int y = 0; y < 1024; y += 2)
            {
                for (int x = 0; x < 1024; x += 2)
                {
                    Color pixel = images[0][y * 1024 + x];
                    float error = 0f;
                    for (int dy = 0; dy < 2; dy++)
                    {
                        for (int dx = 0; dx < 2; dx++)
                        {
                            int address = (y + dy) * 1024 + x + dx;
                            for (int channel = 0; channel < 3; channel++)
                            {
                                error = Mathf.Max(error, Mathf.Abs(pixel[channel] - images[0][address][channel]));
                                roundTripError = Mathf.Max(roundTripError,
                                    Mathf.Abs(images[0][address][channel] - images[2][address][channel]));
                            }
                        }
                    }

                    blockError = Mathf.Max(blockError, error);
                    if (error > 0.002f)
                    {
                        nonconstantBlocks++;
                    }

                    int farAddress = (256 + y / 2) * 1024 + 256 + x / 2;
                    for (int channel = 0; channel < 3; channel++)
                    {
                        overlapError = Mathf.Max(overlapError, Mathf.Abs(pixel[channel] - images[1][farAddress][channel]));
                    }
                }
            }

            string report = $"blockError={blockError:R}; nonconstantBlocks={nonconstantBlocks}; " +
                $"roundTripError={roundTripError:R}; overlapError={overlapError:R}";
            File.WriteAllText(Path.Combine(directory, "image-oracle.txt"), report);
            TestContext.WriteLine(report);
            Assert.That(blockError, Is.LessThanOrEqualTo(0.002f), "World raster does not form 2x2 point-scaled blocks.");
            Assert.That(roundTripError, Is.LessThanOrEqualTo(0.002f), "Zoom round-trip changes frozen world image.");
            Assert.That(overlapError, Is.LessThanOrEqualTo(0.01f), "Identical world pixel changes across zoom.");

            postprocessController.BloomIntensity = PostProcessLook.Bloom.Intensity;
            // The normal artistic path must preserve world-pixel blocks too.
            // Frame-index grain may change between frames, but not inside a block.
            PostProcessRuntimeState.BypassPostProcessEffects = false;
            yield return Skip(30);
            RenderTexture previousOutput = RenderTexture.active;
            Color[] artistic;
            try
            {
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0);
                readback.Apply();
                artistic = readback.GetPixels();
                File.WriteAllBytes(Path.Combine(directory, "artistic.png"), readback.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previousOutput;
            }

            float artisticBlockError = 0f;
            for (int y = 0; y < 1024; y += 2)
            {
                for (int x = 0; x < 1024; x += 2)
                {
                    for (int dy = 0; dy < 2; dy++)
                    {
                        for (int dx = 0; dx < 2; dx++)
                        {
                            for (int channel = 0; channel < 3; channel++)
                            {
                                artisticBlockError = Mathf.Max(artisticBlockError,
                                    Mathf.Abs(artistic[y * 1024 + x][channel] - artistic[(y + dy) * 1024 + x + dx][channel]));
                            }
                        }
                    }
                }
            }
            File.AppendAllText(Path.Combine(directory, "image-oracle.txt"), $"; artisticBlockError={artisticBlockError:R}");
            Assert.That(artisticBlockError, Is.LessThanOrEqualTo(0.002f), "Artistic post processing breaks the world lattice.");
            PostProcessRendererFeature feature = Resources.FindObjectsOfTypeAll<PostProcessRendererFeature>()
                .Single(item => item.RendersCamera(camera));
            PostProcessWorkloadSnapshot work = feature.SceneWorkload
                ?? throw new InvalidOperationException("Production bloom did not publish its workload.");
            Assert.That(work.FrameId, Is.GreaterThanOrEqualTo(Time.frameCount - 2));
            Assert.That(work.Width, Is.EqualTo(514));
            Assert.That(work.Height, Is.EqualTo(514));
            Assert.That(work.DispatchCount, Is.EqualTo(7));
            Assert.That(work.DrawCount, Is.EqualTo(1));
            Assert.That(work.TextureCount, Is.EqualTo(7));
            Assert.That(feature.DisplayWorkload!.Value.Width, Is.EqualTo(514));
            Assert.That(feature.DisplayWorkload!.Value.Height, Is.EqualTo(514));
            File.AppendAllText(Path.Combine(directory, "image-oracle.txt"),
                $"; bloomDispatches={work.DispatchCount}; bloomDraws={work.DrawCount}; bloomBytes={work.TexturePayloadBytes}; world={work.Width}x{work.Height}");

        }
        finally
        {
            camera.targetTexture = originalTarget;
            camera.orthographicSize = originalZoom;
            camera.transform.position = originalPosition;
            follow.enabled = originalFollow;
            cameraData.dithering = originalDither;
            Time.timeScale = originalTimeScale;
            for (int index = 0; index < frozenRobots.Length; index++)
            {
                if (frozenRobots[index] != null)
                {
                    frozenRobots[index].enabled = robotEnabled[index];
                }
            }
            PostProcessRuntimeState.BypassPostProcessEffects = originalBypass;
            PostProcessRuntimeState.DiagnosticOffscreenCamera = originalDiagnostic;
            PostProcessRuntimeState.DiagnosticWorldImage = null;
            postprocessController.BloomIntensity = originalBloomIntensity;
            zoomLighting.RemoveDynamicLight(ZoomProbeId);
            ResolveInScene<GraphicsSettingsController>(game).SelectPreset(originalPreset);
            target.Release();
            Object.Destroy(target);
            Object.Destroy(readback);
        }
    }

    [UnityTest]
    [Timeout(180_000)]
    public IEnumerator MainGame_GroundImageDoesNotFollowDistortion()
    {
        yield return Await(_bootstrap.TransitionAsync("MainMenu"), UITimeoutSeconds);
        yield return Await(_bootstrap.TransitionAsync("MainGame"), WorldTimeoutSeconds);
        Scene game = SceneManager.GetSceneByName("MainGame");
        TerrainRenderer terrain = FindComponentInScene<TerrainRenderer>(game)!;
        yield return WaitUntil(() => terrain.IsReadyForGameplay, WorldTimeoutSeconds, "Terrain not ready.");
        Camera screenCamera = ResolveInScene<IGameplayCamera>(game).Camera;
        using var diagnostic = new DiagnosticCameraScope(screenCamera, ResolveInScene<ISceneObjectFactory>(game));
        Camera camera = diagnostic.Camera;
        CameraFollow follow = FindComponentInScene<CameraFollow>(game)!;
        ClientConfig config = _bootstrap.Container.Resolve<IClientConfigManager>().Config;
        IWorldDataStorage storage = ResolveInScene<IWorldDataStorage>(game);
        IMapDataProvider map = ResolveInScene<IMapDataProvider>(game);
        PostProcessController post = ResolveInScene<PostProcessController>(game);
        bool oldDistortion = config.Terrain.EnableDistortion;
        TerrainDistortionStyle oldStyle = config.Terrain.DistortionStyle;
        bool oldFollow = follow.enabled;
        float oldTime = Time.timeScale;
        float oldBloom = post.BloomIntensity;
        bool oldBypass = PostProcessRuntimeState.BypassPostProcessEffects;
        int stageId = Shader.PropertyToID("_KernTerrainBenchmarkStage");
        int oldStage = Shader.GetGlobalInt(stageId);
        var target = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGBHalf);
        Texture2D readback = RuntimeTextureFactory.CreateRGBAHalfNoMip(1024, 1024, "GroundDistortionReadback",
            RuntimeTextureColorSpace.Linear, FilterMode.Point, TextureWrapMode.Clamp);
        Assert.That(target.Create(), Is.True);
        string directory = DiagnosticArtifactPaths.CreateDirectory("Performance", "ground_distortion");
        try
        {
            follow.enabled = false;
            Time.timeScale = 0f;
            camera.targetTexture = target;
            camera.orthographicSize = 8f;
            camera.transform.position = new Vector3(Mathf.Floor(screenCamera.transform.position.x),
                Mathf.Floor(screenCamera.transform.position.y), screenCamera.transform.position.z);
            camera.GetUniversalAdditionalCameraData().dithering = false;
            camera.enabled = true;
            post.BloomIntensity = 0f;
            PostProcessRuntimeState.BypassPostProcessEffects = true;
            // Production surface checkpoint: same mesh, textures, UV resolver
            // and coverage; exclude illumination/animation changes from this oracle.
            Shader.SetGlobalInt(stageId, 3);
            Color[][] images = new Color[3][];
            for (int mode = 0; mode < 3; mode++)
            {
                config.Terrain.EnableDistortion = mode != 0;
                config.Terrain.DistortionStyle = mode == 2 ? TerrainDistortionStyle.Organic : TerrainDistortionStyle.Classic;
                yield return Skip(90);
                Assert.That(terrain.IsReadyForGameplay, Is.True);
                Assert.That(screenCamera.targetTexture, Is.Null);
                RenderTexture? previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0);
                    readback.Apply();
                    images[mode] = readback.GetPixels();
                    File.WriteAllBytes(Path.Combine(directory, $"ground_{mode}.png"), readback.EncodeToPNG());
                }
                finally { RenderTexture.active = previous; }
            }
            int groundPixels = 0;
            float error = 0f;
            for (int y = 0; y < 1024; y++)
            {
                for (int x = 0; x < 1024; x++)
                {
                    float wx = camera.transform.position.x - 8f + (x + 0.5f) / 64f;
                    float wy = camera.transform.position.y - 8f + (y + 0.5f) / 64f;
                    float u = wx - Mathf.Floor(wx);
                    float v = wy - Mathf.Floor(wy);
                    // Cell interiors cannot be occluded by the neighboring
                    // distorted mass. The oracle is independent of shader UV math.
                    if (u < 0.25f || u > 0.75f || v < 0.25f || v > 0.75f) { continue; }
                    int sx = Mathf.FloorToInt(wx);
                    int sy = CoordinateUtils.UnityToServerY(Mathf.FloorToInt(wy), map.WorldHeight);
                    if (!storage.TryGetCell(sx, sy, out var type) ||
                        (type != MinesServer.Data.CellType.Road && type != MinesServer.Data.CellType.Empty)) { continue; }
                    groundPixels++;
                    int pixel = y * 1024 + x;
                    for (int mode = 1; mode < 3; mode++)
                    {
                        for (int channel = 0; channel < 3; channel++)
                        {
                            error = Mathf.Max(error, Mathf.Abs(images[0][pixel][channel] - images[mode][pixel][channel]));
                        }
                    }
                }
            }
            File.WriteAllText(Path.Combine(directory, "oracle.txt"), $"groundPixels={groundPixels}; maxError={error:R}");
            Assert.That(groundPixels, Is.GreaterThan(0));
            Assert.That(error, Is.LessThanOrEqualTo(0.002f), "Distortion changes ground surface pixels.");
        }
        finally
        {
            config.Terrain.EnableDistortion = oldDistortion;
            config.Terrain.DistortionStyle = oldStyle;
            follow.enabled = oldFollow;
            Time.timeScale = oldTime;
            post.BloomIntensity = oldBloom;
            PostProcessRuntimeState.BypassPostProcessEffects = oldBypass;
            Shader.SetGlobalInt(stageId, oldStage);
            camera.targetTexture = null;
            target.Release();
            Object.Destroy(target);
            Object.Destroy(readback);
        }
    }

    [UnityTest]
    [Timeout(180_000)]
    public IEnumerator MainGame_StaticAtlasReanchorImage()
    {
        IClientConfigManager config = _bootstrap.Container.Resolve<IClientConfigManager>();
        GraphicsPreset originalPreset = config.Config.GraphicsPreset;
        yield return Await(_bootstrap.TransitionAsync("MainMenu"), UITimeoutSeconds);
        config.SelectGraphicsPreset(GraphicsPreset.Standard);
        yield return Await(_bootstrap.TransitionAsync("MainGame"), WorldTimeoutSeconds);
        Scene game = SceneManager.GetSceneByName("MainGame");
        TerrainRenderer terrain = FindComponentInScene<TerrainRenderer>(game)
            ?? throw new InvalidOperationException("MainGame has no terrain.");
        yield return WaitUntil(() => terrain.IsReadyForGameplay, WorldTimeoutSeconds, "Terrain not ready.");
        LightingEngine lighting = ResolveInScene<LightingEngine>(game);
        IFrameTelemetry telemetry = ResolveInScene<IFrameTelemetry>(game);
        Camera screenCamera = ResolveInScene<IGameplayCamera>(game).Camera;
        using var diagnosticCamera = new DiagnosticCameraScope(screenCamera, ResolveInScene<ISceneObjectFactory>(game));
        Camera camera = diagnosticCamera.Camera;
        CameraFollow follow = FindComponentInScene<CameraFollow>(game)
            ?? throw new InvalidOperationException("MainGame has no camera follow.");
        UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
        var target = new RenderTexture(513, 511, 24, RenderTextureFormat.ARGBHalf)
        {
            name = "StaticAtlasProductionReanchorOracle",
        };
        Assert.That(target.Create(), Is.True);
        RenderTexture? originalTarget = camera.targetTexture;
        Vector3 originalPosition = camera.transform.position;
        float originalZoom = camera.orthographicSize;
        float originalTime = Time.timeScale;
        bool originalFollow = follow.enabled;
        bool originalDither = data.dithering;
        bool originalBypass = PostProcessRuntimeState.BypassPostProcessEffects;
        Camera? originalDiagnostic = PostProcessRuntimeState.DiagnosticOffscreenCamera;
        bool originalReference = LightingUpdateCoordinator.DiagnosticForceDenseReanchor;
        bool originalCounters = LightingComputeBinder.DiagnosticTransportCounters;
        GraphicsSettingsController graphics = ResolveInScene<GraphicsSettingsController>(game);
        string directory = DiagnosticArtifactPaths.CreateDirectory("Performance", "static_atlas_reanchor");
        Robot[] frozenRobots = Object.FindObjectsByType<Robot>();
        bool[] robotEnabled = frozenRobots.Select(robot => robot.enabled).ToArray();
        int reusedMoves = 0;
        try
        {
            camera.targetTexture = target;
            camera.enabled = true;
            follow.enabled = false;
            data.dithering = false;
            camera.orthographicSize = 12f;
            Time.timeScale = 0f;
            PostProcessRuntimeState.DiagnosticOffscreenCamera = camera;
            PostProcessRuntimeState.BypassPostProcessEffects = true;
            LightingComputeBinder.DiagnosticTransportCounters = true;
            LightingUpdateCoordinator.DiagnosticForceDenseReanchor = false;
            camera.transform.position = new Vector3(Mathf.Floor(originalPosition.x),
                Mathf.Floor(originalPosition.y), originalPosition.z);
            diagnosticCamera.ApplyPoseToGameplay();
            yield return Skip(5);
            graphics.SelectPreset(GraphicsPreset.Overdrive);
            yield return Skip(60);
            foreach (Robot robot in frozenRobots) { robot.enabled = false; }
            Assert.That(lighting.GPUResources.Direct.Static, Is.Not.Null, "Atlas oracle requires real static transport.");
            Vector2Int[] moves = [new(1, 0), new(-1, 0), new(-1, 0), new(-1, 0),
                new(0, -1), new(0, 1), new(-1, -1), new(1, 1)];
            for (int move = 0; move < moves.Length; move++)
            {
                byte[][] previousFields = new byte[2][];
                int oldInputsRead = 0;
                AsyncGPUReadback.Request(lighting.GPUResources.Geometry.Material!, 0, request =>
                {
                    Assert.That(request.hasError, Is.False);
                    previousFields[0] = request.GetData<byte>().ToArray();
                    oldInputsRead++;
                });
                AsyncGPUReadback.Request(lighting.GPUResources.Geometry.StaticEmission!, 0, request =>
                {
                    Assert.That(request.hasError, Is.False);
                    previousFields[1] = request.GetData<byte>().ToArray();
                    oldInputsRead++;
                });
                yield return WaitUntil(() => oldInputsRead == 2, 10f, "Old static input readbacks did not finish.");
                int scrollBefore = telemetry.LightingAtlasScrollCount;
                ulong solveBefore = lighting.SolveCount;
                Vector4 rectBefore = lighting.WorldRect;
                // Cross the actual published field boundary with the viewport,
                // while preserving overlap. A fixed 40-cell move can stay inside
                // the high-water allocation and does not exercise a reanchor.
                Vector3 next = camera.transform.position;
                float halfWidth = camera.orthographicSize * camera.aspect;
                float halfHeight = camera.orthographicSize;
                if (moves[move].x > 0) { next.x = rectBefore.x + rectBefore.z - halfWidth + 2f; }
                if (moves[move].x < 0) { next.x = rectBefore.x + halfWidth - 2f; }
                if (moves[move].y > 0) { next.y = rectBefore.y + rectBefore.w - halfHeight + 2f; }
                if (moves[move].y < 0) { next.y = rectBefore.y + halfHeight - 2f; }
                camera.transform.position = next;
                diagnosticCamera.ApplyPoseToGameplay();
                File.AppendAllText(Path.Combine(directory, "oracle.txt"),
                    $"start move={move}; camera={camera.transform.position}; oldRect={rectBefore}; oldSolve={solveBefore}\n");
                yield return WaitUntil(() => terrain.IsReadyForGameplay && lighting.SolveCount > solveBefore &&
                    lighting.WorldRect != rectBefore, WorldTimeoutSeconds, "Camera move did not publish a reanchored field.");
                yield return Skip(30);
                if (telemetry.LightingAtlasScrollCount > scrollBefore)
                {
                    reusedMoves++;
                }
                var images = new float[2][];
                var fields = new ushort[2][];
                var atlases = new uint[2][];
                var counters = new uint[2][];
                var materials = new byte[2][];
                var emissions = new byte[2][];
                Vector4 movedRect = lighting.WorldRect;
                for (int reference = 0; reference < 2; reference++)
                {
                    if (reference == 1)
                    {
                        LightingUpdateCoordinator.DiagnosticForceDenseReanchor = true;
                        ulong previousSolve = lighting.SolveCount;
                        lighting.InvalidateRadiance();
                        yield return WaitUntil(() => lighting.SolveCount > previousSolve,
                            WorldTimeoutSeconds, "Dense reference did not run.");
                        yield return Skip(5);
                    }
                    int captured = reference;
                    int completed = 0;
                    bool readbackError = false;
                    PostProcessRuntimeState.DiagnosticWorldImage = (cmd, world, _) =>
                    {
                        PostProcessRuntimeState.DiagnosticWorldImage = null;
                        cmd.RequestAsyncReadback(world, 0, TextureFormat.RGBAFloat, request =>
                        {
                            readbackError |= request.hasError;
                            if (!request.hasError) { images[captured] = request.GetData<float>().ToArray(); }
                            completed++;
                        });
                        cmd.RequestAsyncReadback(lighting.GPUResources.Direct.Static!, 0, request =>
                        {
                            readbackError |= request.hasError;
                            if (!request.hasError) { fields[captured] = request.GetData<ushort>().ToArray(); }
                            completed++;
                        });
                        cmd.RequestAsyncReadback(lighting.GPUResources.Cascade.Atlas!, request =>
                        {
                            readbackError |= request.hasError;
                            if (!request.hasError) { atlases[captured] = request.GetData<uint>().ToArray(); }
                            completed++;
                        });
                        cmd.RequestAsyncReadback(lighting.GPUResources.Geometry.Material!, 0, request =>
                        {
                            readbackError |= request.hasError;
                            if (!request.hasError) { materials[captured] = request.GetData<byte>().ToArray(); }
                            completed++;
                        });
                        cmd.RequestAsyncReadback(lighting.GPUResources.Geometry.StaticEmission!, 0, request =>
                        {
                            readbackError |= request.hasError;
                            if (!request.hasError) { emissions[captured] = request.GetData<byte>().ToArray(); }
                            completed++;
                        });
                        cmd.RequestAsyncReadback(lighting.DiagnosticTransportCounterBuffer, request =>
                        {
                            readbackError |= request.hasError;
                            if (!request.hasError) { counters[captured] = request.GetData<uint>().ToArray(); }
                            completed++;
                        });
                    };
                    yield return WaitUntil(() => completed == 6, 10f, "Production readbacks did not finish.");
                    Assert.That(readbackError, Is.False);
                    Assert.That(lighting.WorldRect, Is.EqualTo(movedRect), "Inputs changed during the differential oracle.");
                }
                for (int reference = 0; reference < 2; reference++)
                {
                    File.WriteAllBytes(Path.Combine(directory, $"atlas_{move}_{reference}.u32"),
                        System.Runtime.InteropServices.MemoryMarshal.AsBytes(atlases[reference].AsSpan()).ToArray());
                    File.WriteAllBytes(Path.Combine(directory, $"material_{move}_{reference}.rgba8"),
                        materials[reference]);
                    File.WriteAllBytes(Path.Combine(directory, $"emission_{move}_{reference}.rgba16half"),
                        emissions[reference]);
                }
                File.WriteAllBytes(Path.Combine(directory, $"material_{move}_old.rgba8"),
                    previousFields[0]);
                File.WriteAllBytes(Path.Combine(directory, $"emission_{move}_old.rgba16half"),
                    previousFields[1]);
                File.AppendAllText(Path.Combine(directory, "oracle.txt"),
                    $"fieldDimensions={lighting.GPUResources.FieldWidth}x{lighting.GPUResources.FieldHeight}; layouts=" +
                    JsonConvert.SerializeObject(lighting.GPUResources.Cascade.Layouts) + "\n");
                float worldError = 0f;
                int worstWorldIndex = -1;
                float fieldError = 0f;
                int changedWords = 0;
                Assert.That(images[0].Length, Is.EqualTo(images[1].Length));
                Assert.That(fields[0].Length, Is.EqualTo(fields[1].Length));
                for (int i = 0; i < images[0].Length; i++)
                {
                    Assert.That(float.IsFinite(images[0][i]), Is.True);
                    float error = Mathf.Abs(images[0][i] - images[1][i]);
                    if (error > worldError) { worldError = error; worstWorldIndex = i; }
                }
                for (int i = 0; i < fields[0].Length; i++)
                {
                    fieldError = Mathf.Max(fieldError, Mathf.Abs(Mathf.HalfToFloat(fields[0][i]) - Mathf.HalfToFloat(fields[1][i])));
                }
                for (int i = 0; i < lighting.GPUResources.Cascade.AtlasEntryCount * 3; i++)
                {
                    if (atlases[0][i] != atlases[1][i]) { changedWords++; }
                }
                string record = $"move={move}; rect={movedRect}; worldError={worldError:R}; fieldError={fieldError:R}; " +
                    $"worstWorldIndex={worstWorldIndex}; " +
                    $"atlasChangedWords={changedWords}; reusedDda={counters[0][0]}/{counters[0][1]}; denseDda={counters[1][0]}/{counters[1][1]}\n";
                File.AppendAllText(Path.Combine(directory, "oracle.txt"), record);
                TestContext.WriteLine(record);
                Assert.That(worldError, Is.LessThanOrEqualTo(0.002f));
                Assert.That(fieldError, Is.LessThanOrEqualTo(0.002f));
                Assert.That(changedWords, Is.EqualTo(0), "Reused intervals differ from a full solve at the same world region.");
                LightingUpdateCoordinator.DiagnosticForceDenseReanchor = false;
            }
            Assert.That(reusedMoves, Is.GreaterThan(0), "Differential oracle never exercised atlas reuse.");
        }
        finally
        {
            PostProcessRuntimeState.DiagnosticWorldImage = null;
            PostProcessRuntimeState.DiagnosticOffscreenCamera = originalDiagnostic;
            PostProcessRuntimeState.BypassPostProcessEffects = originalBypass;
            LightingUpdateCoordinator.DiagnosticForceDenseReanchor = originalReference;
            LightingComputeBinder.DiagnosticTransportCounters = originalCounters;
            for (int index = 0; index < frozenRobots.Length; index++)
            {
                if (frozenRobots[index] != null) { frozenRobots[index].enabled = robotEnabled[index]; }
            }
            graphics.SelectPreset(originalPreset);
            camera.targetTexture = originalTarget;
            camera.transform.position = originalPosition;
            camera.orthographicSize = originalZoom;
            data.dithering = originalDither;
            follow.enabled = originalFollow;
            Time.timeScale = originalTime;
            target.Release();
            Object.Destroy(target);
        }
    }

    [UnityTest]
    [Timeout(900_000)]
    public IEnumerator MainGame_LightingTraversalBeforeAfter()
    {
        string? requestedSources = Environment.GetEnvironmentVariable("KERN_LIGHTING_BENCHMARK_SOURCE_COUNT");
        int sourceCount = 1;
        if (requestedSources != null &&
            (!int.TryParse(requestedSources, out sourceCount) || sourceCount < 1 || sourceCount > 64))
        {
            throw new InvalidOperationException("KERN_LIGHTING_BENCHMARK_SOURCE_COUNT must be from 1 to 64.");
        }
        IClientConfigManager config = _bootstrap.Container.Resolve<IClientConfigManager>();
        GraphicsPreset originalPreset = config.Config.GraphicsPreset;
        yield return Await(_bootstrap.TransitionAsync("MainMenu"), UITimeoutSeconds);
        config.SelectGraphicsPreset(GraphicsPreset.Standard);
        yield return Await(_bootstrap.TransitionAsync("MainGame"), WorldTimeoutSeconds);
        Scene game = SceneManager.GetSceneByName("MainGame");
        TerrainRenderer terrain = FindComponentInScene<TerrainRenderer>(game)!;
        yield return WaitUntil(() => terrain.IsReadyForGameplay, WorldTimeoutSeconds, "Terrain not ready.");
        Camera camera = ResolveInScene<IGameplayCamera>(game).Camera;
        CameraFollow follow = FindComponentInScene<CameraFollow>(game)!;
        GraphicsSettingsController graphics = ResolveInScene<GraphicsSettingsController>(game);
        LightingEngine lighting = ResolveInScene<LightingEngine>(game);
        IFrameTelemetry telemetry = ResolveInScene<IFrameTelemetry>(game);
        using var resolution = new NativeBenchmarkResolution(3420, 2148);
        using var batchCamera = Application.isBatchMode
            ? new DiagnosticCameraScope(camera, ResolveInScene<ISceneObjectFactory>(game)) : null;
        RenderTexture? batchTarget = null;
        bool originalCameraEnabled = camera.enabled;
        Rect originalCameraRect = camera.rect;
        float originalAspect = camera.aspect;
        bool originalFollow = follow.enabled;
        bool originalReference = LightingComputeBinder.DiagnosticTexelTraversalReference;
        bool originalVectorReference = LightingComputeBinder.DiagnosticVectorPolarReference;
        float originalTimeScale = Time.timeScale;
        Camera? originalDiagnosticForOracle = PostProcessRuntimeState.DiagnosticOffscreenCamera;
        UniversalAdditionalCameraData? oracleCameraData = null;
        bool originalOracleDithering = false;
        Action<ScriptableRenderContext, Camera>? deterministicOracleCamera = null;
        Vector3 origin = camera.transform.position;
        Robot[] robots = Object.FindObjectsByType<Robot>();
        bool[] robotEnabled = robots.Select(robot => robot.enabled).ToArray();
        const int ProbeId = int.MinValue + 825;
        int sourceColumns = Mathf.CeilToInt(Mathf.Sqrt(sourceCount));
        int sourceRows = Mathf.CeilToInt(sourceCount / (float)sourceColumns);
        void SetSources(Vector2 shift, float intensity)
        {
            for (int source = 0; source < sourceCount; source++)
            {
                Vector2 offset = new(
                    (source % sourceColumns - (sourceColumns - 1) * 0.5f) * 3f,
                    (source / sourceColumns - (sourceRows - 1) * 0.5f) * 3f);
                lighting.SetDynamicLight(ProbeId + source,
                    new Vector2(origin.x, origin.y) + offset + shift, Color.white, intensity);
            }
        }
        List<Result> results = [];
        List<CaptureWindow> captures = [];
        string directory = DiagnosticArtifactPaths.CreateDirectory("Performance", "lighting_traversal_ab");
        try
        {
            follow.enabled = false;
            if (batchCamera != null)
            {
                // Batch mode has no display presentation. Render the actual
                // Renderer2D/world-grid/URP/output passes into a native-sized
                // target, while gameplay owns terrain/input projection.
                batchTarget = new RenderTexture(3420, 2148, 24, RenderTextureFormat.ARGBHalf)
                {
                    name = "LightingTraversalProductionOutput",
                };
                Assert.That(batchTarget.Create(), Is.True);
                camera.pixelRect = new Rect(0, 0, 3420, 2148);
                camera.aspect = 3420f / 2148f;
                camera.enabled = false;
                batchCamera.Camera.targetTexture = batchTarget;
                batchCamera.Camera.rect = new Rect(0, 0, 1, 1);
                batchCamera.Camera.aspect = camera.aspect;
                batchCamera.Camera.enabled = true;
            }
            yield return Skip(30);
            // Startup may replace replicated robots while assets arrive.
            // Snapshot the live production entities after settling.
            robots = Object.FindObjectsByType<Robot>();
            robotEnabled = robots.Select(robot => robot.enabled).ToArray();
            foreach (Robot robot in robots)
            {
                if (robot != null) { robot.enabled = false; }
            }
            // Settle the explicit source fixture before acquiring dense light
            // targets: the initial replicated crowd is not this workload.
            SetSources(Vector2.zero, 8f);
            graphics.SelectPreset(GraphicsPreset.Overdrive);
            yield return Skip(30);
            Assert.That(lighting.UploadedDynamicLightCount, Is.EqualTo(sourceCount),
                "Production must upload the entire requested source workload.");
            Camera outputCamera = batchCamera?.Camera ?? camera;
            PostProcessRuntimeState.DiagnosticOffscreenCamera = outputCamera;
            Assert.That(outputCamera.pixelWidth, Is.EqualTo(3420));
            Assert.That(outputCamera.pixelHeight, Is.EqualTo(2148));
            TestContext.WriteLine($"productionOutput={outputCamera.pixelWidth}x{outputCamera.pixelHeight}; " +
                $"batch={Application.isBatchMode}; nativeUI={Screen.width}x{Screen.height}; sources={sourceCount}");

            // A faster but different image is a failed benchmark. Capture both
            // real world passes at one continuous source pose before timed work.
            // Freeze shader time only for this comparison, then restore it.
            // URP output dithering changes its blue-noise phase every frame;
            // deterministic image comparison holds that temporal input off.
            // Timed windows use the original production camera setting.
            oracleCameraData = outputCamera.GetUniversalAdditionalCameraData();
            originalOracleDithering = oracleCameraData.dithering;
            oracleCameraData.dithering = false;
            // The display owner periodically configures the gameplay camera.
            // Set the fixture's temporal input at the render boundary, before
            // URP snapshots camera data, rather than racing that owner update.
            deterministicOracleCamera = (_, renderedCamera) =>
            {
                if (renderedCamera == outputCamera) { oracleCameraData.dithering = false; }
            };
            RenderPipelineManager.beginCameraRendering += deterministicOracleCamera;
            Time.timeScale = 0f;
            float[][] traversalImages = new float[3][];
            Vector4[] traversalImageRects = new Vector4[3];
            for (int referenceIndex = 0; referenceIndex < 3; referenceIndex++)
            {
                bool reference = referenceIndex != 1;
                LightingComputeBinder.DiagnosticTexelTraversalReference = reference;
                LightingComputeBinder.DiagnosticVectorPolarReference = reference;
                Vector2 pose = new(0.375f, 0.1875f);
                SetSources(pose, 9f);
                yield return Skip(5);
                SetSources(pose, 8f);
                yield return Skip(5);
                int capturedIndex = referenceIndex;
                bool received = false;
                PostProcessRuntimeState.DiagnosticWorldImage = (cmd, world, rect) =>
                {
                    Assert.That(oracleCameraData.dithering, Is.False,
                        "Temporal output dithering changed the image fixture's input.");
                    PostProcessRuntimeState.DiagnosticWorldImage = null;
                    traversalImageRects[capturedIndex] = rect;
                    cmd.RequestAsyncReadback(world, 0, TextureFormat.RGBAFloat, request =>
                    {
                        Assert.That(request.hasError, Is.False, "Production traversal image readback failed.");
                        traversalImages[capturedIndex] = request.GetData<float>().ToArray();
                        received = true;
                    });
                };
                yield return WaitUntil(() => received, WorldTimeoutSeconds, "Production traversal image was not captured.");
            }
            Time.timeScale = originalTimeScale;
            RenderPipelineManager.beginCameraRendering -= deterministicOracleCamera;
            deterministicOracleCamera = null;
            oracleCameraData.dithering = originalOracleDithering;
            Assert.That(traversalImages[1].Length, Is.EqualTo(traversalImages[0].Length));
            float traversalImageError = 0f;
            float traversalReferenceDrift = 0f;
            int traversalWorstIndex = 0;
            bool finiteTraversalImage = true;
            for (int index = 0; index < traversalImages[0].Length; index++)
            {
                finiteTraversalImage &= float.IsFinite(traversalImages[0][index]) && float.IsFinite(traversalImages[1][index]);
                float error = Mathf.Abs(traversalImages[0][index] - traversalImages[1][index]);
                if (error > traversalImageError) { traversalImageError = error; traversalWorstIndex = index; }
                traversalReferenceDrift = Mathf.Max(traversalReferenceDrift,
                    Mathf.Abs(traversalImages[0][index] - traversalImages[2][index]));
            }
            File.WriteAllText(Path.Combine(directory, "image-oracle.txt"),
                $"production world image values={traversalImages[0].Length}; maxError={traversalImageError:R}; " +
                $"referenceDrift={traversalReferenceDrift:R}; worstIndex={traversalWorstIndex}; " +
                $"referenceValue={traversalImages[0][traversalWorstIndex]:R}; optimizedValue={traversalImages[1][traversalWorstIndex]:R}; " +
                $"repeatReferenceValue={traversalImages[2][traversalWorstIndex]:R}; rect={traversalImageRects[0]}\n");
            if (traversalImageError > 0.002f || traversalReferenceDrift > 0.002f)
            {
                for (int index = 0; index < traversalImages.Length; index++)
                {
                    byte[] bytes = new byte[traversalImages[index].Length * sizeof(float)];
                    Buffer.BlockCopy(traversalImages[index], 0, bytes, 0, bytes.Length);
                    File.WriteAllBytes(Path.Combine(directory, $"world-{index}.rgba32float"), bytes);
                }
            }
            Assert.That(finiteTraversalImage, Is.True, "Non-finite production traversal image.");
            Assert.That(traversalReferenceDrift, Is.LessThanOrEqualTo(0.002f),
                "Production image inputs changed between reference captures.");
            Assert.That(traversalImageError, Is.LessThanOrEqualTo(0.002f),
                "Uniform transport changed the production world image.");
            Array.Clear(traversalImages, 0, traversalImages.Length);
            for (int repetition = 0; repetition < 3; repetition++)
            {
                for (int order = 0; order < 2; order++)
                {
                    bool reference = (order + repetition) % 2 == 0;
                    LightingComputeBinder.DiagnosticTexelTraversalReference = reference;
                    LightingComputeBinder.DiagnosticVectorPolarReference = reference;
                    Action<int> advance = frame =>
                    {
                        // Identical continuous pose input for both paths. The
                        // real geometry, shaders, world output and UI render.
                        float shift = (frame % 32) / 32f;
                        SetSources(new Vector2(shift, 0f), 8f);
                    };
                    for (int warmup = 0; warmup < WarmupFrames; warmup++)
                    {
                        advance(warmup);
                        yield return null;
                    }
                    string scenario = $"rep-{repetition}/{(reference ? "texel-reference" : "uniform-proof")}";
                    yield return Measure(scenario, results, telemetry, captures, scenario, advance, outputCamera);
                    Assert.That(lighting.UploadedDynamicLightCount, Is.EqualTo(sourceCount));
                    var sourceTextures = Resources.FindObjectsOfTypeAll<RenderTexture>()
                        .Where(texture => texture.name is "_DynamicLightTiles" or "_DynamicRayDepth")
                        .Select(texture => new
                        {
                            texture.name,
                            texture.width,
                            texture.height,
                            layers = texture.volumeDepth,
                            dimension = texture.dimension.ToString(),
                            format = texture.graphicsFormat.ToString(),
                            payloadBytes = (long)texture.width * texture.height * texture.volumeDepth *
                                UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetBlockSize(texture.graphicsFormat),
                        }).ToArray();
                    File.WriteAllText(Path.Combine(directory, scenario.Replace('/', '_') + ".source-resources.json"),
                        JsonConvert.SerializeObject(new { sourceCount, sourceTextures }, Formatting.Indented));
                    PostProcessRendererFeature output = Resources.FindObjectsOfTypeAll<PostProcessRendererFeature>()
                        .Single(feature => feature.RendersCamera(outputCamera));
                    Assert.That(output.DisplayWorkload.HasValue, Is.True,
                        "Performance samples did not execute the production display pass.");
                    Assert.That(captures[^1].Samples.Count(sample => sample.LightingDynamicDispatchPixels > 0),
                        Is.GreaterThanOrEqualTo(MeasuredFrames - 2), "Moving source did not update every measured frame.");
                    Assert.That(captures[^1].Samples.All(sample =>
                        sample.LightingFieldRebuilds == 0 && sample.LightingStaticSolves == 0), Is.True,
                        "Moving a dynamic source rebuilt static lighting or geometry.");
                    captures[^1].Write(directory);
                    File.WriteAllText(Path.Combine(directory, "results.json"),
                        JsonConvert.SerializeObject(results, Formatting.Indented));
                    File.WriteAllText(Path.Combine(directory, "report.txt"), BuildReport(results, captures));
                }
            }
            TestContext.WriteLine($"Production lighting traversal captures: {directory}");
            foreach (Result result in results)
            {
                TestContext.WriteLine($"{result.Scenario}: p50={result.P50Ms:R}; p95={result.P95Ms:R}");
            }
            var acceptance = Enumerable.Range(0, 3).Select(repetition =>
            {
                Result before = results.Single(result => result.Scenario == $"rep-{repetition}/texel-reference");
                Result after = results.Single(result => result.Scenario == $"rep-{repetition}/uniform-proof");
                return new
                {
                    repetition,
                    beforeP50Ms = before.P50Ms,
                    afterP50Ms = after.P50Ms,
                    beforeP95Ms = before.P95Ms,
                    afterP95Ms = after.P95Ms,
                    p50Reduction = 1.0 - after.P50Ms / before.P50Ms,
                    passesThirtyPercent = after.P50Ms <= before.P50Ms * 0.7,
                    passesP95 = after.P95Ms <= before.P95Ms,
                    reachesFiveMilliseconds = after.P50Ms <= 5.0,
                };
            }).ToArray();
            File.WriteAllText(Path.Combine(directory, "acceptance.json"), JsonConvert.SerializeObject(new
            {
                scope = "Production world/lighting/postprocess transport comparison; native UI is measured separately",
                outputWidth = outputCamera.pixelWidth,
                outputHeight = outputCamera.pixelHeight,
                nativeUIWidth = Screen.width,
                nativeUIHeight = Screen.height,
                warmupFrames = WarmupFrames,
                measuredFrames = MeasuredFrames,
                sourceCount,
                traversalImageError,
                repetitions = acceptance,
            }, Formatting.Indented));
            Assert.That(acceptance.All(pair => pair.passesThirtyPercent && pair.passesP95), Is.True,
                "Production transport acceptance failed: require >=30% p50 reduction and no p95 regression in every repetition.");
        }
        finally
        {
            LightingComputeBinder.DiagnosticTexelTraversalReference = originalReference;
            LightingComputeBinder.DiagnosticVectorPolarReference = originalVectorReference;
            PostProcessRuntimeState.DiagnosticWorldImage = null;
            Time.timeScale = originalTimeScale;
            if (deterministicOracleCamera != null)
            {
                RenderPipelineManager.beginCameraRendering -= deterministicOracleCamera;
            }
            PostProcessRuntimeState.DiagnosticOffscreenCamera = originalDiagnosticForOracle;
            if (oracleCameraData != null) { oracleCameraData.dithering = originalOracleDithering; }
            for (int source = 0; source < sourceCount; source++) { lighting.RemoveDynamicLight(ProbeId + source); }
            for (int index = 0; index < robots.Length; index++)
            {
                if (robots[index] != null) { robots[index].enabled = robotEnabled[index]; }
            }
            camera.transform.position = origin;
            camera.rect = originalCameraRect;
            camera.aspect = originalAspect;
            camera.enabled = originalCameraEnabled;
            if (batchCamera != null)
            {
                batchCamera.Camera.enabled = false;
                batchCamera.Camera.targetTexture = null;
            }
            if (batchTarget != null)
            {
                batchTarget.Release();
                Object.Destroy(batchTarget);
            }
            follow.enabled = originalFollow;
            graphics.SelectPreset(originalPreset);
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
        NativeBenchmarkResolution? nativeResolution = null;
        Assert.That(camera.targetTexture, Is.Null, "Frame benchmark must preserve native output and UI clear.");
        if (resolution is not null)
        {
            string[] dimensions = resolution.Split('x');
            int width = int.Parse(dimensions[0]);
            int height = int.Parse(dimensions[1]);
            nativeResolution = new NativeBenchmarkResolution(width, height);
            yield return Skip(30);
            Assert.That(camera.pixelWidth, Is.EqualTo(width));
            Assert.That(camera.pixelHeight, Is.EqualTo(height));
        }
        CameraFollow cameraFollow = camera.GetComponent<CameraFollow>() ??
            FindComponentInScene<CameraFollow>(game) ??
            throw new InvalidOperationException("MainGame has no CameraFollow for the zoom workload.");
        bool cameraFollowWasEnabled = cameraFollow.enabled;
        float originalZoom = camera.orthographicSize;

        IRuntimeDebugSettings debug = ResolveInScene<IRuntimeDebugSettings>(game);
        bool originalLightingBypass = debug.BypassLightingCompute;
        bool originalTerrainDrawBypass = debug.BypassTerrainDraw;
        bool originalCPUMeshRebuildBypass = debug.BypassCPUMeshRebuild;
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
            debug.BypassCPUMeshRebuild = false;
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

            debug.BypassCPUMeshRebuild = true;
            yield return Skip(30);
            yield return Measure("без пересборки террейна", results, telemetry, captures, "diagnostic/terrain-build-bypass");

            // Formatting and IO happen only after all measured windows.
            string runDirectory = DiagnosticArtifactPaths.CreateDirectory("Performance", "frame_run");
            foreach (CaptureWindow capture in captures)
            {
                capture.Write(runDirectory);
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
            nativeResolution?.Dispose();

            camera.orthographicSize = originalZoom;
            cameraFollow.enabled = cameraFollowWasEnabled;
            debug.BypassLightingCompute = originalLightingBypass;
            debug.BypassTerrainDraw = originalTerrainDrawBypass;
            debug.BypassCPUMeshRebuild = originalCPUMeshRebuildBypass;
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
            Object.Destroy(fixture);
            foreach (VolumeComponent component in profile.components)
            {
                Object.Destroy(component);
            }

            Object.Destroy(profile);
        }

        yield return Skip(60);
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
        IFrameTelemetry? telemetry, List<CaptureWindow> captures, string captureScenario, Action<int>? advanceFrame = null,
        Camera? renderingCamera = null)
    {
        Camera measuredCamera = renderingCamera ?? ResolveInScene<IGameplayCamera>(SceneManager.GetSceneByName("MainGame")).Camera;
        PostProcessRendererFeature postprocess = Resources.FindObjectsOfTypeAll<PostProcessRendererFeature>()
            .Single(feature => feature.RendersCamera(measuredCamera));
        // Own the counter: telemetry tracking otherwise depends on an open diagnostics window.
        // LastValue observes the completed frame, like CaptureSample.FrameId.
        using var allocationRecorder = ProfilerRecorder.StartNew(
            ProfilerCategory.Memory, "GC Allocated In Frame");
        var recorders = new List<(string Name, bool IsGPUStage, ProfilerRecorder Recorder)>();
        var gpuRecorders = new List<(string Name, ProfilerRecorder Recorder)>();
        using var recorderLifetime = new RecorderLifetime(recorders, gpuRecorders);
        foreach (string marker in s_GPUStageMarkers)
        {
            gpuRecorders.Add((marker, new ProfilerRecorder(ProfilerCategory.Render, marker, MeasuredFrames,
                ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.SumAllSamplesInFrame |
                ProfilerRecorderOptions.WrapAroundWhenCapacityReached | ProfilerRecorderOptions.GpuRecorder)));
        }
        foreach ((string marker, bool isGPUStage) in s_markers.Select(name => (name, false))
                     .Concat(s_GPUStageMarkers.Select(name => (name, true))))
        {
            // Маркеры из разных категорий: конструктор по имени ищет во всех.
            var recorder = new ProfilerRecorder(
                marker,
                MeasuredFrames,
                ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.SumAllSamplesInFrame |
                ProfilerRecorderOptions.WrapAroundWhenCapacityReached);
            recorders.Add((marker, isGPUStage, recorder));
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
            if (FrameTimingManager.GetLatestTimings(1, s_frameTimingBuffer) > 0)
            {
                lastFrameTimingTimestamp = s_frameTimingBuffer[0].frameStartTimestamp;
                hasFrameTimingTimestamp = true;
            }
        }

        CaptureWindow? capture = telemetry is null ? null : new CaptureWindow(captureScenario, MeasuredFrames, measuredCamera)
        {
            Baseline = new CaptureSample(telemetry, postprocess: postprocess),
            FrameMs = frameMs,
        };
        if (capture is not null)
        {
            ClientConfig config = FindBootstrap()!.Container.Resolve<IClientConfigManager>().Config;
            capture.QualityProfile = JsonConvert.SerializeObject(new
            {
                config.GraphicsPreset,
                config.GraphicsQualitySettings,
                lightingTuning = LightingQualityTuningController.Current,
                LightingComputeBinder.DiagnosticTexelTraversalReference,
                LightingComputeBinder.DiagnosticVectorPolarReference,
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
                    PostProcessRuntimeState.DiagnosticFullResolutionWorld,
                },
            });
        }
        for (int frame = 0; frame < MeasuredFrames; frame++)
        {
            advanceFrame?.Invoke(frame);
            yield return null;
            frameMs[frame] = Time.unscaledDeltaTime * 1000.0;
            if (gpuTimingSupported)
            {
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, s_frameTimingBuffer) > 0)
                {
                    FrameTiming timing = s_frameTimingBuffer[0];
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
                        : null, postprocess);
            }
        }

        foreach ((string _, ProfilerRecorder recorder) in gpuRecorders)
        {
            if (recorder.Valid)
            {
                recorder.Stop();
            }
        }

        foreach ((string _, bool _, ProfilerRecorder recorder) in recorders)
        {
            if (recorder.Valid)
            {
                recorder.Stop();
            }
        }

        if (capture is not null)
        {
            captures.Add(capture);
        }

        var markers = new Dictionary<string, double>();
        var postprocessGPUTimes = new Dictionary<string, double>();
        var postprocessGPUCounts = new Dictionary<string, int>();
        foreach ((string name, ProfilerRecorder recorder) in gpuRecorders)
        {
            var measured = new List<double>();
            if (recorder.Valid)
            {
                recorder.Stop();
                var observations = new List<ProfilerRecorderSample>(MeasuredFrames);
                recorder.CopyTo(observations);
                RequireRecorderCapacity(recorder, observations.Count, name);
                foreach (ProfilerRecorderSample observation in observations)
                {
                    if (observation.Count > 0 && observation.Value > 0)
                    {
                        measured.Add(observation.Value * 1e-6);
                    }
                }
            }

            if (measured.Count > 0)
            {
                measured.Sort();
                postprocessGPUTimes[name] = measured[measured.Count / 2];
                postprocessGPUCounts[name] = measured.Count;
            }

            recorder.Dispose();
        }
        var gpuStageMarkers = new Dictionary<string, double>();
        var gpuStageMarkerSampleCounts = new Dictionary<string, int>();
        foreach ((string name, bool isGPUStage, ProfilerRecorder recorder) in recorders)
        {
            if (recorder.Valid)
            {
                recorder.Stop();
                var observations = new List<ProfilerRecorderSample>(MeasuredFrames);
                recorder.CopyTo(observations);
                RequireRecorderCapacity(recorder, observations.Count, name);
                double sum = 0;
                foreach (ProfilerRecorderSample observation in observations)
                {
                    sum += observation.Value;
                }

                if (observations.Count > 0 && isGPUStage)
                {
                    gpuStageMarkers[name] = sum / observations.Count * 1e-6;
                    gpuStageMarkerSampleCounts[name] = observations.Count;
                }
                else if (observations.Count > 0)
                {
                    markers[name] = sum / observations.Count * 1e-6;
                }
            }

            recorder.Dispose();
        }

        double[] sorted = frameMs.OrderBy(value => value).ToArray();
        double[] sortedCPUFrame = cpuFrameMs.OrderBy(value => value).ToArray();
        double[] sortedCPUMainThread = cpuMainThreadMs.OrderBy(value => value).ToArray();
        double[] sortedCPURenderThread = cpuRenderThreadMs.OrderBy(value => value).ToArray();
        double[] sortedGPU = gpuFrameMs.OrderBy(value => value).ToArray();
        results.Add(new Result(
            scenario,
            frameMs.Average(),
            Percentile(sorted, 0.50),
            Percentile(sorted, 0.95),
            Percentile(sorted, 0.99),
            sorted[^1],
            sortedCPUFrame.Length > 0 ? Percentile(sortedCPUFrame, 0.50) : 0,
            sortedCPUMainThread.Length > 0 ? Percentile(sortedCPUMainThread, 0.50) : 0,
            sortedCPURenderThread.Length > 0 ? Percentile(sortedCPURenderThread, 0.50) : 0,
            sortedCPUFrame.Length,
            sortedGPU.Length > 0 ? Percentile(sortedGPU, 0.50) : 0,
            sortedGPU.Length,
            markers,
            gpuStageMarkers,
            gpuStageMarkerSampleCounts,
            postprocessGPUTimes,
            postprocessGPUCounts));
    }

    private static PostProcessWorkloadSnapshot? CorrelatedWork(PostProcessWorkloadSnapshot? snapshot)
    {
        return snapshot is { } work && work.FrameId == Time.frameCount - 1 ? work : null;
    }

    private static void RequireRecorderCapacity(ProfilerRecorder recorder, int copiedCount, string name)
    {
        if (copiedCount > recorder.Capacity)
        {
            throw new InvalidOperationException($"Profiler recorder '{name}' returned {copiedCount} samples for capacity {recorder.Capacity}.");
        }
    }

    private static object? WorkJson(PostProcessWorkloadSnapshot? snapshot)
    {
        if (snapshot is not { } work)
        {
            return null;
        }

        return new
        {
            producerFrameId = work.FrameId,
            width = work.Width,
            height = work.Height,
            dispatchCount = work.DispatchCount,
            drawCount = work.DrawCount,
            logicalPixels = work.LogicalPixels,
            dispatchedThreads = work.DispatchedThreads,
            textureCount = work.TextureCount,
            texturePayloadBytes = work.TexturePayloadBytes,
            cpuRecordingMs = work.CPURecordingMs,
            gpuMs = (double?)null,
        };
    }

    private static string BuildReport(List<Result> results, List<CaptureWindow> captures)
    {
        var report = new StringBuilder(4096);
        report.Append("Бенчмарк кадра, ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
            .Append(", render ").Append(captures[0].Width).Append('×').Append(captures[0].Height)
            .Append(", ").Append(SystemInfo.graphicsDeviceType)
            .Append(Application.isEditor ? ", редактор" : ", сборка").AppendLine()
            .Append("Native UI: ").Append(captures[0].NativeUIWidth).Append('×').Append(captures[0].NativeUIHeight).AppendLine()
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
                report.Append(", FrameTiming p50 CPU ").Append(result.CPUFrameP50Ms.ToString("F2"))
                    .Append(" мс (main ").Append(result.CPUMainThreadP50Ms.ToString("F2"))
                    .Append(", render ").Append(result.CPURenderThreadP50Ms.ToString("F2"))
                    .Append("), GPU ");
                if (result.GPUSampleCount > 0)
                {
                    report.Append("p50 ").Append(result.GPUP50Ms.ToString("F2"))
                        .Append(" мс (уникальных кадров ").Append(result.GPUSampleCount).Append(')');
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

            report.AppendLine("GPU recorder timings (production scopes; delayed window samples, not frame-correlated):");
            foreach (string stage in s_GPUStageMarkers)
            {
                if (result.PostprocessGPUTimes.TryGetValue(stage, out double time))
                {
                    report.Append("  ").Append(time.ToString("F3")).Append(" ms GPU p50, ")
                        .Append(result.PostprocessGPUCounts[stage]).Append(" observations  ").AppendLine(stage);
                }
                else
                {
                    report.Append("  GPU TIMING UNAVAILABLE  ").AppendLine(stage);
                }
            }

            report.AppendLine("Command-buffer profiler scopes (CPU observations; nested values are not additive):");
            foreach (string stage in s_GPUStageMarkers)
            {
                if (result.GPUStageMarkers.TryGetValue(stage, out double stageMs))
                {
                    report.Append("  ").Append(stageMs.ToString("F3")).Append(" ms average over ")
                        .Append(result.GPUStageMarkerSampleCounts[stage]).Append(" marker samples  ")
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

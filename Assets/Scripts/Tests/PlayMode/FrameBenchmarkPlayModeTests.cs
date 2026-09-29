#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces.Diagnostics;
using Kern.Core.Interfaces;
using Kern.Networking;
using Kern.Networking.Auth;
using Kern.World.Terrain;
using MinesServer.Networking.Connection.Client;
using Newtonsoft.Json;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
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
// изолированная стоимость: зависимости и CPU/GPU overlap сохраняются. Маркеры
// Kern.Terrain.* измеряют CPU. Результат пишется в
// Logs/Diagnostics/Performance/benchmark_*.txt.
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

    private BootstrapLifetimeScope _bootstrap = null!;
    private string _originalClientToken = string.Empty;
    private HashSet<string> _originalDummyTokens = [];

    // Observation only: no JSON objects, file IO or strings are created in the sample loop.
    private readonly struct CaptureSample(IFrameTelemetry telemetry)
    {
        // Coroutine resumes after Update, before Terrain.LateUpdate resets the previous frame.
        public readonly int FrameId = Time.frameCount - 1;
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
        public readonly int LightingDynamicSolves = telemetry.LightingDynamicSolveCount;
        public readonly int LightingDynamicTraces = telemetry.LightingDynamicTraceCount;
        public readonly int LightingAtlasScrolls = telemetry.LightingAtlasScrollCount;
        public readonly int LightingFieldRebuilds = telemetry.LightingFieldRebuildCount;
        public readonly int LightingStaticSolves = telemetry.LightingStaticSolveFrameCount;

        public object CumulativeJson()
        {
            var cumulative = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["terrainRebuilds"] = TerrainRebuilds,
                ["terrainFullPopulates"] = TerrainFullPopulates,
                ["terrainDirtyPatches"] = TerrainDirtyPatches,
                ["terrainChunkLoads"] = TerrainChunkLoads,
                ["lightingDynamicSolves"] = LightingDynamicSolves,
                ["lightingDynamicTraces"] = LightingDynamicTraces,
                ["lightingAtlasScrolls"] = LightingAtlasScrolls,
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
        public readonly int Width = Screen.width;
        public readonly int Height = Screen.height;
        public readonly CaptureSample[] Samples = new CaptureSample[count];
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
                        qualityProfile = (string?)null,
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
                        ["qualityProfile"] = "Effective Kern graphics configuration is not captured.",
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
        public readonly Dictionary<string, double> Markers;

        public Result(string scenario, double meanMs, double p50Ms, double p95Ms, double p99Ms, double maxMs, Dictionary<string, double> markers)
        {
            Scenario = scenario;
            MeanMs = meanMs;
            P50Ms = p50Ms;
            P95Ms = p95Ms;
            P99Ms = p99Ms;
            MaxMs = maxMs;
            Markers = markers;
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
        yield return Await(_bootstrap.TransitionAsync("MainMenu"), UITimeoutSeconds);
        yield return Await(_bootstrap.TransitionAsync("MainGame"), WorldTimeoutSeconds);

        Scene game = SceneManager.GetSceneByName("MainGame");
        TerrainRenderer terrain = FindComponentInScene<TerrainRenderer>(game)
            ?? throw new InvalidOperationException("MainGame has no TerrainRenderer.");
        yield return WaitUntil(() => terrain.IsReadyForGameplay, WorldTimeoutSeconds, "Terrain never became ready.");

        IRuntimeDebugSettings debug = ResolveInScene<IRuntimeDebugSettings>(game);
        var results = new List<Result>();
        bool exportCapture = Environment.GetEnvironmentVariable("KERN_FRAME_CAPTURE") == "1";
        IFrameTelemetry? telemetry = exportCapture ? ResolveInScene<IFrameTelemetry>(game) : null;
        var captures = new List<CaptureWindow>();

        yield return Skip(WarmupFrames);
        yield return Measure("всё включено", results, telemetry, captures, "observational/all-enabled");

        debug.BypassTerrainDraw = true;
        yield return Skip(30);
        yield return Measure("без отрисовки террейна", results, telemetry, captures, "diagnostic/terrain-draw-bypass");
        debug.BypassTerrainDraw = false;

        debug.BypassCpuMeshRebuild = true;
        yield return Skip(30);
        yield return Measure("без пересборки террейна", results, telemetry, captures, "diagnostic/terrain-build-bypass");
        debug.BypassCpuMeshRebuild = false;

        yield return Skip(30);
        yield return Measure("всё включено (повтор)", results, telemetry, captures, "observational/all-enabled-repeat");

        // Formatting and IO happen only after all measured windows.
        foreach (CaptureWindow capture in captures)
        {
            capture.Write();
        }

        string report = BuildReport(results);
        DiagnosticReport.Write("Performance", "benchmark", "Бенчмарк кадра", report);
        Debug.Log($"[FrameBenchmark]\n{report}");

        Assert.That(results.All(r => r.MeanMs > 0), Is.True, "Frames were not measured.");
    }

    private static IEnumerator Measure(string scenario, List<Result> results,
        IFrameTelemetry? telemetry, List<CaptureWindow> captures, string captureScenario)
    {
        var recorders = new List<(string Name, ProfilerRecorder Recorder)>();
        foreach (string marker in _Markers)
        {
            // Маркеры из разных категорий: конструктор по имени ищет во всех.
            var recorder = new ProfilerRecorder(marker, MeasuredFrames, ProfilerRecorderOptions.StartImmediately);
            recorders.Add((marker, recorder));
        }

        var frameMs = new double[MeasuredFrames];
        CaptureWindow? capture = telemetry is null ? null : new CaptureWindow(captureScenario, MeasuredFrames)
        {
            Baseline = new CaptureSample(telemetry),
            FrameMs = frameMs,
        };
        for (int frame = 0; frame < MeasuredFrames; frame++)
        {
            yield return null;
            frameMs[frame] = Time.unscaledDeltaTime * 1000.0;
            if (capture is not null && telemetry is not null)
            {
                capture.Samples[frame] = new CaptureSample(telemetry);
            }
        }

        if (capture is not null)
        {
            captures.Add(capture);
        }

        var markers = new Dictionary<string, double>();
        foreach ((string name, ProfilerRecorder recorder) in recorders)
        {
            if (recorder.Valid && recorder.Count > 0)
            {
                double sum = 0;
                for (int i = 0; i < recorder.Count; i++)
                {
                    sum += recorder.GetSample(i).Value;
                }

                markers[name] = sum / recorder.Count * 1e-6;
            }

            recorder.Dispose();
        }

        double[] sorted = frameMs.OrderBy(value => value).ToArray();
        results.Add(new Result(
            scenario,
            frameMs.Average(),
            Percentile(sorted, 0.50),
            Percentile(sorted, 0.95),
            Percentile(sorted, 0.99),
            sorted[^1],
            markers));
    }

    private static string BuildReport(List<Result> results)
    {
        var report = new StringBuilder(4096);
        report.Append("Бенчмарк кадра, ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
            .Append(", ").Append(Screen.width).Append('×').Append(Screen.height)
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
                .Append(result.MaxMs.ToString("F2")).AppendLine();
            foreach (KeyValuePair<string, double> marker in result.Markers.OrderByDescending(entry => entry.Value))
            {
                report.Append("  ").Append(marker.Value.ToString("F3")).Append(" мс  ").AppendLine(marker.Key);
            }

            report.AppendLine();
        }

        return report.ToString();
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

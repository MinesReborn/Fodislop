#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Networking;
using Kern.Networking.Auth;
using Kern.World.Terrain;
using MinesServer.Networking.Connection.Client;
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
// между «всё» и «без отрисовки террейна» — цена террейна на экране; маркеры
// Kern.Terrain.* — его цена на процессоре. Результат пишется в
// Logs/benchmark_*.txt, чтобы сравнивать правки числами, а не на глаз.
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

        yield return Skip(WarmupFrames);
        yield return Measure("всё включено", results);

        debug.BypassTerrainDraw = true;
        yield return Skip(30);
        yield return Measure("без отрисовки террейна", results);
        debug.BypassTerrainDraw = false;

        debug.BypassCpuMeshRebuild = true;
        yield return Skip(30);
        yield return Measure("без пересборки террейна", results);
        debug.BypassCpuMeshRebuild = false;

        yield return Skip(30);
        yield return Measure("всё включено (повтор)", results);

        string report = BuildReport(results);
        string directory = Path.Combine(Application.dataPath, "..", "Logs");
        Directory.CreateDirectory(directory);
        string path = Path.GetFullPath(Path.Combine(directory, $"benchmark_{DateTime.Now:yyyyMMdd_HHmmss}.txt"));
        File.WriteAllText(path, report, new UTF8Encoding(false));
        Debug.Log($"[FrameBenchmark] {path}\n{report}");

        Assert.That(results.All(r => r.MeanMs > 0), Is.True, "Frames were not measured.");
    }

    private static IEnumerator Measure(string scenario, List<Result> results)
    {
        var recorders = new List<(string Name, ProfilerRecorder Recorder)>();
        foreach (string marker in _Markers)
        {
            // Маркеры из разных категорий: конструктор по имени ищет во всех.
            var recorder = new ProfilerRecorder(marker, MeasuredFrames, ProfilerRecorderOptions.StartImmediately);
            recorders.Add((marker, recorder));
        }

        var frameMs = new double[MeasuredFrames];
        for (int frame = 0; frame < MeasuredFrames; frame++)
        {
            yield return null;
            frameMs[frame] = Time.unscaledDeltaTime * 1000.0;
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

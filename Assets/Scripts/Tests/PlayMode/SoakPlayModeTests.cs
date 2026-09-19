#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kern;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.Game.Managers;
using Kern.Networking;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Chat;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Connection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;
using VContainer.Unity;
using Object = UnityEngine.Object;

namespace Kern.Tests.PlayMode;

// Долгие прогоны для ночной сборки (категория Soak, в обычный прогон не
// входят). Критерии из TODO «Системная проверка релиза»: 50 циклов Menu/Game
// не оставляют задач, подписок и объектов; p95 кадра и память к концу прогона
// не хуже начала больше чем на 5%.
[TestFixture]
[Category("Soak")]
public sealed class SoakPlayModeTests
{
    private const string TestDummyToken = "playmode-soak-token";
    private const int SceneCycles = 50;
    private const int ReconnectStorms = 5;
    private const int ReconnectsPerStorm = 20;
    private const int StreamingStops = 40;
    private const double AllowedRegression = 1.05;
    private const int TimeoutMilliseconds = 60 * 60 * 1000;

    private BootstrapLifetimeScope _bootstrap = null!;
    private DummyAuthenticationScope _authentication = null!;
    private IConnectionService _connection = null!;
    private IAsyncOperationSupervisor _operations = null!;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        _authentication = DummyAuthenticationScope.Seed(TestDummyToken);
        yield return PlayModeHarness.StartAtGateway();
        _bootstrap = PlayModeHarness.FindBootstrap()!;
        _connection = _bootstrap.Container.Resolve<IConnectionService>();
        _operations = _bootstrap.Container.Resolve<IAsyncOperationSupervisor>();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        yield return PlayModeHarness.Shutdown();
        _authentication.Restore();
    }

    [UnityTest]
    [Timeout(TimeoutMilliseconds)]
    public IEnumerator FiftyMenuGameCycles_LeaveNoTasksSubscriptionsOrObjects()
    {
        var stillSubscribed = new List<int>();
        var cycleFrames = new List<double>[SceneCycles];

        yield return PlayModeHarness.Await(
            _bootstrap.TransitionAsync(ProjectRuntimeContracts.SceneNames.MainMenu),
            PlayModeHarness.UITimeoutSeconds);

        // Первый цикл прогревает кэши ассетов и шейдеров; мерить от него.
        yield return MenuGameCycle(stillSubscribed, 0, new List<double>());
        yield return Settle();
        Snapshot baseline = Snapshot.Take(_operations);

        for (int cycle = 0; cycle < SceneCycles; cycle++)
        {
            cycleFrames[cycle] = new List<double>();
            yield return MenuGameCycle(stillSubscribed, cycle + 1, cycleFrames[cycle]);
        }

        // Петли офлайн-сервера выходят, досчитав свою задержку (до 12 с).
        yield return PlayModeHarness.WaitUntil(
            () => _operations.ActiveCount <= baseline.ActiveOperations,
            30f,
            $"Supervised operations accumulated: {DescribeOperations()}.");
        yield return Settle();
        Snapshot final = Snapshot.Take(_operations);
        Report("scene_cycles", baseline, final, cycleFrames);

        Assert.That(stillSubscribed, Is.Empty, "Packet handlers of unloaded game scenes kept their subscriptions (cycles listed).");
        Assert.That(final.ActiveOperations, Is.LessThanOrEqualTo(baseline.ActiveOperations), $"Supervised operations accumulated: {DescribeOperations()}.");
        Assert.That(final.LifetimeScopes, Is.EqualTo(baseline.LifetimeScopes), "Lifetime scopes accumulated.");
        Assert.That(final.GameObjects, Is.LessThanOrEqualTo(baseline.GameObjects), "Game objects accumulated.");
        Assert.That(final.RenderTextures, Is.LessThanOrEqualTo(baseline.RenderTextures), "Render textures accumulated.");
        AssertNoManagedMemoryGrowth(baseline.ManagedBytes, final.ManagedBytes, SceneCycles);
        AssertNoRegression("p95 frame time", P95(cycleFrames.Take(5)), P95(cycleFrames.Skip(SceneCycles - 5)));
    }

    [UnityTest]
    [Timeout(TimeoutMilliseconds)]
    public IEnumerator ReconnectStorms_EndInOneHealthySession()
    {
        yield return PlayModeHarness.EnterMainGame(_bootstrap);
        GameManager game = PlayModeHarness.RequireInGame<GameManager>();
        PacketHandler handler = PlayModeHarness.RequireInGame<PacketHandler>();
        yield return Settle();
        Snapshot baseline = Snapshot.Take(_operations);

        for (int storm = 0; storm < ReconnectStorms; storm++)
        {
            for (int attempt = 0; attempt < ReconnectsPerStorm; attempt++)
            {
                _connection.TriggerReconnect($"soak storm {storm}.{attempt}");
                yield return PlayModeHarness.Frames(2);
            }

            yield return WaitForHealthySession(game, $"Session did not recover after reconnect storm {storm}.");
        }

        int pings = 0;
        _connection.OnPacketReceived += OnPacket;
        try
        {
            yield return new WaitForSecondsRealtime(11f);
        }
        finally
        {
            _connection.OnPacketReceived -= OnPacket;
        }

        // Петли оборванных сессий выходят, досчитав свою задержку (до 12 с).
        // Утечка — это задачи, которые не уходят и после неё.
        yield return PlayModeHarness.WaitUntil(
            () => _operations.ActiveCount <= baseline.ActiveOperations,
            30f,
            $"Supervised operations accumulated: {_operations.ActiveCount} alive, {baseline.ActiveOperations} before the storms: {DescribeOperations()}.");
        yield return Settle();
        Snapshot final = Snapshot.Take(_operations);
        Report("reconnect_storm", baseline, final, []);

        // Пинг раз в пять секунд: за 11 секунд одна сессия шлёт не больше
        // трёх. Больше — живы петли сессий, оборванных штормом.
        Assert.That(pings, Is.InRange(1, 3), "Loops of stormed sessions survived.");
        Assert.That(handler.IsSubscribed, Is.True);
        Assert.That(PlayModeHarness.RequireInGame<PacketHandler>(), Is.SameAs(handler));
        AssertNoRegression("managed memory", baseline.ManagedBytes, final.ManagedBytes);

        void OnPacket(ServerPacket packet)
        {
            if (packet.Payload is PingPacket)
            {
                pings++;
            }
        }
    }

    [UnityTest]
    [Timeout(TimeoutMilliseconds)]
    public IEnumerator StreamingAcrossTheMap_StaysWithinCacheAndMemory()
    {
        yield return PlayModeHarness.EnterMainGame(_bootstrap);
        IWorldDataStorage storage = PlayModeHarness.RequireInGame<IWorldDataStorage>();
        IWorldLayer<CellType> layer = storage.CellLayer ?? throw new AssertionException("World layer is not ready.");
        int width = layer.WidthChunks * layer.ChunkSize;
        int height = layer.HeightChunks * layer.ChunkSize;
        (int X, int Y)[] stops = StreamingRoute(width, height);

        var firstLap = new List<double>();
        var secondLap = new List<double>();
        yield return Tour(storage, stops, firstLap);
        yield return Settle();
        Snapshot baseline = Snapshot.Take(_operations);

        yield return Tour(storage, stops, secondLap);

        // Грязные чанки вытесняются только после записи на диск, при
        // следующей подкачке.
        yield return PlayModeHarness.Await(
            PlayModeHarness.RequireInGame<IWorldPersistence>().FlushAsync(durable: false),
            PlayModeHarness.WorldTimeoutSeconds);
        yield return Tour(storage, [stops[0]], new List<double>());
        yield return Settle();
        Snapshot final = Snapshot.Take(_operations);
        Report("map_streaming", baseline, final, [firstLap, secondLap]);

        Assert.That(
            layer.GetLoadedCount(),
            Is.LessThanOrEqualTo(ProjectRuntimeContracts.World.ResidentChunkCacheCapacity),
            "Streaming grew the chunk cache past its capacity.");
        Assert.That(final.RenderTextures, Is.LessThanOrEqualTo(baseline.RenderTextures), "Render textures accumulated while streaming.");
        AssertNoRegression("managed memory", baseline.ManagedBytes, final.ManagedBytes);
        AssertNoRegression("p95 frame time", P95([firstLap]), P95([secondLap]));
    }

    // Из меню в игру и обратно в меню.
    // Обработчик пакетов проверяется сразу после выгрузки и не хранится:
    // ссылка на него удержала бы всю игровую сессию и сама стала бы утечкой.
    private IEnumerator MenuGameCycle(List<int> stillSubscribed, int cycle, List<double> frames)
    {
        yield return PlayModeHarness.Await(
            _bootstrap.TransitionAsync(ProjectRuntimeContracts.SceneNames.MainGame),
            PlayModeHarness.WorldTimeoutSeconds);
        var handler = new WeakReference<PacketHandler>(PlayModeHarness.RequireInGame<PacketHandler>());
        yield return MeasureFrames(frames, 120);
        yield return PlayModeHarness.Await(
            _bootstrap.TransitionAsync(ProjectRuntimeContracts.SceneNames.MainMenu),
            PlayModeHarness.UITimeoutSeconds);
        if (handler.TryGetTarget(out PacketHandler? previous) && previous.IsSubscribed)
        {
            stillSubscribed.Add(cycle);
        }
    }

    private IEnumerator Tour(IWorldDataStorage storage, (int X, int Y)[] stops, List<double> frames)
    {
        foreach ((int x, int y) in stops)
        {
            _connection.Send(new ClientPacket(0, new SendChatMessagePacket("global", $"/tp {x} {y}")));
            yield return PlayModeHarness.WaitUntil(
                () => storage.TryGetCell(x, y, out _),
                PlayModeHarness.WorldTimeoutSeconds,
                $"Chunks around ({x}, {y}) never streamed in.");
            yield return MeasureFrames(frames, 30);
        }
    }

    // Змейка по всей карте с отступом от краёв: каждая остановка далеко от
    // предыдущей, поэтому подкачка идёт с нуля, а старые чанки вытесняются.
    private static (int X, int Y)[] StreamingRoute(int width, int height)
    {
        const int Columns = 4;
        int rows = StreamingStops / Columns;
        int margin = ProjectRuntimeContracts.World.ChunkSize * 4;
        var stops = new (int X, int Y)[Columns * rows];
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < Columns; column++)
            {
                int ordered = row % 2 == 0 ? column : Columns - 1 - column;
                int x = margin + ((width - (2 * margin)) * ordered / (Columns - 1));
                int y = margin + ((height - (2 * margin)) * row / (rows - 1));
                stops[(row * Columns) + column] = (Math.Min(x, ushort.MaxValue), Math.Min(y, ushort.MaxValue));
            }
        }

        return stops;
    }

    private IEnumerator WaitForHealthySession(GameManager game, string failureMessage)
    {
        bool worldInitReceived = false;
        _connection.OnPacketReceived += OnPacket;
        try
        {
            yield return PlayModeHarness.WaitUntil(
                () => worldInitReceived && _connection.IsConnected && game.IsWorldLoaded,
                PlayModeHarness.WorldTimeoutSeconds,
                failureMessage);
        }
        finally
        {
            _connection.OnPacketReceived -= OnPacket;
        }

        void OnPacket(ServerPacket packet)
        {
            worldInitReceived |= packet.Payload is WorldInitPacket;
        }
    }

    private string DescribeOperations() =>
        _operations is AsyncOperationSupervisor supervisor
            ? string.Join(", ", supervisor.ActiveOperationNames.GroupBy(name => name).Select(group => $"{group.Key}×{group.Count()}"))
            : "names unavailable";

    private static IEnumerator MeasureFrames(List<double> frames, int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return null;
            frames.Add(Time.unscaledDeltaTime * 1000.0);
        }
    }

    private static IEnumerator Settle()
    {
        yield return PlayModeHarness.Frames(10);
        AsyncOperation unload = Resources.UnloadUnusedAssets();
        while (!unload.isDone)
        {
            yield return null;
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        yield return PlayModeHarness.Frames(5);
    }

    private static double P95(IEnumerable<List<double>> samples)
    {
        double[] ordered = samples.SelectMany(sample => sample).OrderBy(value => value).ToArray();
        Assert.That(ordered, Is.Not.Empty);
        return ordered[(int)Math.Ceiling(ordered.Length * 0.95) - 1];
    }

    // Тест-раннер хранит каждое сообщение лога за время теста вместе со
    // стеком: на цикл Menu/Game это около 120 КБ, которые принадлежат не игре.
    // Удержанная игровая сессия — десятки мегабайт на цикл, её этот запас не
    // скрывает.
    private const long TestRunnerLogBytesPerCycle = 256 * 1024;

    private static void AssertNoManagedMemoryGrowth(long baseline, long final, int cycles)
    {
        long allowed = (long)(baseline * AllowedRegression) + (TestRunnerLogBytesPerCycle * cycles);
        Assert.That(
            final,
            Is.LessThanOrEqualTo(allowed),
            $"managed memory grew from {baseline / 1048576.0:F1} MB to {final / 1048576.0:F1} MB over {cycles} cycles " +
            $"(limit {allowed / 1048576.0:F1} MB).");
    }

    private static void AssertNoRegression(string metric, double baseline, double final)
    {
        Assert.That(
            final,
            Is.LessThanOrEqualTo(baseline * AllowedRegression),
            $"{metric} regressed from {baseline:F2} to {final:F2} (limit {(AllowedRegression - 1) * 100:F0}%).");
    }

    // Метрики прогона сохраняются рядом с журналом, чтобы ночную сборку можно
    // было сравнивать с предыдущими.
    private static void Report(string name, Snapshot baseline, Snapshot final, IReadOnlyList<List<double>> frames)
    {
        string directory = Path.Combine(Application.persistentDataPath, "Soak");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"{name}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
        string p95 = frames.Count > 0 && frames.Any(sample => sample.Count > 0)
            ? P95(frames.Where(sample => sample.Count > 0)).ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
            : "null";
        File.WriteAllText(
            path,
            "{\n" +
            $"  \"test\": \"{name}\",\n" +
            $"  \"baseline\": {baseline.ToJson()},\n" +
            $"  \"final\": {final.ToJson()},\n" +
            $"  \"p95FrameMs\": {p95}\n" +
            "}\n");
        Debug.Log($"[Soak] {name}: {path}");
    }

    private readonly struct Snapshot
    {
        private Snapshot(int activeOperations, int lifetimeScopes, int gameObjects, int renderTextures, long managedBytes)
        {
            ActiveOperations = activeOperations;
            LifetimeScopes = lifetimeScopes;
            GameObjects = gameObjects;
            RenderTextures = renderTextures;
            ManagedBytes = managedBytes;
        }

        public int ActiveOperations { get; }

        public int LifetimeScopes { get; }

        public int GameObjects { get; }

        public int RenderTextures { get; }

        public long ManagedBytes { get; }

        public static Snapshot Take(IAsyncOperationSupervisor operations) =>
            new(
                operations.ActiveCount,
                Object.FindObjectsByType<LifetimeScope>(FindObjectsInactive.Include).Length,
                Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include).Length,
                Resources.FindObjectsOfTypeAll<RenderTexture>().Length,
                GC.GetTotalMemory(forceFullCollection: true));

        public string ToJson() =>
            $"{{ \"activeOperations\": {ActiveOperations}, \"lifetimeScopes\": {LifetimeScopes}, " +
            $"\"gameObjects\": {GameObjects}, \"renderTextures\": {RenderTextures}, \"managedBytes\": {ManagedBytes} }}";
    }
}

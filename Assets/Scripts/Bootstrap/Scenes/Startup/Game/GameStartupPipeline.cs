#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core.Interfaces;
using Kern.Game;
using Kern.Networking;
using Kern.Networking.Connection;
using Kern.Rendering;
using Kern.Rendering.PostProcessing;
using Kern.UI.Inventory;
using Kern.World;
using Kern.World.Lighting;
using Kern.World.Terrain;
using UnityEngine;
using UnityEngine.UIElements;

using Kern.Game.Session;
using Kern.Networking.Processing;
using Kern.UI.HUD;
using Kern.UI.Screens;
using Kern.Core;


namespace Kern.Bootstrap.Scenes;

internal enum StartupIssueSeverity
{
    Critical,
    Degraded,
}

internal readonly record struct StartupIssue(
    string System,
    StartupIssueSeverity Severity,
    string Message,
    Exception? Exception = null);

internal sealed class GameStartupReport
{
    private readonly List<StartupIssue> _issues = new();

    public IReadOnlyList<StartupIssue> Issues => _issues;

    public void Critical(string system, string message, Exception? exception = null)
    {
        _issues.Add(new StartupIssue(system, StartupIssueSeverity.Critical, message, exception));
    }

    public void Degraded(string system, string message, Exception? exception = null)
    {
        _issues.Add(new StartupIssue(system, StartupIssueSeverity.Degraded, message, exception));
    }

    public void RunCritical(string system, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            Critical(system, exception.Message, exception);
        }
    }

    public bool HasCritical => _issues.Exists(issue => issue.Severity == StartupIssueSeverity.Critical);

    public void ThrowIfCritical()
    {
        List<StartupIssue> critical = _issues.FindAll(
            issue => issue.Severity == StartupIssueSeverity.Critical);
        if (critical.Count == 0)
        {
            return;
        }

        List<Exception> causes = critical.ConvertAll(issue =>
            issue.Exception ?? new InvalidOperationException(
                $"{issue.System}: {issue.Message}"));
        throw new InvalidOperationException(
            $"[GameBootstrap] FATAL STARTUP FAILURE: {critical.Count} critical systems failed:\n- " +
            string.Join("\n- ", critical.ConvertAll(
                issue => $"{issue.System}: {issue.Message}")),
            new AggregateException("Critical startup failures.", causes));
    }
}

public sealed class GameInfrastructureStartup
{
    private readonly IClientConfigManager _clientConfig;
    private readonly NetworkService _network;
    private readonly PacketHandler _packetHandler;
    private readonly IAssetSubscription _assetSubscription;
    private readonly TerrainRenderer _terrain;

    public GameInfrastructureStartup(
        IClientConfigManager clientConfig,
        NetworkService network,
        PacketHandler packetHandler,
        IAssetSubscription assetSubscription,
        TerrainRenderer terrain)
    {
        _clientConfig = clientConfig;
        _network = network;
        _packetHandler = packetHandler;
        _assetSubscription = assetSubscription;
        _terrain = terrain;
    }

    internal void Initialize(GameStartupReport report)
    {
        report.RunCritical("client_config", _clientConfig.EnsureInitialized);
        report.RunCritical("network_subscription", _network.EnsureConnectionSubscription);
        if (!_network.IsConnectionSubscriptionEstablished && Application.isPlaying)
        {
            report.Critical("network_subscription", "NetworkService subscription was not established.");
        }

        report.RunCritical("packet_handler", _packetHandler.EnsureInitialized);
        report.RunCritical("asset_subscription", _assetSubscription.EnsureAssetSubscription);
        if (!_assetSubscription.IsAssetSubscriptionEstablished && Application.isPlaying)
        {
            report.Critical("asset_subscription", "ClientAssetLoader subscription was not established.");
        }

        report.RunCritical("terrain_subscription", _terrain.EnsureSubscriptions);
    }
}

public sealed class GamePresentationStartup
{
    private readonly IAudioSystem _audioSystem;
    private readonly TerrainRenderer _terrain;
    private readonly PostProcessController _postProcess;
    private readonly LightingEngine _lighting;
    private readonly SurfaceRenderer _surface;
    private readonly GameManager _gameManager;
    private readonly PlayerHUDView _playerHud;
    private readonly InventoryView _inventory;
    private readonly UIDocument _uiDocument;
    private readonly IClientConfigManager _clientConfig;

    public GamePresentationStartup(
        IAudioSystem audioSystem,
        TerrainRenderer terrain,
        PostProcessController postProcess,
        LightingEngine lighting,
        SurfaceRenderer surface,
        GameManager gameManager,
        PlayerHUDView playerHud,
        InventoryView inventory,
        UIDocument uiDocument,
        IClientConfigManager clientConfig)
    {
        _audioSystem = audioSystem;
        _terrain = terrain;
        _postProcess = postProcess;
        _lighting = lighting;
        _surface = surface;
        _gameManager = gameManager;
        _playerHud = playerHud;
        _inventory = inventory;
        _uiDocument = uiDocument;
        _clientConfig = clientConfig;
    }

    internal void Initialize(GameStartupReport report)
    {
        report.RunCritical("terrain_settings", () => _terrain.ApplyClientConfig());
        report.RunCritical("post_process", () =>
        {
            // Подготовка и применение разделены: первая идемпотентна и
            // молчит, если всё уже готово, второе стартовому конвейеру
            // нужно всегда — он обязан довести конфиг до подсистем.
            _postProcess.EnsureVolumeSetup();
            _postProcess.ApplyClientConfig();
        });
        report.RunCritical("lighting", () => _lighting.EnsureInitialized());
        report.RunCritical("surface_settings", () => _surface.ApplyClientConfig());
        report.RunCritical("ui_scale", () =>
        {
            Kern.UI.Screens.UIScreen.Bind(_uiDocument, _clientConfig.Config.Interface.UIScale, _clientConfig.Config.Interface.TextScale);
        });
        report.RunCritical("game_ui", _gameManager.EnsureUISetup);
        report.RunCritical("player_hud", _playerHud.EnsureInitialized);
        report.RunCritical("inventory", _inventory.EnsureInitialized);

        ValidateShader(report, ShaderNames.Terrain);
        ValidateShader(report, ShaderNames.WorldSurface);
        ValidateShader(report, ShaderNames.WorldEntity);
        ValidateShader(report, ShaderNames.MissionVirtualRing);

        var lightingCompute = Resources.Load<ComputeShader>(ResourcePaths.WorldLightingCompute);
        if (lightingCompute == null)
        {
            report.Critical(
                "world_lighting_compute",
                $"Resources/{ResourcePaths.WorldLightingCompute}.compute is missing.");
        }
        else if (!lightingCompute.HasKernel("SolveCascade") || !lightingCompute.HasKernel("CompositeLighting"))
        {
            report.Critical(
                "world_lighting_compute",
                "WorldLighting.compute has invalid or uncompiled kernels.");
        }
    }

    internal UniTask BeginLightingOrientationValidationAsync() =>
        _lighting.BeginOrientationValidationAsync();

    internal async UniTask WaitUntilReadyAsync(
        SceneTransitionTicket ticket,
        GameStartupReport report,
        CancellationToken cancellationToken)
    {
        await WaitForWorldReadyAsync(ticket, cancellationToken);
        // AudioSystem starts its own supervised bank-loading operation in
        // Start(). Scene presentation must not depend on native FMOD sample
        // callbacks: missing/slow banks are a degraded audio state, not a
        // reason to hold the world transition open.
        if (_audioSystem.IsDegraded)
        {
            report.Degraded(
                "audio",
                "Required FMOD banks are unavailable; continuing without complete audio.");
        }
    }

    private async UniTask WaitForWorldReadyAsync(
        SceneTransitionTicket ticket,
        CancellationToken cancellationToken)
    {
        if (_gameManager.IsWorldLoaded)
        {
            return;
        }

        var completion = new UniTaskCompletionSource();
        void OnWorldLoaded()
        {
            completion.TrySetResult();
        }

        _gameManager.OnWorldLoaded += OnWorldLoaded;
        try
        {
            if (_gameManager.IsWorldLoaded)
            {
                return;
            }

            UniTask worldReady = completion.Task.AttachExternalCancellation(cancellationToken);
            UniTask transitionFailed = ticket.WaitForFailureAsync().AttachExternalCancellation(cancellationToken);
            int winner = await UniTask.WhenAny(worldReady, transitionFailed);
            cancellationToken.ThrowIfCancellationRequested();
            if (winner == 1)
            {
                await ticket.WaitForPresentationAsync().AttachExternalCancellation(cancellationToken);
            }
        }
        finally
        {
            _gameManager.OnWorldLoaded -= OnWorldLoaded;
        }
    }

    private static void ValidateShader(GameStartupReport report, string shaderName)
    {
        Shader? shader = shaderName == ShaderNames.MissionVirtualRing
            ? Resources.Load<Shader>(ResourcePaths.MissionVirtualRingShader)
            : Shader.Find(shaderName);
        if (shader == null || !shader.isSupported)
        {
            report.Critical("shader", $"Required shader '{shaderName}' is missing or unsupported.");
        }
    }
}

public sealed class GameStartupPipeline
{
    private readonly GameInfrastructureStartup _infrastructure;
    private readonly GamePresentationStartup _presentation;
    private readonly IConnectionService _connection;

    public GameStartupPipeline(
        GameInfrastructureStartup infrastructure,
        GamePresentationStartup presentation,
        IConnectionService connection)
    {
        _infrastructure = infrastructure;
        _presentation = presentation;
        _connection = connection;
    }

    // Инфраструктура (подписки на пакеты, ассеты, террейн) готова — соединение можно открывать, не дожидаясь презентации:
    // рукопожатие, вход и ответ мира идут по сети, пока свет и интерфейс инициализируются. Пакеты удержаны до
    // CompletePresentation: обработчики мира рассчитывают на готовый интерфейс.
    // Соединение может быть уже открыто спуском (авторизация до входа в мир
    // проходила в сцене меню): тогда просто продолжаем удержание до релиза.
    internal GameStartupReport InitializeInfrastructure()
    {
        var report = new GameStartupReport();
        _infrastructure.Initialize(report);
        if (!report.HasCritical)
        {
            _connection.HoldInboundPackets();
            if (!_connection.IsConnected)
            {
                _connection.Connect();
            }
        }

        return report;
    }

    internal void InitializePresentation(GameStartupReport report)
    {
        _presentation.Initialize(report);
        report.ThrowIfCritical();
        _connection.ReleaseInboundPackets();
        Debug.Log("[GameBootstrap] Startup validation PASSED — MainGame contract is ready");
    }

    internal UniTask PreparePersistentDataAsync(CancellationToken cancellationToken) =>
        MapStorage.PrepareExistingWorldCachesAsync(cancellationToken);

    internal UniTask BeginLightingOrientationValidationAsync() =>
        _presentation.BeginLightingOrientationValidationAsync();

    internal async UniTask WaitUntilReadyAsync(
        SceneTransitionTicket ticket,
        GameStartupReport report,
        CancellationToken cancellationToken)
    {
        var disconnected = new UniTaskCompletionSource();
        void OnTransportDisconnected() => disconnected.TrySetResult();

        _connection.OnTransportDisconnected += OnTransportDisconnected;
        try
        {
            if (_connection.IsAutoReconnecting && !_connection.IsConnected)
            {
                throw new ConnectionStartupInterruptedException();
            }

            using CancellationTokenSource readinessCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // WhenAny awaits each input source to observe completion. The winning
            // task is then awaited below as well so its exception is propagated
            // before the world is marked ready. Memoize this single-consumer
            // UniTask because the transition path intentionally observes it twice.
            UniTask presentationReady = _presentation.WaitUntilReadyAsync(
                ticket,
                report,
                readinessCancellation.Token).Preserve();
            UniTask connectionLost = disconnected.Task.AttachExternalCancellation(cancellationToken);
            int winner = await UniTask.WhenAny(presentationReady, connectionLost);
            cancellationToken.ThrowIfCancellationRequested();
            if (winner == 1)
            {
                readinessCancellation.Cancel();
                try
                {
                    await presentationReady;
                }
                catch (OperationCanceledException)
                {
                    // The competing readiness wait was stopped after transport loss.
                }

                throw new ConnectionStartupInterruptedException();
            }

            await presentationReady;
            if (!_connection.IsConnected || _connection.IsAutoReconnecting)
            {
                throw new ConnectionStartupInterruptedException();
            }

            _connection.MarkWorldReady();
        }
        finally
        {
            _connection.OnTransportDisconnected -= OnTransportDisconnected;
        }
    }
}

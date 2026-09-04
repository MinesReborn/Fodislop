#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core.Interfaces;
using Fodinae.Game;
using Fodinae.Game.Managers;
using Fodinae.Networking;
using Fodinae.Networking.Connection;
using Fodinae.Rendering;
using Fodinae.Rendering.PostProcessing;
using Fodinae.UI.HUD.Inventory.View;
using Fodinae.UI.HUD.Player.View;
using Fodinae.World;
using Fodinae.World.Lighting;
using Fodinae.World.Terrain;
using UnityEngine;

namespace Fodinae.Core;

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
    private readonly List<StartupIssue> _issues = [];

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

    public GamePresentationStartup(
        IAudioSystem audioSystem,
        TerrainRenderer terrain,
        PostProcessController postProcess,
        LightingEngine lighting,
        SurfaceRenderer surface,
        GameManager gameManager,
        PlayerHUDView playerHud,
        InventoryView inventory)
    {
        _audioSystem = audioSystem;
        _terrain = terrain;
        _postProcess = postProcess;
        _lighting = lighting;
        _surface = surface;
        _gameManager = gameManager;
        _playerHud = playerHud;
        _inventory = inventory;
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
        report.RunCritical("game_ui", _gameManager.EnsureUISetup);
        report.RunCritical("player_hud", _playerHud.EnsureInitialized);
        report.RunCritical("inventory", _inventory.EnsureInitialized);

        ValidateShader(report, ProjectRuntimeContracts.ShaderNames.Terrain);
        ValidateShader(report, ProjectRuntimeContracts.ShaderNames.DynamicEmission);
        ValidateShader(report, ProjectRuntimeContracts.ShaderNames.WorldSurface);
        ValidateShader(report, ProjectRuntimeContracts.ShaderNames.WorldEntity);

        var lightingCompute = Resources.Load<ComputeShader>(ProjectRuntimeContracts.ResourcePaths.WorldLightingCompute);
        if (lightingCompute == null)
        {
            report.Critical(
                "world_lighting_compute",
                $"Resources/{ProjectRuntimeContracts.ResourcePaths.WorldLightingCompute}.compute is missing.");
        }
        else if (!lightingCompute.HasKernel("SolveCascade") || !lightingCompute.HasKernel("CompositeLighting"))
        {
            report.Critical(
                "world_lighting_compute",
                "WorldLighting.compute has invalid or uncompiled kernels.");
        }
    }

    internal async UniTask WaitUntilReadyAsync(
        SceneTransitionTicket ticket,
        GameStartupReport report,
        CancellationToken cancellationToken)
    {
        await WaitForWorldReadyAsync(ticket, cancellationToken);
        await _audioSystem.WaitUntilBanksReadyAsync(cancellationToken);
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
            if (winner == 1)
            {
                await ticket.WaitForPresentationAsync();
            }
        }
        finally
        {
            _gameManager.OnWorldLoaded -= OnWorldLoaded;
        }
    }

    private static void ValidateShader(GameStartupReport report, string shaderName)
    {
        Shader? shader = Shader.Find(shaderName);
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

    internal GameStartupReport Initialize()
    {
        var report = new GameStartupReport();
        _infrastructure.Initialize(report);
        _presentation.Initialize(report);
        report.ThrowIfCritical();
        _connection.Connect();
        Debug.Log("[GameBootstrap] Startup validation PASSED — MainGame contract is ready");
        return report;
    }

    internal UniTask WaitUntilReadyAsync(
        SceneTransitionTicket ticket,
        GameStartupReport report,
        CancellationToken cancellationToken)
    {
        return _presentation.WaitUntilReadyAsync(ticket, report, cancellationToken);
    }
}

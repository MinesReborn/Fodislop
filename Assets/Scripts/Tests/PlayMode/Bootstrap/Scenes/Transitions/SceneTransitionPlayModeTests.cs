#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Game;
using UnityEngine.InputSystem;
using Kern.Networking;
using Kern.Networking.Connection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VContainer;
using VContainer.Unity;
using Object = UnityEngine.Object;

using Kern.Bootstrap.Scenes;
using Kern.Game.Entities.Robots;
using Kern.Game.Session;
using Kern.Networking.Offline;
using Kern.Networking.Processing;

namespace Kern.Tests.PlayMode;

[TestFixture]
public sealed class SceneTransitionPlayModeTests
{
    private const string TestDummyToken = "playmode-scene-transition-token";
    private BootstrapLifetimeScope _bootstrap = null!;
    private DummyAuthenticationScope _authentication = null!;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        yield return DestroyPersistentBootstrapIfPresent();
        _authentication = DummyAuthenticationScope.Seed(TestDummyToken);
        yield return PlayModeHarness.StartAtGateway();
        _bootstrap = FindBootstrap()!;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        yield return PlayModeHarness.Shutdown();
        _authentication.Restore();
    }

    [UnityTest]
    public IEnumerator BootstrapToGateway_WaitsForConcreteGatewayUI()
    {
        Scene gateway = SceneManager.GetSceneByName("Gateway");
        Assert.That(_bootstrap.CurrentSceneName, Is.EqualTo("Gateway"));
        Assert.That(gateway.isLoaded, Is.True);
        Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(gateway));
        Assert.That(CountScopes(gateway), Is.EqualTo(1));
        Assert.That(HasNamedUIElement(gateway, "GatewayRoot"), Is.True);
        yield break;
    }

    [UnityTest]
    public IEnumerator GatewayToMainMenu_UnloadsGatewayOnlyAfterMenuUIExists()
    {
        bool menuUIObservedBeforeCompletion = false;
        UniTask transition = _bootstrap.TransitionAsync("MainMenu").Preserve();
        while (!transition.Status.IsCompleted())
        {
            Scene menuDuringTransition = SceneManager.GetSceneByName("MainMenu");
            menuUIObservedBeforeCompletion |= menuDuringTransition.isLoaded &&
                HasNamedUIElement(menuDuringTransition, "MainMenuContainer");
            yield return null;
        }

        transition.GetAwaiter().GetResult();
        Scene menu = SceneManager.GetSceneByName("MainMenu");
        Assert.That(menuUIObservedBeforeCompletion, Is.True);
        Assert.That(SceneManager.GetSceneByName("Gateway").isLoaded, Is.False);
        Assert.That(menu.isLoaded, Is.True);
        Assert.That(CountScopes(menu), Is.EqualTo(1));
        Assert.That(HasNamedUIElement(menu, "MainMenuContainer"), Is.True);
    }

    [UnityTest]
    public IEnumerator MainMenuToMainGame_KeepsLoaderSceneUntilWorldReady()
    {
        yield return Await(_bootstrap.TransitionAsync("MainMenu"), PlayModeHarness.UITimeoutSeconds);
        bool menuObservedWhileWorldNotReady = false;
        UniTask transition = _bootstrap.TransitionAsync("MainGame").Preserve();
        float deadline = Time.realtimeSinceStartup + PlayModeHarness.WorldTimeoutSeconds;
        while (!transition.Status.IsCompleted() && Time.realtimeSinceStartup < deadline)
        {
            Scene game = SceneManager.GetSceneByName("MainGame");
            GameManager? manager = ResolveInScene<GameManager>(game);
            if (manager != null && !manager.IsWorldLoaded)
            {
                menuObservedWhileWorldNotReady |= SceneManager.GetSceneByName("MainMenu").isLoaded;
            }

            yield return null;
        }

        Assert.That(transition.Status.IsCompleted(), Is.True, "MainGame transition timed out.");
        transition.GetAwaiter().GetResult();
        GameManager gameManager = ResolveInScene<GameManager>(SceneManager.GetSceneByName("MainGame"))!;
        Assert.That(menuObservedWhileWorldNotReady, Is.True);
        Assert.That(gameManager, Is.Not.Null);
        Assert.That(gameManager.IsWorldLoaded, Is.True);
        Assert.That(SceneManager.GetSceneByName("MainMenu").isLoaded, Is.False);
    }

    [UnityTest]
    public IEnumerator MainMenuPlayCommand_PreparesLocalMapAndEntersWorld()
    {
        IClientConfigManager config = _bootstrap.Container.Resolve<IClientConfigManager>();
        config.UpdateSection(value => value.Connection, settings => settings.UseDummyConnection = true);
        yield return Await(_bootstrap.TransitionAsync("MainMenu"), PlayModeHarness.UITimeoutSeconds);

        UIDocument document = FindComponentInScene<UIDocument>(SceneManager.GetSceneByName("MainMenu"))!;
        Button play = document.rootVisualElement.Q<Button>("PlayButton");
        Assert.That(play, Is.Not.Null);
        using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
        {
            submit.target = play;
            play.SendEvent(submit);
        }
        yield return PlayModeHarness.WaitUntil(
            () => _bootstrap.CurrentSceneName == SceneNames.MainGame,
            PlayModeHarness.WorldTimeoutSeconds,
            "The menu Play command did not enter the local world.");

        GameManager manager = PlayModeHarness.RequireInGame<GameManager>();
        Assert.That(manager.IsWorldLoaded, Is.True);
        Assert.That(_bootstrap.Container.Resolve<IConnectionService>().IsOffline, Is.True);
        Assert.That(SceneManager.GetSceneByName("MainMenu").isLoaded, Is.False);
    }

    [UnityTest]
    public IEnumerator RetryPending_DummyWorldEntryClearsRetryAndKeepsOneGameScope()
    {
        IClientConfigManager config = _bootstrap.Container.Resolve<IClientConfigManager>();
        config.UpdateSection(value => value.Connection, settings => settings.UseDummyConnection = true);
        yield return Await(_bootstrap.TransitionAsync("MainMenu"), PlayModeHarness.UITimeoutSeconds);
        ConnectionManager connection = _bootstrap.Container.Resolve<ConnectionManager>();
        connection.HandleServerReconnect();
        Assert.That(connection.IsConnected, Is.False);
        Assert.That(connection.IsAutoReconnecting, Is.True);
        Assert.Throws<ConnectionStartupInterruptedException>(connection.MarkWorldReady);

        yield return Await(_bootstrap.TransitionAsync("MainGame"), PlayModeHarness.WorldTimeoutSeconds);

        Assert.That(connection.IsOffline, Is.True);
        Assert.That(connection.IsConnected, Is.True);
        Assert.That(connection.IsAutoReconnecting, Is.False);
        Assert.That(connection.IsWorldStartupPending, Is.False);
        Assert.That(PlayModeHarness.RequireInGame<GameManager>().IsWorldLoaded, Is.True);
        Assert.That(CountScopes(SceneManager.GetSceneByName("MainGame")), Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator DummyDisconnectDuringHandshake_DoesNotEnterWorld()
    {
        IClientConfigManager config = _bootstrap.Container.Resolve<IClientConfigManager>();
        config.UpdateSection(value => value.Connection, settings => settings.UseDummyConnection = true);
        yield return Await(_bootstrap.TransitionAsync("MainMenu"), PlayModeHarness.UITimeoutSeconds);
        ConnectionManager connection = _bootstrap.Container.Resolve<ConnectionManager>();
        IDummyScenarioSettings scenario = _bootstrap.Container.Resolve<IDummyScenarioSettings>();
        scenario.Scenario = DummyScenario.DisconnectDuringHandshake;
        LogAssert.Expect(LogType.Error,
            "[GameBootstrap] Startup failed in scene 'MainGame' during transition phase 'StartupReady'.");
        LogAssert.Expect(LogType.Exception,
            new Regex("^ConnectionStartupInterruptedException: Server connection was lost during world startup\\."));

        UniTask transition = _bootstrap.TransitionAsync("MainGame").Preserve();
        yield return AwaitFailure(transition, PlayModeHarness.WorldTimeoutSeconds);

        Assert.That(connection.IsConnected, Is.False);
        Assert.That(_bootstrap.CurrentSceneName, Is.EqualTo("MainMenu"));
        Assert.That(SceneManager.GetSceneByName("MainGame").isLoaded, Is.False);
        Assert.Throws<ConnectionStartupInterruptedException>(connection.MarkWorldReady);
        connection.Disconnect();
        scenario.Scenario = DummyScenario.HappyPath;
    }

    [UnityTest]
    public IEnumerator DummyMoveUp_ReleaseKeepsFacingUpAfterServerSnapshot()
    {
        IClientConfigManager config = _bootstrap.Container.Resolve<IClientConfigManager>();
        config.UpdateSection(value => value.Connection, settings => settings.UseDummyConnection = true);
        yield return PlayModeHarness.EnterMainGame(_bootstrap);
        ILocalPlayer player = PlayModeHarness.RequireInGame<ILocalPlayerState>().Current!;
        Robot robot = player.GetComponent<Robot>();
        // Exercise movement away from the offline training gun and teleporter.
        _bootstrap.Container.Resolve<DummyConnection>().SendAsync(new MinesServer.Networking.Client.Packets.ClientPacket(
            0, new MinesServer.Networking.Client.Packets.Chat.SendChatMessagePacket("FED", "/tp 200 200")));
        yield return PlayModeHarness.WaitUntil(() => player.Position == new Vector2Int(200, 200), 3f,
            "The offline movement fixture did not reach its test area.");
        player.IgnoreCollision = true;
        using var keyboard = new VirtualKeyboard();

        keyboard.Hold(Key.LeftShift);
        keyboard.Hold(Key.A);
        yield return PlayModeHarness.Frames(3);
        keyboard.Release(Key.A);
        keyboard.Release(Key.LeftShift);
        yield return PlayModeHarness.Frames(2);
        Assert.That(Mathf.Abs(Mathf.DeltaAngle(robot.TargetAngle, 180f)), Is.LessThan(0.1f));

        Vector2Int start = player.Position;
        keyboard.Hold(Key.W);
        yield return PlayModeHarness.WaitUntil(() => player.Position.y < start.y, 3f,
            "The production input path did not move the player up.");
        keyboard.Release(Key.W);
        float deadline = Time.realtimeSinceStartup + 0.4f;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(robot.TargetAngle, 90f)), Is.LessThan(0.1f),
                "A delayed server snapshot turned the stopped player away from up.");
        }
    }

    [UnityTest]
    [Timeout(90_000)]
    public IEnumerator MainGameWorldInitializationTimeout_DisconnectsStaleTransport()
    {
        yield return Await(_bootstrap.TransitionAsync("MainMenu"), PlayModeHarness.UITimeoutSeconds);

        ConnectionManager connection = _bootstrap.Container.Resolve<ConnectionManager>();
        IDummyScenarioSettings scenario = _bootstrap.Container.Resolve<IDummyScenarioSettings>();
        scenario.Scenario = DummyScenario.WorldInitializationTimeout;

        LogAssert.Expect(LogType.Error,
            "[GameBootstrap] Startup failed in scene 'MainGame' during transition phase 'Failed'.");
        LogAssert.Expect(LogType.Exception,
            "TimeoutException: Scene transition to 'MainGame' timed out after 30 seconds.");
        UniTask transition = _bootstrap.TransitionAsync("MainGame").Preserve();
        float deadline = Time.realtimeSinceStartup + 40f;
        while (!transition.Status.IsCompleted() && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        Assert.That(transition.Status.IsCompleted(), Is.True, "MainGame transition did not honor its timeout.");
        Exception? failure = null;
        try
        {
            transition.GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        Assert.That(failure, Is.TypeOf<TimeoutException>());
        Assert.That(connection.Connection, Is.Null,
            "A failed MainGame candidate must not leave a connected transport with a lost WorldInit packet.");
        Assert.That(_bootstrap.CurrentSceneName, Is.EqualTo("MainMenu"));
        scenario.Scenario = DummyScenario.HappyPath;
    }

    [UnityTest]
    public IEnumerator DummyConnection_PublishesNoGameplayPacketsBeforeProtocolHandshake()
    {
        DummyConnection dummy = _bootstrap.Container.Resolve<DummyConnection>();
        int packetCount = 0;
        void OnPacket(MinesServer.Networking.Server.Packets.ServerPacket _) => packetCount++;
        dummy.OnReceived += OnPacket;
        try
        {
            dummy.Connect();
            yield return null;
            yield return null;
            Assert.That(packetCount, Is.Zero);
        }
        finally
        {
            dummy.OnReceived -= OnPacket;
            dummy.Disconnect();
        }
    }

    [UnityTest]
    public IEnumerator MainGameToMenuToMainGame_LeavesNoOldScopeListenersOrDummyPackets()
    {
        yield return Await(_bootstrap.TransitionAsync("MainMenu"), PlayModeHarness.UITimeoutSeconds);
        yield return Await(_bootstrap.TransitionAsync("MainGame"), PlayModeHarness.WorldTimeoutSeconds);
        PacketHandler firstHandler = FindComponentInScene<GameLifetimeScope>(SceneManager.GetSceneByName("MainGame"))!
            .Container.Resolve<PacketHandler>();
        DummyConnection dummy = _bootstrap.Container.Resolve<DummyConnection>();

        yield return Await(_bootstrap.TransitionAsync("MainMenu"), PlayModeHarness.UITimeoutSeconds);
        Assert.That(firstHandler.IsSubscribed, Is.False, "The first game PacketHandler kept its packet subscriptions after scene unload.");

        int packetsAfterDisconnect = 0;
        void OnPacket(MinesServer.Networking.Server.Packets.ServerPacket _) => packetsAfterDisconnect++;
        dummy.OnReceived += OnPacket;
        yield return new WaitForSecondsRealtime(0.35f);
        dummy.OnReceived -= OnPacket;
        Assert.That(packetsAfterDisconnect, Is.Zero, "A retired DummyConnection loop emitted packets in MainMenu.");

        yield return Await(_bootstrap.TransitionAsync("MainGame"), PlayModeHarness.WorldTimeoutSeconds);
        PacketHandler secondHandler = FindComponentInScene<GameLifetimeScope>(SceneManager.GetSceneByName("MainGame"))!
            .Container.Resolve<PacketHandler>();
        Assert.That(secondHandler, Is.Not.Null);
        Assert.That(secondHandler, Is.Not.SameAs(firstHandler));
        Assert.That(CountScopes(SceneManager.GetSceneByName("MainGame")), Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator FailedTransition_FiresOnceAndKeepsPreviousUIOperational()
    {
        int failureCount = 0;
        _bootstrap.TransitionChanged += OnChanged;
        try
        {
            UniTask transition = _bootstrap.TransitionAsync("MissingSceneContractFixture").Preserve();
            yield return AwaitFailure(transition, PlayModeHarness.UITimeoutSeconds);
            Assert.That(failureCount, Is.EqualTo(1));
            Assert.That(SceneManager.GetSceneByName("Gateway").isLoaded, Is.True);
            Assert.That(HasNamedUIElement(SceneManager.GetSceneByName("Gateway"), "GatewayRoot"), Is.True);
        }
        finally
        {
            _bootstrap.TransitionChanged -= OnChanged;
        }

        void OnChanged(SceneTransitionStatus status)
        {
            if (status.Phase == SceneTransitionPhase.Failed)
            {
                failureCount++;
            }
        }
    }

    [UnityTest]
    public IEnumerator ThrowingTransitionObserver_DoesNotAbortTransition()
    {
        int completionCount = 0;
        LogAssert.Expect(
            LogType.Error,
            new Regex("\\[Bootstrap\\] Transition observer failed"));
        _bootstrap.TransitionChanged += ThrowingObserver;
        _bootstrap.TransitionChanged += CountingObserver;
        try
        {
            yield return Await(_bootstrap.TransitionAsync("MainMenu"), PlayModeHarness.UITimeoutSeconds);

            Assert.That(_bootstrap.CurrentSceneName, Is.EqualTo("MainMenu"));
            Assert.That(completionCount, Is.EqualTo(1));
        }
        finally
        {
            _bootstrap.TransitionChanged -= ThrowingObserver;
            _bootstrap.TransitionChanged -= CountingObserver;
        }

        static void ThrowingObserver(SceneTransitionStatus status)
        {
            if (status.Phase == SceneTransitionPhase.Created)
            {
                throw new InvalidOperationException("observer failure");
            }
        }

        void CountingObserver(SceneTransitionStatus status)
        {
            if (status.Phase == SceneTransitionPhase.Completed)
            {
                completionCount++;
            }
        }
    }

    [UnityTest]
    public IEnumerator LoadedScenes_ContainOneContentScopeAndOnePersistentBootstrapScope()
    {
        yield return Await(_bootstrap.TransitionAsync("MainMenu"), PlayModeHarness.UITimeoutSeconds);
        LifetimeScope[] scopes = Object.FindObjectsByType<LifetimeScope>(FindObjectsInactive.Include);
        Assert.That(scopes.Count(scope => scope is BootstrapLifetimeScope), Is.EqualTo(1));
        Assert.That(CountScopes(SceneManager.GetSceneByName("MainMenu")), Is.EqualTo(1));
        Assert.That(scopes.Length, Is.EqualTo(2));
    }

    private static IEnumerator DestroyPersistentBootstrapIfPresent() => PlayModeHarness.DestroyPersistentBootstrapIfPresent();

    private static BootstrapLifetimeScope? FindBootstrap() => PlayModeHarness.FindBootstrap();

    private static int CountScopes(Scene scene) => PlayModeHarness.CountScopes(scene);

    private static T? FindComponentInScene<T>(Scene scene)
        where T : Component => PlayModeHarness.FindComponentInScene<T>(scene);

    private static T? ResolveInScene<T>(Scene scene)
        where T : class
    {
        GameLifetimeScope? scope = FindComponentInScene<GameLifetimeScope>(scene);
        return scope != null && scope.Container != null && scope.Container.TryResolve(out T? service)
            ? service
            : null;
    }

    private static bool HasNamedUIElement(Scene scene, string elementName) =>
        PlayModeHarness.HasNamedUIElement(scene, elementName);

    private static IEnumerator Await(UniTask task, float timeoutSeconds) => PlayModeHarness.Await(task, timeoutSeconds);

    private static IEnumerator AwaitFailure(UniTask task, float timeoutSeconds) =>
        PlayModeHarness.AwaitFailure(task, timeoutSeconds);
}

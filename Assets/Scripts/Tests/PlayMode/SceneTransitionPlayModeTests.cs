#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Game.Managers;
using Kern.Networking;
using MinesServer.Networking.Connection.Client;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VContainer;
using VContainer.Unity;
using Object = UnityEngine.Object;

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
    public IEnumerator BootstrapToGateway_WaitsForConcreteGatewayUi()
    {
        Scene gateway = SceneManager.GetSceneByName("Gateway");
        Assert.That(_bootstrap.CurrentSceneName, Is.EqualTo("Gateway"));
        Assert.That(gateway.isLoaded, Is.True);
        Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(gateway));
        Assert.That(CountScopes(gateway), Is.EqualTo(1));
        Assert.That(HasNamedUiElement(gateway, "GatewayRoot"), Is.True);
        yield break;
    }

    [UnityTest]
    public IEnumerator GatewayToMainMenu_UnloadsGatewayOnlyAfterMenuUiExists()
    {
        bool menuUiObservedBeforeCompletion = false;
        UniTask transition = _bootstrap.TransitionAsync("MainMenu").Preserve();
        while (!transition.Status.IsCompleted())
        {
            Scene menuDuringTransition = SceneManager.GetSceneByName("MainMenu");
            menuUiObservedBeforeCompletion |= menuDuringTransition.isLoaded &&
                HasNamedUiElement(menuDuringTransition, "MainMenuContainer");
            yield return null;
        }

        transition.GetAwaiter().GetResult();
        Scene menu = SceneManager.GetSceneByName("MainMenu");
        Assert.That(menuUiObservedBeforeCompletion, Is.True);
        Assert.That(SceneManager.GetSceneByName("Gateway").isLoaded, Is.False);
        Assert.That(menu.isLoaded, Is.True);
        Assert.That(CountScopes(menu), Is.EqualTo(1));
        Assert.That(HasNamedUiElement(menu, "MainMenuContainer"), Is.True);
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
    public IEnumerator FailedTransition_FiresOnceAndKeepsPreviousUiOperational()
    {
        int failureCount = 0;
        _bootstrap.TransitionChanged += OnChanged;
        try
        {
            UniTask transition = _bootstrap.TransitionAsync("MissingSceneContractFixture").Preserve();
            yield return AwaitFailure(transition, PlayModeHarness.UITimeoutSeconds);
            Assert.That(failureCount, Is.EqualTo(1));
            Assert.That(SceneManager.GetSceneByName("Gateway").isLoaded, Is.True);
            Assert.That(HasNamedUiElement(SceneManager.GetSceneByName("Gateway"), "GatewayRoot"), Is.True);
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

    private static bool HasNamedUiElement(Scene scene, string elementName) =>
        PlayModeHarness.HasNamedUiElement(scene, elementName);

    private static IEnumerator Await(UniTask task, float timeoutSeconds) => PlayModeHarness.Await(task, timeoutSeconds);

    private static IEnumerator AwaitFailure(UniTask task, float timeoutSeconds) =>
        PlayModeHarness.AwaitFailure(task, timeoutSeconds);
}

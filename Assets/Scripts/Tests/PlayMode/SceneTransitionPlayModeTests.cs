#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using Fodinae.Networking;
using Fodinae.Networking.Auth;
using MinesServer.Networking.Connection.Client;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VContainer;
using VContainer.Unity;
using Object = UnityEngine.Object;

namespace Fodinae.Tests.PlayMode
{
    [TestFixture]
    public sealed class SceneTransitionPlayModeTests
    {
        private const float UiTimeoutSeconds = 20f;
        private const float WorldTimeoutSeconds = 45f;
        private const string TestDummyToken = "playmode-scene-transition-token";
        private BootstrapLifetimeScope _bootstrap = null!;
        private string _originalClientToken = string.Empty;
        private HashSet<string> _originalDummyTokens = [];

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return DestroyPersistentBootstrapIfPresent();
            SeedDummyAuthentication();
            yield return SceneManager.LoadSceneAsync("Bootstrap", LoadSceneMode.Single);
            yield return WaitUntil(
                () => FindBootstrap() is { Container: not null },
                UiTimeoutSeconds,
                "Bootstrap container was not built.");
            _bootstrap = FindBootstrap()!;
            yield return WaitUntil(
                () => _bootstrap.CurrentSceneName == "Gateway" &&
                    SceneManager.GetSceneByName("Gateway").isLoaded &&
                    HasNamedUiElement(SceneManager.GetSceneByName("Gateway"), "GatewayRoot"),
                UiTimeoutSeconds,
                "ApplicationBootstrap did not finish Bootstrap -> Gateway with ready UI.");
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

            yield return DestroyPersistentBootstrapIfPresent();
            RestoreDummyAuthentication();
        }

        private void SeedDummyAuthentication()
        {
            var gameTokenStore = new GameTokenStore();
            _originalClientToken = gameTokenStore.Load();
            DummyTokenStore tokenStore = new();
            _originalDummyTokens = tokenStore.Load();

            HashSet<string> tokens = new(_originalDummyTokens)
            {
                TestDummyToken,
            };
            tokenStore.Save(tokens);
            gameTokenStore.Save(TestDummyToken);
        }

        private void RestoreDummyAuthentication()
        {
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
            yield return Await(_bootstrap.TransitionAsync("MainMenu"), UiTimeoutSeconds);
            bool menuObservedWhileWorldNotReady = false;
            UniTask transition = _bootstrap.TransitionAsync("MainGame").Preserve();
            float deadline = Time.realtimeSinceStartup + WorldTimeoutSeconds;
            while (!transition.Status.IsCompleted() && Time.realtimeSinceStartup < deadline)
            {
                Scene game = SceneManager.GetSceneByName("MainGame");
                GameManager? manager = FindComponentInScene<GameManager>(game);
                if (manager != null && !manager.IsWorldLoaded)
                {
                    menuObservedWhileWorldNotReady |= SceneManager.GetSceneByName("MainMenu").isLoaded;
                }

                yield return null;
            }

            Assert.That(transition.Status.IsCompleted(), Is.True, "MainGame transition timed out.");
            transition.GetAwaiter().GetResult();
            GameManager gameManager = FindComponentInScene<GameManager>(SceneManager.GetSceneByName("MainGame"))!;
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
            yield return Await(_bootstrap.TransitionAsync("MainMenu"), UiTimeoutSeconds);
            yield return Await(_bootstrap.TransitionAsync("MainGame"), WorldTimeoutSeconds);
            PacketHandler firstHandler = FindComponentInScene<PacketHandler>(SceneManager.GetSceneByName("MainGame"))!;
            DummyConnection dummy = _bootstrap.Container.Resolve<DummyConnection>();

            yield return Await(_bootstrap.TransitionAsync("MainMenu"), UiTimeoutSeconds);
            Assert.That(firstHandler == null, Is.True, "The first game PacketHandler survived scene unload.");

            int packetsAfterDisconnect = 0;
            void OnPacket(MinesServer.Networking.Server.Packets.ServerPacket _) => packetsAfterDisconnect++;
            dummy.OnReceived += OnPacket;
            yield return new WaitForSecondsRealtime(0.35f);
            dummy.OnReceived -= OnPacket;
            Assert.That(packetsAfterDisconnect, Is.Zero, "A retired DummyConnection loop emitted packets in MainMenu.");

            yield return Await(_bootstrap.TransitionAsync("MainGame"), WorldTimeoutSeconds);
            PacketHandler secondHandler = FindComponentInScene<PacketHandler>(SceneManager.GetSceneByName("MainGame"))!;
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
                yield return AwaitFailure(transition, UiTimeoutSeconds);
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
                yield return Await(_bootstrap.TransitionAsync("MainMenu"), UiTimeoutSeconds);

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
            yield return Await(_bootstrap.TransitionAsync("MainMenu"), UiTimeoutSeconds);
            LifetimeScope[] scopes = Object.FindObjectsByType<LifetimeScope>(FindObjectsInactive.Include);
            Assert.That(scopes.Count(scope => scope is BootstrapLifetimeScope), Is.EqualTo(1));
            Assert.That(CountScopes(SceneManager.GetSceneByName("MainMenu")), Is.EqualTo(1));
            Assert.That(scopes.Length, Is.EqualTo(2));
        }

        private static IEnumerator DestroyPersistentBootstrapIfPresent()
        {
            BootstrapLifetimeScope? existing = FindBootstrap();
            if (existing == null)
            {
                yield break;
            }

            Object.Destroy(existing.gameObject);
            yield return null;
            yield return null;
            Assert.That(FindBootstrap(), Is.Null, "Persistent Bootstrap scope survived test cleanup.");
        }

        private static BootstrapLifetimeScope? FindBootstrap()
        {
            return Object.FindAnyObjectByType<BootstrapLifetimeScope>(FindObjectsInactive.Include);
        }

        private static int CountScopes(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return 0;
            }

            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                count += root.GetComponentsInChildren<LifetimeScope>(true).Length;
            }

            return count;
        }

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
                if (component != null && component.gameObject.scene == scene)
                {
                    return component;
                }
            }

            return null;
        }

        private static bool HasNamedUiElement(Scene scene, string elementName)
        {
            UIDocument? document = FindComponentInScene<UIDocument>(scene);
            return document != null && document.isActiveAndEnabled &&
                document.rootVisualElement?.Q(elementName) != null;
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

        private static IEnumerator AwaitFailure(UniTask task, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!task.Status.IsCompleted() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(task.Status.IsCompleted(), Is.True, $"Failed operation timed out after {timeoutSeconds:F0}s.");
            Assert.Catch<Exception>(() => task.GetAwaiter().GetResult());
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
}

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Networking.Auth;
using MinesServer.Networking.Connection.Client;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using VContainer;
using VContainer.Unity;
using Object = UnityEngine.Object;

namespace Kern.Tests.PlayMode;

// Общая обвязка PlayMode-тестов: запуск игры с Bootstrap на офлайн-сервере,
// ожидания с таймаутом и доступ к сервисам сцен через их контейнеры.
internal static class PlayModeHarness
{
    public const float UITimeoutSeconds = 20f;
    public const float WorldTimeoutSeconds = 45f;

    public static IEnumerator StartAtGateway()
    {
        yield return Shutdown();
        yield return SceneManager.LoadSceneAsync(ProjectRuntimeContracts.SceneNames.Bootstrap, LoadSceneMode.Single);
        yield return WaitUntil(
            () => FindBootstrap() is { Container: not null },
            UITimeoutSeconds,
            "Bootstrap container was not built.");
        BootstrapLifetimeScope bootstrap = FindBootstrap()!;
        yield return WaitUntil(
            () => bootstrap.CurrentSceneName == ProjectRuntimeContracts.SceneNames.Gateway &&
                SceneManager.GetSceneByName(ProjectRuntimeContracts.SceneNames.Gateway).isLoaded &&
                HasNamedUiElement(SceneManager.GetSceneByName(ProjectRuntimeContracts.SceneNames.Gateway), "GatewayRoot"),
            UITimeoutSeconds,
            "ApplicationBootstrap did not finish Bootstrap -> Gateway with ready UI.");
    }

    public static IEnumerator EnterMainGame(BootstrapLifetimeScope bootstrap)
    {
        yield return Await(bootstrap.TransitionAsync(ProjectRuntimeContracts.SceneNames.MainMenu), UITimeoutSeconds);
        yield return Await(bootstrap.TransitionAsync(ProjectRuntimeContracts.SceneNames.MainGame), WorldTimeoutSeconds);
    }

    // Уничтожить объект Bootstrap мало: сцены, которые он загрузил, остаются
    // в памяти вместе с миром, освещением, звуком и петлями и продолжают жить
    // в следующем тесте. Выгружается всё, кроме сцены тест-раннера.
    public static IEnumerator Shutdown()
    {
        BootstrapLifetimeScope? bootstrap = FindBootstrap();
        if (bootstrap?.Container != null &&
            bootstrap.Container.TryResolve(out Kern.Core.Interfaces.IConnectionService connection))
        {
            connection.Disconnect();
        }

        yield return DestroyPersistentBootstrapIfPresent();

        // Последнюю загруженную сцену Unity не выгружает.
        const string EmptySceneName = "KernTestEmpty";
        if (!SceneManager.GetSceneByName(EmptySceneName).IsValid())
        {
            SceneManager.SetActiveScene(SceneManager.CreateScene(EmptySceneName));
        }

        for (int index = SceneManager.sceneCount - 1; index >= 0; index--)
        {
            Scene scene = SceneManager.GetSceneAt(index);
            if (scene.isLoaded && _ProjectSceneNames.Contains(scene.name))
            {
                yield return SceneManager.UnloadSceneAsync(scene);
            }
        }
    }

    private static readonly HashSet<string> _ProjectSceneNames =
    [
        ProjectRuntimeContracts.SceneNames.Bootstrap,
        ProjectRuntimeContracts.SceneNames.Gateway,
        ProjectRuntimeContracts.SceneNames.MainMenu,
        ProjectRuntimeContracts.SceneNames.MainGame,
    ];

    public static IEnumerator DestroyPersistentBootstrapIfPresent()
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

    public static BootstrapLifetimeScope? FindBootstrap() =>
        Object.FindAnyObjectByType<BootstrapLifetimeScope>(FindObjectsInactive.Include);

    public static Scene Scene(string name) => SceneManager.GetSceneByName(name);

    public static int CountScopes(Scene scene)
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

    public static T? FindComponentInScene<T>(Scene scene)
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

    // Сервисы сцены живут в её контейнере, а не на объектах (SCENE_STANDARD.md §1).
    public static T? ResolveInGame<T>()
        where T : class
    {
        GameLifetimeScope? scope = FindComponentInScene<GameLifetimeScope>(Scene(ProjectRuntimeContracts.SceneNames.MainGame));
        return scope != null && scope.Container != null && scope.Container.TryResolve(out T? service)
            ? service
            : null;
    }

    public static T RequireInGame<T>()
        where T : class =>
        ResolveInGame<T>() ?? throw new AssertionException($"MainGame container does not provide {typeof(T).Name}.");

    public static bool HasNamedUiElement(Scene scene, string elementName)
    {
        UIDocument? document = FindComponentInScene<UIDocument>(scene);
        return document != null && document.isActiveAndEnabled &&
            document.rootVisualElement?.Q(elementName) != null;
    }

    public static IEnumerator Await(UniTask task, float timeoutSeconds)
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

    public static IEnumerator AwaitFailure(UniTask task, float timeoutSeconds)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (!task.Status.IsCompleted() && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        Assert.That(task.Status.IsCompleted(), Is.True, $"Failed operation timed out after {timeoutSeconds:F0}s.");
        Assert.Catch<Exception>(() => task.GetAwaiter().GetResult());
    }

    public static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds, string failureMessage)
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

    // Unity не умеет свернуть приложение из теста; сообщение получают все
    // живые объекты, включая DontDestroyOnLoad, как при настоящей паузе.
    public static void SendApplicationPause(bool paused)
    {
        foreach (GameObject gameObject in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude))
        {
            gameObject.SendMessage("OnApplicationPause", paused, SendMessageOptions.DontRequireReceiver);
        }
    }

    public static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return null;
        }
    }
}

// Токен офлайн-сервера на время теста; исходные токены игрока возвращаются.
internal sealed class DummyAuthenticationScope
{
    private readonly string _originalClientToken;
    private readonly HashSet<string> _originalDummyTokens;

    private DummyAuthenticationScope(string originalClientToken, HashSet<string> originalDummyTokens)
    {
        _originalClientToken = originalClientToken;
        _originalDummyTokens = originalDummyTokens;
    }

    public static DummyAuthenticationScope Seed(string testToken)
    {
        var gameTokenStore = new GameTokenStore();
        var dummyTokenStore = new DummyTokenStore();
        var scope = new DummyAuthenticationScope(gameTokenStore.Load(), dummyTokenStore.Load());
        dummyTokenStore.Save(new HashSet<string>(scope._originalDummyTokens) { testToken });
        gameTokenStore.Save(testToken);
        return scope;
    }

    public void Restore()
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
}

// Отдельная виртуальная клавиатура: нажатия теста не смешиваются с
// физической клавиатурой разработчика, а после теста устройство удаляется.
internal sealed class VirtualKeyboard : IDisposable
{
    private readonly HashSet<Key> _held = [];

    public VirtualKeyboard()
    {
        Device = InputSystem.AddDevice<Keyboard>("KernTestKeyboard");
        Device.MakeCurrent();
    }

    public Keyboard Device { get; }

    // Нажатие видно как wasPressedThisFrame в Update следующего кадра.
    public IEnumerator Tap(Key key)
    {
        Hold(key);
        yield return null;
        Release(key);
        yield return null;
    }

    public void Hold(Key key)
    {
        _held.Add(key);
        Push();
    }

    public void Release(Key key)
    {
        _held.Remove(key);
        Push();
    }

    public void Dispose()
    {
        InputSystem.RemoveDevice(Device);
    }

    private void Push()
    {
        var keys = new Key[_held.Count];
        _held.CopyTo(keys);
        InputSystem.QueueStateEvent(Device, new KeyboardState(keys));
    }
}

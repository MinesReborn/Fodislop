#nullable enable

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Kern.Core;
using Kern.Core.Lifecycle;
using Kern.World.Lighting;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;

namespace Kern.Tests.PlayMode;

// После уничтожения Bootstrap от игры не остаётся ничего: ни звука, ни
// объектов DontDestroyOnLoad, ни загруженных сцен, ни GPU-ресурсов, ни живых
// задач, ни ссылок в статических владельцах. Так закрывается игра в
// редакторе и так тесты передают мир друг другу.
[TestFixture]
public sealed class TeardownPlayModeTests
{
    private const string TestDummyToken = "playmode-teardown-token";

    // Объект FMOD создаётся плагином лениво и живёт весь процесс.
    private static readonly HashSet<string> _ProcessLifetimeObjects = ["FMOD.UnityIntegration.RuntimeManager"];

    private DummyAuthenticationScope _authentication = null!;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        _authentication = DummyAuthenticationScope.Seed(TestDummyToken);
        yield return PlayModeHarness.DestroyPersistentBootstrapIfPresent();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        yield return PlayModeHarness.Shutdown();
        _authentication.Restore();
    }

    [UnityTest]
    public IEnumerator DestroyingBootstrapInWorld_ReleasesEverythingTheGameOwned()
    {
        string testScene = SceneManager.GetActiveScene().name;
        List<string> persistentBefore = PersistentRootNames();
        List<string> renderTexturesBefore = GameRenderTextures();

        yield return PlayModeHarness.StartAtGateway();
        BootstrapLifetimeScope bootstrap = PlayModeHarness.FindBootstrap()!;
        var operations = (AsyncOperationSupervisor)bootstrap.Container.Resolve<IAsyncOperationSupervisor>();
        yield return PlayModeHarness.EnterMainGame(bootstrap);

        // Даём игре завести музыку, эмбиент и фоновые петли.
        yield return new WaitForSecondsRealtime(3f);

        yield return PlayModeHarness.Shutdown();
        yield return PlayModeHarness.Frames(10);
        AsyncOperation unload = Resources.UnloadUnusedAssets();
        while (!unload.isDone)
        {
            yield return null;
        }

        var problems = new List<string>();

        string[] playing = PlayingFmodEvents();
        if (playing.Length > 0)
        {
            problems.Add($"FMOD events still playing: {string.Join(", ", playing)}");
        }

        string[] leftovers = PersistentRootNames()
            .Except(persistentBefore)
            .Where(name => !_ProcessLifetimeObjects.Contains(name))
            .ToArray();
        if (leftovers.Length > 0)
        {
            problems.Add($"DontDestroyOnLoad objects survived: {string.Join(", ", leftovers)}");
        }

        string[] scenes = Enumerable.Range(0, SceneManager.sceneCount)
            .Select(index => SceneManager.GetSceneAt(index))
            .Where(scene => scene.isLoaded && scene.name != testScene && scene.name != "KernTestEmpty")
            .Select(scene => scene.name)
            .ToArray();
        if (scenes.Length > 0)
        {
            problems.Add($"Scenes stayed loaded: {string.Join(", ", scenes)}");
        }

        List<string> renderTexturesAfter = GameRenderTextures();
        foreach (string name in renderTexturesBefore)
        {
            renderTexturesAfter.Remove(name);
        }

        if (renderTexturesAfter.Count > 0)
        {
            problems.Add($"Render textures survived: {string.Join(", ", renderTexturesAfter)}");
        }

        if (operations.ActiveCount > 0)
        {
            problems.Add($"Supervised operations still alive: {string.Join(", ", operations.ActiveOperationNames)}");
        }

        if (Shader.IsKeywordEnabled(LightingPresentation.WorldLightingKeyword))
        {
            problems.Add($"Global shader keyword {LightingPresentation.WorldLightingKeyword} stayed enabled");
        }

        if (GameplayCamera.Resolve() != null)
        {
            problems.Add("GameplayCamera still resolves a camera");
        }

        if (!Mathf.Approximately(Time.timeScale, 1f))
        {
            problems.Add($"Time.timeScale left at {Time.timeScale}");
        }

        Assert.That(problems, Is.Empty, "Game teardown leaked:\n- " + string.Join("\n- ", problems));
    }

    // Выход в меню оставляет Bootstrap жить, а игровую сессию — нет: после
    // сборки мусора ни один объект прошлого мира не должен быть достижим.
    // Удержанная сессия — это десятки мегабайт на каждый вход в игру.
    [UnityTest]
    public IEnumerator LeavingWorld_LetsTheGameSessionBeCollected()
    {
        yield return PlayModeHarness.StartAtGateway();
        BootstrapLifetimeScope bootstrap = PlayModeHarness.FindBootstrap()!;
        yield return PlayModeHarness.EnterMainGame(bootstrap);

        Dictionary<string, System.WeakReference> session = CaptureSession();
        yield return PlayModeHarness.Await(
            bootstrap.TransitionAsync(ProjectRuntimeContracts.SceneNames.MainMenu),
            PlayModeHarness.UITimeoutSeconds);

        // Петли офлайн-сервера выходят, досчитав свою задержку.
        yield return new WaitForSecondsRealtime(13f);
        AsyncOperation unload = Resources.UnloadUnusedAssets();
        while (!unload.isDone)
        {
            yield return null;
        }

        for (int pass = 0; pass < 3; pass++)
        {
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            yield return null;
        }

        string[] alive = session.Where(entry => entry.Value.IsAlive).Select(entry => entry.Key).ToArray();
        Assert.That(alive, Is.Empty, "Objects of the unloaded game session are still reachable: " + string.Join(", ", alive));
    }

    private static Dictionary<string, System.WeakReference> CaptureSession()
    {
        GameLifetimeScope scope = PlayModeHarness.FindComponentInScene<GameLifetimeScope>(
            PlayModeHarness.Scene(ProjectRuntimeContracts.SceneNames.MainGame))!;
        var session = new Dictionary<string, System.WeakReference>
        {
            ["GameLifetimeScope.Container"] = new(scope.Container),
        };
        Add<Kern.Game.Managers.GameManager>();
        Add<Kern.Networking.PacketHandler>();
        Add<Kern.World.MapStorage>();
        Add<Kern.Core.Interfaces.IWorldDataStorage>();
        Add<Kern.UI.UIInputManager>();
        Add<LightingEngine>();
        Add<Kern.World.Terrain.TerrainRenderer>();
        Add<Kern.World.MapManager>();
        return session;

        void Add<T>()
            where T : class
        {
            if (scope.Container.TryResolve(out T? service) && service != null)
            {
                session[typeof(T).Name] = new System.WeakReference(service);
            }
        }
    }

    // Служебные цели редактора, URP и UI Toolkit живут весь процесс и
    // пересоздаются сами; игре они не принадлежат.
    private static readonly string[] _EngineRenderTexturePrefixes =
    [
        "GUIView", "GameView", "EditorAtlas", "UIR Dynamic Atlas", "_Camera",
        "_InternalColorGradingLut", "DefaultShadowTexture", "SceneView", "PreviewRenderUtility",
    ];

    private static List<string> GameRenderTextures() =>
        Resources.FindObjectsOfTypeAll<RenderTexture>()
            .Where(texture => texture != null)
            .Select(texture => texture.name)
            .Where(name => !_EngineRenderTexturePrefixes.Any(prefix => name.StartsWith(prefix, System.StringComparison.Ordinal)))
            .ToList();

    private static List<string> PersistentRootNames() =>
        Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include)
            .Where(gameObject => gameObject.transform.parent == null && gameObject.scene.name == "DontDestroyOnLoad")
            .Select(gameObject => gameObject.name)
            .ToList();

    private static string[] PlayingFmodEvents()
    {
        if (!FMODUnity.RuntimeManager.IsInitialized ||
            FMODUnity.RuntimeManager.StudioSystem.getBankList(out FMOD.Studio.Bank[] banks) != FMOD.RESULT.OK)
        {
            return [];
        }

        var playing = new List<string>();
        foreach (FMOD.Studio.Bank bank in banks)
        {
            if (bank.getEventList(out FMOD.Studio.EventDescription[] events) != FMOD.RESULT.OK)
            {
                continue;
            }

            foreach (FMOD.Studio.EventDescription description in events)
            {
                if (description.getInstanceList(out FMOD.Studio.EventInstance[] instances) != FMOD.RESULT.OK)
                {
                    continue;
                }

                foreach (FMOD.Studio.EventInstance instance in instances)
                {
                    instance.getPlaybackState(out FMOD.Studio.PLAYBACK_STATE state);
                    if (state != FMOD.Studio.PLAYBACK_STATE.STOPPED)
                    {
                        description.getPath(out string path);
                        playing.Add(path);
                    }
                }
            }
        }

        return playing.ToArray();
    }
}

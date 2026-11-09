#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using Kern.Bootstrap.Scenes;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Networking.Gateways;
using Kern.Rendering.PostProcessing;
using Kern.UI.Chat;
using Kern.UI.Input;
using Kern.UI.Gateway;
using Kern.UI.Map;
using Kern.UI.ServerWindows;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using VContainer;

namespace Kern.Editor;

// Снимки настоящей игры для сверки с макетом visual/kern-ui: обычный запуск (Bootstrap → меню → мир через заглушку
// сервера), кадр мира камерой игры и все панели UI Toolkit поверх него, 1920×1080. Не тест: игра идёт так же,
// как у игрока, без тестовой обвязки. Batch-запуск:
//   Unity -batchmode -projectPath . -executeMethod Kern.Editor.GameSnapshotTool.Run -kernShots <каталог>
// Снимки: HUD, раскрытый чат, пауза, вкладки настроек, окно ссылки, карта мира.
[InitializeOnLoad]
public static class GameSnapshotTool
{
    private const string DirKey = "Kern.GameSnapshot.Dir";
    private const int Width = 1920;
    private const int Height = 1080;
    private const float MenuTimeoutSeconds = 120f;
    private const float WorldTimeoutSeconds = 240f;
    private const float SettleSeconds = 6f;

    // Сценарий и вложенные ожидания: yield вложенного IEnumerator проходит его целиком, любое другое yield — кадр
    private static readonly Stack<IEnumerator> s_run = new();

    static GameSnapshotTool()
    {
        if (!string.IsNullOrEmpty(SessionState.GetString(DirKey, string.Empty)))
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }
    }

    public static void Run()
    {
        string[] args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, "-kernShots");
        if (at < 0 || at + 1 >= args.Length)
        {
            throw new ArgumentException("-kernShots <каталог> обязателен.");
        }

        SessionState.SetString(DirKey, Path.GetFullPath(args[at + 1]));
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        // Вход в игру из любой сцены идёт через Bootstrap (PlayModeSceneBootstrapper)
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            s_run.Clear();
            s_run.Push(Shoot(SessionState.GetString(DirKey, string.Empty)));
            EditorApplication.update += Step;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.EraseString(DirKey);
            EditorApplication.Exit(0);
        }
    }

    // Один шаг сценария на обновление редактора: каждое yield — ожидание следующего кадра игры
    private static void Step()
    {
        try
        {
            while (s_run.Count > 0)
            {
                IEnumerator top = s_run.Peek();
                if (!top.MoveNext())
                {
                    s_run.Pop();
                    continue;
                }

                if (top.Current is IEnumerator nested)
                {
                    s_run.Push(nested);
                    continue;
                }

                return;
            }
        }
        catch (Exception exception)
        {
            Debug.LogError($"[GameShots] {exception}");
            s_run.Clear();
        }

        EditorApplication.update -= Step;
        EditorApplication.ExitPlaymode();
    }

    private static IEnumerator Shoot(string dir)
    {
        Directory.CreateDirectory(dir);

        BootstrapLifetimeScope? bootstrap = null;
        yield return WaitUntil(
            () => (bootstrap = UnityEngine.Object.FindAnyObjectByType<BootstrapLifetimeScope>()) is { Container: not null } &&
                bootstrap.CurrentSceneName == SceneNames.MainMenu &&
                SceneManager.GetSceneByName(SceneNames.MainMenu).isLoaded,
            MenuTimeoutSeconds,
            "главное меню");

        // Пресет и настройки — сохранённые игроком: снимок показывает игру такой, какой её видит игрок
        UniTask entry = bootstrap!.TransitionAsync(SceneNames.MainGame);
        yield return WaitUntil(() => entry.Status != UniTaskStatus.Pending, WorldTimeoutSeconds, "вход в мир");

        if (entry.Status != UniTaskStatus.Succeeded)
        {
            throw new InvalidOperationException($"[GameShots] Вход в мир закончился статусом {entry.Status}.");
        }

        using var ui = new PanelRedirect(Width, Height);
        yield return Wait(SettleSeconds);

        yield return Shot(ui, Path.Combine(dir, "game-hud.png"));

        GlobalChatUI chat = UnityEngine.Object.FindAnyObjectByType<GlobalChatUI>()
            ?? throw new InvalidOperationException("[GameShots] GlobalChatUI не найден.");
        chat.Show();
        yield return Wait(1f);

        yield return Shot(ui, Path.Combine(dir, "game-chat.png"));
        chat.Hide();
        yield return Wait(0.5f);

        TouchCommands commands = Require<TouchCommands>();
        commands.RequestPause();
        yield return Wait(1f);

        yield return Shot(ui, Path.Combine(dir, "game-pause.png"));

        // Окно настроек открывается из паузы; каждая вкладка — отдельный снимок
        Submit("SettingsButton");
        yield return Wait(1f);

        foreach (string tab in new[] { "Graphics", "Audio", "Interface", "Controls" })
        {
            Submit("SettingsTab" + tab);
            yield return Wait(0.5f);

            yield return Shot(ui, Path.Combine(dir, "game-settings-" + tab.ToLowerInvariant() + ".png"));
        }

        // Калибровка дисплея: в batch-запуске HDR нет, поэтому шаг один — яркость SDR по знакам у чёрного
        Submit("SettingsTabGraphics");
        yield return Wait(0.3f);
        Submit("SettingsCalibrate");
        yield return Wait(1f);
        yield return Shot(ui, Path.Combine(dir, "game-calibration.png"));
        DumpBoxes("CalibLayer", Path.Combine(dir, "game-calibration.txt"));
        Submit("CalibCancel");
        yield return Wait(0.5f);

        Submit("SettingsClose");
        yield return Wait(0.5f);

        commands.RequestPause();
        yield return Wait(0.5f);

        // Раскрытый инвентарь и окно возврата на респаун
        Submit("InventoryToggleBtn");
        yield return Wait(0.8f);
        yield return Shot(ui, Path.Combine(dir, "game-inventory.png"));
        Submit("InventoryToggleBtn");
        yield return Wait(0.5f);
        Submit("RespawnButton");
        yield return Wait(0.5f);
        yield return Shot(ui, Path.Combine(dir, "game-respawn.png"));
        Require<ServerWindowPresenter>().TryRequestCloseTopWindow();
        yield return Wait(0.5f);

        // Сервер просит открыть ссылку: окно согласия
        Require<WindowCommandStream>().PublishOpenLink("https://discord.gg/projectkern");
        yield return Wait(0.5f);

        yield return Shot(ui, Path.Combine(dir, "game-link.png"));
        Require<ServerWindowPresenter>().DismissModal();
        yield return Wait(0.5f);

        WorldMapController map = UnityEngine.Object.FindAnyObjectByType<WorldMapController>()
            ?? throw new InvalidOperationException("[GameShots] WorldMapController не найден.");
        map.ToggleMapMode();
        yield return Wait(1.5f);

        yield return Shot(ui, Path.Combine(dir, "game-map.png"));
        DumpBoxes("WorldMapOverlay", Path.Combine(dir, "game-map.txt"));

        // «Моя метка»: инструмент, затем клик по карте правее и выше центра
        Submit("WorldMapMarkButton");
        yield return Wait(0.3f);
        yield return Shot(ui, Path.Combine(dir, "game-map-marking.png"));
        ClickAt("WorldMapImage", new Vector2(160f, -90f));
        yield return Wait(0.5f);
        yield return Shot(ui, Path.Combine(dir, "game-map-mark.png"));
        map.ToggleMapMode();
        ui.Dispose();

        // Онбординг (слой ворот поверх меню) — последним: открытие ворот с формой входа сбрасывает вход,
        // и после него мир уже не загрузить
        UniTask back = bootstrap.TransitionAsync(SceneNames.MainMenu);
        yield return WaitUntil(() => back.Status != UniTaskStatus.Pending, MenuTimeoutSeconds, "возврат в меню");
        yield return Wait(2f);
        yield return ShootOnboarding(dir);
    }

    // Рамки видимых элементов поддерева: x,y,ширина,высота, имя, классы, прозрачность
    private static void DumpBoxes(string rootName, string path)
    {
        VisualElement root = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude)
            .Select(d => d.rootVisualElement?.Q(rootName))
            .FirstOrDefault(e => e != null)
            ?? throw new InvalidOperationException($"[GameShots] '{rootName}' не найден.");
        var text = new System.Text.StringBuilder();
        foreach (VisualElement element in root.Query<VisualElement>().Build())
        {
            Rect box = element.worldBound;
            text.AppendLine($"{box.x:F0},{box.y:F0},{box.width:F0},{box.height:F0} {element.name} .{string.Join(".", element.GetClasses())} " +
                $"display={element.resolvedStyle.display} opacity={element.resolvedStyle.opacity:F2} visible={element.visible}");
        }

        File.WriteAllText(path, text.ToString());
    }

    private static IEnumerator ShootOnboarding(string dir)
    {
        Scene menuScene = SceneManager.GetSceneByName(SceneNames.MainMenu);
        MainMenuLifetimeScope scope = menuScene.GetRootGameObjects()
            .Select(root => root.GetComponentInChildren<MainMenuLifetimeScope>(true))
            .First(found => found != null);
        UIDocument menu = menuScene.GetRootGameObjects()
            .Select(root => root.GetComponentInChildren<UIDocument>(true))
            .First(found => found != null);
        using (var menuShot = new PanelRedirect(Width, Height))
        {
            yield return Wait(1f);
            yield return ShotUI(menuShot, Path.Combine(dir, "menu-main.png"));
            menu.rootVisualElement.Q("MainMenuContainer")!.AddToClassList("is-hidden");
            menu.rootVisualElement.Q("LoaderContainer")!.RemoveFromClassList("is-hidden");
            menu.rootVisualElement.Q("LoaderContent")!.RemoveFromClassList("is-hidden");
            Label tip = menu.rootVisualElement.Q<Label>("LoaderTip")!;
            tip.text = "<line-height=1.5em>Дорога ускоряет передвижение — навык <b>Передвижение по дороге</b> ускоряет ещё сильнее.";
            tip.RemoveFromClassList("is-hidden");
            yield return Wait(0.5f);
            yield return ShotUI(menuShot, Path.Combine(dir, "menu-descent.png"));
            menu.rootVisualElement.Q("LoaderContainer")!.AddToClassList("is-hidden");
            menu.rootVisualElement.Q("MainMenuContainer")!.RemoveFromClassList("is-hidden");
        }

        MenuGate gate = scope.Container.Resolve<MenuGate>();
        gate.Open(menu, menu.rootVisualElement, forceForm: true);
        VisualElement gateway = menu.rootVisualElement.Q("GatewayRoot")!;
        gateway.RemoveFromClassList("gateway--auth");
        gateway.AddToClassList("gateway--onboarding");

        using (var ui = new PanelRedirect(Width, Height))
        {
            yield return Wait(1f);
            yield return ShotUI(ui, Path.Combine(dir, "menu-onboarding-1.png"));
            menu.rootVisualElement.Q("OnbStep1")!.RemoveFromClassList("onb-step--active");
            menu.rootVisualElement.Q("OnbStep2")!.AddToClassList("onb-step--active");
            yield return Wait(0.5f);
            yield return ShotUI(ui, Path.Combine(dir, "menu-onboarding-2.png"));
        }

        gate.Dispose();
        yield return Wait(0.5f);
    }

    // Только интерфейс на чёрном: в меню нет камеры игры
    private static IEnumerator ShotUI(PanelRedirect ui, string path)
    {
        yield return null;
        yield return null;
        Color32[] overlay = ui.Read();
        var result = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        for (int i = 0; i < overlay.Length; i++)
        {
            overlay[i].a = 255;
        }

        result.SetPixels32(overlay);
        result.Apply();
        File.WriteAllBytes(path, result.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(result);
        Debug.Log($"[GameShots] {path}");
    }

    private static T Require<T>()
        where T : class
    {
        GameLifetimeScope? scope = SceneManager.GetSceneByName(SceneNames.MainGame).GetRootGameObjects()
            .Select(root => root.GetComponentInChildren<GameLifetimeScope>(true))
            .FirstOrDefault(found => found != null);
        return scope != null && scope.Container != null && scope.Container.TryResolve(out T? service) && service != null
            ? service
            : throw new InvalidOperationException($"[GameShots] Контейнер MainGame не даёт {typeof(T).Name}.");
    }

    // Нажатие кнопки событиями в её панель: ввод мыши в batch-запуске без фокуса окна отбрасывается
    private static void Submit(string name)
    {
        Button[] found = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude)
            .SelectMany(d => d.rootVisualElement?.Query<Button>(name).ToList() ?? new List<Button>())
            .ToArray();
        Button button = found.FirstOrDefault(b => b.panel != null && b.resolvedStyle.display != DisplayStyle.None && b.enabledInHierarchy)
            ?? throw new InvalidOperationException($"[GameShots] Видимая кнопка '{name}' не найдена (всего {found.Length}).");
        Debug.Log($"[GameShots] {name}: найдено {found.Length}, enabled={button.enabledInHierarchy}, focusable={button.focusable}");
        // Clickable слушает указатель: нажатие и отпускание в центре кнопки, как от мыши
        Vector2 center = button.worldBound.center;
        Send(button, PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = center, button = 0, clickCount = 1 }));
        Send(button, PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = center, button = 0, clickCount = 1 }));
    }

    // Клик по элементу в точке со сдвигом от его центра (карта слушает ClickEvent)
    private static void ClickAt(string name, Vector2 offset)
    {
        VisualElement target = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude)
            .Select(d => d.rootVisualElement?.Q(name))
            .FirstOrDefault(e => e != null && e.panel != null)
            ?? throw new InvalidOperationException($"[GameShots] '{name}' не найден.");
        Vector2 point = target.worldBound.center + offset;
        Send(target, ClickEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = point, button = 0, clickCount = 1 }));
    }

    private static void Send(VisualElement target, EventBase evt)
    {
        using (evt)
        {
            evt.target = target;
            target.SendEvent(evt);
        }
    }

    private static IEnumerator Wait(float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
        {
            yield return null;
        }
    }

    private static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds, string what)
    {
        float end = Time.realtimeSinceStartup + timeoutSeconds;
        while (!condition())
        {
            if (Time.realtimeSinceStartup > end)
            {
                throw new TimeoutException($"[GameShots] Не дождался: {what}.");
            }

            yield return null;
        }
    }

    private static IEnumerator Shot(PanelRedirect ui, string path)
    {
        // Панели перерисовываются в цикле игрока: пара кадров после смены состояния
        yield return null;
        yield return null;

        Color32[] world = RenderWorld();
        Color32[] overlay = ui.Read();
        var result = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        var pixels = new Color32[world.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            // Панель рисует в прозрачную текстуру смешиванием SrcAlpha/OneMinusSrcAlpha: цвет уже умножен на альфу
            Color32 u = overlay[i];
            Color32 w = world[i];
            int inv = 255 - u.a;
            pixels[i] = new Color32(
                (byte)Math.Min(255, u.r + (w.r * inv / 255)),
                (byte)Math.Min(255, u.g + (w.g * inv / 255)),
                (byte)Math.Min(255, u.b + (w.b * inv / 255)),
                255);
        }

        result.SetPixels32(pixels);
        result.Apply();
        File.WriteAllBytes(path, result.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(result);
        Debug.Log($"[GameShots] {path}");
    }

    private static Color32[] RenderWorld()
    {
        Camera camera = Require<IGameplayCamera>().Camera;
        RenderTexture target = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Camera? previousCamera = PostProcessRuntimeState.DiagnosticOffscreenCamera;
        RenderTexture? previousCameraTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        try
        {
            PostProcessRuntimeState.DiagnosticOffscreenCamera = camera;
            camera.targetTexture = target;
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest
            {
                destination = target,
            });
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            texture.Apply();
            Color32[] pixels = texture.GetPixels32();
            UnityEngine.Object.DestroyImmediate(texture);
            return pixels;
        }
        finally
        {
            camera.targetTexture = previousCameraTarget;
            RenderTexture.active = previousActive;
            PostProcessRuntimeState.DiagnosticOffscreenCamera = previousCamera;
            RenderTexture.ReleaseTemporary(target);
        }
    }

    // Все документы игры — на копии их PanelSettings с выводом в одну текстуру. Копии — чтобы не менять ассеты
    // проекта; порядок панелей сохраняется, очищает текстуру нижняя.
    private sealed class PanelRedirect : IDisposable
    {
        private readonly RenderTexture _target;
        private readonly List<(UIDocument Document, PanelSettings Original)> _documents = new();
        private readonly List<PanelSettings> _copies = new();

        public PanelRedirect(int width, int height)
        {
            _target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var copies = new Dictionary<PanelSettings, PanelSettings>();
            UIDocument[] documents = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Include);
            PanelSettings? lowest = documents.Select(d => d.panelSettings).Where(p => p != null)
                .OrderBy(p => p.sortingOrder).FirstOrDefault();
            foreach (UIDocument document in documents)
            {
                PanelSettings original = document.panelSettings;
                if (original == null)
                {
                    continue;
                }

                if (!copies.TryGetValue(original, out PanelSettings? copy))
                {
                    copy = UnityEngine.Object.Instantiate(original);
                    copy.targetTexture = _target;
                    copy.forceGammaRendering = true;
                    copy.clearColor = original == lowest;
                    copy.colorClearValue = new Color(0f, 0f, 0f, 0f);
                    copies.Add(original, copy);
                    _copies.Add(copy);
                }

                _documents.Add((document, original));
                document.panelSettings = copy;
            }

            Debug.Log($"[GameShots] {_documents.Count} documents on {_copies.Count} panels redirected");
        }

        public Color32[] Read()
        {
            RenderTexture previous = RenderTexture.active;
            var texture = new Texture2D(_target.width, _target.height, TextureFormat.RGBA32, false);
            RenderTexture.active = _target;
            texture.ReadPixels(new Rect(0, 0, _target.width, _target.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            Color32[] pixels = texture.GetPixels32();
            UnityEngine.Object.DestroyImmediate(texture);
            return pixels;
        }

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach ((UIDocument document, PanelSettings original) in _documents)
            {
                if (document != null)
                {
                    document.panelSettings = original;
                }
            }

            foreach (PanelSettings copy in _copies)
            {
                UnityEngine.Object.DestroyImmediate(copy);
            }

            _target.Release();
            UnityEngine.Object.DestroyImmediate(_target);
        }
    }
}

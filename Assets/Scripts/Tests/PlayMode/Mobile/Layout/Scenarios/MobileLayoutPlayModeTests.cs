#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Kern.Core;
using Kern.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

using Kern.Bootstrap.Scenes;
using Kern.UI.Menu;
using VContainer;
using Kern.UI.Screens;

namespace Kern.Tests.PlayMode.Mobile;

// Каждый экран игры на каждом устройстве матрицы: настоящие сцены, DI и
// контроллеры, подменён только экран. Нарушения собираются в один отчёт
// (общий каталог MobileLayout), снимки — рядом, по экранам.
[TestFixture]
public sealed class MobileLayoutPlayModeTests
{
    private readonly Dictionary<string, List<string>> _violations = new();
    private readonly Dictionary<string, byte[]?> _savedFiles = new();
    private MobileLayoutProbe? _probe;

    // Программатор сохраняет программы на диск игрока; тест создаёт свою и
    // обязан вернуть файлы как были, иначе каждый прогон дописывал бы её
    // в список программ разработчика.
    private static readonly string[] PersistedFiles =
    {
        "programmator.json",
        "programmator.json.backup",
    };

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        foreach (string name in PersistedFiles)
        {
            string path = Path.Combine(Application.persistentDataPath, name);
            _savedFiles[path] = File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        UIScreen.OverrideForTests(null);
        _probe?.Dispose();
        _probe = null;
        yield return PlayModeHarness.Shutdown();

        foreach (KeyValuePair<string, byte[]?> saved in _savedFiles)
        {
            if (saved.Value != null)
            {
                File.WriteAllBytes(saved.Key, saved.Value);
            }
            else if (File.Exists(saved.Key))
            {
                File.Delete(saved.Key);
            }
        }
    }

    [UnityTest]
    public IEnumerator EveryScreen_FitsEveryDevice()
    {
        if (Directory.Exists(MobileLayoutProbe.OutputRoot))
        {
            Directory.Delete(MobileLayoutProbe.OutputRoot, recursive: true);
        }

        yield return PlayModeHarness.StartAtMainMenu();
        BootstrapLifetimeScope bootstrap = PlayModeHarness.FindBootstrap()!;

        yield return CaptureAll("MainMenu", SceneNames.MainMenu);

        yield return PlayModeHarness.Await(
            bootstrap.TransitionAsync(SceneNames.MainGame),
            PlayModeHarness.WorldTimeoutSeconds);
        yield return CaptureAll("Game", SceneNames.MainGame);

        // Состояния открываются кнопками HUD, как их открывает палец:
        // у тач-экрана нет ни I, ни Esc, ни Tab.
        UIDocument game = GameDocument();
        yield return CaptureState("Inventory", game, "InventoryToggleBtn", "InventoryGrid", "InventoryToggleBtn");
        yield return CaptureState("Chat", game, "TouchChatButton", "ChatPanel", "TouchChatButton");
        yield return CaptureState("Say", game, "TouchSayButton", "LocalChatPanel", "TouchSayButton");
        yield return CaptureState("Map", game, "TouchMapButton", "WorldMapOverlay", "WorldMapCloseButton");
        yield return CaptureAll(
            "Programmator",
            SceneNames.MainGame,
            () => TapAndExpect(game, "ProgrammatorButton", "ProgramListPanel"));
        yield return CaptureAll(
            "ProgrammatorCreate",
            SceneNames.MainGame,
            () => TapAndExpect(game, "CreateButton", "CreateDialog"));
        yield return CaptureAll(
            "ProgrammatorEditor",
            SceneNames.MainGame,
            () => CreateProgram(game),
            () => CloseProgrammator(game));
        yield return CaptureAll(
            "Pause",
            SceneNames.MainGame,
            () => TapAndExpect(game, "TouchMenuButton", "PauseOverlay"));
        yield return CaptureAll(
            "Settings",
            SceneNames.MainGame,
            () => TapAndExpect(game, "SettingsButton", "SettingsLayer"),
            () => TapOnly(game, "SettingsClose"));

        string report = WriteReport();
        int total = 0;
        foreach (List<string> list in _violations.Values)
        {
            total += list.Count;
        }

        Assert.That(total, Is.Zero, $"Раскладка не годится для мобилок ({total} нарушений):\n{report}");
    }

    private IEnumerator CaptureState(string screen, UIDocument document, string open, string shown, string close)
    {
        yield return CaptureAll(
            screen,
            SceneNames.MainGame,
            () => TapAndExpect(document, open, shown),
            () => TapOnly(document, close));
    }

    private static IEnumerator TapAndExpect(UIDocument document, string open, string shown)
    {
        Button button = document.rootVisualElement.panel.visualTree.Q<Button>(open) ??
            throw new AssertionException($"Button {open} is not in the game document.");
        string picked = Picked(button);
        int clicks = 0;
        void Count() => clicks++;
        button.clicked += Count;
        Tap(document, open);
        yield return null;
        yield return null;
        button.clicked -= Count;
        AssertShown(document, shown, $"tapped {open} ({clicks} clicks), picked at its centre: {picked}");
    }

    // Новая программа открывает редактор сетки — так же, как палец: имя в
    // поле, затем «Создать».
    private static IEnumerator CreateProgram(UIDocument document)
    {
        TextField input = document.rootVisualElement.panel.visualTree.Q<TextField>("CreateInput") ??
            throw new AssertionException("CreateInput is missing.");
        input.value = "mobile-layout";
        yield return TapAndExpect(document, "DialogConfirmButton", "ProgrammatorPanel");
    }

    private static IEnumerator CloseProgrammator(UIDocument document)
    {
        yield return TapOnly(document, "ProgrammatorCloseButton");
        yield return TapOnly(document, "ListCloseButton");
    }

    private static IEnumerator TapOnly(UIDocument document, string name)
    {
        Tap(document, name);
        yield return null;
        yield return null;
    }

    // Состояние обязано реально открыться: иначе снимок «паузы» был бы
    // снимком HUD, и отчёт молча проверял бы не тот экран.
    private static void AssertShown(UIDocument document, string elementName, string context = "")
    {
        VisualElement element = document.rootVisualElement.panel.visualTree.Q(elementName) ??
            throw new AssertionException($"{elementName} is not in the game document.");
        for (VisualElement? current = element; current != null; current = current.parent)
        {
            Assert.That(
                current.resolvedStyle.display,
                Is.Not.EqualTo(DisplayStyle.None),
                $"{elementName} did not open: {current.name}/{string.Join(".", current.GetClasses())} is display:none. {context}");
        }
    }

    private static string Picked(VisualElement element)
    {
        VisualElement? picked = element.panel.Pick(element.worldBound.center);
        var chain = new List<string>();
        for (VisualElement? current = picked; current != null && chain.Count < 6; current = current.parent)
        {
            chain.Add($"{current.GetType().Name}#{current.name}.{string.Join(".", current.GetClasses())}");
        }

        return string.Join(" < ", chain);
    }

    private static UIDocument GameDocument() =>
        PlayModeHarness.FindComponentInScene<UIDocument>(PlayModeHarness.Scene(SceneNames.MainGame)) ??
        throw new AssertionException("MainGame has no UIDocument.");

    // Нажатие кнопки той же парой событий, что Clickable получает от
    // пальца: PointerDown и PointerUp в центре кнопки. ClickEvent кнопку
    // не нажимает — Clickable слушает именно указатель.
    private static void Tap(UIDocument document, string buttonName)
    {
        Button button = document.rootVisualElement.panel.visualTree.Q<Button>(buttonName) ??
            throw new AssertionException($"Button {buttonName} is not in the game document.");
        Vector2 center = button.worldBound.center;
        Send(button, PointerDownEvent.GetPooled(new TestTouchPointer(center)));
        Send(button, PointerUpEvent.GetPooled(new TestTouchPointer(center) { pressedButtons = 0 }));
    }

    private static void Send(VisualElement target, EventBase evt)
    {
        using (evt)
        {
            evt.target = target;
            target.SendEvent(evt);
        }
    }

    private IEnumerator CaptureAll(
        string screen,
        string sceneName,
        Func<IEnumerator>? open = null,
        Func<IEnumerator>? close = null)
    {
        UIDocument document = PlayModeHarness.FindComponentInScene<UIDocument>(PlayModeHarness.Scene(sceneName)) ??
            throw new AssertionException($"Scene {sceneName} has no UIDocument.");
        _probe?.Dispose();
        _probe = new MobileLayoutProbe(document);

        // Нажатия идут, пока панель нарисована в размер телефона: тач-кнопки
        // существуют только при сенсорной форме экрана.
        if (open != null)
        {
            yield return _probe.Show(MobileDeviceMatrix.All[2]);
            yield return open();
        }

        foreach (MobileDevice device in MobileDeviceMatrix.All)
        {
            yield return _probe.Show(device);
            _probe.Capture(screen, device);
            _probe.DumpLayout(screen, device);
            List<string> found = _probe.Audit(device);
            if (found.Count > 0)
            {
                _violations[$"{screen} / {device.Name}"] = found;
            }
        }

        if (close != null)
        {
            yield return _probe.Show(MobileDeviceMatrix.All[2]);
            yield return close();
        }

        _probe.Dispose();
        _probe = null;
        yield return null;
    }

    private string WriteReport()
    {
        var builder = new StringBuilder();
        foreach (KeyValuePair<string, List<string>> entry in _violations)
        {
            builder.Append("## ").Append(entry.Key).Append(" — ").Append(entry.Value.Count).AppendLine();
            foreach (string line in entry.Value)
            {
                builder.Append("  ").AppendLine(line);
            }
        }

        Directory.CreateDirectory(MobileLayoutProbe.OutputRoot);
        string text = builder.ToString();
        File.WriteAllText(Path.Combine(MobileLayoutProbe.OutputRoot, "report.txt"), text);
        Debug.Log($"[MobileLayout] {MobileLayoutProbe.OutputRoot}/report.txt\n{text}");
        return text;
    }
}

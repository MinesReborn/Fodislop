#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Kern.Editor;

// Снимок экрана UI Toolkit в PNG — сверка переноса макета visual/kern-ui без сцен игры и без тестовой обвязки:
// пустая сцена, одна панель с темой KernTheme, текст из русского словаря. Batch-запуск:
//   Unity -batchmode -projectPath . -executeMethod Kern.Editor.UISnapshotTool.Run -kernSnapshots <файл заданий>
// Задание — строка «uxml|классы корня через пробел|out.png|ширина|высота[|масштаб панели]»; # — комментарий.
// Среди классов «fill:hud» заполняет HUD данными стенда (HudSnapshotFill).
[InitializeOnLoad]
public static class UISnapshotTool
{
    private const string JobKey = "Kern.UISnapshot.Jobs";
    private const string ThemePath = "Assets/UI/Toolkit/Themes/Kern/KernTheme.tss";
    private const string RussianPath = "Assets/Resources/Localization/Locales/ru.json";
    private const int SettleFrames = 45;

    static UISnapshotTool()
    {
        if (string.IsNullOrEmpty(SessionState.GetString(JobKey, string.Empty)))
        {
            return;
        }

        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    public static void Run()
    {
        string[] args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, "-kernSnapshots");
        if (at < 0 || at + 1 >= args.Length)
        {
            throw new ArgumentException("-kernSnapshots <файл заданий> обязателен.");
        }

        SessionState.SetString(JobKey, File.ReadAllText(args[at + 1]));
        // Префикс служебной сцены тест-раннера: PlayModeSceneBootstrapper не подменяет её на Bootstrap
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "InitTestSceneUISnapshot";
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            new Capture(ParseJobs(SessionState.GetString(JobKey, string.Empty))).Start();
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.EraseString(JobKey);
            EditorApplication.Exit(0);
        }
    }

    private static List<string[]> ParseJobs(string text) => text.Split('\n')
        .Select(line => line.Trim())
        .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
        .Select(line => line.Split('|'))
        .ToList();

    private sealed class Capture
    {
        private readonly List<string[]> _jobs;
        private readonly Dictionary<string, string> _russian;
        private int _job;
        private int _frame;
        private UIDocument? _document;
        private RenderTexture? _target;

        public Capture(List<string[]> jobs)
        {
            _jobs = jobs;
            _russian = Regex.Matches(File.ReadAllText(RussianPath), "\"([^\"]+)\":\\s*\"((?:[^\"\\\\]|\\\\.)*)\"")
                .ToDictionary(m => m.Groups[1].Value, m => Regex.Unescape(m.Groups[2].Value));
        }

        public void Start() => EditorApplication.update += Tick;

        private void Tick()
        {
            if (_job >= _jobs.Count)
            {
                EditorApplication.update -= Tick;
                EditorApplication.ExitPlaymode();
                return;
            }

            string[] job = _jobs[_job];
            if (_document == null)
            {
                Open(job);
                return;
            }

            // Ключи — после постройки дерева, повторно: код экрана мог дописать подписи
            if (++_frame % 10 == 0)
            {
                Localize(_document.rootVisualElement);
            }

            if (_frame < SettleFrames)
            {
                return;
            }

            RenderTexture.active = _target;
            var pixels = new Texture2D(_target!.width, _target.height, TextureFormat.RGBA32, false);
            pixels.ReadPixels(new Rect(0, 0, _target.width, _target.height), 0, 0);
            pixels.Apply();
            RenderTexture.active = null;
            Directory.CreateDirectory(Path.GetDirectoryName(job[2])!);
            File.WriteAllBytes(job[2], pixels.EncodeToPNG());
            File.WriteAllText(Path.ChangeExtension(job[2], ".txt"), Boxes(_document.rootVisualElement));
            Debug.Log($"[UISnapshot] {job[2]}");
            UnityEngine.Object.Destroy(pixels);
            UnityEngine.Object.Destroy(_document.gameObject);
            _target.Release();
            _document = null;
            _frame = 0;
            _job++;
        }

        private void Open(string[] job)
        {
            int width = int.Parse(job[3]);
            int height = int.Parse(job[4]);
            _target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            PanelSettings panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            panel.scaleMode = PanelScaleMode.ConstantPixelSize;
            panel.scale = job.Length > 5 ? float.Parse(job[5], System.Globalization.CultureInfo.InvariantCulture) : 1f;
            panel.targetTexture = _target;
            panel.clearColor = true;
            panel.forceGammaRendering = true;
            panel.colorClearValue = Color.black;

            var host = new GameObject("UISnapshot");
            _document = host.AddComponent<UIDocument>();
            _document.panelSettings = panel;
            _document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(job[0])
                ?? throw new FileNotFoundException("UXML не найден.", job[0]);

            VisualElement tree = _document.rootVisualElement;
            tree.AddToClassList("ui-fullscreen");
            foreach (string cls in job[1].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                // «fill:hud» — заполнить HUD данными стенда через рабочий код HUD (HudSnapshotFill)
                if (cls == "fill:hud")
                {
                    HudSnapshotFill.Apply(tree);
                    continue;
                }

                // «Имя.класс» — класс на элемент по имени; просто «класс» — на корень документа
                int dot = cls.IndexOf('.');
                VisualElement target = dot > 0 ? tree.Q(cls[..dot]) ?? tree : tree;
                string name = dot > 0 ? cls[(dot + 1)..] : cls;
                if (name.StartsWith("-", StringComparison.Ordinal))
                {
                    target.RemoveFromClassList(name[1..]);
                }
                else
                {
                    target.AddToClassList(name);
                }
            }

            Localize(tree);

            // Микрографика макета (рамки, уголки, шкалы, точки) — тот же векторный слой, что в игре
            Kern.UI.Screens.KernVector.Attach(tree);
        }

        // Рамки видимых элементов — для сверки с getBoundingClientRect макета: x,y,ширина,высота, имя и классы
        private static string Boxes(VisualElement root)
        {
            var text = new System.Text.StringBuilder();
            foreach (VisualElement element in root.Query<VisualElement>().Build())
            {
                if (element.resolvedStyle.display == DisplayStyle.None || element.worldBound.width <= 0)
                {
                    continue;
                }

                Rect box = element.worldBound;
                string label = element is TextElement t && !string.IsNullOrEmpty(t.text) ? $" «{t.text}»" : string.Empty;
                text.AppendLine($"{box.x:0.#},{box.y:0.#},{box.width:0.#},{box.height:0.#} #{element.name} .{string.Join(".", element.GetClasses())}{label}");
            }

            return text.ToString();
        }

        private void Localize(VisualElement root)
        {
            foreach (TextElement text in root.Query<TextElement>().Build())
            {
                if (_russian.TryGetValue(text.text, out string? value))
                {
                    text.text = text.ClassListContains(Kern.UI.Localization.UILocalizer.CapsClass) ? value.ToUpperInvariant() : value;
                }
            }

            foreach (TextField field in root.Query<TextField>().Build())
            {
                if (_russian.TryGetValue(field.textEdition.placeholder ?? string.Empty, out string? value))
                {
                    field.textEdition.placeholder = value;
                }
            }
        }
    }
}

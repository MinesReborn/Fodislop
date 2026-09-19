#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Kern.Tools.Imgui.Profiling;

// Кто заставляет интерфейс игры перераскладываться.
//
// Маркер PanelSettings.ValidateLayout говорит «сколько», но не «из-за кого».
// Здесь на каждый элемент игровых UIDocument вешается GeometryChangedEvent:
// оно приходит после раскладки и только тем, у кого сменился размер или
// позиция. Изменения, накопленные между двумя Tick, относятся к одному кадру
// и сравниваются со временем раскладки этого кадра.
//
// Каждое изменение пишется в журнал Logs/ui_layout_*.tsv, а по кнопке из
// накопленного собирается список правок Logs/ui_layout_todo.md.
public sealed class UiLayoutTracker : IDisposable
{
    public const double SpikeMilliseconds = 2.0;
    private const float RescanSeconds = 1f;
    private const float FlushSeconds = 1f;
    private const int PathDepth = 4;
    private const int FullPathDepth = 64;

    public sealed class Stat
    {
        public string Label = string.Empty;
        public string FullPath = string.Empty;
        public string ElementType = string.Empty;
        public int Changes;
        public int SizeChanges;
        public int PositionOnlyChanges;
        public int SpikeChanges;
        public int FirstFrame;
        public int LastFrame;
        public Rect LastRect;
        public float MinWidth = float.MaxValue;
        public float MaxWidth;
        public float MinHeight = float.MaxValue;
        public float MaxHeight;
        public readonly List<string> TextSamples = [];
    }

    private readonly struct Change(VisualElement element, Rect oldRect, Rect newRect, string? text)
    {
        public VisualElement Element { get; } = element;
        public Rect OldRect { get; } = oldRect;
        public Rect NewRect { get; } = newRect;
        public string? Text { get; } = text;
    }

    private readonly HashSet<VisualElement> _registered = [];
    private readonly Dictionary<VisualElement, Stat> _stats = [];
    private readonly List<Change> _pending = [];
    private readonly HashSet<VisualElement> _pendingSet = [];
    private readonly List<VisualElement> _scratch = [];
    private readonly List<Stat> _sorted = [];
    private readonly EventCallback<GeometryChangedEvent> _onGeometryChanged;
    private readonly StringBuilder _label = new(256);
    private readonly StringBuilder _line = new(512);

    private readonly UiRenderStatsCollector _renderStatsCollector = new();
    private ProfilerRecorder _layoutRecorder;
    private StreamWriter? _log;
    private float _nextRescan;
    private float _nextFlush;
    private bool _running;

    public UiLayoutTracker()
    {
        _onGeometryChanged = OnGeometryChanged;
    }

    public static string LogDirectory =>
        Application.isEditor
            ? Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Logs")
            : Path.Combine(Application.persistentDataPath, "Logs");

    public string? LogPath { get; private set; }

    public string? LastTodoPath { get; private set; }

    public int ElementCount => _registered.Count;

    public int Frames { get; private set; }

    public int SpikeFrames { get; private set; }

    public int ChangedInLastFrame { get; private set; }

    public int PeakChangedInFrame { get; private set; }

    public double LastLayoutMilliseconds { get; private set; }

    public double PeakLayoutMilliseconds { get; private set; }

    public long TotalChanges { get; private set; }

    public bool LayoutMarkerAvailable => _layoutRecorder.Valid;

    public IReadOnlyList<Stat> Sorted => _sorted;

    public void Start()
    {
        _running = true;
        _nextRescan = 0f;
        OpenLog();
    }

    private void OpenLog()
    {
        if (_log != null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(LogDirectory);
            LogPath = Path.Combine(LogDirectory, $"ui_layout_{DateTime.Now:yyyyMMdd_HHmmss}.tsv");
            _log = new StreamWriter(LogPath, append: false, new UTF8Encoding(false));
            _log.WriteLine("frame\ttime_s\tlayout_ms\tspike\tchanged_in_frame\tkind\tpath\told_x\told_y\told_w\told_h\tnew_x\tnew_y\tnew_w\tnew_h\ttext");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[UiLayoutTracker] Журнал раскладки не открыт: {exception.Message}");
            _log = null;
            LogPath = null;
        }
    }

    // Каждый кадр, пока окно открыто.
    public void Tick()
    {
        if (!_running)
        {
            return;
        }

        if (!_layoutRecorder.Valid && MarkerDirectory.TryGet("PanelSettings.ValidateLayout", out MarkerInfo info))
        {
            _layoutRecorder = new ProfilerRecorder(
                info.Handle,
                1,
                ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame);
            _layoutRecorder.Start();
        }

        if (Time.unscaledTime >= _nextRescan)
        {
            _nextRescan = Time.unscaledTime + RescanSeconds;
            Rescan();
        }

        CloseFrame();

        if (_log != null && Time.unscaledTime >= _nextFlush)
        {
            _nextFlush = Time.unscaledTime + FlushSeconds;
            _log.Flush();
        }
    }

    // Изменения, пришедшие после прошлого Tick, раскладывались в прошлом кадре;
    // рекордер к этому моменту тоже отдаёт прошлый кадр.
    private void CloseFrame()
    {
        double layout = _layoutRecorder.Valid ? _layoutRecorder.LastValue * 1e-6 : 0d;
        bool spike = layout >= SpikeMilliseconds;
        int frame = Time.frameCount - 1;
        Frames++;
        LastLayoutMilliseconds = layout;
        PeakLayoutMilliseconds = Math.Max(PeakLayoutMilliseconds, layout);
        ChangedInLastFrame = _pending.Count;
        PeakChangedInFrame = Math.Max(PeakChangedInFrame, _pending.Count);
        if (spike)
        {
            SpikeFrames++;
        }

        foreach (Change change in _pending)
        {
            if (!_stats.TryGetValue(change.Element, out Stat? stat))
            {
                continue;
            }

            if (spike)
            {
                stat.SpikeChanges++;
            }

            WriteLine(frame, layout, spike, _pending.Count, change, stat);
        }

        _pending.Clear();
        _pendingSet.Clear();
    }

    private void WriteLine(int frame, double layout, bool spike, int changedInFrame, in Change change, Stat stat)
    {
        if (_log == null)
        {
            return;
        }

        CultureInfo invariant = CultureInfo.InvariantCulture;
        bool sized = change.OldRect.size != change.NewRect.size;
        _line.Clear();
        _line.Append(frame).Append('\t')
            .Append(Time.unscaledTime.ToString("F3", invariant)).Append('\t')
            .Append(layout.ToString("F3", invariant)).Append('\t')
            .Append(spike ? 1 : 0).Append('\t')
            .Append(changedInFrame).Append('\t')
            .Append(sized ? "size" : "position").Append('\t')
            .Append(stat.FullPath).Append('\t');
        AppendRect(change.OldRect, invariant);
        AppendRect(change.NewRect, invariant);
        _line.Append(Sanitize(change.Text));
        _log.WriteLine(_line.ToString());
    }

    private void AppendRect(Rect rect, CultureInfo invariant)
    {
        _line.Append(rect.x.ToString("F1", invariant)).Append('\t')
            .Append(rect.y.ToString("F1", invariant)).Append('\t')
            .Append(rect.width.ToString("F1", invariant)).Append('\t')
            .Append(rect.height.ToString("F1", invariant)).Append('\t');
    }

    private static string Sanitize(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : text.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

    private void OnGeometryChanged(GeometryChangedEvent evt)
    {
        if (evt.target is not VisualElement element)
        {
            return;
        }

        if (!_stats.TryGetValue(element, out Stat? stat))
        {
            stat = new Stat
            {
                Label = Describe(element, PathDepth),
                FullPath = Describe(element, FullPathDepth),
                ElementType = element.GetType().Name,
                FirstFrame = Time.frameCount,
            };
            _stats[element] = stat;
        }

        Rect newRect = evt.newRect;
        bool sized = evt.oldRect.size != newRect.size;
        stat.Changes++;
        if (sized)
        {
            stat.SizeChanges++;
        }
        else
        {
            stat.PositionOnlyChanges++;
        }

        stat.LastFrame = Time.frameCount;
        stat.LastRect = newRect;
        stat.MinWidth = Mathf.Min(stat.MinWidth, newRect.width);
        stat.MaxWidth = Mathf.Max(stat.MaxWidth, newRect.width);
        stat.MinHeight = Mathf.Min(stat.MinHeight, newRect.height);
        stat.MaxHeight = Mathf.Max(stat.MaxHeight, newRect.height);

        string? text = element is TextElement textElement ? textElement.text : null;
        if (!string.IsNullOrEmpty(text) && stat.TextSamples.Count < 5 && !stat.TextSamples.Contains(text!))
        {
            stat.TextSamples.Add(text!);
        }

        TotalChanges++;
        if (_pendingSet.Add(element))
        {
            _pending.Add(new Change(element, evt.oldRect, newRect, text));
        }
    }

    private void Rescan()
    {
        // Отцепиться от ушедших из панели элементов, чтобы не держать их.
        _scratch.Clear();
        foreach (VisualElement element in _registered)
        {
            if (element.panel == null)
            {
                _scratch.Add(element);
            }
        }

        foreach (VisualElement element in _scratch)
        {
            element.UnregisterCallback(_onGeometryChanged);
            _registered.Remove(element);
        }

        foreach (UIDocument document in _renderStatsCollector.CollectDocuments())
        {
            if (document.rootVisualElement != null)
            {
                Register(document.rootVisualElement);
            }
        }

        _renderStatsCollector.CollectRenderStats(Short);
    }

    public sealed class RenderStats
    {
        public int Visible;
        public int Texts;
        public int TextCharacters;
        public int OutlinedTexts;
        public int ShadowedTexts;
        public int Clips;
        public int RoundedClips;
        public int Translucent;
        public int Translated;
        public int Images;
        public int AtlasLimit = 64;
        public readonly Dictionary<Texture, int> Textures = [];
        public readonly Dictionary<string, int> VisibleBySubtree = new(StringComparer.Ordinal);
    }

    public RenderStats Stats => _renderStatsCollector.Stats;

    private void Register(VisualElement root)
    {
        _scratch.Clear();
        _scratch.Add(root);
        while (_scratch.Count > 0)
        {
            VisualElement element = _scratch[^1];
            _scratch.RemoveAt(_scratch.Count - 1);
            if (_registered.Add(element))
            {
                element.RegisterCallback(_onGeometryChanged);
            }

            for (int i = 0; i < element.hierarchy.childCount; i++)
            {
                _scratch.Add(element.hierarchy[i]);
            }
        }
    }

    // Строки собираются в Tick окна, а не в OnGUI.
    public void Rebuild(int keep)
    {
        SortInto(_sorted);
        if (_sorted.Count > keep)
        {
            _sorted.RemoveRange(keep, _sorted.Count - keep);
        }
    }

    private void SortInto(List<Stat> target)
    {
        target.Clear();
        foreach (Stat stat in _stats.Values)
        {
            target.Add(stat);
        }

        target.Sort(static (left, right) =>
        {
            int bySpike = right.SpikeChanges.CompareTo(left.SpikeChanges);
            return bySpike != 0 ? bySpike : right.Changes.CompareTo(left.Changes);
        });
    }

    public string? WriteTodo()
    {
        _log?.Flush();
        var all = new List<Stat>(_stats.Count);
        SortInto(all);

        string? path = UiLayoutTodoWriter.WriteTodo(
            LogDirectory,
            LogPath,
            Frames,
            SpikeFrames,
            PeakLayoutMilliseconds,
            ElementCount,
            TotalChanges,
            all);

        if (path != null)
        {
            LastTodoPath = path;
        }

        return path;
    }

    public void ResetStats()
    {
        _stats.Clear();
        _sorted.Clear();
        _pending.Clear();
        _pendingSet.Clear();
        Frames = 0;
        SpikeFrames = 0;
        PeakChangedInFrame = 0;
        PeakLayoutMilliseconds = 0d;
        TotalChanges = 0;
    }

    private string Describe(VisualElement element, int depthLimit)
    {
        _label.Clear();
        VisualElement? current = element;
        for (int depth = 0; depth < depthLimit && current != null; depth++)
        {
            if (depth > 0)
            {
                _label.Insert(0, " / ");
            }

            _label.Insert(0, Short(current));
            current = current.hierarchy.parent;
        }

        if (current != null)
        {
            _label.Insert(0, "… / ");
        }

        return _label.ToString();
    }

    private static string Short(VisualElement element)
    {
        if (!string.IsNullOrEmpty(element.name))
        {
            return "#" + element.name;
        }

        foreach (string className in element.GetClasses())
        {
            return element.GetType().Name + "." + className;
        }

        return element.GetType().Name;
    }

    public void Stop()
    {
        _running = false;
        foreach (VisualElement element in _registered)
        {
            element.UnregisterCallback(_onGeometryChanged);
        }

        _registered.Clear();
        _pending.Clear();
        _pendingSet.Clear();
        if (_layoutRecorder.Valid)
        {
            _layoutRecorder.Dispose();
        }

        _layoutRecorder = default;
        if (_log != null)
        {
            if (TotalChanges > 0)
            {
                WriteTodo();
            }

            _log.Dispose();
            _log = null;
        }
    }

    public void Dispose()
    {
        Stop();
    }
}

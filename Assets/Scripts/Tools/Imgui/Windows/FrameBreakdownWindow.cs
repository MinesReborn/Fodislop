#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using Kern.Tools.Imgui.Profiling;
using UnityEngine;

namespace Kern.Tools.Imgui.Windows;

public sealed class FrameBreakdownWindow : ToolWindow
{
    private const float RefreshInterval = 0.25f;
    private const double BudgetMilliseconds = 1000.0 / 60.0;
    private const int FrameTabLoopRows = 8;

    private enum Tab
    {
        Frame,
        Loop,
        Hot,
        Spikes,
        Layout,
        Stages,
        Tools,
        Memory,
        Search,
        Scene,
    }

    private static readonly (Tab Tab, string Label)[] _Tabs =
    [
        (Tab.Frame, "Кадр"),
        (Tab.Loop, "Цикл"),
        (Tab.Hot, "Горячее"),
        (Tab.Spikes, "Всплески"),
        (Tab.Layout, "Раскладка"),
        (Tab.Stages, "Участки"),
        (Tab.Tools, "Окна"),
        (Tab.Memory, "Память"),
        (Tab.Search, "Поиск"),
        (Tab.Scene, "Сцена"),
    ];

    private readonly FrameBreakdownRowRenderer _rowRenderer = new();
    private readonly ThreadTimings _timings = new();
    private readonly PlayerLoopBreakdown _loop = new();
    private readonly MarkerSearch _search = new();
    private readonly HotMarkerSweep _sweep = new();
    private readonly SpikeSweep _spikes = new();
    private bool _spikesRequested;
    private bool _layoutActive;
    private readonly UiLayoutTracker _layout = new();
    private readonly SceneCensus _census = new();
    private bool _sweepRequested;
    private bool _layoutResetRequested;
    private bool _layoutTodoRequested;
    private readonly FrameProbe _playerLoop = new("Игровой цикл (PlayerLoop)", "PlayerLoop");
    private readonly List<FrameProbe> _cpu = FrameProbeCatalog.CreateCpuProbes();
    private readonly List<FrameProbe> _gpu = FrameProbeCatalog.CreateGpuProbes();
    private readonly List<FrameProbe> _gpuRecord = FrameProbeCatalog.CreateGpuRecordProbes();
    private readonly List<FrameProbe> _interface = FrameProbeCatalog.CreateInterfaceProbes();
    private readonly List<FrameProbe> _memory = FrameProbeCatalog.CreateMemoryProbes();
    private readonly List<FrameProbe> _render = FrameProbeCatalog.CreateRenderCounters();
    private readonly FrameProbeDiscoverer _discoverer = new();
    private readonly HashSet<string> _catalogNames = new(StringComparer.Ordinal);
    private readonly Dictionary<Tab, List<FrameBreakdownRow>> _rows = new()
    {
        [Tab.Frame] = [],
        [Tab.Loop] = [],
        [Tab.Hot] = [],
        [Tab.Spikes] = [],
        [Tab.Layout] = [],
        [Tab.Stages] = [],
        [Tab.Tools] = [],
        [Tab.Memory] = [],
        [Tab.Search] = [],
        [Tab.Scene] = [],
    };

    private Tab _tab = Tab.Frame;
    private Tab _requestedTab = Tab.Frame;
    private bool _started;
    private bool _profilerAvailable;
    private bool _copyRequested;
    private float _copiedUntil;
    private float _nextUpdate;
    private Vector2 _scroll;

    public FrameBreakdownWindow()
        : base("Разбор кадра", new Rect(628f, 596f, 470f, 560f))
    {
        foreach (List<FrameProbe> list in AllCatalogLists())
        {
            foreach (FrameProbe probe in list)
            {
                _catalogNames.Add(probe.MarkerName);
            }
        }
    }

    public override bool WantsSampling => Visible;

    public override Vector2 MinimumSize => new(400f, 320f);

    private IEnumerable<List<FrameProbe>> AllCatalogLists()
    {
        yield return _cpu;
        yield return _gpu;
        yield return _gpuRecord;
        yield return _interface;
        yield return _memory;
        yield return _render;
    }

    protected override void OnVisibilityChanged(bool visible)
    {
        if (visible)
        {
            StartAll();
            return;
        }

        StopAll();
    }

    public override void Tick()
    {
        if (!Visible)
        {
            return;
        }

        if (!_started)
        {
            StartAll();
        }

        // Тайминги потоков движок отдаёт только за последние кадры, поэтому
        // снимаются каждый кадр, а не с частотой обновления строк.
        _timings.Tick();
        foreach (FrameProbe probe in _gpu)
        {
            probe.TickGpu();
        }

        _search.Tick(_tab == Tab.Search);
        // Копирование отчёта тоже снимает перепись: иначе в отчёте, снятом
        // с другой вкладки, раздел «Сцена» оставался бы пустым.
        _census.Tick(_tab == Tab.Scene || _copyRequested);
        if (_sweepRequested)
        {
            _sweepRequested = false;
            _sweep.Begin();
        }

        _sweep.Tick();

        // Два прохода сразу мешали бы друг другу: сотни лишних рекордеров
        // сами дают всплески при открытии пачек.
        if (_spikesRequested && !_sweep.Running)
        {
            _spikesRequested = false;
            _spikes.Begin();
        }

        _spikes.Tick();
        if (_layoutResetRequested)
        {
            _layoutResetRequested = false;
            _layout.ResetStats();
        }

        // Трекер раз в секунду ищет все UIDocument и обходит всё дерево
        // интерфейса, вместе с подписями мира: сам давал рывок каждую
        // секунду. Он работает, только пока открыта его вкладка.
        bool layoutWanted = _tab == Tab.Layout;
        if (layoutWanted != _layoutActive)
        {
            _layoutActive = layoutWanted;
            if (layoutWanted)
            {
                _layout.Start();
            }
            else
            {
                _layout.Stop();
            }
        }

        _layout.Tick();
        if (_layoutTodoRequested)
        {
            _layoutTodoRequested = false;
            string? todo = _layout.WriteTodo();
            if (todo != null)
            {
                Debug.Log($"[FrameBreakdown] TODO раскладки: {todo}");
            }
        }

        // Отчёт без прохода по всем маркерам бесполезен для вопроса «что
        // именно рендерится»: копирование сначала прочёсывает, потом копирует.
        if (_copyRequested && !_sweep.HasResults && !_sweep.Running)
        {
            _sweep.Begin();
        }

        if (_copyRequested && !_sweep.Running)
        {
            _copyRequested = false;
            GUIUtility.systemCopyBuffer = BuildReport();
            _copiedUntil = Time.unscaledTime + 2f;
        }

        UpdateButtonLabels();
        if (Time.unscaledTime < _nextUpdate)
        {
            return;
        }

        _nextUpdate = Time.unscaledTime + RefreshInterval;
        SampleAll();
        RebuildRows();
    }

    // GPU-тайминги приходят с задержкой. Без связи по идентификатору кадра
    // нельзя группировать их по CPU-маркеру текущего или соседнего кадра.

    // Подписи кнопок собираются в Tick и только при смене чисел: в DrawContent
    // интерполяция шла на каждое событие IMGUI.
    private string _copyLabel = "Копировать отчёт";
    private string _sweepLabel = "Прочесать все маркеры";
    private int _labelProcessed = -1;
    private int _labelState = -1;

    private void UpdateButtonLabels()
    {
        int state = _copyRequested ? 1 : _sweep.Running ? 2 : Time.unscaledTime < _copiedUntil ? 3 : 0;
        if (state == _labelState && (state is 0 or 3 || _sweep.Processed == _labelProcessed))
        {
            return;
        }

        _labelState = state;
        _labelProcessed = _sweep.Processed;
        _copyLabel = state switch
        {
            1 => $"Прочёсываю: {_sweep.Processed} / {_sweep.Total}",
            3 => "Скопировано",
            _ => "Копировать отчёт",
        };
        _sweepLabel = _sweep.Running
            ? $"Идёт проход: {_sweep.Processed} / {_sweep.Total}"
            : "Прочесать все маркеры";
    }

    protected override void OnPlaySessionReset()
    {
        _scroll = default;
        _nextUpdate = 0f;
        StopAll();
        _timings.Clear();
        foreach (List<FrameBreakdownRow> rows in _rows.Values)
        {
            rows.Clear();
        }
    }

    protected override void OnDispose()
    {
        StopAll();
        _search.Dispose();
        _sweep.Dispose();
        _spikes.Dispose();
        _layout.Dispose();
        _loop.Dispose();
    }

    private void StartAll()
    {
        _timings.Clear();
        _playerLoop.Start();
        _loop.Begin();
        Kern.Core.Interfaces.Diagnostics.AllocationLedger.Enabled = true;
        foreach (List<FrameProbe> list in AllCatalogLists())
        {
            foreach (FrameProbe probe in list)
            {
                probe.Start();
            }
        }

        _started = true;
    }

    private void StopAll()
    {
        _playerLoop.Stop();
        _loop.Stop();
        _search.Stop();
        _sweep.Cancel();
        _spikes.Cancel();
        _layout.Stop();
        _layoutActive = false;
        Kern.Core.Interfaces.Diagnostics.AllocationLedger.Enabled = false;
        foreach (List<FrameProbe> list in AllCatalogLists())
        {
            foreach (FrameProbe probe in list)
            {
                probe.Stop();
            }
        }

        _discoverer.Reset();
        _started = false;
    }

    private void SampleAll()
    {
        _playerLoop.Sample();
        _loop.Sample();
        _search.Sample();
        foreach (List<FrameProbe> list in AllCatalogLists())
        {
            foreach (FrameProbe probe in list)
            {
                probe.Sample();
            }
        }

        _discoverer.Discover(_catalogNames);
        _discoverer.Sample();
        _profilerAvailable = _playerLoop.Available || MarkerDirectory.All.Count > 0;
    }

    // Строки собираются только для открытой вкладки: остальные никто не
    // видит, а их сборка каждые 250 мс была главным мусором самого окна.
    private void RebuildRows()
    {
        List<FrameBreakdownRow> rows = _rows[_tab];
        switch (_tab)
        {
            case Tab.Frame: FrameBreakdownRowBuilder.BuildFrameRows(rows, _timings, _playerLoop, _loop, _search, _memory, ToolAllocatedBytes()); break;
            case Tab.Loop: FrameBreakdownRowBuilder.BuildLoopRows(rows, _loop, _playerLoop, _search); break;
            case Tab.Hot: FrameBreakdownRowBuilder.BuildHotRows(rows, _sweep); break;
            case Tab.Spikes: FrameBreakdownRowBuilder.BuildSpikeRows(rows, _spikes); break;
            case Tab.Layout: FrameBreakdownRowBuilder.BuildLayoutRows(rows, _layout); break;
            case Tab.Stages: FrameBreakdownRowBuilder.BuildStageRows(rows, _cpu, _gpu, _gpuRecord, _discoverer.Discovered, _timings, _search); break;
            case Tab.Tools: FrameBreakdownRowBuilder.BuildToolRows(rows, new List<ToolWindow>(ToolWindows.All), _interface, _search); break;
            case Tab.Memory: FrameBreakdownRowBuilder.BuildMemoryRows(rows, _memory, _render); break;
            case Tab.Search: FrameBreakdownRowBuilder.BuildSearchRows(rows, _search); break;
            case Tab.Scene: FrameBreakdownRowBuilder.BuildSceneRows(rows, _census); break;
            default: throw new ArgumentOutOfRangeException(nameof(_tab), _tab, "Unhandled frame breakdown tab");
        }
    }

    private void RebuildAllRows()
    {
        FrameBreakdownRowBuilder.BuildFrameRows(_rows[Tab.Frame], _timings, _playerLoop, _loop, _search, _memory, ToolAllocatedBytes());
        FrameBreakdownRowBuilder.BuildLoopRows(_rows[Tab.Loop], _loop, _playerLoop, _search);
        FrameBreakdownRowBuilder.BuildHotRows(_rows[Tab.Hot], _sweep);
        FrameBreakdownRowBuilder.BuildSpikeRows(_rows[Tab.Spikes], _spikes);
        FrameBreakdownRowBuilder.BuildLayoutRows(_rows[Tab.Layout], _layout);
        FrameBreakdownRowBuilder.BuildStageRows(_rows[Tab.Stages], _cpu, _gpu, _gpuRecord, _discoverer.Discovered, _timings, _search);
        FrameBreakdownRowBuilder.BuildToolRows(_rows[Tab.Tools], new List<ToolWindow>(ToolWindows.All), _interface, _search);
        FrameBreakdownRowBuilder.BuildMemoryRows(_rows[Tab.Memory], _memory, _render);
        FrameBreakdownRowBuilder.BuildSearchRows(_rows[Tab.Search], _search);
        FrameBreakdownRowBuilder.BuildSceneRows(_rows[Tab.Scene], _census);
    }

    private static long ToolAllocatedBytes()
    {
        long total = 0;
        foreach (ToolWindow window in ToolWindows.All)
        {
            total += window.TickAllocatedBytes + window.DrawAllocatedBytes;
        }

        return total;
    }

    private string BuildReport()
    {
        SampleAll();
        RebuildAllRows();
        return FrameBreakdownReportBuilder.BuildReport(_Tabs, _rows, Tab.Search, _search);
    }

    protected override void DrawContent()
    {
        if (Event.current.type == EventType.Layout && _tab != _requestedTab)
        {
            _tab = _requestedTab;

            // Строки новой вкладки ещё не собраны: пересборка на ближайшем Tick.
            _nextUpdate = 0f;
        }

        using (ToolLayout.Horizontal())
        {
            foreach ((Tab tab, string label) in _Tabs)
            {
                if (GUILayout.Toggle(_tab == tab, label, SegmentedButtonStyle) && _tab != tab)
                {
                    _requestedTab = tab;
                }
            }
        }

        using (ToolLayout.Horizontal())
        {
            if (GUILayout.Button(
                    _copyLabel,
                    SecondaryButtonStyle))
            {
                _copyRequested = true;
            }

            GUILayout.Label("обновление 250 мс", MutedLabelStyle);
        }

        if (_tab == Tab.Search)
        {
            _search.Query = GUILayout.TextField(_search.Query);
        }

        if (_tab == Tab.Layout)
        {
            using (ToolLayout.Horizontal())
            {
                if (GUILayout.Button("Сохранить TODO раскладки", ActiveButtonStyle))
                {
                    _layoutTodoRequested = true;
                }

                if (GUILayout.Button("Сбросить счёт", SecondaryButtonStyle))
                {
                    _layoutResetRequested = true;
                }
            }
        }

        if (_tab == Tab.Hot &&
            GUILayout.Button(
                _sweepLabel,
                ActiveButtonStyle) &&
            !_sweep.Running)
        {
            _sweepRequested = true;
        }

        if (_tab == Tab.Spikes &&
            GUILayout.Button(
                _spikes.Running ? "Идёт проход…" : "Поймать всплески",
                ActiveButtonStyle) &&
            !_spikes.Running)
        {
            _spikesRequested = true;
        }

        using (ToolLayout.ScrollView(ref _scroll))
        {
            if (!_profilerAvailable && _tab != Tab.Tools)
            {
                ToolChrome.Banner("МАРКЕРЫ НЕДОСТУПНЫ", ToolTheme.Warning);
                GUILayout.Label(
                    "В этой сборке не определён ENABLE_PROFILER. Он есть в вариантах " +
                    "Instrumented, Checked и Debug, но не в Release. Вкладка «Окна» " +
                    "и тайминги потоков работают и без него.",
                    WrappedLabelStyle);
            }

            _rowRenderer.DrawRows(_rows[_tab], (int)_tab, _scroll, MutedLabelStyle, SecondaryButtonStyle, _search);
        }

        if (Event.current.type == EventType.Repaint)
        {
            _rowRenderer.UpdateViewportHeight(GUILayoutUtility.GetLastRect().height);
        }
    }
}

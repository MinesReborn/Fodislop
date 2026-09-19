#nullable enable

using System.Collections.Generic;
using Kern.Networking.Diagnostics;
using UnityEngine;

namespace Kern.Tools.Imgui.Windows;

public sealed class PacketTrafficWindow : ToolWindow
{
    private const float RefreshInterval = 0.5f;

    private const double SilenceThresholdSeconds = 5d;

    private readonly List<PacketStat> _incoming = [];
    private readonly List<PacketStat> _outgoing = [];
    private readonly List<PacketEvent> _history = [];
    private readonly List<string> _incomingRows = [];
    private readonly List<string> _outgoingRows = [];
    private readonly List<string> _historyRows = [];

    private string _summary = "учёт не ведётся";
    private string _rateSummary = string.Empty;
    private string _batchSummary = string.Empty;
    private string _queueSummary = string.Empty;
    private string _unhandledBanner = string.Empty;
    private string _filter = string.Empty;
    private bool _frozen;
    private long _peakCount;
    private bool _showHistory = true;
    private float _nextUpdate;
    private Vector2 _scroll;

    public PacketTrafficWindow()
        : base("Пакеты сервера", new Rect(292f, 736f, 380f, 420f))
    {
    }

    public override bool WantsSampling => Visible;

    public override Vector2 MinimumSize => new(340f, 280f);

    protected override void OnVisibilityChanged(bool visible)
    {
        PacketTelemetry.Enabled = visible;
        if (!visible)
        {
            return;
        }

        // Показания за время, пока окно было закрыто, не собирались, и
        // оставлять прежние значит показывать смесь двух разных отрезков.
        PacketTelemetry.Reset();
    }

    public override void Tick()
    {
        if (!Visible)
        {
            return;
        }

        // Частоты считаются каждым кадром, а не по расписанию окна: секундное
        // окно должно закрываться вовремя даже тогда, когда не пришло ничего,
        // иначе прекратившийся поток показывал бы последнюю ненулевую частоту.
        PacketTelemetry.Poll(Time.unscaledTimeAsDouble);

        if (_frozen || Time.unscaledTime < _nextUpdate)
        {
            return;
        }

        _nextUpdate = Time.unscaledTime + RefreshInterval;
        PacketTelemetry.CollectIncoming(_incoming);
        PacketTelemetry.CollectOutgoing(_outgoing);
        PacketTelemetry.CollectHistory(_history);
        Rebuild();
    }

    protected override void OnPlaySessionReset()
    {
        _scroll = default;
        _nextUpdate = 0f;
        _showHistory = true;
        PacketTelemetry.Reset();
        _incoming.Clear();
        _outgoing.Clear();
        _history.Clear();
        _incomingRows.Clear();
        _outgoingRows.Clear();
        _summary = "учёт не ведётся";
        _rateSummary = string.Empty;
        _batchSummary = string.Empty;
        _queueSummary = string.Empty;
        _historyRows.Clear();
        _unhandledBanner = string.Empty;
        _filter = string.Empty;
        _frozen = false;
    }

    private void ResetCounters()
    {
        PacketTelemetry.Reset();
        _unhandledBanner = string.Empty;
        _incoming.Clear();
        _outgoing.Clear();
        _history.Clear();
        _incomingRows.Clear();
        _outgoingRows.Clear();
        _historyRows.Clear();
        _nextUpdate = 0f;
    }

    private void DrawQueue()
    {
        ToolChrome.SectionHeader("ОЧЕРЕДЬ ПРИЁМА");
        bool starved = PacketTelemetry.BudgetStopCount > 0 || PacketTelemetry.BatchCapStopCount > 0;
        using (ToolLayout.Horizontal())
        {
            ToolChrome.StatusPip(starved ? ToolTheme.Warning : ToolTheme.Success);
            GUILayout.Label(_queueSummary, MutedLabelStyle);
        }
    }

    protected override void OnDispose()
    {
        PacketTelemetry.Enabled = false;
    }

    private void Rebuild()
    {
        _summary =
            $"принято {PacketTelemetry.TotalIncoming}, " +
            $"отправлено {PacketTelemetry.TotalOutgoing}, " +
            $"без обработчика {PacketTelemetry.TotalUnhandled}";
        _rateSummary =
            $"сейчас {PacketTelemetry.IncomingPerSecond:F1} вход/с, " +
            $"{PacketTelemetry.OutgoingPerSecond:F1} исход/с";
        _queueSummary =
            $"в очереди {PacketTelemetry.QueueDepth}, пик {PacketTelemetry.PeakQueueDepth}; " +
            $"разбор оборван бюджетом {PacketTelemetry.BudgetStopCount}, " +
            $"потолком партии {PacketTelemetry.BatchCapStopCount}";

        double perBatch = PacketTelemetry.BatchCount > 0
            ? PacketTelemetry.BatchedPacketCount / (double)PacketTelemetry.BatchCount
            : 0d;
        _batchSummary =
            $"пачек HB {PacketTelemetry.BatchCount} (по {perBatch:F1} пакета), " +
            $"снято сжатых обёрток {PacketTelemetry.CompressedCount}" +
            (PacketTelemetry.SlowestHandlerName.Length > 0
                ? $"\nсамый медленный обработчик: {PacketTelemetry.SlowestHandlerName} — {PacketTelemetry.SlowestHandlerMilliseconds:F2} мс"
                : string.Empty);

        // Решение «есть баннер» принимается здесь, а не в отрисовке: живой
        // счётчик мог измениться между Layout и Repaint, и число контролов
        // событий разошлось бы. Заодно строка не собирается на каждое событие.
        long unhandled = PacketTelemetry.TotalUnhandled;
        _unhandledBanner = unhandled > 0 ? $"БЕЗ ОБРАБОТЧИКА: {unhandled}" : string.Empty;

        double now = Time.unscaledTimeAsDouble;
        _peakCount = 0;
        BuildRows(_incoming, _incomingRows, ref _peakCount, now);
        long outgoingPeak = 0;
        BuildRows(_outgoing, _outgoingRows, ref outgoingPeak, now);
        _peakCount = System.Math.Max(_peakCount, outgoingPeak);
        BuildHistoryRows();
    }

    private void BuildHistoryRows()
    {
        double now = Time.unscaledTimeAsDouble;
        _historyRows.Clear();
        foreach (PacketEvent entry in _history)
        {
            double age = now - entry.TimeSeconds;
            _historyRows.Add(
                $"{(entry.Incoming ? "◄" : "►")}  −{age:F1} с   {entry.Name}" +
                (entry.Handled ? string.Empty : "   ·   без обработчика"));
        }
    }

    private static void BuildRows(List<PacketStat> stats, List<string> rows, ref long peak, double now)
    {
        stats.Sort(static (left, right) => right.Count.CompareTo(left.Count));
        rows.Clear();
        foreach (PacketStat stat in stats)
        {
            peak = System.Math.Max(peak, stat.Count);

            // Время с последнего появления показывается только когда его
            // прилично много. Тип, идущий прямо сейчас, в этой отметке не
            // нуждается, а вот замолчавший по одному лишь счётчику неотличим
            // от идущего: число у него так и стоит на месте.
            double silence = now - stat.LastSeenSeconds;
            string tail = silence >= SilenceThresholdSeconds
                ? $"   ·   молчит {silence:F0} с"
                : string.Empty;
            string cost = stat.HandlerCount > 0
                ? $"   ·   обработка {stat.HandlerAverageMilliseconds:F2} мс, пик {stat.HandlerPeakMilliseconds:F2}, всего {stat.HandlerTotalMilliseconds:F0}"
                : string.Empty;
            rows.Add(stat.UnhandledCount > 0
                ? $"{stat.Name}   {stat.Count}   ·   без обработчика {stat.UnhandledCount}{cost}{tail}"
                : $"{stat.Name}   {stat.Count}{cost}{tail}");
        }
    }

    protected override void DrawContent()
    {
        using (ToolLayout.ScrollView(ref _scroll))
        {
            if (_unhandledBanner.Length > 0)
            {
                ToolChrome.Banner(_unhandledBanner, ToolTheme.Warning);
                GUILayout.Space(4f);
            }

            ToolChrome.SectionHeader("ИТОГО");
            GUILayout.Label(_summary, MutedLabelStyle);
            GUILayout.Label(_rateSummary, MutedLabelStyle);
            GUILayout.Label(_batchSummary, MutedLabelStyle);
            using (ToolLayout.Horizontal())
            {
                if (GUILayout.Button("Обнулить счёт", SecondaryButtonStyle))
                {
                    ResetCounters();
                }

                _frozen = GUILayout.Toggle(_frozen, "Заморозить", ToolTheme.SegmentedButton);
            }

            DrawQueue();

            ToolChrome.SectionHeader("ОТ СЕРВЕРА");
            DrawGroup(_incomingRows, _incoming, ToolTheme.FrameGraphColor);

            ToolChrome.SectionHeader("К СЕРВЕРУ");
            DrawGroup(_outgoingRows, _outgoing, ToolTheme.Success);

            DrawHistory();
        }
    }

    private void DrawGroup(List<string> rows, List<PacketStat> stats, Color color)
    {
        if (rows.Count == 0)
        {
            GUILayout.Label("Пока ничего.", MutedLabelStyle);
            return;
        }

        for (int i = 0; i < rows.Count && i < stats.Count; i++)
        {
            using (ToolLayout.Horizontal())
            {
                ToolChrome.StatusPip(stats[i].UnhandledCount > 0 ? ToolTheme.Warning : color);
                GUILayout.Label(rows[i], MutedLabelStyle);
            }

            float share = _peakCount > 0 ? stats[i].Count / (float)_peakCount : 0f;
            ToolChrome.MeterLine(share, stats[i].UnhandledCount > 0 ? ToolTheme.Warning : color, 3f);
            GUILayout.Space(2f);
        }
    }

    private void DrawHistory()
    {
        ToolChrome.SectionHeader("ЛЕНТА");
        _showHistory = GUILayout.Toggle(_showHistory, "Показывать ленту", ToolTheme.SegmentedButton);
        if (!_showHistory)
        {
            return;
        }

        _filter = GUILayout.TextField(_filter);
        GUILayout.Label("Фильтр по имени пакета; пусто — показывать всё.", MutedLabelStyle);

        if (_historyRows.Count == 0)
        {
            GUILayout.Label("Событий ещё нет.", MutedLabelStyle);
            return;
        }

        int shown = 0;
        for (int i = 0; i < _historyRows.Count && i < _history.Count; i++)
        {
            PacketEvent entry = _history[i];
            if (_filter.Length > 0 &&
                entry.Name.IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            using (ToolLayout.Horizontal())
            {
                ToolChrome.StatusPip(
                    !entry.Handled
                        ? ToolTheme.Warning
                        : entry.Incoming ? ToolTheme.FrameGraphColor : ToolTheme.Success,
                    5f);
                GUILayout.Label(_historyRows[i], MutedLabelStyle);
            }

            shown++;
        }

        if (shown == 0)
        {
            GUILayout.Label("Под фильтр ничего не подошло.", MutedLabelStyle);
        }
    }
}

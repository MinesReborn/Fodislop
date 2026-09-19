#nullable enable

using System;
using System.Collections.Generic;
using Kern.Tools.Imgui.Profiling;
using UnityEngine;

namespace Kern.Tools.Imgui.Windows;

internal static class FrameBreakdownRowBuilder
{
    private const double BudgetMilliseconds = 1000.0 / 60.0;
    private const int FrameTabLoopRows = 8;

    private static readonly List<(int Start, int Length, double Weight)> _stageOrder = [];
    private static readonly List<FrameProbe> _reordered = [];
    private static readonly List<Kern.Core.Interfaces.Diagnostics.AllocationLedger.Entry> _ledgerSorted = [];

    public static void BuildFrameRows(
        List<FrameBreakdownRow> rows,
        ThreadTimings timings,
        FrameProbe playerLoop,
        PlayerLoopBreakdown loop,
        MarkerSearch search,
        IReadOnlyList<FrameProbe> memory,
        long toolBytes)
    {
        rows.Clear();
        rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Header, "ПОТОКИ — АГРЕГАТЫ FRAMETIMINGMANAGER"));
        if (!timings.Available)
        {
            rows.Add(new FrameBreakdownRow(
                FrameBreakdownRowKind.Warning,
                "Тайминги потоков недоступны. В сборке нужен флаг Frame Timing Stats " +
                "в Player Settings; в редакторе они есть не на всех платформах."));
        }
        else
        {
            double scale = Math.Max(BudgetMilliseconds, timings.Frame.Peak);
            AddSeries(rows, "Кадр целиком", timings.Frame, scale, ToolTheme.Accent);
            AddSeries(rows, "· главный поток — активная работа", timings.MainThread, scale, ToolTheme.Warning);
            AddSeries(rows, "· ожидание вывода — отдельная метрика", timings.PresentWait, scale, ToolTheme.Warning);
            AddSeries(rows, "· поток рендера", timings.RenderThread, scale, ToolTheme.FrameGraphColor);
            if (timings.GPUSampledFrames > 0)
            {
                AddSeries(rows, "· видеокарта — последние доступные замеры", timings.GPU, scale, ToolTheme.FrameGraphColor);
                rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Text,
                    $"GPU-замеров {timings.GPUSampledFrames} из {timings.SampledFrames} уникальных кадров за сеанс окна; " +
                    "пропуски не считаются нулевой стоимостью."));
            }
            else
            {
                rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Text, "· видеокарта — валидные замеры пока не получены"));
            }

            rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Text,
                "Причина длительности кадра по этим агрегатам не установлена. " +
                "Нужна временная шкала одного кадра с потоками и GPU."));
        }

        rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Header, "ИГРОВОЙ ЦИКЛ"));
        if (!playerLoop.Available)
        {
            rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Warning, "Маркер PlayerLoop не найден."));
        }
        else
        {
            double scale = Math.Max(BudgetMilliseconds, playerLoop.Peak);
            AddProbe(rows, playerLoop, scale, ToolTheme.Warning, search);
            rows.Add(new FrameBreakdownRow(
                FrameBreakdownRowKind.Text,
                $"· сумма найденных маркеров систем   {loop.AccountedMilliseconds:F2} мс",
                (float)(loop.AccountedMilliseconds / scale),
                ToolTheme.Warning));
            rows.Add(new FrameBreakdownRow(
                FrameBreakdownRowKind.Text,
                "Разность с PlayerLoop не определяет стоимость редактора: " +
                "покрытие маркерами неполное, окна выборок могут различаться."));

            int shown = Math.Min(FrameTabLoopRows, loop.Sorted.Count);
            for (int i = 0; i < shown; i++)
            {
                AddProbe(rows, loop.Sorted[i], scale, ToolTheme.Warning, search, "· " + loop.Sorted[i].Title, pinnable: true);
            }
        }

        if (search.Pins.Count > 0)
        {
            rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Header, "ЗАКРЕПЛЁННЫЕ"));
            AddProbeList(rows, search.Pins, ToolTheme.Accent, search, pinnable: true);
        }

        rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Header, "МУСОР"));
        AddCounter(rows, memory[0]);
        AddCounter(rows, memory[1]);
        rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Text, $"· отдельный замер окон: {toolBytes / 1024d:F1} КБ (главный поток)"));
        AddAllocationLedgerRows(rows);
    }

    public static void BuildLoopRows(
        List<FrameBreakdownRow> rows,
        PlayerLoopBreakdown loop,
        FrameProbe playerLoop,
        MarkerSearch search)
    {
        rows.Clear();
        rows.Add(new FrameBreakdownRow(
            FrameBreakdownRowKind.Header,
            $"СИСТЕМЫ PLAYERLOOP ({loop.Sorted.Count} из {loop.SystemCount})"));
        if (loop.MissingCount > 0)
        {
            rows.Add(new FrameBreakdownRow(
                FrameBreakdownRowKind.Text,
                $"Без маркера в этом сеансе: {loop.MissingCount} (ещё не срабатывали или не размечены)."));
        }

        double scale = Math.Max(BudgetMilliseconds, playerLoop.Available ? playerLoop.Peak : 0d);
        AddProbeList(rows, loop.Sorted, ToolTheme.Warning, search, pinnable: true, scale);
    }

    public static void BuildLayoutRows(
        List<FrameBreakdownRow> rows,
        UiLayoutTracker layout)
    {
        FrameBreakdownDiagnosticsBuilder.BuildLayoutRows(rows, layout);
    }

    public static void BuildSpikeRows(List<FrameBreakdownRow> rows, SpikeSweep spikes)
    {
        FrameBreakdownDiagnosticsBuilder.BuildSpikeRows(rows, spikes);
    }

    public static void BuildHotRows(List<FrameBreakdownRow> rows, HotMarkerSweep sweep)
    {
        FrameBreakdownDiagnosticsBuilder.BuildHotRows(rows, sweep);
    }

    public static void BuildStageRows(
        List<FrameBreakdownRow> rows,
        List<FrameProbe> cpu,
        List<FrameProbe> gpu,
        List<FrameProbe> gpuRecord,
        List<FrameProbe> discovered,
        ThreadTimings timings,
        MarkerSearch search)
    {
        rows.Clear();
        rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Header, "ПРОЦЕССОР — УЧАСТКИ"));
        AddStageGroup(rows, cpu, ToolTheme.Warning, search);

        rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Header, "ПЕРЕСБОРКИ — ПРИЧИНЫ"));
        var ledger = Kern.Core.Interfaces.Diagnostics.RebuildLedger.Entries;
        if (ledger.Count == 0)
        {
            rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Text, "Пересборок в этом сеансе не было."));
        }

        foreach (var entry in ledger)
        {
            int rate = Kern.Core.Interfaces.Diagnostics.RebuildLedger.RateOf(entry);
            string ago = entry.LastTime < 0f
                ? "не было"
                : $"{Time.unscaledTime - entry.LastTime:F1} с назад";
            rows.Add(new FrameBreakdownRow(
                rate > 0 ? FrameBreakdownRowKind.Warning : FrameBreakdownRowKind.Text,
                $"{entry.Name}   за секунду {rate}, всего {entry.Total:N0}, последняя {ago}"));
        }

        rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Header, "ВИДЕОКАРТА — ВРЕМЯ ИСПОЛНЕНИЯ"));
        rows.Add(new FrameBreakdownRow(
            SystemInfo.supportsGpuRecorder ? FrameBreakdownRowKind.Text : FrameBreakdownRowKind.Warning,
            SystemInfo.supportsGpuRecorder
                ? $"GPU-рекордеры поддерживаются ({SystemInfo.graphicsDeviceType}). Нули значат, что пасс не оборачивает команды в сэмплер."
                : $"GPU-рекордеры не поддерживаются на {SystemInfo.graphicsDeviceType} в этом режиме: время по пассам недоступно, нули ниже — не замер."));
        if (timings.GPUSampledFrames > 0)
        {
            rows.Add(new FrameBreakdownRow(
                FrameBreakdownRowKind.Text,
                $"Последние доступные GPU-замеры (FrameTimingManager): {timings.GPU.Average:F2} мс, пик {timings.GPU.Peak:F2}"));
        }

        AddStageGroup(rows, gpu, ToolTheme.FrameGraphColor, search);

        rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Header, "ВИДЕОКАРТА — ЗАПИСЬ КОМАНД НА ПРОЦЕССОРЕ"));
        AddProbeList(rows, gpuRecord, ToolTheme.FrameGraphColor, search, pinnable: true);

        rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Header, $"НАШИ МАРКЕРЫ ВНЕ ПЕРЕЧНЯ ({discovered.Count})"));
        if (discovered.Count == 0)
        {
            rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Text, "Все сработавшие маркеры Kern.* уже в перечне."));
        }

        AddProbeList(rows, discovered, ToolTheme.Accent, search, pinnable: true);
    }

    public static void BuildToolRows(
        List<FrameBreakdownRow> rows,
        List<ToolWindow> toolWindows,
        IReadOnlyList<FrameProbe> interfaceProbes,
        MarkerSearch search)
    {
        FrameBreakdownSystemBuilder.BuildToolRows(rows, toolWindows, interfaceProbes, search, AddProbeList);
    }

    public static void BuildMemoryRows(
        List<FrameBreakdownRow> rows,
        IReadOnlyList<FrameProbe> memory,
        IReadOnlyList<FrameProbe> render)
    {
        FrameBreakdownSystemBuilder.BuildMemoryRows(rows, memory, render, AddCounter);
    }

    public static void BuildSceneRows(List<FrameBreakdownRow> rows, SceneCensus censusTracker)
    {
        FrameBreakdownSystemBuilder.BuildSceneRows(rows, censusTracker);
    }

    public static void BuildSearchRows(List<FrameBreakdownRow> rows, MarkerSearch search)
    {
        rows.Clear();
        if (search.QueryTooShort)
        {
            rows.Add(new FrameBreakdownRow(
                FrameBreakdownRowKind.Text,
                $"Введите хотя бы 2 символа. Всего маркеров и счётчиков: {MarkerDirectory.All.Count}."));
            return;
        }

        rows.Add(new FrameBreakdownRow(
            FrameBreakdownRowKind.Header,
            search.MatchCount > MarkerSearch.MaxResults
                ? $"НАЙДЕНО {search.MatchCount}, ЗАПИСЫВАЮТСЯ ПЕРВЫЕ {MarkerSearch.MaxResults}"
                : $"НАЙДЕНО {search.MatchCount}"));
        AddProbeList(rows, search.Results, ToolTheme.Accent, search, pinnable: true);
    }

    private static void AddSeries(List<FrameBreakdownRow> rows, string title, RollingSeries series, double scale, Color color)
    {
        rows.Add(new FrameBreakdownRow(
            FrameBreakdownRowKind.Text,
            $"{title}   {series.Average:F2} мс  (пик {series.Peak:F2}, последний {series.Last:F2})",
            (float)(series.Average / scale),
            color));
    }

    private static void AddProbeList(
        List<FrameBreakdownRow> rows,
        IReadOnlyList<FrameProbe> probes,
        Color color,
        MarkerSearch search,
        bool pinnable,
        double scale = 0d)
    {
        if (scale <= 0d)
        {
            scale = BudgetMilliseconds;
            foreach (FrameProbe probe in probes)
            {
                if (probe.Available && probe.IsTime)
                {
                    scale = Math.Max(scale, probe.Peak);
                }
            }
        }

        foreach (FrameProbe probe in probes)
        {
            AddProbe(rows, probe, scale, color, search, pinnable: pinnable);
        }
    }

    private static void AddProbe(
        List<FrameBreakdownRow> rows,
        FrameProbe probe,
        double scale,
        Color color,
        MarkerSearch search,
        string? title = null,
        bool pinnable = false)
    {
        string label = title ?? probe.Title;
        string? pin = pinnable ? probe.MarkerName : null;
        if (probe.GPUUnsupported)
        {
            rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Text, $"{label}  —  у маркера нет GPU-метки", Pin: pin));
            return;
        }

        if (!probe.Available)
        {
            rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Text, $"{label}  —  маркер не сработал в этом сеансе", Pin: pin));
            return;
        }

        if (!probe.IsTime)
        {
            rows.Add(new FrameBreakdownRow(
                FrameBreakdownRowKind.Text,
                $"{label}   {probe.FormatValue(probe.Last)}  (пик {probe.FormatValue(probe.Peak)})",
                Pin: pin));
            return;
        }

        rows.Add(new FrameBreakdownRow(
            FrameBreakdownRowKind.Text,
            $"{label}   {probe.Average:F2} мс  (пик {probe.Peak:F2}, последний {probe.Last:F2})",
            (float)(probe.Average / scale),
            color,
            pin));
    }

    private static void AddCounter(List<FrameBreakdownRow> rows, FrameProbe probe)
    {
        rows.Add(new FrameBreakdownRow(
            FrameBreakdownRowKind.Text,
            probe.Available
                ? $"{probe.Title}: {probe.FormatValue(probe.Last)}   (пик {probe.FormatValue(probe.Peak)})"
                : $"{probe.Title}: счётчика нет в этой сборке"));
    }

    private static void AddStageGroup(List<FrameBreakdownRow> rows, List<FrameProbe> probes, Color color, MarkerSearch search)
    {
        SortByStage(probes);
        double scale = BudgetMilliseconds;
        foreach (FrameProbe probe in probes)
        {
            if (probe.Available)
            {
                scale = Math.Max(scale, probe.Peak);
            }
        }

        foreach (FrameProbe probe in probes)
        {
            AddProbe(rows, probe, scale, color, search, pinnable: true);
        }
    }

    private static void SortByStage(List<FrameProbe> probes)
    {
        _stageOrder.Clear();
        for (int i = 0; i < probes.Count; i++)
        {
            if (probes[i].IsDetail)
            {
                continue;
            }

            int end = i + 1;
            double weight = probes[i].Available ? probes[i].Average : 0d;
            while (end < probes.Count && probes[end].IsDetail)
            {
                if (!probes[i].Available && probes[end].Available)
                {
                    weight = Math.Max(weight, probes[end].Average);
                }

                end++;
            }

            _stageOrder.Add((i, end - i, weight));
        }

        _stageOrder.Sort(static (left, right) => right.Weight.CompareTo(left.Weight));

        _reordered.Clear();
        foreach ((int start, int length, double _) in _stageOrder)
        {
            for (int i = start; i < start + length; i++)
            {
                _reordered.Add(probes[i]);
            }
        }

        if (_reordered.Count != probes.Count)
        {
            foreach (FrameProbe probe in probes)
            {
                if (!_reordered.Contains(probe))
                {
                    _reordered.Add(probe);
                }
            }
        }

        probes.Clear();
        probes.AddRange(_reordered);
    }

    private static void AddAllocationLedgerRows(List<FrameBreakdownRow> rows)
    {
        rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Header, "МУСОР ПО ИГРОВЫМ ПУТЯМ (СРЕДНЕЕ ЗА КАДР)"));
        _ledgerSorted.Clear();
        _ledgerSorted.AddRange(Kern.Core.Interfaces.Diagnostics.AllocationLedger.Entries);
        _ledgerSorted.Sort(static (left, right) => right.AverageBytes.CompareTo(left.AverageBytes));
        if (_ledgerSorted.Count == 0)
        {
            rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Text, "Ни один замеренный путь ещё не выполнялся."));
            return;
        }

        double scale = 1024d;
        foreach (var entry in _ledgerSorted)
        {
            scale = Math.Max(scale, entry.AverageBytes);
        }

        foreach (var entry in _ledgerSorted)
        {
            rows.Add(new FrameBreakdownRow(
                FrameBreakdownRowKind.Text,
                $"{entry.Name}   {entry.AverageBytes / 1024d:F2} КБ  (пик {entry.PeakBytes / 1024d:F1}, " +
                $"последний {entry.LastFrameBytes / 1024d:F2}, вызовов {entry.LastFrameCalls}" +
                (entry.CollectionsInside > 0 ? $", сборок внутри {entry.CollectionsInside}" : string.Empty) + ")",
                (float)(entry.AverageBytes / scale),
                ToolTheme.Warning));
        }

        rows.Add(new FrameBreakdownRow(FrameBreakdownRowKind.Text,
            "Пути могут быть вложены. Общий GC, окна и ledger измерены отдельно; остаток не вычисляется."));
    }
}

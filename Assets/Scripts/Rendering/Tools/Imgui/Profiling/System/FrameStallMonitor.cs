#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using Kern.Core.Interfaces.Diagnostics;
using UnityEngine;
using VContainer.Unity;

namespace Kern.Tools.Imgui.Profiling;

/// <summary>
/// Единственный фоновый отчёт о провисе кадра.
/// </summary>
///
/// Провис — по <see cref="FrameBudget"/>, относительно скользящей медианы
/// (<see cref="FrameBaseline"/>). За интервал печатается самый дорогой кадр:
/// провис обычно повторяется, и сто одинаковых строк ничего не добавляют.
///
/// Что печатается:
/// - участки фиксированного набора (<see cref="FrameProbeCatalog.CreateStallProbes"/>)
///   в этом кадре, от дорогих к дешёвым. Участки вложены, это список
///   подозреваемых, а не бухгалтерия;
/// - события <see cref="FrameEventLog"/> за несколько кадров до провиса: работа,
///   отданная рендеру, всплывает ожиданием через кадр-два. Сюда же пишут
///   подсистемы свой разбор (террейн — свои стадии), вместо отдельных строк
///   лога со своими порогами.
///
/// Раньше отчёт жил в LateUpdate террейна и, сменив подход, сам перебирал все
/// маркеры пачками по 512. Открытие каждой пачки давало длинный кадр — отчёт
/// менял то, что мерил, и его отключили. Здесь набор открывается один раз при
/// старте. Повторный опрос всех маркеров после отчёта происходил уже в
/// следующем кадре и сам мог создавать новый провис. Полный перебор остался
/// по запросу — вкладка «Всплески» окна «Разбор кадра».
public sealed class FrameStallMonitor : IStartable, ITickable, IDisposable
{
    public const string ReportCategory = "Stalls";
    public const string ReportName = "frame_stalls";
    public const string ReportKind = "Провисы кадра";

    private const float IntervalSeconds = 2f;
    private const int ShownProbes = 14;
    private const double ShownMinimumMilliseconds = 0.5;
    private const int EventLookbackFrames = 3;

    private readonly FrameBaseline _baseline = new();
    private readonly List<FrameProbe> _probes = FrameProbeCatalog.CreateStallProbes();
    private readonly FrameProbe _allocated = new("Мусор за кадр", "GC Allocated In Frame");
    private readonly double[] _worstValues;
    private readonly int[] _order;
    private readonly Comparer<int> _byWorstValue;
    private readonly StringBuilder _text = new(2048);
    private double _worstFrameMs;
    private double _worstBaselineMs;
    private double _worstAllocatedBytes;
    private int _worstFrame;
    private int _stallsInInterval;
    private float _nextReportTime;

    public FrameStallMonitor()
    {
        _worstValues = new double[_probes.Count];
        _order = new int[_probes.Count];
        _byWorstValue = Comparer<int>.Create((left, right) => _worstValues[right].CompareTo(_worstValues[left]));
    }

    public void Start()
    {
        foreach (FrameProbe probe in _probes)
        {
            probe.EnsureStarted();
        }

        _allocated.EnsureStarted();
    }

    /// <summary>
    /// Раз в кадр. Читает прошлый, уже закончившийся кадр: его длительность и
    /// последние значения маркеров.
    /// </summary>
    public void Tick()
    {
        double frameMs = Time.unscaledDeltaTime * 1000.0;
        double baselineMs = _baseline.Milliseconds;
        bool stall = baselineMs > 0.0 && FrameBudget.IsStall(frameMs, baselineMs);
        _baseline.Push(frameMs);
        if (stall)
        {
            _stallsInInterval++;
            if (frameMs > _worstFrameMs)
            {
                Capture(frameMs, baselineMs);
            }
        }

        float now = Time.unscaledTime;
        if (_worstFrameMs <= 0.0 || now < _nextReportTime)
        {
            return;
        }

        _nextReportTime = now + IntervalSeconds;
        Report();
        _worstFrameMs = 0.0;
        _stallsInInterval = 0;
    }

    private void Capture(double frameMs, double baselineMs)
    {
        _worstFrameMs = frameMs;
        _worstBaselineMs = baselineMs;
        _worstFrame = Time.frameCount - 1;
        _worstAllocatedBytes = _allocated.Available ? _allocated.ReadLast() : -1.0;
        for (int index = 0; index < _probes.Count; index++)
        {
            FrameProbe probe = _probes[index];
            _worstValues[index] = probe.Available && probe.IsTime ? probe.ReadLast() : -1.0;
        }
    }

    private void Report()
    {
        _text.Clear();
        _text.Append("[FrameStall] ").Append(_worstFrameMs.ToString("F1")).Append(" мс кадр (обычный ")
            .Append(_worstBaselineMs.ToString("F1")).Append(", провисов за ").Append(IntervalSeconds.ToString("F0"))
            .Append(" с: ").Append(_stallsInInterval).Append(')');
        if (_worstAllocatedBytes >= 0.0)
        {
            _text.Append(" · мусор ").Append((_worstAllocatedBytes / 1024.0).ToString("F0")).Append(" КБ");
        }

        if (FrameEventLog.AppendRange(_text, _worstFrame - EventLookbackFrames, _worstFrame) == 0)
        {
            _text.Append(" · тяжёлых событий за ").Append(EventLookbackFrames)
                .Append(" кадра до провиса не было");
        }

        int shown = RankProbes();
        if (shown == 0)
        {
            _text.Append("\nучастки: ни один не дороже ").Append(ShownMinimumMilliseconds.ToString("F1"))
                .Append(" мс или маркеры недоступны в этой сборке");
        }
        else
        {
            _text.Append("\nучастки в этом кадре (вложены, не складываются):");
        }

        for (int rank = 0; rank < shown; rank++)
        {
            FrameProbe probe = _probes[_order[rank]];
            _text.Append("\n  ").Append(probe.Title.TrimStart('·', ' '))
                .Append("  ").Append(_worstValues[_order[rank]].ToString("F1")).Append(" мс")
                .Append("  [").Append(probe.MarkerName).Append(']');
        }

        string report = _text.ToString();
        Debug.LogWarning(report);
        DiagnosticReport.Append(ReportCategory, ReportName, ReportKind, report);
    }

    private int RankProbes()
    {
        int count = 0;
        for (int index = 0; index < _probes.Count; index++)
        {
            if (_worstValues[index] >= ShownMinimumMilliseconds)
            {
                _order[count++] = index;
            }
        }

        Array.Sort(_order, 0, count, _byWorstValue);
        return Math.Min(count, ShownProbes);
    }

    public void Dispose()
    {
        foreach (FrameProbe probe in _probes)
        {
            probe.Dispose();
        }

        _allocated.Dispose();
    }
}

#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using Kern.Core.Interfaces.Diagnostics;
using UnityEngine;

namespace Kern.Tools.Imgui.Windows;

/// <summary>Owns the sequential settle/measure/report lifecycle for render bypass A/B sampling.</summary>
internal sealed class RenderBypassSweep
{
    private const int SweepSettleFrames = 45;
    private const int SweepMeasureFrames = 150;

    private readonly IReadOnlyList<(string Label, Action<bool> Apply)> _steps;
    private readonly List<double> _frameMs = new(SweepMeasureFrames);
    private readonly List<double> _gpuMs = new(SweepMeasureFrames);
    private readonly List<(string Label, double FrameMs, double GpuMs, int GpuSamples)> _results = [];
    private readonly FrameTiming[] _timings = new FrameTiming[1];
    private int _step = -1;
    private int _frame;

    public RenderBypassSweep(IReadOnlyList<(string Label, Action<bool> Apply)> steps)
    {
        _steps = steps ?? throw new ArgumentNullException(nameof(steps));
    }

    public bool IsRunning => _step >= 0;

    public string Summary { get; private set; } = string.Empty;

    public string ProgressLabel =>
        $"Замер: {GetStepLabel(_step)} ({_step + 1} из {_steps.Count + 2}). Не трогайте игру.";

    public void Start()
    {
        foreach ((_, Action<bool> apply) in _steps)
        {
            apply(false);
        }

        _results.Clear();
        _frameMs.Clear();
        _gpuMs.Clear();
        _frame = 0;
        _step = 0;
        Summary = string.Empty;
    }

    public void Cancel() => _step = -1;

    public void Tick()
    {
        if (_step < 0)
        {
            return;
        }

        _frame++;
        if (_frame <= SweepSettleFrames)
        {
            return;
        }

        _frameMs.Add(Time.unscaledDeltaTime * 1000.0);
        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, _timings) > 0 && _timings[0].gpuFrameTime > 0.0)
        {
            _gpuMs.Add(_timings[0].gpuFrameTime);
        }

        if (_frame < SweepSettleFrames + SweepMeasureFrames)
        {
            return;
        }

        _results.Add((GetStepLabel(_step), Median(_frameMs), Median(_gpuMs), _gpuMs.Count));
        SetStep(_step, false);
        _step++;
        if (_step > _steps.Count + 1)
        {
            Finish();
            return;
        }

        SetStep(_step, true);
        _frame = 0;
        _frameMs.Clear();
        _gpuMs.Clear();
    }

    private string GetStepLabel(int step) =>
        step >= 1 && step <= _steps.Count ? _steps[step - 1].Label :
        step == 0 ? "без обходов" : "без обходов (повтор)";

    private void SetStep(int step, bool enabled)
    {
        if (step >= 1 && step <= _steps.Count)
        {
            _steps[step - 1].Apply(enabled);
        }
    }

    private void Finish()
    {
        _step = -1;
        double baseFrame = (_results[0].FrameMs + _results[^1].FrameMs) * 0.5;
        double baseGpu = (_results[0].GpuMs + _results[^1].GpuMs) * 0.5;
        var text = new StringBuilder(1024);
        text.Append("Кадр без обходов: ").Append(baseFrame.ToString("F2")).Append(" мс, GPU ")
            .Append(baseGpu.ToString("F2")).Append(" мс (первый ").Append(_results[0].FrameMs.ToString("F2"))
            .Append(", повтор ").Append(_results[^1].FrameMs.ToString("F2")).AppendLine(")");
        text.AppendLine("Что снимает обход (медиана кадра; GPU — по доступным замерам FrameTimingManager):");
        for (int index = 1; index < _results.Count - 1; index++)
        {
            (string label, double frameMs, double gpuMs, int gpuSamples) = _results[index];
            text.Append("  ").Append(label).Append(": кадр ").Append(frameMs.ToString("F2"))
                .Append(" мс (").Append((baseFrame - frameMs).ToString("+0.00;-0.00")).Append("), GPU ")
                .Append(gpuMs.ToString("F2")).Append(" мс (").Append((baseGpu - gpuMs).ToString("+0.00;-0.00"))
                .Append(", замеров ").Append(gpuSamples).AppendLine(")");
        }

        Summary = text.ToString();
        DiagnosticReport.Write("Performance", "bypass_cost", "Цена этапов кадра", Summary);
        Debug.Log("[BypassCost]\n" + Summary);
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return 0.0;
        }

        values.Sort();
        return values[values.Count / 2];
    }
}

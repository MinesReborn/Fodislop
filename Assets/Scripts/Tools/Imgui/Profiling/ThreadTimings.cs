#nullable enable

using System;
using UnityEngine;

namespace Kern.Tools.Imgui.Profiling;

public sealed class RollingSeries
{
    private readonly double[] _values;
    private int _next;
    private int _count;

    public RollingSeries(int capacity)
    {
        _values = new double[capacity];
    }

    public double Last { get; private set; }

    public double Average { get; private set; }

    public double Peak { get; private set; }

    public void Push(double value)
    {
        _values[_next] = value;
        _next = (_next + 1) % _values.Length;
        _count = Math.Min(_count + 1, _values.Length);
        Last = value;

        double total = 0d;
        double peak = 0d;
        for (int i = 0; i < _count; i++)
        {
            total += _values[i];
            peak = Math.Max(peak, _values[i]);
        }

        Average = total / _count;
        Peak = peak;
    }

    public void Clear()
    {
        Array.Clear(_values, 0, _values.Length);
        _next = 0;
        _count = 0;
        Last = Average = Peak = 0d;
    }
}

// Агрегаты FrameTimingManager не устанавливают причинную цепочку кадра.
// cpuMainThreadFrameTime уже содержит активную работу: повторно вычитать
// cpuMainThreadPresentWaitTime из него нельзя.
public sealed class ThreadTimings
{
    private const int Window = 60;

    private readonly FrameTiming[] _buffer = new FrameTiming[1];
    private ulong _lastTimestamp;
    private bool _hasTimestamp;
    private bool _gpuRecordedForLastFrame;

    public int SampledFrames { get; private set; }

    public int GPUSampledFrames { get; private set; }

    public RollingSeries Frame { get; } = new(Window);

    public RollingSeries MainThread { get; } = new(Window);

    public RollingSeries PresentWait { get; } = new(Window);

    public RollingSeries RenderThread { get; } = new(Window);

    public RollingSeries GPU { get; } = new(Window);

    public bool Available { get; private set; }

    // Вызывать каждый кадр: движок отдаёт только последние снятые кадры.
    public void Tick()
    {
        FrameTimingManager.CaptureFrameTimings();
        uint captured = FrameTimingManager.GetLatestTimings(1, _buffer);
        if (captured == 0)
        {
            Available = false;
            return;
        }

        FrameTiming timing = _buffer[0];
        Available = IsPositiveFinite(timing.cpuFrameTime);
        if (!Available)
        {
            return;
        }

        // GetLatestTimings может вернуть тот же завершённый кадр повторно.
        // Повтор не должен увеличивать его вес в средних и число замеров.
        if (!_hasTimestamp || timing.frameStartTimestamp != _lastTimestamp)
        {
            _lastTimestamp = timing.frameStartTimestamp;
            _hasTimestamp = true;
            _gpuRecordedForLastFrame = false;
            SampledFrames++;
            Frame.Push(timing.cpuFrameTime);
            MainThread.Push(timing.cpuMainThreadFrameTime);
            PresentWait.Push(timing.cpuMainThreadPresentWaitTime);
            RenderThread.Push(timing.cpuRenderThreadFrameTime);
        }

        // Для уже учтённого CPU-кадра GPU-результат может появиться позже.
        if (!_gpuRecordedForLastFrame && IsPositiveFinite(timing.gpuFrameTime))
        {
            GPU.Push(timing.gpuFrameTime);
            GPUSampledFrames++;
            _gpuRecordedForLastFrame = true;
        }
    }

    private static bool IsPositiveFinite(double value) =>
        value > 0d && !double.IsNaN(value) && !double.IsInfinity(value);

    public void Clear()
    {
        Frame.Clear();
        MainThread.Clear();
        PresentWait.Clear();
        RenderThread.Clear();
        GPU.Clear();
        Available = false;
        _lastTimestamp = 0;
        _hasTimestamp = false;
        _gpuRecordedForLastFrame = false;
        SampledFrames = 0;
        GPUSampledFrames = 0;
    }
}

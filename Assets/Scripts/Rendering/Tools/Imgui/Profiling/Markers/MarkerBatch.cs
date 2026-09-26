#nullable enable

using System;
using System.Collections.Generic;
using Unity.Profiling;

namespace Kern.Tools.Imgui.Profiling;

// Общий движок переборов всех маркеров: очередь, пачки рекордеров, отсев.
//
// Рекордер не видит иерархию, а держать открытыми тысячи рекордеров сразу
// дорого. Поэтому перебор идёт пачками: пачка открывается, копит кадры,
// анализатор читает её, пачка закрывается. Раньше этот цикл жил в трёх
// копиях — «Горячее», «Всплески» и фоновый отчёт о провисе, — и только одна
// из них знала, что маркеры с замером GPU трогать нельзя. Анализаторы теперь
// разные, а очередь и фильтр — одни.
internal sealed class MarkerBatch : IDisposable
{
    // Маркеры джобов и загрузки пишут в «наносекунды» не время, а
    // идентификаторы: больше секунды на кадр — не замер.
    private const long MaximumPlausibleNanoseconds = 1_000_000_000L;

    private readonly int _batchSize;
    private readonly int _framesPerBatch;
    private readonly List<MarkerInfo> _queue = [];
    private readonly List<(MarkerInfo Marker, ProfilerRecorder Recorder)> _open = [];
    private int _next;
    private int _frames;

    public MarkerBatch(int batchSize, int framesPerBatch)
    {
        _batchSize = batchSize;
        _framesPerBatch = framesPerBatch;
    }

    public int Total => _queue.Count;

    public int Processed => Math.Min(_next, _queue.Count);

    public bool HasMore => _next < _queue.Count;

    public int FramesPerBatch => _framesPerBatch;

    public IReadOnlyList<(MarkerInfo Marker, ProfilerRecorder Recorder)> Open => _open;

    public static bool IsPlausibleTime(long nanoseconds) =>
        nanoseconds >= 0 && nanoseconds < MaximumPlausibleNanoseconds;

    /// <summary>Заново собрать очередь из всех маркеров, которые можно перебирать.</summary>
    public void Reset()
    {
        Close();
        _queue.Clear();
        MarkerDirectory.Refresh(force: true);
        foreach (MarkerInfo info in MarkerDirectory.All)
        {
            if (info.IsSweepable)
            {
                _queue.Add(info);
            }
        }

        _next = 0;
    }

    public void OpenNext()
    {
        _frames = 0;
        int end = Math.Min(_queue.Count, _next + _batchSize);
        for (; _next < end; _next++)
        {
            MarkerInfo info = _queue[_next];
            var recorder = new ProfilerRecorder(
                info.Handle,
                _framesPerBatch,
                ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame);
            if (!recorder.Valid)
            {
                recorder.Dispose();
                continue;
            }

            recorder.Start();
            _open.Add((info, recorder));
        }
    }

    /// <summary>Отсчитать кадр; true — пачка набрала свои кадры и готова к разбору.</summary>
    public bool Advance() => ++_frames >= _framesPerBatch;

    public void Close()
    {
        foreach ((_, ProfilerRecorder recorder) in _open)
        {
            recorder.Dispose();
        }

        _open.Clear();
    }

    public void Dispose() => Close();
}

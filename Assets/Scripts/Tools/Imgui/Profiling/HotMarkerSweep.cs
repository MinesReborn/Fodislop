#nullable enable

using System;
using System.Collections.Generic;
using Unity.Profiling;

namespace Kern.Tools.Imgui.Profiling;

// Прочёсывание всех временных маркеров профайлера.
//
// Рекордер не видит иерархию, а держать открытыми тысячи рекордеров сразу
// дорого. Поэтому маркеры записываются пачками: пачка открывается, копит
// FramesPerBatch кадров, её средние запоминаются, пачка закрывается. После
// прохода остаются самые горячие маркеры — в них видны пассы RenderGraph,
// участки UI и всё, что не размечено нашим перечнем.
//
// Родитель и ребёнок суммируются независимо: в списке оба, и родитель
// всегда не меньше своих детей.
public sealed class HotMarkerSweep : IDisposable
{
    public const int KeepTop = 60;
    private const int BatchSize = 256;
    private const int FramesPerBatch = 12;

    public readonly record struct Hit(MarkerInfo Marker, double AverageMilliseconds, double PeakMilliseconds, int Frames);

    private readonly List<MarkerInfo> _queue = [];
    private readonly List<(MarkerInfo Marker, ProfilerRecorder Recorder)> _batch = [];
    private readonly List<Hit> _hits = [];
    private readonly List<Hit> _pendingHits = [];
    private int _next;
    private int _batchFrames;

    public bool Running { get; private set; }

    public bool HasResults => _hits.Count > 0;

    public int Total => _queue.Count;

    public int Processed => Math.Min(_next, _queue.Count);

    public IReadOnlyList<Hit> Hits => _hits;

    public void Begin()
    {
        CloseBatch(keep: false);
        _queue.Clear();
        _pendingHits.Clear();
        MarkerDirectory.Refresh(force: true);
        foreach (MarkerInfo info in MarkerDirectory.All)
        {
            if (info.Unit == ProfilerMarkerDataUnit.TimeNanoseconds)
            {
                _queue.Add(info);
            }
        }

        _next = 0;
        Running = _queue.Count > 0;
        OpenBatch();
    }

    // Каждый кадр, пока идёт проход.
    public void Tick()
    {
        if (!Running)
        {
            return;
        }

        _batchFrames++;
        if (_batchFrames < FramesPerBatch)
        {
            return;
        }

        CloseBatch(keep: true);
        if (_next >= _queue.Count)
        {
            Finish();
            return;
        }

        OpenBatch();
    }

    public void Cancel()
    {
        CloseBatch(keep: false);
        _pendingHits.Clear();
        Running = false;
    }

    private void OpenBatch()
    {
        _batchFrames = 0;
        int end = Math.Min(_queue.Count, _next + BatchSize);
        for (; _next < end; _next++)
        {
            MarkerInfo info = _queue[_next];
            var recorder = new ProfilerRecorder(
                info.Handle,
                FramesPerBatch,
                ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame);
            if (!recorder.Valid)
            {
                recorder.Dispose();
                continue;
            }

            recorder.Start();
            _batch.Add((info, recorder));
        }
    }

    private void CloseBatch(bool keep)
    {
        foreach ((MarkerInfo marker, ProfilerRecorder recorder) in _batch)
        {
            if (keep && recorder.Valid)
            {
                int count = recorder.Count;
                long total = 0;
                long peak = 0;
                for (int i = 0; i < count; i++)
                {
                    long value = recorder.GetSample(i).Value;
                    total += value;
                    peak = Math.Max(peak, value);
                }

                // Маркеры джобов и загрузки пишут в «наносекунды» не время,
                // а идентификаторы: больше секунды на кадр — не замер.
                if (count > 0 && total > 0 && peak < 1_000_000_000L)
                {
                    _pendingHits.Add(new Hit(marker, total * 1e-6 / count, peak * 1e-6, count));
                }
            }

            recorder.Dispose();
        }

        _batch.Clear();
    }

    private void Finish()
    {
        Running = false;
        _pendingHits.Sort(static (left, right) => right.AverageMilliseconds.CompareTo(left.AverageMilliseconds));
        _hits.Clear();
        for (int i = 0; i < _pendingHits.Count && i < KeepTop; i++)
        {
            _hits.Add(_pendingHits[i]);
        }

        _pendingHits.Clear();
    }

    public void Dispose()
    {
        Cancel();
    }
}

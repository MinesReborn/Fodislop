#nullable enable

using System;
using System.Collections.Generic;
using Unity.Profiling;

namespace Kern.Tools.Imgui.Profiling;

// Прочёсывание всех временных маркеров профайлера: средняя стоимость.
//
// Пачки ведёт MarkerBatch; здесь только разбор: средние и пики пачки. После
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

    private readonly MarkerBatch _batch = new(BatchSize, FramesPerBatch);
    private readonly List<Hit> _hits = [];
    private readonly List<Hit> _pendingHits = [];

    public bool Running { get; private set; }

    public bool HasResults => _hits.Count > 0;

    public int Total => _batch.Total;

    public int Processed => _batch.Processed;

    public IReadOnlyList<Hit> Hits => _hits;

    public void Begin()
    {
        _pendingHits.Clear();
        _batch.Reset();
        Running = _batch.Total > 0;
        if (Running)
        {
            _batch.OpenNext();
        }
    }

    // Каждый кадр, пока идёт проход.
    public void Tick()
    {
        if (!Running || !_batch.Advance())
        {
            return;
        }

        Collect();
        _batch.Close();
        if (!_batch.HasMore)
        {
            Finish();
            return;
        }

        _batch.OpenNext();
    }

    public void Cancel()
    {
        _batch.Close();
        _pendingHits.Clear();
        Running = false;
    }

    private void Collect()
    {
        foreach ((MarkerInfo marker, ProfilerRecorder recorder) in _batch.Open)
        {
            if (!recorder.Valid)
            {
                continue;
            }

            int count = recorder.Count;
            long total = 0;
            long peak = 0;
            for (int i = 0; i < count; i++)
            {
                long value = recorder.GetSample(i).Value;
                total += value;
                peak = Math.Max(peak, value);
            }

            if (count > 0 && total > 0 && MarkerBatch.IsPlausibleTime(peak))
            {
                _pendingHits.Add(new Hit(marker, total * 1e-6 / count, peak * 1e-6, count));
            }
        }
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
        _batch.Dispose();
    }
}

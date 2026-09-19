#nullable enable

using System;
using System.Collections.Generic;
using Unity.Profiling;

namespace Kern.Tools.Imgui.Profiling;

// Ловец периодических всплесков.
//
// Средние из HotMarkerSweep размазывают кадр, который раз в секунду стоит
// втрое дороже: 12 кадров пачки его то ловят, то нет, а в среднем он теряется.
// Здесь пачка держит рекордеры дольше секунды и хранит значения покадрово
// вместе с временем кадра главного потока. Кадры-выбросы находятся по
// медиане кадра, и для каждого маркера считается, на сколько он в этих кадрах
// дороже своей обычной медианы. Наверху оказывается то, что растёт именно в
// просевшем кадре, а не то, что дорого всегда.
public sealed class SpikeSweep : IDisposable
{
    public const int KeepTop = 60;
    private const int BatchSize = 256;
    private const int FramesPerBatch = 120;

    // Открытие пачки само создаёт сотни рекордеров: первые кадры не в счёт.
    private const int SkipFirstFrames = 4;
    private const double SpikeRatio = 1.5;
    private const double SpikeMinimumExtraMilliseconds = 4.0;
    private const double MinimumExcessMilliseconds = 0.2;

    public readonly record struct Hit(MarkerInfo Marker, double ExcessMilliseconds, double BaselineMilliseconds, int SpikeFrames);

    private readonly List<MarkerInfo> _queue = [];
    private readonly List<(MarkerInfo Marker, ProfilerRecorder Recorder)> _batch = [];
    private readonly List<Hit> _hits = [];
    private readonly List<Hit> _pendingHits = [];
    private readonly List<int> _spikeIndices = [];
    private readonly double[] _frame = new double[FramesPerBatch];
    private readonly double[] _values = new double[FramesPerBatch];
    private readonly double[] _sorted = new double[FramesPerBatch];
    private ProfilerRecorder _frameRecorder;
    private int _next;
    private int _batchFrames;

    public bool Running { get; private set; }

    public bool HasResults { get; private set; }

    public int Total => _queue.Count;

    public int Processed => Math.Min(_next, _queue.Count);

    public IReadOnlyList<Hit> Hits => _hits;

    public string FrameSource { get; private set; } = "";

    public int FramesSeen { get; private set; }

    public int SpikesSeen { get; private set; }

    public double MedianFrameMilliseconds { get; private set; }

    public double SpikeFrameMilliseconds { get; private set; }

    private double _medianSum;
    private double _spikeSum;
    private int _batchesWithFrames;

    public void Begin()
    {
        Cancel();
        _queue.Clear();
        MarkerDirectory.Refresh(force: true);
        foreach (MarkerInfo info in MarkerDirectory.All)
        {
            if (info.Unit == ProfilerMarkerDataUnit.TimeNanoseconds)
            {
                _queue.Add(info);
            }
        }

        _next = 0;
        FramesSeen = 0;
        SpikesSeen = 0;
        _medianSum = 0;
        _spikeSum = 0;
        _batchesWithFrames = 0;
        Running = _queue.Count > 0;
        if (Running)
        {
            OpenBatch();
        }
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

        // «Main Thread» охватывает весь кадр главного потока, в редакторе
        // вместе с его окнами; PlayerLoop — запасной вариант без них.
        _frameRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", FramesPerBatch);
        FrameSource = "Main Thread";
        if (!_frameRecorder.Valid)
        {
            _frameRecorder.Dispose();
            _frameRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "PlayerLoop", FramesPerBatch);
            FrameSource = "PlayerLoop";
        }

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
        if (keep && _frameRecorder.Valid)
        {
            Analyze();
        }

        _frameRecorder.Dispose();
        _frameRecorder = default;
        foreach ((_, ProfilerRecorder recorder) in _batch)
        {
            recorder.Dispose();
        }

        _batch.Clear();
    }

    private void Analyze()
    {
        int frames = _frameRecorder.Count;
        if (frames <= SkipFirstFrames + 8)
        {
            return;
        }

        for (int i = 0; i < frames; i++)
        {
            _frame[i] = _frameRecorder.GetSample(i).Value * 1e-6;
        }

        double median = Median(_frame, SkipFirstFrames, frames);
        _spikeIndices.Clear();
        double spikeSum = 0;
        for (int i = SkipFirstFrames; i < frames; i++)
        {
            if (_frame[i] >= median * SpikeRatio && _frame[i] - median >= SpikeMinimumExtraMilliseconds)
            {
                _spikeIndices.Add(i);
                spikeSum += _frame[i];
            }
        }

        FramesSeen += frames - SkipFirstFrames;
        SpikesSeen += _spikeIndices.Count;
        _batchesWithFrames++;
        _medianSum += median;
        MedianFrameMilliseconds = _medianSum / _batchesWithFrames;
        if (_spikeIndices.Count > 0)
        {
            _spikeSum += spikeSum;
            SpikeFrameMilliseconds = _spikeSum / SpikesSeen;
        }

        if (_spikeIndices.Count == 0)
        {
            return;
        }

        foreach ((MarkerInfo marker, ProfilerRecorder recorder) in _batch)
        {
            if (!recorder.Valid)
            {
                continue;
            }

            // Рекордеры открыты в один кадр с рекордером кадра, но число
            // сэмплов может разойтись на кадр: выравнивание идёт с конца.
            int count = recorder.Count;
            int offset = count - frames;
            bool invalid = false;
            for (int i = 0; i < frames; i++)
            {
                int source = i + offset;
                long value = source >= 0 && source < count ? recorder.GetSample(source).Value : 0;

                // Маркеры джобов и загрузки пишут в «наносекунды» не время.
                if (value >= 1_000_000_000L)
                {
                    invalid = true;
                    break;
                }

                _values[i] = value * 1e-6;
            }

            if (invalid)
            {
                continue;
            }

            double baseline = Median(_values, SkipFirstFrames, frames);
            double excess = 0;
            foreach (int index in _spikeIndices)
            {
                excess += _values[index] - baseline;
            }

            excess /= _spikeIndices.Count;
            if (excess >= MinimumExcessMilliseconds)
            {
                _pendingHits.Add(new Hit(marker, excess, baseline, _spikeIndices.Count));
            }
        }
    }

    private double Median(double[] source, int start, int end)
    {
        int length = end - start;
        Array.Copy(source, start, _sorted, 0, length);
        Array.Sort(_sorted, 0, length);
        return _sorted[length / 2];
    }

    private void Finish()
    {
        Running = false;
        HasResults = true;
        _pendingHits.Sort(static (left, right) => right.ExcessMilliseconds.CompareTo(left.ExcessMilliseconds));
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

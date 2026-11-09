#nullable enable

using System;
using Unity.Profiling;

namespace Kern.Rendering.Diagnostics.Profiling;

// Один маркер или счётчик профайлера: время участка, байты или количество.
// Имя разрешается через MarkerDirectory, без угадывания категории; можно
// дать несколько имён — берётся первое, которое есть в этой сборке.
public sealed class FrameProbe : IDisposable
{
    public const int Capacity = 60;

    private readonly string[] _names;
    private readonly MarkerInfo? _marker;
    private readonly bool _gpu;
    private ProfilerRecorder _recorder;
    private UnityEngine.Profiling.Recorder? _gpuRecorder;
    private readonly RollingSeries _gpuSeries = new(Capacity);
    private int _triedDirectoryVersion = -1;
    private int _gpuSampleAgeFrames = int.MaxValue;

    public FrameProbe(string title, string markerName, bool isDetail = false, bool gpu = false, params string[] aliases)
    {
        Title = title;
        IsDetail = isDetail;
        _gpu = gpu;
        if (aliases.Length == 0)
        {
            _names = new[] { markerName };
        }
        else
        {
            _names = new string[aliases.Length + 1];
            _names[0] = markerName;
            Array.Copy(aliases, 0, _names, 1, aliases.Length);
        }
    }

    public FrameProbe(MarkerInfo marker, string? title = null, bool isDetail = true)
    {
        _marker = marker;
        _names = new[] { marker.Name };
        Title = title ?? marker.Name;
        IsDetail = isDetail;
    }

    public string Title { get; }

    public bool IsDetail { get; }

    public string MarkerName => ResolvedName ?? _names[0];

    public string? ResolvedName { get; private set; }

    public bool Available => _recorder.Valid || _gpuRecorder != null;

    public bool HasSamples { get; private set; }

    public int SampleCount => _gpuRecorder != null ? _gpuSeries.Count : _recorder.Valid ? _recorder.Count : 0;

    public ProfilerMarkerDataUnit Unit { get; private set; } = ProfilerMarkerDataUnit.TimeNanoseconds;

    public bool IsTime => Unit == ProfilerMarkerDataUnit.TimeNanoseconds;

    public bool IsGPU => _gpu;

    public int SampleAgeFrames => _gpuSampleAgeFrames;

    // Для времени — миллисекунды, для остального — значение как есть.
    public double Last { get; private set; }

    public double Average { get; private set; }

    public double Peak { get; private set; }

    // Маркер есть, но у него нет GPU-метки: время видеокарты по нему не снять.
    public bool GPUUnsupported { get; private set; }

    public void EnsureStarted()
    {
        if (Available)
        {
            return;
        }

        if (_marker.HasValue)
        {
            Open(_marker.Value);
            return;
        }

        MarkerDirectory.Refresh();
        if (_triedDirectoryVersion == MarkerDirectory.Version)
        {
            return;
        }

        _triedDirectoryVersion = MarkerDirectory.Version;
        foreach (string name in _names)
        {
            if (MarkerDirectory.TryGet(name, out MarkerInfo info))
            {
                Open(info);
                return;
            }
        }
    }

    private void Open(MarkerInfo info)
    {
        ProfilerRecorderOptions options =
            ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame;
        if (_gpu)
        {
            // Сэмплеры RenderGraph не несут флага SampleGPU, и GPU-рекордер
            // по ним пуст. Recorder по имени сэмплера отдаёт время
            // видеокарты и на Metal.
            UnityEngine.Profiling.Recorder gpuRecorderByName = UnityEngine.Profiling.Recorder.Get(info.Name);
            GPUUnsupported = gpuRecorderByName == null || !gpuRecorderByName.isValid;
            ResolvedName = info.Name;
            if (!GPUUnsupported)
            {
                gpuRecorderByName!.enabled = true;
                _gpuRecorder = gpuRecorderByName;
            }

            return;
        }

        var recorder = new ProfilerRecorder(info.Handle, Capacity, options);
        if (!recorder.Valid)
        {
            recorder.Dispose();
            return;
        }

        recorder.Start();
        _recorder = recorder;
        ResolvedName = info.Name;
        Unit = info.Unit;
    }

    public void Stop()
    {
        if (_recorder.Valid)
        {
            _recorder.Dispose();
        }

        _recorder = default;
        if (_gpuRecorder != null)
        {
            _gpuRecorder.enabled = false;
            _gpuRecorder = null;
        }

        _gpuSeries.Clear();
        _gpuSampleAgeFrames = int.MaxValue;
        _triedDirectoryVersion = -1;
        Last = 0d;
        Average = 0d;
        Peak = 0d;
        HasSamples = false;
    }

    // Старый Recorder хранит только последний кадр, поэтому GPU-пробу надо
    // опрашивать каждый кадр, а не с частотой обновления строк.
    public void TickGPU()
    {
        if (_gpuRecorder == null)
        {
            return;
        }

        double gpuMilliseconds = _gpuRecorder.gpuElapsedNanoseconds * 1e-6;
        if (gpuMilliseconds > 0d && !double.IsNaN(gpuMilliseconds) && !double.IsInfinity(gpuMilliseconds))
        {
            _gpuSeries.Push(gpuMilliseconds);
            _gpuSampleAgeFrames = 0;
        }
        else if (_gpuSampleAgeFrames < int.MaxValue)
        {
            _gpuSampleAgeFrames++;
        }
    }

    public void Sample()
    {
        if (!Available)
        {
            EnsureStarted();
            if (!Available)
            {
                Last = Average = Peak = 0d;
                HasSamples = false;
                return;
            }
        }

        if (_gpuRecorder != null)
        {
            Last = _gpuSeries.Last;
            Average = _gpuSeries.Average;
            Peak = _gpuSeries.Peak;
            HasSamples = _gpuSeries.Count > 0;
            return;
        }

        int count = _recorder.Count;
        if (count <= 0)
        {
            Last = Average = Peak = 0d;
            HasSamples = false;
            return;
        }

        double scale = IsTime ? 1e-6 : 1d;
        Last = _recorder.LastValue * scale;
        double total = 0d;
        long peak = 0;
        for (int i = 0; i < count; i++)
        {
            long value = _recorder.GetSample(i).Value;
            total += value;
            peak = Math.Max(peak, value);
        }

        Average = total / count * scale;
        Peak = peak * scale;
        HasSamples = true;

    }

    // Значение прошлого кадра без разбора истории: фоновый монитор читает его
    // только в провисшем кадре, и обходить 60 сэмплов там незачем.
    public bool TryReadLast(out double value)
    {
        if (_recorder.Valid && _recorder.Count > 0)
        {
            value = _recorder.LastValue * (IsTime ? 1e-6 : 1d);
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        value = 0d;
        return false;
    }

    public string FormatValue(double value) => Format(value, Unit);

    public static string Format(double value, ProfilerMarkerDataUnit unit) => unit switch
    {
        ProfilerMarkerDataUnit.TimeNanoseconds => $"{value:F2} мс",
        ProfilerMarkerDataUnit.Bytes when Math.Abs(value) >= 1024d * 1024d => $"{value / (1024d * 1024d):F1} МБ",
        ProfilerMarkerDataUnit.Bytes => $"{value / 1024d:F1} КБ",
        ProfilerMarkerDataUnit.Percent => $"{value:F1}%",
        ProfilerMarkerDataUnit.FrequencyHz => $"{value:F1} Гц",
        _ => value.ToString("N0"),
    };

    public void Dispose()
    {
        Stop();
    }
}

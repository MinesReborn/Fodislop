#nullable enable

using System;
using Unity.Profiling;

namespace Kern.Tools.Imgui.Profiling;

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

    public FrameProbe(string title, string markerName, bool isDetail = false, bool gpu = false, params string[] aliases)
    {
        Title = title;
        IsDetail = isDetail;
        _gpu = gpu;
        _names = aliases.Length == 0 ? [markerName] : [markerName, ..aliases];
    }

    public FrameProbe(MarkerInfo marker, string? title = null, bool isDetail = true)
    {
        _marker = marker;
        _names = [marker.Name];
        Title = title ?? marker.Name;
        IsDetail = isDetail;
    }

    public string Title { get; }

    public bool IsDetail { get; }

    public string MarkerName => ResolvedName ?? _names[0];

    public string? ResolvedName { get; private set; }

    public bool Available => _recorder.Valid || _gpuRecorder != null;

    public ProfilerMarkerDataUnit Unit { get; private set; } = ProfilerMarkerDataUnit.TimeNanoseconds;

    public bool IsTime => Unit == ProfilerMarkerDataUnit.TimeNanoseconds;

    // Для времени — миллисекунды, для остального — значение как есть.
    public double Last { get; private set; }

    public double Average { get; private set; }

    public double Peak { get; private set; }

    // Маркер есть, но у него нет GPU-метки: время видеокарты по нему не снять.
    public bool GPUUnsupported { get; private set; }

    public void Start()
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
        _triedDirectoryVersion = -1;
        Last = 0d;
        Average = 0d;
        Peak = 0d;
    }

    // Старый Recorder хранит только последний кадр, поэтому GPU-пробу надо
    // опрашивать каждый кадр, а не с частотой обновления строк.
    public void TickGpu()
    {
        if (_gpuRecorder != null)
        {
            _gpuSeries.Push(_gpuRecorder.gpuElapsedNanoseconds * 1e-6);
        }
    }

    public void Sample()
    {
        if (!Available)
        {
            Start();
            if (!Available)
            {
                return;
            }
        }

        if (_gpuRecorder != null)
        {
            Last = _gpuSeries.Last;
            Average = _gpuSeries.Average;
            Peak = _gpuSeries.Peak;
            return;
        }

        double scale = IsTime ? 1e-6 : 1d;
        Last = _recorder.LastValue * scale;

        int count = _recorder.Count;
        if (count <= 0)
        {
            Average = Last;
            Peak = Last;
        }
        else
        {
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
        }

#if UNITY_EDITOR
        if (Last == 0d && _names[0] == "Draw Calls Count")
        {
            Last = Average = Peak = UnityEditor.UnityStats.drawCalls;
        }
#endif
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

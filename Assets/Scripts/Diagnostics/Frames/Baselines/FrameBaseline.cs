#nullable enable

using System;
using Kern.Core.Interfaces.Diagnostics;


namespace Kern.Diagnostics.Frames;

/// <summary>Обычный кадр — скользящая медиана последних кадров.</summary>
///
/// Медиана, а не среднее: сами провисы среднее тянут вверх, и следующий провис
/// рядом с ними перестаёт им считаться. Пересчитывается раз в
/// <see cref="RecomputeEvery"/> кадров — обычный кадр за полсекунды не меняется.
public sealed class FrameBaseline
{
    private const int WindowFrames = 121;
    private const int RecomputeEvery = 30;
    private const int MinimumFramesForBaseline = 30;

    private readonly double[] _window = new double[WindowFrames];
    private readonly double[] _sorted = new double[WindowFrames];
    private int _next;
    private int _count;
    private int _sinceRecompute;

    /// <summary>Медиана; 0, пока кадров не набралось.</summary>
    public double Milliseconds { get; private set; }

    public bool IsReady => _count >= MinimumFramesForBaseline && Milliseconds > 0.0;

    public void Push(double frameMilliseconds)
    {
        if (frameMilliseconds <= 0.0 || double.IsNaN(frameMilliseconds) || double.IsInfinity(frameMilliseconds))
        {
            return;
        }

        _window[_next] = frameMilliseconds;
        _next = (_next + 1) % WindowFrames;
        if (_count < WindowFrames)
        {
            _count++;
        }

        if (++_sinceRecompute < RecomputeEvery && _count >= MinimumFramesForBaseline)
        {
            return;
        }

        _sinceRecompute = 0;
        Array.Copy(_window, _sorted, _count);
        Array.Sort(_sorted, 0, _count);
        Milliseconds = _sorted[_count / 2];
    }

    public void Clear()
    {
        _next = 0;
        _count = 0;
        _sinceRecompute = 0;
        Milliseconds = 0.0;
    }
}

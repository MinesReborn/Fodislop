#nullable enable

using System;
using System.Diagnostics;

namespace Kern.Rendering.PostProcessing;

/// <summary>
/// Command-recording observations, owned by one PostProcessRenderPass. Bytes describe
/// its created color targets (not imported scene color or driver allocation). CPU
/// duration measures recording; GPU duration is unavailable here. No per-frame allocation.
/// </summary>
internal sealed class PostProcessWorkload
{
    public PostProcessWorkloadSnapshot? Latest { get; private set; }

    public void Publish(PostProcessWorkloadSnapshot snapshot)
    {
        Latest = snapshot;
    }
}

internal readonly struct PostProcessWorkloadSnapshot(int frameId, int width, int height,
    int dispatchCount, int drawCount, long logicalPixels, long dispatchedThreads,
    int textureCount, long texturePayloadBytes, double cpuRecordingMs)
{
    public int FrameId { get; } = frameId;
    public int Width { get; } = width;
    public int Height { get; } = height;
    public int DispatchCount { get; } = dispatchCount;
    public int DrawCount { get; } = drawCount;
    public long LogicalPixels { get; } = logicalPixels;
    public long DispatchedThreads { get; } = dispatchedThreads;
    public int TextureCount { get; } = textureCount;
    public long TexturePayloadBytes { get; } = texturePayloadBytes;
    public double CPURecordingMs { get; } = cpuRecordingMs;
}

internal struct PostProcessWorkloadAccumulator
{
    private int _dispatchCount;
    private long _logicalPixels;
    private long _dispatchedThreads;

    public void RecordDispatch(int width, int height, int coverageWidth, int coverageHeight,
        int threadsPerGroup = 64)
    {
        if (width <= 0 || height <= 0 || coverageWidth <= 0 || coverageHeight <= 0 || threadsPerGroup <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        checked
        {
            _dispatchCount++;
            _logicalPixels += (long)width * height;
            _dispatchedThreads += ((width + (long)coverageWidth - 1) / coverageWidth) *
                ((height + (long)coverageHeight - 1) / coverageHeight) * threadsPerGroup;
        }
    }

    public void RecordRaster(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }
        _logicalPixels = checked(_logicalPixels + (long)width * height);
    }

    public PostProcessWorkloadSnapshot Complete(int frameId, int width, int height,
        int drawCount, int textureCount, long texturePayloadBytes, long startedAt)
    {
        return new PostProcessWorkloadSnapshot(frameId, width, height, _dispatchCount, drawCount,
            _logicalPixels, _dispatchedThreads, textureCount, texturePayloadBytes,
            (Stopwatch.GetTimestamp() - startedAt) * 1000.0 / Stopwatch.Frequency);
    }
}

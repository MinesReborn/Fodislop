#nullable enable

using Kern.Diagnostics.Frames;

namespace Kern.Infrastructure.Audio;

internal static class FmodSlowCallRecorder
{
    public static void Record(string operation, string eventPath, long started)
    {
        double milliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) *
            1000.0 / System.Diagnostics.Stopwatch.Frequency;
        if (milliseconds >= 2.0)
        {
            FrameEventLog.Record($"FMOD {operation}: {eventPath} {milliseconds:F1} мс");
        }
    }
}

namespace UnityEngine
{
    public static class Time
    {
        public static int frameCount { get; set; }
    }
}

namespace Unity.Profiling
{
    public readonly struct ProfilerCategory
    {
        public static ProfilerCategory Memory => default;
    }

    public struct ProfilerRecorder : System.IDisposable
    {
        public bool Valid { get; }
        public long LastValue { get; }
        public static ProfilerRecorder StartNew(ProfilerCategory category, string name) => default;
        public void Dispose() { }
    }
}

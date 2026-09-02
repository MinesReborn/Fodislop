#nullable enable

using System;
using System.Diagnostics;
using Unity.Profiling;
namespace Fodinae.Core
{
    public interface IFrameTelemetry
    {
        float TerrainMeshTimeMs { get; set; }
        float TerrainCacheTimeMs { get; set; }
        float TerrainFloodFillTimeMs { get; set; }
        float TerrainGpuUploadTimeMs { get; set; }
        float LightingBuildCommandsTimeMs { get; set; }
        float LightingExecuteCommandsTimeMs { get; set; }
        int LightingCommandBufferBytes { get; set; }
        int ActiveDynamicLights { get; set; }
        long GcAllocPerFrameBytes { get; set; }
        int TerrainRebuildCount { get; set; }
        int TerrainFullPopulateCount { get; set; }
        int TerrainMeshClearCount { get; set; }
        int TerrainDirtyPatchCount { get; set; }
        int LightingRegionInvalidationCount { get; set; }
        int LightingStaticSolveCount { get; set; }
        int LightingDynamicSolveCount { get; set; }
        long GcAllocTotalPerSecondBytes { get; }
        int GcCollectionCount { get; }

        void BeginFrame();
        void SetAllocationTrackingEnabled(bool enabled);
        void ResetFrameTimers();
    }

    public sealed class FrameTelemetry : IFrameTelemetry, IDisposable
    {
        public float TerrainMeshTimeMs { get; set; }
        public float TerrainCacheTimeMs { get; set; }
        public float TerrainFloodFillTimeMs { get; set; }
        public float TerrainGpuUploadTimeMs { get; set; }
        public float LightingBuildCommandsTimeMs { get; set; }
        public float LightingExecuteCommandsTimeMs { get; set; }
        public int LightingCommandBufferBytes { get; set; }
        public int ActiveDynamicLights { get; set; }
        public long GcAllocPerFrameBytes { get; set; }

        // Cumulative terrain rebuild counters, deliberately not reset per frame.
        //
        // "The terrain rebuilds and looks different while walking" has two very
        // different causes and reading the code cannot tell them apart: either
        // rebuilds are frequent (a cost problem), or a rebuild produces a
        // different image from the one before it (a correctness problem). Rates
        // separate the two in one walk.
        public int TerrainRebuildCount { get; set; }

        // Rebuilds that could not scroll the cache and repopulated from scratch.
        public int TerrainFullPopulateCount { get; set; }

        // Rebuilds that had to drop and reallocate the mesh, which shows as a
        // frame with no terrain at all.
        public int TerrainMeshClearCount { get; set; }

        public int TerrainDirtyPatchCount { get; set; }

        public int LightingRegionInvalidationCount { get; set; }
        public int LightingStaticSolveCount { get; set; }
        public int LightingDynamicSolveCount { get; set; }

        // Allocation rate for the whole process, sampled over a one second
        // window, from the "GC Allocated In Frame" profiler counter - the same
        // figure the Profiler window's Memory module shows.
        //
        // It has to be a ProfilerRecorder rather than a BCL call.
        // GC.GetTotalAllocatedBytes does not exist in Unity's Mono profile at
        // all, and GC.GetAllocatedBytesForCurrentThread - which is what this
        // file used to rely on - returns 0 under Unity's Boehm collector, so
        // the overlay read "GC: 0 KB/f" forever while the heap climbed by
        // megabytes a second. A number nobody can see is a number nobody
        // fixes; a number that is always zero is worse, because it looks like
        // an answer.
        //
        // The recorder needs the profiler enabled, which is the editor and
        // development builds - exactly where this overlay runs.
        public long GcAllocTotalPerSecondBytes { get; private set; }

        // Deliberately NOT split into "main thread" and "worker threads".
        //
        // The obvious way to get that split is to subtract
        // GC.GetAllocatedBytesForCurrentThread from the total. It does not
        // work: under Unity's Boehm collector that call returns 0, so the
        // subtraction reports every byte as coming from a worker no matter
        // where it really came from. It read 0.0 KB/f on the main thread while
        // this very overlay was building a multi-line interpolated string ten
        // times a second, which is impossible - and the "off-main" figure
        // matched the total to the last decimal, because it WAS the total.
        //
        // A wrong attribution is worse than no attribution: it sends whoever
        // reads it to the wrong half of the codebase. To localise the source,
        // use the F4-F8 bypass toggles and watch this number move.

        // Whether the collector runs at all. A heap that only grows is not the
        // same defect as a heap that is collected often and expensively.
        public int GcCollectionCount { get; private set; }

        private const double AllocationRateWindowSeconds = 1.0;

        private long _windowTotalAllocatedBytes;
        private double _windowStartSeconds;
        private readonly Stopwatch _allocationClock = Stopwatch.StartNew();

        // The counter is per frame and resets itself, so it is accumulated
        // across the window rather than read as a running total.
        private ProfilerRecorder _allocatedInFrameRecorder;
        private bool _allocationRecorderStarted;

        public void BeginFrame()
        {
            if (_allocationRecorderStarted)
            {
                UpdateAllocationRates();
            }
        }

        public void SetAllocationTrackingEnabled(bool enabled)
        {
            if (enabled == _allocationRecorderStarted)
            {
                return;
            }

            if (!enabled)
            {
                if (_allocatedInFrameRecorder.Valid)
                {
                    _allocatedInFrameRecorder.Dispose();
                }

                _allocatedInFrameRecorder = default;
                _allocationRecorderStarted = false;
                GcAllocPerFrameBytes = 0;
                GcAllocTotalPerSecondBytes = 0;
                _windowTotalAllocatedBytes = 0;
                _windowStartSeconds = 0d;
                return;
            }

            _allocatedInFrameRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC Allocated In Frame");
            _allocationRecorderStarted = true;
            _windowTotalAllocatedBytes = 0;
            _windowStartSeconds = _allocationClock.Elapsed.TotalSeconds;
        }

        private void UpdateAllocationRates()
        {
            if (_allocatedInFrameRecorder.Valid)
            {
                GcAllocPerFrameBytes = _allocatedInFrameRecorder.LastValue;
                _windowTotalAllocatedBytes += GcAllocPerFrameBytes;
            }

            double now = _allocationClock.Elapsed.TotalSeconds;
            double elapsed = now - _windowStartSeconds;
            if (elapsed < AllocationRateWindowSeconds)
            {
                return;
            }

            if (_windowStartSeconds > 0d)
            {
                GcAllocTotalPerSecondBytes = (long)(_windowTotalAllocatedBytes / elapsed);
            }

            GcCollectionCount = GC.CollectionCount(0);
            _windowTotalAllocatedBytes = 0;
            _windowStartSeconds = now;
        }

        public void ResetFrameTimers()
        {
            TerrainMeshTimeMs = 0f;
            TerrainCacheTimeMs = 0f;
            TerrainFloodFillTimeMs = 0f;
            TerrainGpuUploadTimeMs = 0f;
            LightingBuildCommandsTimeMs = 0f;
            LightingExecuteCommandsTimeMs = 0f;
        }

        public void Dispose()
        {
            SetAllocationTrackingEnabled(false);
            _allocationClock.Stop();
        }
    }
}

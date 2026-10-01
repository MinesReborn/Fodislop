#nullable enable

using System;
using System.Threading;

namespace Kern.Core.Diagnostics;

/// <summary>Rejects unsafe allocations only while a test run owns the memory budget.</summary>
public static class MemoryAllocationGuard
{
    public const int MinimumAvailablePercent = 20;
    public const long MaximumTestProcessBytes = 3L * 1024 * 1024 * 1024;
    private const long MinimumReserveBytes = 512L * 1024 * 1024;
    private static long _testProcessLimit;

    // Set for the whole test run, including gaps between cases; no environment bypass.
    public static void BeginTestRun()
    {
        ProcessMemorySnapshot memory = ProcessMemorySnapshot.Capture();
        long limit = Math.Min(MaximumTestProcessBytes, memory.PhysicalBytes / 100 * 35);
        if (Rejection(memory, 0, limit) is string reason)
        {
            throw new InvalidOperationException($"[MemoryGuard] test run: {reason}");
        }
        Interlocked.Exchange(ref _testProcessLimit, limit);
    }

    public static void EndTestRun() => Interlocked.Exchange(ref _testProcessLimit, 0);

    public static void Require(string owner, long allocationBytes)
    {
        long limit = Interlocked.Read(ref _testProcessLimit);
        if (limit == 0) { return; }
        ProcessMemorySnapshot memory = ProcessMemorySnapshot.Capture();
        if (Rejection(memory, allocationBytes, limit) is string reason)
        {
            throw new InvalidOperationException($"[MemoryGuard] {owner}: {reason}");
        }
    }

    // Independent of Unity APIs so the test watchdog can check while the render thread is blocked.
    public static string? Rejection(ProcessMemorySnapshot memory, long allocationBytes, long processLimitBytes = 0)
    {
        if (allocationBytes < 0) { throw new ArgumentOutOfRangeException(nameof(allocationBytes)); }
        if (processLimitBytes < 0) { throw new ArgumentOutOfRangeException(nameof(processLimitBytes)); }
        RequireValidSnapshot(memory);
        long overhead = checked(allocationBytes + (allocationBytes + 3) / 4);
        long limit = processLimitBytes > 0 ? processLimitBytes :
            Math.Min(MaximumTestProcessBytes, memory.PhysicalBytes / 100 * 35);
        if (memory.ProcessBytes > limit || overhead > limit - memory.ProcessBytes)
        {
            return $"процесс {MiB(memory.ProcessBytes)} МиБ + аллокация {MiB(overhead)} МиБ " +
                $"с запасом превышают предел {MiB(limit)} МиБ";
        }
        long available = memory.PhysicalBytes / 100 * memory.AvailablePercent!.Value;
        long reserve = Math.Max(MinimumReserveBytes, memory.PhysicalBytes / 100 * MinimumAvailablePercent);
        if (available < reserve || overhead > available - reserve)
        {
            return $"свободно {memory.AvailablePercent}% физической памяти; аллокация {MiB(overhead)} МиБ " +
                $"нарушит резерв {MiB(reserve)} МиБ";
        }
        return null;
    }

    public static string? TestRunRejection(ProcessMemorySnapshot memory)
    {
        long limit = Interlocked.Read(ref _testProcessLimit);
        return limit == 0 ? null : Rejection(memory, 0, limit);
    }

    private static void RequireValidSnapshot(ProcessMemorySnapshot memory)
    {
        if (memory.PhysicalBytes <= 0 || memory.ProcessBytes <= 0 ||
            memory.AvailablePercent is not (>= 0 and <= 100))
        {
            throw new InvalidOperationException("[MemoryGuard] Не удалось измерить физическую память; аллокация запрещена.");
        }
    }

    private static long MiB(long bytes) => bytes / (1024 * 1024);
}

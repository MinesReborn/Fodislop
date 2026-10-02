using Kern.Core.Diagnostics;
using Kern.World.Lighting;
using NUnit.Framework;

namespace Kern.FrameHarness.Tests;

[TestFixture]
[NonParallelizable]
public sealed class MemoryAllocationGuardTests
{
    private const long GiB = 1024L * 1024 * 1024;
    private const long MiB = 1024L * 1024;

    [Test]
    public void FitsWithinProcessCapAndPhysicalReserve()
    {
        var memory = new ProcessMemorySnapshot(8 * GiB, 2 * GiB, 80);
        Assert.That(MemoryAllocationGuard.Rejection(memory, 768 * MiB, 3 * GiB), Is.Null);
    }

    [Test]
    public void PendingGenerationIncludesDriverOverheadAndExistingProcess()
    {
        var memory = new ProcessMemorySnapshot(8 * GiB, 2 * GiB, 80);
        Assert.That(MemoryAllocationGuard.Rejection(memory, GiB, 3 * GiB), Is.Not.Null);
    }

    [Test]
    public void AvailableMemoryCannotConsumeTheSystemReserve()
    {
        var memory = new ProcessMemorySnapshot(8 * GiB, GiB, 24);
        Assert.That(MemoryAllocationGuard.Rejection(memory, 300 * MiB), Is.Not.Null);
    }

    [Test]
    public void ExactReserveAllowsNoAdditionalAllocation()
    {
        var memory = new ProcessMemorySnapshot(8 * GiB, GiB, 20);
        Assert.That(MemoryAllocationGuard.Rejection(memory, 0), Is.Null);
        Assert.That(MemoryAllocationGuard.Rejection(memory, 1), Is.Not.Null);
    }

    [Test]
    public void UnknownMeasurementFailsClosed()
    {
        Assert.Throws<InvalidOperationException>(() => MemoryAllocationGuard.Rejection(default, 0));
        Assert.Throws<InvalidOperationException>(() =>
            MemoryAllocationGuard.Rejection(new ProcessMemorySnapshot(8 * GiB, GiB, null), 0));
    }

    [Test]
    public void WindowsPhysicalMemoryLayoutMatchesTheNativeAbi()
    {
        Type status = typeof(ProcessMemorySnapshot).GetNestedType("WindowsMemoryStatus",
            System.Reflection.BindingFlags.NonPublic)!;
        Assert.That(System.Runtime.InteropServices.Marshal.SizeOf(status), Is.EqualTo(64));
        Assert.That(System.Runtime.InteropServices.Marshal.OffsetOf(status, "Length").ToInt32(), Is.Zero);
        Assert.That(System.Runtime.InteropServices.Marshal.OffsetOf(status, "TotalPhysical").ToInt32(), Is.EqualTo(8));
        Assert.That(System.Runtime.InteropServices.Marshal.OffsetOf(status, "AvailablePhysical").ToInt32(), Is.EqualTo(16));
    }

    [Test]
    public void OverflowCannotTurnAnUnsafeEstimateIntoASmallAllocation()
    {
        var memory = new ProcessMemorySnapshot(8 * GiB, GiB, 80);
        Assert.Throws<OverflowException>(() => MemoryAllocationGuard.Rejection(memory, long.MaxValue));
        Assert.Throws<OverflowException>(() => LightingAllocationEstimate.TextureBytes(int.MaxValue, int.MaxValue, 64, 16));
    }

    [Test]
    public void FortyFourSourceVectorCacheIsRejectedWithoutCreatingIt()
    {
        // Independent expected payload: 4096 x 2560 x 64 x 8 = 5 GiB.
        long payload = LightingAllocationEstimate.TextureBytes(4096, 2560, 64, 8);
        Assert.That(payload, Is.EqualTo(5 * GiB));
        var memory = new ProcessMemorySnapshot(8 * GiB, GiB, 80);
        Assert.That(MemoryAllocationGuard.Rejection(memory, payload, 3 * GiB), Is.Not.Null);
    }

    [Test]
    public void OrdinaryRenderingHasNoMemoryPolicyOutsideTestRun()
    {
        Assert.DoesNotThrow(() => MemoryAllocationGuard.Require("ordinary render", long.MaxValue));
        Assert.That(MemoryAllocationGuard.TestRunRejection(default), Is.Null);
    }

    [Test]
    public void NativeReaderAndTestRunGuardRejectLargeEstimateEvenWithOldBypass()
    {
        ProcessMemorySnapshot memory = ProcessMemorySnapshot.Capture();
        Assert.That(memory.PhysicalBytes, Is.GreaterThan(0));
        Assert.That(memory.ProcessBytes, Is.GreaterThan(0));
        Assert.That(memory.AvailablePercent, Is.InRange(0, 100));
        string? oldValue = Environment.GetEnvironmentVariable("KERN_DISABLE_TEST_MEMORY_GUARD");
        try
        {
            Environment.SetEnvironmentVariable("KERN_DISABLE_TEST_MEMORY_GUARD", "1");
            MemoryAllocationGuard.BeginTestRun();
            Assert.Throws<InvalidOperationException>(() =>
                MemoryAllocationGuard.Require("bounded OOM regression", memory.PhysicalBytes));
        }
        finally
        {
            MemoryAllocationGuard.EndTestRun();
            Environment.SetEnvironmentVariable("KERN_DISABLE_TEST_MEMORY_GUARD", oldValue);
        }
        Assert.DoesNotThrow(() => MemoryAllocationGuard.Require("ordinary render after test", long.MaxValue));
        Assert.That(MemoryAllocationGuard.TestRunRejection(default), Is.Null);
    }
}

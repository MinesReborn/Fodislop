#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Kern.Core.Diagnostics;

/// <summary>Physical memory only. Swap is never counted as allocation headroom.</summary>
public readonly struct ProcessMemorySnapshot(long physicalBytes, long processBytes, int? availablePercent)
{
    public long PhysicalBytes { get; } = physicalBytes;
    public long ProcessBytes { get; } = processBytes;
    public int? AvailablePercent { get; } = availablePercent;

    public static ProcessMemorySnapshot Capture()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return MacMemory.Capture();
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var status = new WindowsMemoryStatus { Length = (uint)Marshal.SizeOf<WindowsMemoryStatus>() };
            if (!GlobalMemoryStatusEx(ref status) || status.TotalPhysical == 0) { return default; }
            return new ProcessMemorySnapshot(checked((long)status.TotalPhysical), WorkingSet(),
                (int)(status.AvailablePhysical * 100 / status.TotalPhysical));
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            long total = 0;
            long available = 0;
            foreach (string line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:", StringComparison.Ordinal)) { total = Kilobytes(line); }
                if (line.StartsWith("MemAvailable:", StringComparison.Ordinal)) { available = Kilobytes(line); }
            }
            return new ProcessMemorySnapshot(total, WorkingSet(), total > 0 ? (int)(available * 100 / total) : null);
        }
        return default;
    }

    private static long Kilobytes(string line) =>
        checked(long.Parse(line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[1],
            System.Globalization.CultureInfo.InvariantCulture) * 1024);

    private static long WorkingSet()
    {
        using Process process = Process.GetCurrentProcess();
        process.Refresh();
        return process.WorkingSet64;
    }

    // Native MEMORYSTATUSEX is 64 bytes. Only the physical-memory fields are
    // exposed; page file and virtual-address figures are not allocation headroom.
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct WindowsMemoryStatus
    {
        [FieldOffset(0)]
        public uint Length;
        [FieldOffset(8)]
        public ulong TotalPhysical;
        [FieldOffset(16)]
        public ulong AvailablePhysical;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref WindowsMemoryStatus status);

    private static class MacMemory
    {
        private static readonly object s_sync = new();
        private static readonly byte[] s_rusage = new byte[96];

        [DllImport("libSystem.dylib", EntryPoint = "getpid")]
        private static extern int GetPid();

        [DllImport("libSystem.dylib", EntryPoint = "proc_pid_rusage")]
        private static extern int ReadProcessUsage(int pid, int flavor, byte[] buffer);

        [DllImport("libSystem.dylib", EntryPoint = "sysctlbyname")]
        private static extern int ReadInt64(string name, out long value, ref IntPtr length, IntPtr input, IntPtr inputLength);

        [DllImport("libSystem.dylib", EntryPoint = "sysctlbyname")]
        private static extern int ReadInt32(string name, out int value, ref IntPtr length, IntPtr input, IntPtr inputLength);

        public static ProcessMemorySnapshot Capture()
        {
            lock (s_sync)
            {
                var totalLength = (IntPtr)sizeof(long);
                var availableLength = (IntPtr)sizeof(int);
                if (ReadInt64("hw.memsize", out long total, ref totalLength, IntPtr.Zero, IntPtr.Zero) != 0 ||
                    ReadInt32("kern.memorystatus_level", out int available, ref availableLength, IntPtr.Zero, IntPtr.Zero) != 0 ||
                    ReadProcessUsage(GetPid(), 0, s_rusage) != 0)
                {
                    return default;
                }
                // rusage_info_v0. WorkingSet64 does not report the macOS physical footprint.
                return new ProcessMemorySnapshot(total, checked((long)BitConverter.ToUInt64(s_rusage, 72)), available);
            }
        }
    }
}

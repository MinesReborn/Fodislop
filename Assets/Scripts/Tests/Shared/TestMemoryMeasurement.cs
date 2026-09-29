#nullable enable

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Kern.Core.Interfaces.Diagnostics;
using Kern.Tests;
using NUnit.Framework.Interfaces;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestRunner;
using Debug = UnityEngine.Debug;

[assembly: TestRunCallback(typeof(TestMemoryMeasurement))]

namespace Kern.Tests;

/// <summary>
/// Замер оперативки вокруг каждого теста проекта.
/// </summary>
///
/// Регистрируется колбэком прогона (<see cref="TestRunCallbackAttribute"/>):
/// Unity зовёт его для каждого теста каждой тестовой сборки — EditMode и
/// PlayMode одинаково. Атрибут-действие NUnit на сборке здесь не работает:
/// Unity собирает действия только с методов и классов.
///
/// До и после теста снимаются память процесса (phys_footprint — число из
/// «Мониторинга системы»), нативная память Unity, управляемая куча, память
/// графического драйвера и свободная память системы; пока тест идёт — пик
/// процесса. Всё пишется строкой в Logs/Diagnostics/Tests/test_memory_*.csv,
/// одна таблица на прогон.
///
/// Заодно это предохранитель: прогоны PlayMode подряд уже трижды выедали
/// память машины. Пороги — по системе, а не по доле процесса: на машине с
/// 8 ГБ редактор сам по себе занимает больше половины. И не по одной
/// свободной памяти: 23.09 она показывала 28%, а своп был занят на 11.3 ГБ
/// из 12.3 — система держится, пока есть куда выгружать, и падает, когда
/// своп кончился. Поэтому прогон останавливается, если выполнено любое:
/// свободно меньше <see cref="MinimumAvailablePercent"/>% памяти, свободно
/// меньше <see cref="MinimumSwapFreeBytes"/> свопа, своп вырос за прогон
/// больше чем на <see cref="MaximumSwapGrowthBytes"/>. Сторож по кадрам
/// убивает тест посреди выполнения — отменяет прогон и выходит из Play
/// Mode; следующий тест не начинается. CSV лежат в Logs/Diagnostics/Tests/.
public sealed class TestMemoryMeasurement : ITestRunCallback
{
    /// <summary>Сколько процентов памяти системы должно оставаться свободным.</summary>
    public const int MinimumAvailablePercent = 20;

    /// <summary>Сколько свопа должно оставаться свободным.</summary>
    public const long MinimumSwapFreeBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>На сколько своп может вырасти за прогон.</summary>
    public const long MaximumSwapGrowthBytes = 512L * 1024 * 1024;

    // Тест, выросший больше этого, отмечается в консоли.
    private const long NotableGrowthBytes = 128L * 1024 * 1024;

    // Сторож смотрит на память не чаще, чем раз в столько секунд.
    private const double WatchIntervalSeconds = 0.25;

    private static readonly UTF8Encoding _Utf8 = new(false);

    private string? _reportPath;
    private Snapshot _before;
    private long _peak;
    private int _lowestAvailable;
    private long _runSwapUsed = -1;
    private long _peakSwapUsed;
    private double _nextWatch;
    private bool _running;

    public void RunStarted(ITest testsToRun)
    {
        _reportPath = null;
        _runSwapUsed = SystemMemory.Swap() is SwapUsage swap ? swap.UsedBytes : -1;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= Watch;
        UnityEditor.EditorApplication.update += Watch;
#endif
    }

    public void RunFinished(ITestResult testResults)
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= Watch;
#endif
        _running = false;
        if (_reportPath != null)
        {
            DiagnosticReport.Announce("Оперативка тестов", _reportPath);
        }
    }

    public void TestStarted(ITest test)
    {
        if (test.IsSuite)
        {
            return;
        }

        Snapshot now = Snapshot.Take();
        if (Violation(now.AvailablePercent, now.Swap) is string reason)
        {
            // Колбэк не может пропустить тест, а исключение отсюда Unity
            // пробрасывает в раннер и обрывает прогон — это и нужно.
            throw new InvalidOperationException(
                $"[TestMemory] Прогон остановлен перед {test.FullName}: {reason}; " +
                $"процесс занимает {Megabytes(now.ProcessBytes)} МБ.");
        }

        _before = now;
        _peak = now.ProcessBytes;
        _lowestAvailable = now.AvailablePercent ?? -1;
        _peakSwapUsed = now.Swap?.UsedBytes ?? -1;
        _running = true;
    }

    public void TestFinished(ITestResult result)
    {
        if (result.Test.IsSuite || !_running)
        {
            return;
        }

        _running = false;
        Snapshot after = Snapshot.Take();
        Observe(after.ProcessBytes, after.AvailablePercent, after.Swap);
        Write(result, after);

        long growth = after.ProcessBytes - _before.ProcessBytes;
        if (growth >= NotableGrowthBytes)
        {
            Debug.LogWarning(
                $"[TestMemory] {result.Test.FullName}: процесс вырос на {Megabytes(growth)} МБ " +
                $"({Megabytes(_before.ProcessBytes)} → {Megabytes(after.ProcessBytes)}, пик {Megabytes(_peak)}).");
        }

        if (Violation(after.AvailablePercent, after.Swap) is string reason)
        {
            Debug.LogError(
                $"[TestMemory] {result.Test.FullName}: {reason}; пик процесса {Megabytes(_peak)} МБ, " +
                $"свободно минимум {_lowestAvailable}%, своп на пике {Megabytes(_peakSwapUsed)} МБ.");
        }
    }

    private void Observe(long processBytes, int? availablePercent, SwapUsage? swap)
    {
        _peak = Math.Max(_peak, processBytes);
        if (availablePercent is int available && (_lowestAvailable < 0 || available < _lowestAvailable))
        {
            _lowestAvailable = available;
        }

        if (swap is SwapUsage usage)
        {
            _peakSwapUsed = Math.Max(_peakSwapUsed, usage.UsedBytes);
        }
    }

    // Причина остановить прогон или null, если память в порядке.
    private string? Violation(int? availablePercent, SwapUsage? swap)
    {
        if (Environment.GetEnvironmentVariable("KERN_DISABLE_TEST_MEMORY_GUARD") == "1")
        {
            return null;
        }

        if (availablePercent is int available && available < MinimumAvailablePercent)
        {
            return $"у системы свободно {available}% памяти (порог {MinimumAvailablePercent}%)";
        }

        if (swap is not SwapUsage usage)
        {
            return null;
        }

        if (usage.FreeBytes < MinimumSwapFreeBytes)
        {
            return $"свопа свободно {Megabytes(usage.FreeBytes)} МБ из {Megabytes(usage.TotalBytes)} " +
                $"(порог {Megabytes(MinimumSwapFreeBytes)} МБ)";
        }

        if (_runSwapUsed >= 0 && usage.UsedBytes - _runSwapUsed > MaximumSwapGrowthBytes)
        {
            return $"своп вырос за прогон на {Megabytes(usage.UsedBytes - _runSwapUsed)} МБ " +
                $"(порог {Megabytes(MaximumSwapGrowthBytes)} МБ)";
        }

        return null;
    }

#if UNITY_EDITOR
    // Сторож по кадрам: тест, который сам съедает память, до конца не
    // дожидается. Выход из Play Mode обрывает PlayMode-прогон.
    private void Watch()
    {
        if (!_running)
        {
            return;
        }

        double now = UnityEditor.EditorApplication.timeSinceStartup;
        if (now < _nextWatch)
        {
            return;
        }

        _nextWatch = now + WatchIntervalSeconds;
        int? available = SystemMemory.AvailablePercent();
        SwapUsage? swap = SystemMemory.Swap();
        Observe(SystemMemory.ProcessBytes(), available, swap);
        if (Violation(available, swap) is not string reason)
        {
            return;
        }

        Debug.LogError($"[TestMemory] Посреди теста {reason}: тест убит, прогон остановлен.");
        _running = false;
        KillRuns();
        if (UnityEditor.EditorApplication.isPlaying)
        {
            UnityEditor.EditorApplication.ExitPlaymode();
        }
    }

    // Отмена прогона выбрасывает перечислитель текущего теста — тест
    // останавливается посреди кадра, а не доигрывает до конца.
    // TestRunnerApi.CancelTestRun публичный, но просит guid прогона, а
    // список прогонов лежит во внутреннем TestJobDataHolder: guid'ы
    // достаются рефлексией.
    private static void KillRuns()
    {
        try
        {
            Type? holderType = Type.GetType(
                "UnityEditor.TestTools.TestRunner.TestRun.TestJobDataHolder, UnityEditor.TestRunner");
            Type? apiType = Type.GetType(
                "UnityEditor.TestTools.TestRunner.Api.TestRunnerApi, UnityEditor.TestRunner");
            object? holder = holderType?
                .GetProperty("instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?
                .GetValue(null);
            MethodInfo? cancel = apiType?.GetMethod("CancelTestRun", BindingFlags.Public | BindingFlags.Static);
            if (holder == null || cancel == null ||
                holderType!.GetMethod("GetAllRunners")?.Invoke(holder, null) is not Array runners)
            {
                Debug.LogError("[TestMemory] Не нашёл прогоны Unity Test Framework: тест не убит.");
                return;
            }

            foreach (object runner in runners)
            {
                object? data = runner.GetType().GetMethod("GetData")?.Invoke(runner, null);
                if (data?.GetType().GetField("guid")?.GetValue(data) is string guid && guid.Length > 0)
                {
                    cancel.Invoke(null, new object[] { guid });
                }
            }
        }
        catch (Exception exception) when (exception is TargetInvocationException or MemberAccessException)
        {
            Debug.LogError($"[TestMemory] Прогон не остановлен: {exception.InnerException?.Message ?? exception.Message}");
        }
    }
#endif

    private void Write(ITestResult result, Snapshot after)
    {
        try
        {
            if (_reportPath == null)
            {
                _reportPath = DiagnosticArtifactPaths.CreatePath("Tests", "test_memory", "csv");
                File.WriteAllText(
                    _reportPath,
                    "test;result;seconds;process_before_mb;process_after_mb;process_peak_mb;" +
                    "system_free_before_pct;system_free_lowest_pct;swap_used_before_mb;swap_used_peak_mb;" +
                    "native_before_mb;native_after_mb;managed_before_mb;managed_after_mb;gfx_before_mb;gfx_after_mb\n",
                    _Utf8);
            }

            var line = new StringBuilder(256);
            line.Append(result.Test.FullName.Replace(';', ',')).Append(';')
                .Append(result.ResultState.Status).Append(';')
                .Append(result.Duration.ToString("F2", CultureInfo.InvariantCulture)).Append(';')
                .Append(Megabytes(_before.ProcessBytes)).Append(';')
                .Append(Megabytes(after.ProcessBytes)).Append(';')
                .Append(Megabytes(_peak)).Append(';')
                .Append(_before.AvailablePercent?.ToString(CultureInfo.InvariantCulture) ?? "-").Append(';')
                .Append(_lowestAvailable >= 0 ? _lowestAvailable.ToString(CultureInfo.InvariantCulture) : "-").Append(';')
                .Append(_before.Swap is SwapUsage swapBefore ? Megabytes(swapBefore.UsedBytes) : "-").Append(';')
                .Append(_peakSwapUsed >= 0 ? Megabytes(_peakSwapUsed) : "-").Append(';')
                .Append(Megabytes(_before.NativeBytes)).Append(';')
                .Append(Megabytes(after.NativeBytes)).Append(';')
                .Append(Megabytes(_before.ManagedBytes)).Append(';')
                .Append(Megabytes(after.ManagedBytes)).Append(';')
                .Append(Megabytes(_before.GraphicsBytes)).Append(';')
                .Append(Megabytes(after.GraphicsBytes)).Append('\n');
            File.AppendAllText(_reportPath, line.ToString(), _Utf8);
        }
        catch (IOException exception)
        {
            // Отчёт — не повод ронять прогон; сторож работает и без него.
            Debug.LogWarning($"[TestMemory] Отчёт не записан: {exception.Message}");
        }
    }

    private static string Megabytes(long bytes) =>
        (bytes / (1024.0 * 1024.0)).ToString("F0", CultureInfo.InvariantCulture);

    private readonly struct Snapshot
    {
        private Snapshot(long process, int? available, SwapUsage? swap, long native, long managed, long graphics)
        {
            ProcessBytes = process;
            AvailablePercent = available;
            Swap = swap;
            NativeBytes = native;
            ManagedBytes = managed;
            GraphicsBytes = graphics;
        }

        /// <summary>Память процесса целиком — то, что видит система.</summary>
        public long ProcessBytes { get; }

        /// <summary>Свободная память системы в процентах; null, если узнать нельзя.</summary>
        public int? AvailablePercent { get; }

        /// <summary>Своп системы; null, если узнать нельзя.</summary>
        public SwapUsage? Swap { get; }

        public long NativeBytes { get; }

        public long ManagedBytes { get; }

        public long GraphicsBytes { get; }

        public static Snapshot Take() =>
            new(
                SystemMemory.ProcessBytes(),
                SystemMemory.AvailablePercent(),
                SystemMemory.Swap(),
                Profiler.GetTotalAllocatedMemoryLong(),
                GC.GetTotalMemory(forceFullCollection: false),
                Profiler.GetAllocatedMemoryForGraphicsDriver());
    }

    private readonly struct SwapUsage
    {
        public SwapUsage(long total, long used, long free)
        {
            TotalBytes = total;
            UsedBytes = used;
            FreeBytes = free;
        }

        public long TotalBytes { get; }

        public long UsedBytes { get; }

        public long FreeBytes { get; }
    }

    /// <summary>
    /// Память процесса и системы так, как их считает сама система.
    /// </summary>
    ///
    /// Process.WorkingSet64 в Mono на macOS врёт: редактор в гигабайтах, а он
    /// показывает полторы сотни мегабайт. На macOS память процесса —
    /// phys_footprint из proc_pid_rusage, свободная память системы —
    /// kern.memorystatus_level, своп — vm.swapusage: по ним система сама
    /// решает, что памяти не хватает.
    private static class SystemMemory
    {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        private const int RusageInfoV0 = 0;
        private const int PhysFootprintOffset = 72;
        private const int RusageInfoV0Size = 96;

        private static readonly byte[] _Buffer = new byte[RusageInfoV0Size];

        [System.Runtime.InteropServices.DllImport("libSystem.dylib")]
        private static extern int proc_pid_rusage(int pid, int flavor, byte[] buffer);

        [System.Runtime.InteropServices.DllImport("libSystem.dylib")]
        private static extern int getpid();

        // struct xsw_usage: total, avail, used (uint64), pagesize, encrypted.
        private const int SwapUsageSize = 32;

        private static readonly byte[] _SwapBuffer = new byte[SwapUsageSize];

        [System.Runtime.InteropServices.DllImport("libSystem.dylib")]
        private static extern int sysctlbyname(string name, out int value, ref IntPtr length, IntPtr newValue, IntPtr newLength);

        [System.Runtime.InteropServices.DllImport("libSystem.dylib", EntryPoint = "sysctlbyname")]
        private static extern int sysctlbynameBytes(string name, byte[] value, ref IntPtr length, IntPtr newValue, IntPtr newLength);

        public static long ProcessBytes()
        {
            if (proc_pid_rusage(getpid(), RusageInfoV0, _Buffer) == 0)
            {
                return (long)BitConverter.ToUInt64(_Buffer, PhysFootprintOffset);
            }

            return WorkingSet();
        }

        public static int? AvailablePercent()
        {
            var length = (IntPtr)sizeof(int);
            return sysctlbyname("kern.memorystatus_level", out int level, ref length, IntPtr.Zero, IntPtr.Zero) == 0
                ? level
                : null;
        }

        public static SwapUsage? Swap()
        {
            var length = (IntPtr)SwapUsageSize;
            if (sysctlbynameBytes("vm.swapusage", _SwapBuffer, ref length, IntPtr.Zero, IntPtr.Zero) != 0)
            {
                return null;
            }

            long total = (long)BitConverter.ToUInt64(_SwapBuffer, 0);
            long free = (long)BitConverter.ToUInt64(_SwapBuffer, 8);
            long used = (long)BitConverter.ToUInt64(_SwapBuffer, 16);
            return new SwapUsage(total, used, free);
        }
#else
        public static long ProcessBytes() => WorkingSet();

        public static int? AvailablePercent() => null;

        public static SwapUsage? Swap() => null;
#endif

        private static long WorkingSet()
        {
            using Process process = Process.GetCurrentProcess();
            process.Refresh();
            return process.WorkingSet64;
        }
    }
}

#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Kern.Core.Diagnostics;
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
/// Заодно это предохранитель: прогон останавливается, если физически
/// доступная системная память опускается ниже
/// <see cref="MinimumAvailablePercent"/>%, или процесс превышает свой лимит.
/// Аллокации lighting проверяются до создания ресурсов. Swap записывается только как
/// диагностическая метрика и не блокирует запуск или продолжение теста.
/// Сторож по кадрам убивает тест посреди выполнения, если системный порог
/// нарушен; следующий тест не начинается. CSV лежат в Logs/Diagnostics/Tests/.
public sealed class TestMemoryMeasurement : ITestRunCallback
{
    /// <summary>Сколько процентов памяти системы должно оставаться свободным.</summary>
    public const int MinimumAvailablePercent = MemoryAllocationGuard.MinimumAvailablePercent;

    // Тест, выросший больше этого, отмечается в консоли.
    private const long NotableGrowthBytes = 128L * 1024 * 1024;

    // Сторож смотрит на память не чаще, чем раз в столько секунд.
    private const double WatchIntervalSeconds = 0.25;

    private static readonly UTF8Encoding _Utf8 = new(false);

    private string? _reportPath;
    private Snapshot _before;
    private long _peak;
    private int _lowestAvailable;
    private long _peakSwapUsed;
    private double _nextWatch;
    private int _pendingPlayModeExitUpdates;
    private volatile bool _running;
    private Timer? _watchdog;
    private string? _abortReason;
    private string? _abortReportPath;
    private bool _batchMode;

    public void RunStarted(ITest testsToRun)
    {
        _running = false;
        _watchdog?.Dispose();
        _abortReason = null;
        _batchMode = Application.isBatchMode;
        _abortReportPath = DiagnosticArtifactPaths.CreatePath("Tests", "test_memory_abort", "txt");
        try
        {
            MemoryAllocationGuard.BeginTestRun();
        }
        catch (InvalidOperationException exception)
        {
            _abortReason = exception.Message;
            if (_batchMode) { ExitUnsafeBatch(exception.Message); }
#if UNITY_EDITOR
            // Callback exceptions may be logged by the framework. Schedule
            // cancellation as well, so swallowing one cannot start an unsafe run.
            _running = true;
            UnityEditor.EditorApplication.update -= Watch;
            UnityEditor.EditorApplication.update += Watch;
#endif
            throw;
        }
        _watchdog = new Timer(CheckMemoryOutsideFrame, null, 250, 250);
        _reportPath = null;
        _pendingPlayModeExitUpdates = 0;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= Watch;
        UnityEditor.EditorApplication.update += Watch;
#endif
    }

    public void RunFinished(ITestResult testResults)
    {
        _running = false;
        _watchdog?.Dispose();
        _watchdog = null;
        MemoryAllocationGuard.EndTestRun();
#if UNITY_EDITOR
        if (_pendingPlayModeExitUpdates == 0)
        {
            UnityEditor.EditorApplication.update -= Watch;
        }
#endif
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
        // PlayMode domain reload can recreate callbacks without RunStarted.
        if (_watchdog == null) { RunStarted(test); }
        Snapshot now = Snapshot.Take();
        if (Violation(now.Memory) is string reason)
        {
            _abortReason = reason;
            if (_batchMode) { ExitUnsafeBatch(reason); }
            _running = true;
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

        if (Violation(after.Memory) is string reason)
        {
            Debug.LogError(
                $"[TestMemory] {result.Test.FullName}: {reason}; пик процесса {Megabytes(_peak)} МБ, " +
                $"свободно минимум {_lowestAvailable}%.");
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
    private static string? Violation(ProcessMemorySnapshot memory) => MemoryAllocationGuard.TestRunRejection(memory);

    // No Unity API here. The frame callback cannot protect a batch process stuck
    // inside a synchronous allocation/render wait. Exit only the owned batch test
    // process on a breach; GUI Editor cancellation stays on the main thread.
    private void CheckMemoryOutsideFrame(object? state)
    {
        if (!_running || Volatile.Read(ref _abortReason) != null) { return; }
        string? reason;
        try
        {
            reason = Violation(ProcessMemorySnapshot.Capture());
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            reason = $"сторож не смог измерить память: {exception.Message}";
        }
        if (reason == null || !_running || Interlocked.CompareExchange(ref _abortReason, reason, null) != null) { return; }
        if (_batchMode)
        {
            ExitUnsafeBatch(reason);
        }
    }

    private void ExitUnsafeBatch(string reason)
    {
        try
        {
            if (_abortReportPath != null)
            {
                File.WriteAllText(_abortReportPath, $"[TestMemory] Batch stopped: {reason}\n", _Utf8);
            }
        }
        catch (IOException) { /* Exit still protects memory if the report cannot be written. */ }
        Environment.Exit(1);
    }

#if UNITY_EDITOR
    // Сторож по кадрам: тест, который сам съедает память, до конца не
    // дожидается. Выход из Play Mode обрывает PlayMode-прогон.
    private void Watch()
    {
        if (_pendingPlayModeExitUpdates > 0)
        {
            _pendingPlayModeExitUpdates--;
            if (_pendingPlayModeExitUpdates == 0)
            {
                UnityEditor.EditorApplication.update -= Watch;
                if (UnityEditor.EditorApplication.isPlaying)
                {
                    UnityEditor.EditorApplication.ExitPlaymode();
                }
            }

            return;
        }

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
        ProcessMemorySnapshot memory = ProcessMemorySnapshot.Capture();
        int? available = memory.AvailablePercent;
        SwapUsage? swap = SystemMemory.Swap();
        Observe(memory.ProcessBytes, available, swap);
        string? reason = Volatile.Read(ref _abortReason) ?? Violation(memory);
        if (reason == null)
        {
            return;
        }

        Debug.LogError($"[TestMemory] Посреди теста {reason}: тест убит, прогон остановлен.");
        _running = false;
        KillRuns();
        if (UnityEditor.EditorApplication.isPlaying)
        {
            // Дать callbacks отмены завершиться до domain reload при выходе из Play Mode.
            // Иначе Pipeline может бесконечно переподключать незавершённый async-запрос.
            _pendingPlayModeExitUpdates = 2;
        }
        else
        {
            UnityEditor.EditorApplication.update -= Watch;
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
        private Snapshot(ProcessMemorySnapshot memory, SwapUsage? swap, long native, long managed, long graphics)
        {
            Memory = memory;
            Swap = swap;
            NativeBytes = native;
            ManagedBytes = managed;
            GraphicsBytes = graphics;
        }

        /// <summary>Память процесса целиком — то, что видит система.</summary>
        public ProcessMemorySnapshot Memory { get; }
        public long ProcessBytes => Memory.ProcessBytes;

        /// <summary>Свободная память системы в процентах; null, если узнать нельзя.</summary>
        public int? AvailablePercent => Memory.AvailablePercent;

        /// <summary>Своп системы; null, если узнать нельзя.</summary>
        public SwapUsage? Swap { get; }

        public long NativeBytes { get; }

        public long ManagedBytes { get; }

        public long GraphicsBytes { get; }

        public static Snapshot Take() =>
            new(
                ProcessMemorySnapshot.Capture(),
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
    /// Дополнительная метрика swap; она не считается запасом для аллокаций.
    /// </summary>
    ///
    /// Память процесса и физическая доступность измеряются ProcessMemorySnapshot.
    /// На macOS vm.swapusage остаётся в CSV только для диагностики.
    private static class SystemMemory
    {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        // struct xsw_usage: total, avail, used (uint64), pagesize, encrypted.
        private const int SwapUsageSize = 32;

        private static readonly byte[] _SwapBuffer = new byte[SwapUsageSize];

        [System.Runtime.InteropServices.DllImport("libSystem.dylib", EntryPoint = "sysctlbyname")]
        private static extern int sysctlbynameBytes(string name, byte[] value, ref IntPtr length, IntPtr newValue, IntPtr newLength);

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
        public static SwapUsage? Swap() => null;
#endif
    }
}

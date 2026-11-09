#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

using Kern.Diagnostics.Artifacts;

namespace Kern.Diagnostics.Logging;

// Кольцевой буфер того, что Unity пишет в консоль. Подписка ставится до загрузки
// первой сцены: строки, написанные до появления интерфейса — в том числе
// исключение, оборвавшее стартовый пайплайн, — иначе до плашки не доходили бы,
// а именно их на телефоне без logcat и нужно увидеть.
public static class RuntimeLogCapture
{
    public enum LogKind
    {
        Log,
        Warning,
        Error,
        Exception,
    }

    // Стек нужен только ошибкам: у обычной строки он пустой и не читается.
    public readonly record struct Entry(
        LogKind Kind,
        string Time,
        string Message,
        string StackTrace);

    private const int Capacity = 1000;
    private const int MaxPendingPersistentEntries = 256;
    private const long MaxPersistentLogBytes = 2L * 1024 * 1024;
    private const int PersistentLogFilesToKeep = 3;

    private static readonly object s_gate = new();
    private static readonly Queue<Entry> s_entries = new(Capacity);
    private static readonly ConcurrentQueue<string> s_persistentEntries = new();
    private static string? s_persistentLogPath;
    private static int s_pendingPersistentEntries;
    private static int s_persistentWriterActive;

    private static int s_errorCount;
    private static int s_warningCount;
    private static bool s_installed;

    public static int ErrorCount
    {
        get
        {
            lock (s_gate)
            {
                return s_errorCount;
            }
        }
    }

    public static int WarningCount
    {
        get
        {
            lock (s_gate)
            {
                return s_warningCount;
            }
        }
    }

    public static int Count
    {
        get
        {
            lock (s_gate)
            {
                return s_entries.Count;
            }
        }
    }

    public static string? PersistentLogPath => s_persistentLogPath;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Install()
    {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        s_persistentLogPath = Path.Combine(DiagnosticArtifactPaths.RootDirectory, "mobile_runtime.log");
        Enable();
#endif

        // В обычной сборке буфер не нужен и не должен ловить каждую строку.
        if (Debug.isDebugBuild)
        {
            Enable();
        }
    }

    public static void Enable()
    {
        lock (s_gate)
        {
            if (s_installed)
            {
                return;
            }

            s_installed = true;
        }

        // Потоковый вариант, а не logMessageReceived: часть строк приходит из
        // сокетов и пулов, и на главный поток они бы не попали вовсе.
        Application.logMessageReceivedThreaded += HandleLogMessage;
    }

    public static void Clear()
    {
        lock (s_gate)
        {
            s_entries.Clear();
            s_errorCount = 0;
            s_warningCount = 0;
        }
    }

    public static void CopyTo(List<Entry> destination)
    {
        lock (s_gate)
        {
            destination.Clear();
            destination.AddRange(s_entries);
        }
    }

    private static void HandleLogMessage(string message, string stackTrace, LogType type)
    {
        LogKind kind = ToKind(type);

        // Время снимается здесь, а не в отрисовке: строка может прийти с другого
        // потока, и порядок в буфере обязан совпадать с порядком появления.
        Entry entry = new(
            kind,
            DateTime.Now.ToString("HH:mm:ss.fff"),
            message,
            kind is LogKind.Error or LogKind.Exception ? stackTrace : string.Empty);

        lock (s_gate)
        {
            if (kind is LogKind.Error or LogKind.Exception)
            {
                s_errorCount++;
            }
            else if (kind is LogKind.Warning)
            {
                s_warningCount++;
            }

            while (s_entries.Count >= Capacity)
            {
                Entry removed = s_entries.Dequeue();
                if (removed.Kind is LogKind.Error or LogKind.Exception)
                {
                    s_errorCount--;
                }
                else if (removed.Kind is LogKind.Warning)
                {
                    s_warningCount--;
                }
            }

            s_entries.Enqueue(entry);
        }

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        if (kind is LogKind.Warning or LogKind.Error or LogKind.Exception)
        {
            EnqueuePersistent(entry);
        }
#endif
    }

    private static void EnqueuePersistent(Entry entry)
    {
        string? path = s_persistentLogPath;
        if (path == null)
        {
            return;
        }

        string text = new StringBuilder(entry.Message.Length + entry.StackTrace.Length + 64)
            .Append(DateTime.UtcNow.ToString("O"))
            .Append(" [").Append(entry.Kind).Append("] ")
            .AppendLine(entry.Message)
            .AppendLine(entry.StackTrace)
            .ToString();
        s_persistentEntries.Enqueue(text);
        int pending = Interlocked.Increment(ref s_pendingPersistentEntries);
        while (pending > MaxPendingPersistentEntries && s_persistentEntries.TryDequeue(out _))
        {
            pending = Interlocked.Decrement(ref s_pendingPersistentEntries);
        }

        StartPersistentWriter();
    }

    private static void StartPersistentWriter()
    {
        if (Interlocked.CompareExchange(ref s_persistentWriterActive, 1, 0) == 0)
        {
            _ = Task.Run(FlushPersistentEntries);
        }
    }

    private static void FlushPersistentEntries()
    {
        try
        {
            string? path = s_persistentLogPath;
            if (path == null)
            {
                return;
            }

            var batch = new StringBuilder();
            while (s_persistentEntries.TryDequeue(out string? entry))
            {
                Interlocked.Decrement(ref s_pendingPersistentEntries);
                batch.AppendLine(entry);
            }

            if (batch.Length == 0)
            {
                return;
            }

            string? directory = Path.GetDirectoryName(path);
            if (directory == null)
            {
                return;
            }

            Directory.CreateDirectory(directory);
            RotatePersistentLogIfNeeded(path, batch.Length);
            File.AppendAllText(path, batch.ToString(), new UTF8Encoding(false));
        }
        catch (IOException)
        {
            // Logging must not recursively report its own storage failure.
        }
        catch (UnauthorizedAccessException)
        {
            // Logging must not recursively report its own storage failure.
        }
        finally
        {
            Interlocked.Exchange(ref s_persistentWriterActive, 0);
            if (!s_persistentEntries.IsEmpty)
            {
                StartPersistentWriter();
            }
        }
    }

    private static void RotatePersistentLogIfNeeded(string path, int incomingBytes)
    {
        if (!File.Exists(path) || new FileInfo(path).Length + incomingBytes <= MaxPersistentLogBytes)
        {
            return;
        }

        string oldestPath = $"{path}.{PersistentLogFilesToKeep - 1}";
        if (File.Exists(oldestPath))
        {
            File.Delete(oldestPath);
        }

        for (int index = PersistentLogFilesToKeep - 2; index >= 1; index--)
        {
            string sourcePath = $"{path}.{index}";
            if (File.Exists(sourcePath))
            {
                File.Move(sourcePath, $"{path}.{index + 1}");
            }
        }

        File.Move(path, $"{path}.1");
    }

    private static LogKind ToKind(LogType type) => type switch
    {
        LogType.Error or LogType.Assert => LogKind.Error,
        LogType.Exception => LogKind.Exception,
        LogType.Warning => LogKind.Warning,
        _ => LogKind.Log,
    };
}

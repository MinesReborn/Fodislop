#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using Kern.Core.Interfaces.Diagnostics;


namespace Kern.Diagnostics.Artifacts;

/// <summary>
/// Один выход для текстовых отчётов диагностики: общий заголовок, файл в
/// <see cref="DiagnosticArtifactPaths"/> и одна строка в логе «куда записано».
/// </summary>
///
/// Раньше каждый отчёт писал по-своему: провис — только в лог, разбор кадра —
/// только в буфер обмена, свет — в свою папку со своей ротацией, тесты — каждый
/// своим текстом. Сравнить два отчёта одного прогона было нельзя: у одного нет
/// экрана и устройства, у другого нет времени. Теперь заголовок один и тот же,
/// а в логе у каждого файла одна метка — <c>[Diag]</c>.
///
/// Дописываемые отчёты (сеансовые файлы) идут через фонового писателя. Отчёт
/// о провисе печатается сразу после тяжёлого кадра, и синхронный
/// <c>File.AppendAllText</c> в том же кадре сам становился следующим провисом:
/// отчёт создавал провис, который затем снова попадал в отчёт. Очередь
/// сохраняет порядок записей, а главный поток только формирует текст.
/// Разовые отчёты пишутся сразу: их зовут по действию пользователя или в
/// конце теста, и ждать фоновой записи там нельзя.
public static class DiagnosticReport
{
    public const string LogTag = "[Diag]";

    private static readonly UTF8Encoding s_utf8 = new(false);
    private static readonly ConcurrentQueue<PendingWrite> s_pendingWrites = new();
    private static readonly HashSet<string> s_sessionFilesStarted = new(StringComparer.Ordinal);
    private static int s_writerRunning;

    private readonly record struct PendingWrite(string Path, string Text, string Kind);

    /// <summary>Шапка отчёта: что это, когда, где и на чём.</summary>
    public static string Header(string kind)
    {
        var text = new StringBuilder(256);
        text.Append("# Kern · ").Append(kind).Append(" · ")
            .AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        text.Append("# ").Append(Application.isEditor ? "редактор" : "сборка")
            .Append(" · Unity ").Append(Application.unityVersion)
            .Append(" · версия ").Append(Application.version)
            .Append(" · ").Append(SystemInfo.graphicsDeviceType)
            .Append(" · ").Append(SystemInfo.graphicsDeviceName)
            .Append(" · ").Append(Screen.width).Append('×').Append(Screen.height)
            .AppendLine();
        return text.ToString();
    }

    /// <summary>Отдельный файл отчёта. Возвращает путь или null, если записать не удалось.</summary>
    public static string? Write(string category, string name, string kind, string body)
    {
        try
        {
            string path = DiagnosticArtifactPaths.CreatePath(category, name, "txt");
            File.WriteAllText(path, Header(kind) + Environment.NewLine + body, s_utf8);
            Announce(kind, path);
            return path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Отчёт — не повод ронять игру или прогон.
            Debug.LogWarning($"{LogTag} {kind}: отчёт не записан: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// Дописать запись в файл сеанса. Шапка пишется один раз, путь в лог —
    /// тоже один раз, при создании файла.
    /// </summary>
    public static string? Append(string category, string name, string kind, string entry)
    {
        try
        {
            string path = DiagnosticArtifactPaths.SessionPath(category, name, "txt");
            bool created = s_sessionFilesStarted.Add(path);
            Enqueue(new PendingWrite(
                path,
                (created ? Header(kind) : string.Empty) + Environment.NewLine + entry + Environment.NewLine,
                kind));
            if (created)
            {
                Announce(kind, path);
            }

            return path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Debug.LogWarning($"{LogTag} {kind}: запись не дописана: {exception.Message}");
            return null;
        }
    }

    /// <summary>Для файлов со своим форматом (csv, tsv, json, png): только строка в лог.</summary>
    public static void Announce(string kind, string path) =>
        Debug.Log($"{LogTag} {kind} → {path}");

    private static void Enqueue(PendingWrite write)
    {
        s_pendingWrites.Enqueue(write);
        StartWriter();
    }

    private static void StartWriter()
    {
        if (Interlocked.CompareExchange(ref s_writerRunning, 1, 0) == 0)
        {
            ThreadPool.QueueUserWorkItem(static _ => DrainWrites());
        }
    }

    private static void DrainWrites()
    {
        try
        {
            while (s_pendingWrites.TryDequeue(out PendingWrite write))
            {
                WriteFile(write);
            }
        }
        finally
        {
            Volatile.Write(ref s_writerRunning, 0);

            // Запись могли добавить между опустошением очереди и сбросом флага.
            if (!s_pendingWrites.IsEmpty)
            {
                StartWriter();
            }
        }
    }

    private static void WriteFile(in PendingWrite write)
    {
        try
        {
            File.AppendAllText(write.Path, write.Text, s_utf8);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Debug.LogWarning($"{LogTag} {write.Kind}: отчёт не записан: {exception.Message}");
        }
    }
}

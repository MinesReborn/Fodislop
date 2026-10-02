#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Kern.Core.Interfaces.Diagnostics;

/// <summary>Единый каталог для файлов диагностики клиента и тестов.</summary>
///
/// Лежит в контрактах, потому что пишут сюда все слои: свет из Kern.World,
/// окна инструментов из Kern.Presentation, тесты. Файл получает имя
/// «имя_метка.расширение», каталог вида держит не больше
/// <see cref="DefaultRetained"/> последних записей — иначе дампы копятся
/// сессиями и в них уже не найти нужный.
public static class DiagnosticArtifactPaths
{
    public const int DefaultRetained = 20;

    private const string EditorDirectoryName = "Logs";
    private const string ArtifactDirectoryName = "Diagnostics";
    private const string StampFormat = "yyyyMMdd_HHmmss";

    // Одна метка на процесс: файлы, которые дописываются весь сеанс, делят её
    // и лежат рядом по имени.
    private static readonly string s_sessionStamp = DateTime.Now.ToString(StampFormat);

    public static string RootDirectory => Application.isEditor
        ? Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", EditorDirectoryName, ArtifactDirectoryName)
        : Path.Combine(Application.persistentDataPath, ArtifactDirectoryName);

    public static string GetDirectory(string category)
    {
        string directory = Path.Combine(RootDirectory, category);
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>Новый файл с меткой времени; старые записи вида подчищаются.</summary>
    public static string CreatePath(string category, string name, string extension, int retained = DefaultRetained)
    {
        string directory = GetDirectory(category);
        Prune(directory, retained - 1);
        return Path.Combine(directory, $"{name}_{DateTime.Now.ToString(StampFormat)}.{extension}");
    }

    /// <summary>Новый каталог с меткой времени — для дампов из нескольких файлов.</summary>
    public static string CreateDirectory(string category, string name, int retained = DefaultRetained)
    {
        string directory = GetDirectory(category);
        Prune(directory, retained - 1);
        string path = Path.Combine(directory, $"{name}_{DateTime.Now.ToString(StampFormat)}");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Файл сеанса: одно имя на весь запуск, в него дописывают.</summary>
    public static string SessionPath(string category, string name, string extension, int retained = DefaultRetained)
    {
        string directory = GetDirectory(category);
        string path = Path.Combine(directory, $"{name}_{s_sessionStamp}.{extension}");
        if (!File.Exists(path))
        {
            Prune(directory, retained - 1);
        }

        return path;
    }

    private static void Prune(string directory, int keep)
    {
        var entries = new List<FileSystemInfo>(new DirectoryInfo(directory).GetFileSystemInfos());
        if (entries.Count <= keep)
        {
            return;
        }

        entries.Sort(static (left, right) => left.CreationTimeUtc.CompareTo(right.CreationTimeUtc));
        int excess = entries.Count - Math.Max(0, keep);
        for (int index = 0; index < excess; index++)
        {
            FileSystemInfo entry = entries[index];
            try
            {
                if (entry is DirectoryInfo folder)
                {
                    folder.Delete(recursive: true);
                }
                else
                {
                    entry.Delete();
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Debug.LogWarning($"[Diag] Старая запись не удалена: {entry.FullName}: {exception.Message}");
            }
        }
    }
}

#nullable enable

using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace MinesServer.Networking.Connection.Client;

// Карта офлайн-сервера лежит в StreamingAssets/WorldMaps как <мир>_cells.zip
// (~78 МБ), внутри — <мир>_cells.mapb (~300 МБ). Распакованная карта живёт в
// persistentDataPath: temporaryCachePath система чистит при нехватке места, и
// тогда каждый вход в мир распаковывал бы её заново.
internal static class DummyWorldMapArchive
{
    internal const string CacheFolderName = "DummyServerMaps";
    private const string StreamingFolderName = "WorldMaps";
    private const string StampSuffix = ".stamp";
    private const string TempSuffix = ".tmp";
    private const long FreeSpaceMarginBytes = 64L * 1024 * 1024;
    private static readonly TimeSpan _AbandonedTempAge = TimeSpan.FromHours(1);

    public static async UniTask<string> ResolveMapFileAsync(string worldCodeName, CancellationToken cancellationToken)
    {
        // Application.* доступны только с главного потока: читаются до ухода в пул.
        string streamingRoot = Application.streamingAssetsPath;
        string streamingDirectory = Path.Combine(streamingRoot, StreamingFolderName);
        string cacheDirectory = Path.Combine(Application.persistentDataPath, CacheFolderName);
        string buildGuid = Application.buildGUID;

        string mapPath = IsLocalPath(streamingRoot)
            ? await UniTask.RunOnThreadPool(
                () => ResolveFromLocalStreamingAssets(streamingDirectory, cacheDirectory, worldCodeName),
                cancellationToken: cancellationToken)
            : await ResolveFromPackagedStreamingAssetsAsync(
                streamingDirectory,
                cacheDirectory,
                worldCodeName,
                buildGuid,
                cancellationToken);

        return mapPath;
    }

    internal static string ExtractIfStale(
        string archivePath,
        string cacheDirectory,
        string worldCodeName,
        string sourceStamp)
    {
        Directory.CreateDirectory(cacheDirectory);
        DeleteAbandonedTempFiles(cacheDirectory);

        string mapPath = Path.Combine(cacheDirectory, MapFileName(worldCodeName));
        if (IsCacheCurrent(mapPath, sourceStamp))
        {
            return mapPath;
        }

        using ZipArchive archive = OpenArchive(archivePath);
        ZipArchiveEntry entry = archive.GetEntry(MapFileName(worldCodeName)) ??
            throw new InvalidDataException(
                $"Dummy server archive '{archivePath}' does not contain '{MapFileName(worldCodeName)}'.");
        EnsureFreeSpace(cacheDirectory, entry.Length);

        // Отметка снимается до замены карты: сбой между шагами оставит карту без
        // отметки, и следующий запуск распакует её заново, а не поверит ей.
        string stampPath = StampPath(mapPath);
        if (File.Exists(stampPath))
        {
            File.Delete(stampPath);
        }

        // Уникальное имя: два процесса (два редактора, редактор и билд) пишут
        // каждый в свой файл, а на место встаёт целиком записанный.
        string tempPath = NewTempPath(mapPath);
        try
        {
            entry.ExtractToFile(tempPath, overwrite: false);
            long written = new FileInfo(tempPath).Length;
            if (written != entry.Length)
            {
                throw new InvalidDataException(
                    $"Dummy server map '{worldCodeName}' extracted {written} of {entry.Length} bytes.");
            }

            ReplaceFile(tempPath, mapPath);
            WriteStamp(mapPath, sourceStamp);
        }
        catch (IOException exception)
        {
            throw new IOException(
                $"Could not extract dummy server map '{worldCodeName}' to '{cacheDirectory}': {exception.Message}",
                exception);
        }
        finally
        {
            DeleteFileQuietly(tempPath);
        }

        return mapPath;
    }

    internal static bool IsCacheCurrent(string mapPath, string sourceStamp)
    {
        string stampPath = StampPath(mapPath);
        try
        {
            return File.Exists(mapPath) &&
                File.Exists(stampPath) &&
                string.Equals(File.ReadAllText(stampPath), sourceStamp, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static bool HasValidHeader(string mapPath)
    {
        try
        {
            ReadDimensions(mapPath);
            return true;
        }
        catch (InvalidDataException exception)
        {
            // Недочитанный заголовок — порча. Прочие ошибки ввода-вывода (файл
            // занят соседним процессом) о содержимом ничего не говорят: их
            // переживает повторное чтение в ReadDimensionsWithRetryAsync.
            return exception.InnerException is IOException and not EndOfStreamException;
        }
    }

    public static (int width, int height) ReadDimensions(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);
            int widthChunks = reader.ReadInt32();
            int heightChunks = reader.ReadInt32();
            int chunkSize = reader.ReadInt32();
            reader.ReadInt32();

            if (widthChunks > 0 && heightChunks > 0 && chunkSize > 0 && chunkSize <= 1024)
            {
                return (widthChunks * chunkSize, heightChunks * chunkSize);
            }

            throw new InvalidDataException(
                $"Dummy map '{path}' has invalid header ({widthChunks}x{heightChunks}, chunk {chunkSize}).");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            throw new InvalidDataException($"Failed to read dummy map header '{path}'.", ex);
        }
    }

    public static async UniTask<(int width, int height)> ReadDimensionsWithRetryAsync(string path)
    {
        const int maxAttempts = 10;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return ReadDimensions(path);
            }
            catch (InvalidDataException ex) when (
                attempt < maxAttempts - 1 &&
                ex.InnerException is IOException)
            {
                await UniTask.Delay(25 * (attempt + 1));
            }
        }
    }

    private static string ResolveFromLocalStreamingAssets(
        string streamingDirectory,
        string cacheDirectory,
        string worldCodeName)
    {
        string projectMapPath = Path.Combine(streamingDirectory, MapFileName(worldCodeName));
        if (File.Exists(projectMapPath))
        {
            ReadDimensions(projectMapPath);
            return projectMapPath;
        }

        string archivePath = Path.Combine(streamingDirectory, ArchiveFileName(worldCodeName));
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException(
                $"Dummy server map '{worldCodeName}' is missing both '{projectMapPath}' and '{archivePath}'.",
                archivePath);
        }

        // Отметка — длина и время записи самого архива, сравниваемые с ними же:
        // обновлённый архив той же длины распакуется заново, а сравнения
        // времени файла со временем записи внутри zip (разные зоны) здесь нет.
        var archive = new FileInfo(archivePath);
        string sourceStamp = $"file:{archive.Length}:{archive.LastWriteTimeUtc.Ticks}";
        return ExtractValidated(archivePath, cacheDirectory, worldCodeName, sourceStamp);
    }

    private static async UniTask<string> ResolveFromPackagedStreamingAssetsAsync(
        string streamingDirectory,
        string cacheDirectory,
        string worldCodeName,
        string buildGuid,
        CancellationToken cancellationToken)
    {
        // Android: StreamingAssets лежат внутри APK, File.* их не видит. Архив
        // копируется из пакета только если распакованная карта не от этой сборки.
        string sourceStamp = $"package:{buildGuid}";
        string mapPath = Path.Combine(cacheDirectory, MapFileName(worldCodeName));
        bool current = await UniTask.RunOnThreadPool(
            () =>
            {
                Directory.CreateDirectory(cacheDirectory);
                return IsCacheCurrent(mapPath, sourceStamp) && HasValidHeader(mapPath);
            },
            cancellationToken: cancellationToken);
        if (current)
        {
            return mapPath;
        }

        string archiveCopy = Path.Combine(cacheDirectory, ArchiveFileName(worldCodeName));
        await CopyPackagedFileAsync($"{streamingDirectory}/{ArchiveFileName(worldCodeName)}", archiveCopy, cancellationToken);
        try
        {
            return await UniTask.RunOnThreadPool(
                () => ExtractValidated(archiveCopy, cacheDirectory, worldCodeName, sourceStamp),
                cancellationToken: cancellationToken);
        }
        finally
        {
            DeleteFileQuietly(archiveCopy);
        }
    }

    private static string ExtractValidated(
        string archivePath,
        string cacheDirectory,
        string worldCodeName,
        string sourceStamp)
    {
        string mapPath = ExtractIfStale(archivePath, cacheDirectory, worldCodeName, sourceStamp);
        if (HasValidHeader(mapPath))
        {
            return mapPath;
        }

        // Отметка совпала, а заголовок испорчен: файл повреждён уже после
        // распаковки. Одна повторная распаковка; вторая порча — отказ.
        InvalidateCache(mapPath);
        mapPath = ExtractIfStale(archivePath, cacheDirectory, worldCodeName, sourceStamp);
        ReadDimensions(mapPath);
        return mapPath;
    }

    private static async UniTask CopyPackagedFileAsync(
        string uri,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        string tempPath = NewTempPath(destinationPath);
        try
        {
            using (var request = new UnityWebRequest(uri, UnityWebRequest.kHttpVerbGET))
            {
                request.downloadHandler = new DownloadHandlerFile(tempPath) { removeFileOnAbort = true };
                await request.SendWebRequest().WithCancellation(cancellationToken);
            }

            ReplaceFile(tempPath, destinationPath);
        }
        catch (UnityWebRequestException exception)
        {
            throw new FileNotFoundException(
                $"Packaged dummy server archive '{uri}' could not be read: {exception.Error}",
                uri,
                exception);
        }
        finally
        {
            DeleteFileQuietly(tempPath);
        }
    }

    private static ZipArchive OpenArchive(string archivePath)
    {
        try
        {
            return ZipFile.OpenRead(archivePath);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException(
                $"Dummy server archive '{archivePath}' is corrupt: {exception.Message}",
                exception);
        }
    }

    private static void EnsureFreeSpace(string directory, long requiredBytes)
    {
        long availableBytes;
        try
        {
            availableBytes = new DriveInfo(Path.GetFullPath(directory)).AvailableFreeSpace;
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Размер тома узнать нельзя — нехватку места покажет сама запись.
            return;
        }

        if (availableBytes < requiredBytes + FreeSpaceMarginBytes)
        {
            throw new IOException(
                $"Not enough disk space for the dummy server map in '{directory}': " +
                $"needs {ToMegabytes(requiredBytes + FreeSpaceMarginBytes)} MB, " +
                $"available {ToMegabytes(availableBytes)} MB.");
        }
    }

    private static void InvalidateCache(string mapPath)
    {
        string stampPath = StampPath(mapPath);
        if (File.Exists(stampPath))
        {
            File.Delete(stampPath);
        }

        if (File.Exists(mapPath))
        {
            File.Delete(mapPath);
        }
    }

    private static void WriteStamp(string mapPath, string sourceStamp)
    {
        string stampPath = StampPath(mapPath);
        string tempPath = NewTempPath(stampPath);
        try
        {
            File.WriteAllText(tempPath, sourceStamp);
            ReplaceFile(tempPath, stampPath);
        }
        finally
        {
            DeleteFileQuietly(tempPath);
        }
    }

    private static void ReplaceFile(string sourcePath, string destinationPath)
    {
        // В netstandard2.1 нет File.Move(..., overwrite); rename в пределах тома атомарен.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }

                File.Move(sourcePath, destinationPath);
                return;
            }
            catch (IOException) when (attempt < 2)
            {
                // Между удалением и переносом цель записал соседний процесс
                // из того же источника — повторяем.
            }
        }
    }

    private static void DeleteAbandonedTempFiles(string cacheDirectory)
    {
        DateTime threshold = DateTime.UtcNow - _AbandonedTempAge;
        foreach (string tempPath in Directory.GetFiles(cacheDirectory, "*" + TempSuffix))
        {
            // Свежий временный файл может дописывать соседний процесс.
            if (File.GetLastWriteTimeUtc(tempPath) < threshold)
            {
                DeleteFileQuietly(tempPath);
            }
        }
    }

    private static void DeleteFileQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Брошенный файл подберёт DeleteAbandonedTempFiles.
        }
    }

    private static bool IsLocalPath(string path) => !path.Contains("://", StringComparison.Ordinal);

    private static string MapFileName(string worldCodeName) => $"{worldCodeName}_cells.mapb";

    private static string ArchiveFileName(string worldCodeName) => $"{worldCodeName}_cells.zip";

    private static string StampPath(string mapPath) => mapPath + StampSuffix;

    private static string NewTempPath(string path) => $"{path}.{Guid.NewGuid():N}{TempSuffix}";

    private static long ToMegabytes(long bytes) => bytes / (1024 * 1024);
}

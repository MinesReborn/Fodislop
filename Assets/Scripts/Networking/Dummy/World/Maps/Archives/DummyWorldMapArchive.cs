#nullable enable

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Persistence;
using UnityEngine;
using UnityEngine.Networking;

namespace Kern.Networking.Dummy;

// Карта офлайн-сервера лежит в StreamingAssets/WorldMaps как <мир>_cells.zip
// (~78 МБ), внутри — <мир>_cells.mapb (~300 МБ). Распакованная карта живёт в
// persistentDataPath: temporaryCachePath система чистит при нехватке места, и
// тогда каждый вход в мир распаковывал бы её заново.
internal static class DummyWorldMapArchive
{
    internal const string CacheFolderName = "DummyServerMaps";

    // internal: окно диагностики собирает тот же путь к источнику карты.
    internal const string StreamingFolderName = "WorldMaps";
    internal const long FreeSpaceMarginBytes = 64L * 1024 * 1024;
    internal static readonly TimeSpan s_abandonedTempAge = TimeSpan.FromHours(1);

    public static async UniTask<string> ResolveMapFileAsync(string worldCodeName, CancellationToken cancellationToken)
    {
        // Application.* доступны только с главного потока: читаются до ухода в пул.
        string streamingRoot = Application.streamingAssetsPath;
        bool isLocalStreamingAssets = IsLocalPath(streamingRoot);
        string streamingDirectory = isLocalStreamingAssets
            ? Path.Combine(streamingRoot, StreamingFolderName)
            : CombineStreamingUri(streamingRoot, StreamingFolderName);
        string cacheDirectory = Path.Combine(Application.persistentDataPath, CacheFolderName);
        Debug.Log($"[DummyWorldMapArchive] Resolving '{worldCodeName}' map; cache='{cacheDirectory}'.");

        string mapPath = isLocalStreamingAssets
            ? await UniTask.RunOnThreadPool(
                () => ResolveFromLocalStreamingAssets(streamingDirectory, cacheDirectory, worldCodeName),
                cancellationToken: cancellationToken)
            : await ResolveFromPackagedStreamingAssetsAsync(
                streamingDirectory,
                cacheDirectory,
                worldCodeName,
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
        DummyMapArchiveFiles.DeleteAbandonedTempFiles(cacheDirectory);

        string mapPath = Path.Combine(cacheDirectory, MapFileName(worldCodeName));
        if (IsCacheCurrent(mapPath, sourceStamp))
        {
            return mapPath;
        }

        using ZipArchive archive = OpenArchive(archivePath);
        ZipArchiveEntry entry = archive.GetEntry(MapFileName(worldCodeName)) ??
            throw new InvalidDataException(
                $"Dummy server archive '{archivePath}' does not contain '{MapFileName(worldCodeName)}'.");
        DummyMapArchiveFiles.EnsureFreeSpace(cacheDirectory, entry.Length);

        // Отметка снимается до замены карты: сбой между шагами оставит карту без
        // отметки, и следующий запуск распакует её заново, а не поверит ей.
        string stampPath = DummyMapArchiveFiles.StampPath(mapPath);
        if (File.Exists(stampPath))
        {
            File.Delete(stampPath);
        }

        // Уникальное имя: два процесса (два редактора, редактор и билд) пишут
        // каждый в свой файл, а на место встаёт целиком записанный.
        string tempPath = DummyMapArchiveFiles.NewTempPath(mapPath);
        try
        {
            entry.ExtractToFile(tempPath, overwrite: false);
            long written = new FileInfo(tempPath).Length;
            if (written != entry.Length)
            {
                throw new InvalidDataException(
                    $"Dummy server map '{worldCodeName}' extracted {written} of {entry.Length} bytes.");
            }

            DummyMapArchiveFiles.ReplaceFile(tempPath, mapPath);
            DummyMapArchiveFiles.WriteStamp(mapPath, sourceStamp);
        }
        catch (IOException exception)
        {
            throw new IOException(
                $"Could not extract dummy server map '{worldCodeName}' to '{cacheDirectory}': {exception.Message}",
                exception);
        }
        finally
        {
            DummyMapArchiveFiles.DeleteFileQuietly(tempPath);
        }

        return mapPath;
    }

    internal static bool IsCacheCurrent(string mapPath, string sourceStamp)
    {
        string stampPath = DummyMapArchiveFiles.StampPath(mapPath);
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
            int formatVersion = reader.ReadInt32();

            if (widthChunks > 0 && heightChunks > 0 && chunkSize == WorldGridDefaults.ChunkSize)
            {
                long worldWidth = (long)widthChunks * chunkSize;
                long worldHeight = (long)heightChunks * chunkSize;
                if (worldWidth <= ushort.MaxValue && worldHeight <= ushort.MaxValue)
                {
                    if (formatVersion < 0 || formatVersion > WorldLayerFileHeader.CurrentFormatVersion)
                    {
                        throw new InvalidDataException(
                            $"Dummy map '{path}' uses unsupported format version {formatVersion}.");
                    }

                    long chunkCount = (long)widthChunks * heightChunks;
                    long offsetTableEnd = checked(
                        WorldLayerFileHeader.HeaderSize + (chunkCount * sizeof(long)));
                    if (stream.Length < offsetTableEnd)
                    {
                        throw new InvalidDataException(
                            $"Dummy map '{path}' has a truncated chunk offset table.");
                    }

                    stream.Seek(WorldLayerFileHeader.HeaderSize, SeekOrigin.Begin);
                    for (long index = 0; index < chunkCount; index++)
                    {
                        long offset = reader.ReadInt64();
                        if (offset != -1 && (offset < offsetTableEnd || offset >= stream.Length))
                        {
                            throw new InvalidDataException(
                                $"Dummy map '{path}' has an invalid chunk offset at index {index}.");
                        }
                    }

                    return ((int)worldWidth, (int)worldHeight);
                }
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
                // ReadDimensions validates every entry in the offset table. For
                // Pallada that is hundreds of thousands of file reads; never do
                // this validation on Unity's main thread during world entry.
                return await UniTask.RunOnThreadPool(() => ReadDimensions(path));
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
            return CopyLocalMapToCache(projectMapPath, cacheDirectory, worldCodeName);
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

    internal static string CopyLocalMapToCache(
        string sourcePath,
        string cacheDirectory,
        string worldCodeName)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Source map path is required.", nameof(sourcePath));
        }

        if (string.IsNullOrWhiteSpace(cacheDirectory))
        {
            throw new ArgumentException("Map cache directory is required.", nameof(cacheDirectory));
        }

        if (string.IsNullOrWhiteSpace(worldCodeName))
        {
            throw new ArgumentException("World code name is required.", nameof(worldCodeName));
        }

        ReadDimensions(sourcePath);
        Directory.CreateDirectory(cacheDirectory);
        DummyMapArchiveFiles.DeleteAbandonedTempFiles(cacheDirectory);

        var sourceInfo = new FileInfo(sourcePath);
        long sourceLength = sourceInfo.Length;
        long sourceLastWriteTicks = sourceInfo.LastWriteTimeUtc.Ticks;
        string sourceStamp = $"local:{sourceInfo.FullName}:{sourceLength}:{sourceLastWriteTicks}";
        string mapPath = Path.Combine(cacheDirectory, MapFileName(worldCodeName));
        if (IsCacheCurrent(mapPath, sourceStamp) && HasValidHeader(mapPath))
        {
            return mapPath;
        }

        DummyMapArchiveFiles.EnsureFreeSpace(cacheDirectory, sourceLength);
        string tempPath = DummyMapArchiveFiles.NewTempPath(mapPath);
        try
        {
            using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var destination = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(destination);
                destination.Flush(flushToDisk: true);
            }

            var copiedInfo = new FileInfo(tempPath);
            sourceInfo.Refresh();
            if (copiedInfo.Length != sourceLength ||
                sourceInfo.Length != sourceLength ||
                sourceInfo.LastWriteTimeUtc.Ticks != sourceLastWriteTicks)
            {
                throw new IOException($"Source map '{sourcePath}' changed while it was being copied.");
            }

            ReadDimensions(tempPath);
            DummyMapArchiveFiles.ReplaceFile(tempPath, mapPath);
            DummyMapArchiveFiles.WriteStamp(mapPath, sourceStamp);
        }
        catch (IOException exception)
        {
            throw new IOException(
                $"Could not copy local dummy map '{sourcePath}' to '{cacheDirectory}': {exception.Message}",
                exception);
        }
        finally
        {
            DummyMapArchiveFiles.DeleteFileQuietly(tempPath);
        }

        return mapPath;
    }

    private static async UniTask<string> ResolveFromPackagedStreamingAssetsAsync(
        string streamingDirectory,
        string cacheDirectory,
        string worldCodeName,
        CancellationToken cancellationToken)
    {
        // Android: StreamingAssets лежат внутри APK, File.* их не видит. Архив
        // копируется из пакета только если распакованная карта не от этой сборки.
        string sourceStamp;
        string archiveName = ArchiveFileName(worldCodeName);
        string manifest = await DownloadPackagedTextAsync(
            $"{streamingDirectory}/{StreamingFolderName}.manifest",
            cancellationToken);
        string? archiveHash = null;
        foreach (string line in manifest.Split('\n'))
        {
            string[] fields = line.TrimEnd('\r').Split('|');
            if (fields.Length == 2 && string.Equals(fields[0], archiveName, StringComparison.Ordinal))
            {
                archiveHash = fields[1];
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(archiveHash))
        {
            throw new InvalidDataException(
                $"Packaged world-map manifest does not contain SHA-256 for '{archiveName}'.");
        }

        sourceStamp = $"package:{archiveHash}";
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
            Debug.Log($"[DummyWorldMapArchive] Reusing cached map '{mapPath}' for archive SHA-256 {archiveHash}.");
            return mapPath;
        }

        Debug.Log($"[DummyWorldMapArchive] Cache miss for '{worldCodeName}'; copying packaged archive '{archiveName}'.");
        string archiveCopy = Path.Combine(cacheDirectory, archiveName);
        await CopyPackagedFileAsync($"{streamingDirectory}/{archiveName}", archiveCopy, cancellationToken);
        try
        {
            string extractedPath = await UniTask.RunOnThreadPool(
                () => ExtractValidated(archiveCopy, cacheDirectory, worldCodeName, sourceStamp),
                cancellationToken: cancellationToken);
            Debug.Log($"[DummyWorldMapArchive] Map '{worldCodeName}' extracted and validated at '{extractedPath}'.");
            return extractedPath;
        }
        finally
        {
            DummyMapArchiveFiles.DeleteFileQuietly(archiveCopy);
        }
    }

    private static async UniTask<string> DownloadPackagedTextAsync(
        string uri,
        CancellationToken cancellationToken)
    {
        using var request = UnityWebRequest.Get(uri);
        await request.SendWebRequest().WithCancellation(cancellationToken);
        return request.downloadHandler.text;
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
        DummyMapArchiveFiles.InvalidateCache(mapPath);
        mapPath = ExtractIfStale(archivePath, cacheDirectory, worldCodeName, sourceStamp);
        ReadDimensions(mapPath);
        return mapPath;
    }

    private static async UniTask CopyPackagedFileAsync(
        string uri,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        string tempPath = DummyMapArchiveFiles.NewTempPath(destinationPath);
        try
        {
            using (var request = new UnityWebRequest(uri, UnityWebRequest.kHttpVerbGET))
            {
                request.downloadHandler = new DownloadHandlerFile(tempPath) { removeFileOnAbort = true };
                await request.SendWebRequest().WithCancellation(cancellationToken);
            }

            DummyMapArchiveFiles.ReplaceFile(tempPath, destinationPath);
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
            DummyMapArchiveFiles.DeleteFileQuietly(tempPath);
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

    private static bool IsLocalPath(string path) => !path.Contains("://", StringComparison.Ordinal);

    internal static string CombineStreamingUri(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("StreamingAssets URI root is required.", nameof(root));
        }

        if (string.IsNullOrWhiteSpace(relativePath) || relativePath[0] is '/' or '\\')
        {
            throw new ArgumentException(
                "StreamingAssets URI path must be relative.",
                nameof(relativePath));
        }

        string[] segments = relativePath.Replace('\\', '/').Split('/');
        if (segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment) ||
                segment is "." or ".."))
        {
            throw new ArgumentException(
                $"StreamingAssets URI path contains an invalid segment: '{relativePath}'.",
                nameof(relativePath));
        }

        string escapedPath = string.Join("/", segments.Select(Uri.EscapeDataString));
        return $"{root.TrimEnd('/')}/{escapedPath}";
    }

    internal static string MapFileName(string worldCodeName) => $"{worldCodeName}_cells.mapb";

    internal static string ArchiveFileName(string worldCodeName) => $"{worldCodeName}_cells.zip";
}

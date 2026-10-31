#nullable enable

using System;
using System.IO;
using Cysharp.Threading.Tasks;

namespace Kern.AssetPipeline.Cache;

internal static class PersistentAssetCacheFiles
{
    public static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public static void Touch(string assetPath)
    {
        try
        {
            File.SetLastAccessTimeUtc(assetPath, DateTime.UtcNow);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Access timestamps are only an eviction hint.
        }
    }

    public static void WriteAtomically(string path, byte[] data)
    {
        CreateParentDirectory(path);
        string temporaryPath = GetTemporaryPath(path);
        try
        {
            File.WriteAllBytes(temporaryPath, data);
            ReplaceFile(temporaryPath, path);
        }
        finally
        {
            DeleteIfExists(temporaryPath);
        }
    }

    public static async UniTask WriteAtomicallyAsync(string path, byte[] data)
    {
        CreateParentDirectory(path);
        string temporaryPath = GetTemporaryPath(path);
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, data).AsUniTask();
            ReplaceFile(temporaryPath, path);
        }
        finally
        {
            DeleteIfExists(temporaryPath);
        }
    }

    public static void ReplaceFile(string temporaryPath, string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            File.Replace(temporaryPath, destinationPath, null);
            return;
        }

        File.Move(temporaryPath, destinationPath);
    }

    private static string GetTemporaryPath(string path) => path + ".tmp-" + Guid.NewGuid().ToString("N");

    private static void CreateParentDirectory(string path)
    {
        string? parentPath = Path.GetDirectoryName(path);
        if (parentPath == null)
        {
            throw new InvalidOperationException($"Cache file path has no parent directory: '{path}'.");
        }

        Directory.CreateDirectory(parentPath);
    }
}

#nullable enable

using System;
using System.IO;
using UnityEngine;

namespace Kern.AssetPipeline.Cache;

internal static class PersistentAssetCachePaths
{
    private const string AssetsDirectoryName = "Assets";

    public static string GetDefaultCachePath()
    {
        string persistentPath = Application.persistentDataPath;
        if (string.IsNullOrWhiteSpace(persistentPath))
        {
            throw new InvalidOperationException(
                "Application.persistentDataPath is required for the persistent asset cache.");
        }

        string? parentPath = Path.GetDirectoryName(persistentPath);
        if (string.IsNullOrWhiteSpace(parentPath) || !Directory.Exists(parentPath))
        {
            throw new DirectoryNotFoundException(
                $"Persistent data parent directory '{parentPath}' does not exist.");
        }

        return Path.Combine(persistentPath, AssetsDirectoryName);
    }

    public static string GetAssetPath(string cachePath, string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            throw new ArgumentException("Asset filename cannot be empty.", nameof(filename));
        }

        if (Path.IsPathRooted(filename) || filename.Contains('\\'))
        {
            throw new ArgumentException("Asset filename must be a relative path using forward slashes.", nameof(filename));
        }

        string[] segments = filename.Split('/');
        if (Array.Exists(segments, segment =>
                string.IsNullOrWhiteSpace(segment) ||
                segment == "." ||
                segment == ".."))
        {
            throw new ArgumentException("Asset filename contains an invalid path segment.", nameof(filename));
        }

        string fullPath = Path.GetFullPath(Path.Combine(cachePath, filename));
        string relativePath = Path.GetRelativePath(cachePath, fullPath);
        if (relativePath == ".." ||
            relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("Asset filename escapes the persistent cache directory.", nameof(filename));
        }

        return fullPath;
    }
}

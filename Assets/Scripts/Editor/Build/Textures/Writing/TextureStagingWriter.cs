#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kern.AssetPipeline.Loading;

namespace Kern.Editor;

public static class TextureStagingWriter
{
    public static void CopyRuntimeFiles(
        string source,
        string destination,
        IReadOnlyDictionary<string, string> catalog)
    {
        ValidateCatalogCoverage(source, catalog);
        CopyCatalogFiles(source, destination, catalog);
    }

    public static void CopyCatalogFiles(
        string source,
        string destination,
        IReadOnlyDictionary<string, string> catalog)
    {
        var publishedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> entry in catalog)
        {
            if (!TextureDeliveryCatalog.IsRelativeAssetPath(entry.Key) ||
                !TextureDeliveryCatalog.IsRelativeAssetPath(entry.Value) ||
                !publishedPaths.Add(entry.Value))
            {
                throw new InvalidDataException(
                    $"Texture delivery group contains an invalid or duplicate path: '{entry.Key}' -> '{entry.Value}'.");
            }

            string sourcePath = Path.Combine(source, entry.Key.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException(
                    $"Texture delivery catalog references missing source asset: {entry.Key}",
                    sourcePath);
            }

            string target = Path.Combine(destination, entry.Value.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(sourcePath, target, overwrite: true);
        }
    }

    public static void ValidateCatalogCoverage(
        string source,
        IReadOnlyDictionary<string, string> catalog)
    {
        var matchedSources = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file);
            if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) || name.StartsWith('.'))
            {
                continue;
            }

            string relative = Path.GetRelativePath(source, file).Replace('\\', '/');
            if (!catalog.TryGetValue(relative, out string? published) || string.IsNullOrWhiteSpace(published))
            {
                throw new InvalidDataException($"Authored asset has no delivery category and published identity: {relative}");
            }

            matchedSources.Add(relative);
        }

        string[] missing = catalog.Keys.Where(path => !matchedSources.Contains(path)).ToArray();
        if (missing.Length > 0)
        {
            throw new FileNotFoundException($"Texture delivery catalog references missing source assets: {string.Join(", ", missing)}");
        }
    }
}

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Kern.AssetPipeline.Loading;

public static class AuthoredTextureCatalog
{
    public const string AssetPath = "Assets/Settings/Build/Textures/TextureStagingCatalog.json";

    [Serializable]
    private sealed class Document
    {
        public Entry[] Entries = Array.Empty<Entry>();
    }

    [Serializable]
    private sealed class Entry
    {
        public string Source = string.Empty;
        public string Published = string.Empty;
    }

    public static IReadOnlyDictionary<string, string> Read(string path)
    {
        Document document = JsonUtility.FromJson<Document>(File.ReadAllText(path)) ??
            throw new InvalidDataException($"Texture staging catalog is empty: {path}");
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        var published = new HashSet<string>(StringComparer.Ordinal);
        foreach (Entry entry in document.Entries)
        {
            if (entry == null || !IsRelativeAssetPath(entry.Source) || !IsRelativeAssetPath(entry.Published))
            {
                throw new InvalidDataException(
                    $"Texture staging catalog contains an invalid entry (source='{entry?.Source}', published='{entry?.Published}'): {path}");
            }

            if (!entries.TryAdd(entry.Source, entry.Published) || !published.Add(entry.Published))
            {
                throw new InvalidDataException($"Texture staging catalog has a duplicate source or published path: {entry.Source}");
            }
        }

        if (entries.Count == 0)
        {
            throw new InvalidDataException($"Texture staging catalog has no assets: {path}");
        }

        return entries;
    }

    private static bool IsRelativeAssetPath(string path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
            !Path.IsPathRooted(path) &&
            !path.Contains('\\') &&
            Array.TrueForAll(path.Split('/'), segment => segment.Length > 0 && segment is not "." and not "..");
    }
}

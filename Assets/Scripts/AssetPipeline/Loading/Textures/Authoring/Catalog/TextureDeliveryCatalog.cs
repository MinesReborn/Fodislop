#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using UnityEngine;

namespace Kern.AssetPipeline.Loading;

public sealed class TextureDeliveryCatalogData
{
    /// <summary>All authored source paths mapped to their unique published runtime paths.</summary>
    public IReadOnlyDictionary<string, string> All { get; }

    /// <summary>Textures shipped as client-owned content, such as UI, item icons and the surface scene.</summary>
    public IReadOnlyDictionary<string, string> Embedded { get; }

    /// <summary>Local world textures used by DummyConnection; online worlds use their server asset manifest.</summary>
    public IReadOnlyDictionary<string, string> DummyWorld { get; }

    internal TextureDeliveryCatalogData(
        IDictionary<string, string> all,
        IDictionary<string, string> embedded,
        IDictionary<string, string> dummyWorld)
    {
        All = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(all, StringComparer.Ordinal));
        Embedded = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(embedded, StringComparer.Ordinal));
        DummyWorld = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(dummyWorld, StringComparer.Ordinal));
    }
}

/// <summary>Maps authored texture files to published identities and local package groups.</summary>
public static class TextureDeliveryCatalog
{
    public const string AssetPath = "Assets/Settings/Build/Textures/TextureDeliveryCatalog.json";
    private const string EmbeddedGroup = "Embedded";
    private const string DummyWorldGroup = "DummyWorld";

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
        public string Delivery = string.Empty;
    }

    public static TextureDeliveryCatalogData Read(string path)
    {
        Document document = JsonUtility.FromJson<Document>(File.ReadAllText(path)) ??
            throw new InvalidDataException($"Texture delivery catalog is empty: {path}");
        if (document.Entries == null || document.Entries.Length == 0)
        {
            throw new InvalidDataException($"Texture delivery catalog contains no entries: {path}");
        }
        var all = new Dictionary<string, string>(StringComparer.Ordinal);
        var embedded = new Dictionary<string, string>(StringComparer.Ordinal);
        var dummyWorld = new Dictionary<string, string>(StringComparer.Ordinal);
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var published = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Entry entry in document.Entries)
        {
            if (entry == null ||
                !IsRelativeAssetPath(entry.Source) ||
                !IsRelativeAssetPath(entry.Published))
            {
                throw new InvalidDataException(
                    $"Texture delivery catalog contains an invalid entry (source='{entry?.Source}', published='{entry?.Published}', delivery='{entry?.Delivery}'): {path}");
            }

            if (!sources.Add(entry.Source) || !published.Add(entry.Published) ||
                !all.TryAdd(entry.Source, entry.Published))
            {
                throw new InvalidDataException(
                    $"Texture delivery catalog has a duplicate source or published path: {entry.Source} -> {entry.Published}");
            }

            Dictionary<string, string> group = entry.Delivery switch
            {
                EmbeddedGroup => embedded,
                DummyWorldGroup => dummyWorld,
                _ => throw new InvalidDataException(
                    $"Texture delivery catalog has an unknown group '{entry.Delivery}' for '{entry.Source}'."),
            };
            group.Add(entry.Source, entry.Published);
        }

        if (embedded.Count == 0 || dummyWorld.Count == 0)
        {
            throw new InvalidDataException(
                $"Texture delivery catalog must contain both Embedded and DummyWorld assets: {path}");
        }

        return new TextureDeliveryCatalogData(all, embedded, dummyWorld);
    }

    public static bool IsRelativeAssetPath(string path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
            !Path.IsPathRooted(path) &&
            !path.Contains('\\') &&
            Array.TrueForAll(path.Split('/'), segment => segment.Length > 0 && segment is not "." and not "..");
    }
}

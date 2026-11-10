#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Kern.AssetPipeline.Cache;

/// <summary>
/// Манифест кэша на диске: что лежит в кэше и какой у него MD5 (ETag).
/// Отличается от манифеста сервера: тот говорит, что должно быть, этот — что есть и проверено. По нему ловится
/// тихая порча файла (MD5 при чтении) и чистится устаревшее (файлы, которых сервер больше не присылает в манифесте мира).
/// </summary>
internal sealed class PersistentAssetCacheManifest
{
    internal const string FileName = "manifest.json";

    // Файл — дерево папок, чтобы путь не повторялся в каждой записи: { "Visual": { "UI": { "knob.png": "<md5>" } } }.
    // Лист — MD5 файла, папка — объект

    private readonly object _gate = new();
    private readonly object _writeGate = new();
    private readonly Dictionary<string, PersistentAssetCacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly string _path;

    /// <summary>Файл манифеста есть, но не читается: что в кэше цело, узнать уже нельзя.</summary>
    public bool WasBroken { get; private set; }

    public PersistentAssetCacheManifest(string cachePath)
    {
        _path = Path.Combine(cachePath, FileName);
        Load();
    }

    public bool TryGet(string filename, out PersistentAssetCacheEntry entry)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(filename, out entry);
        }
    }

    public void Set(string filename, PersistentAssetCacheEntry entry)
    {
        lock (_gate)
        {
            _entries[filename] = entry;
        }
    }

    public bool Remove(string filename)
    {
        lock (_gate)
        {
            return _entries.Remove(filename);
        }
    }

    /// <summary>Удаляет записи файлов, которых нет в <paramref name="keep"/>.</summary>
    public void RemoveUnlessKept(HashSet<string> keep)
    {
        lock (_gate)
        {
            var removed = new List<string>();
            foreach (KeyValuePair<string, PersistentAssetCacheEntry> pair in _entries)
            {
                if (!keep.Contains(pair.Key))
                {
                    removed.Add(pair.Key);
                }
            }

            foreach (string filename in removed)
            {
                _entries.Remove(filename);
            }
        }
    }

    /// <summary>Записывает манифест атомарно; безопасно звать с любого потока.</summary>
    public void Flush()
    {
        var tree = new SortedDictionary<string, object>(StringComparer.Ordinal);
        lock (_gate)
        {
            foreach (KeyValuePair<string, PersistentAssetCacheEntry> pair in _entries)
            {
                Insert(tree, pair.Key, pair.Value.ETag);
            }
        }

        string json = JsonConvert.SerializeObject(tree, Formatting.Indented);
        lock (_writeGate)
        {
            PersistentAssetCacheFiles.WriteAtomically(_path, Encoding.UTF8.GetBytes(json));
        }
    }

    private static SortedDictionary<string, object> GetFolder(SortedDictionary<string, object> parent, string name)
    {
        if (parent.TryGetValue(name, out object? existing) && existing is SortedDictionary<string, object> folder)
        {
            return folder;
        }

        folder = new SortedDictionary<string, object>(StringComparer.Ordinal);
        parent[name] = folder;
        return folder;
    }

    // Файл «Visual/UI/knob.png» ложится в папки Visual → UI листом knob.png
    private static void Insert(SortedDictionary<string, object> tree, string filename, string etag)
    {
        string[] segments = filename.Split('/');
        SortedDictionary<string, object> folder = tree;
        for (int index = 0; index < segments.Length - 1; index++)
        {
            folder = GetFolder(folder, segments[index]);
        }

        folder[segments[segments.Length - 1]] = etag;
    }

    public static PersistentAssetCacheEntry Fingerprint(byte[] payload) =>
        new(ETagCalculator.Calculate(payload) ?? string.Empty);

    // Нет файла — кэш прошлых версий: начинаем с пустого, файлы без записи усыновляются при чтении. Файл есть, но сломан —
    // WasBroken: отпечатков нет, а порченые файлы усыновились бы как целые, поэтому кэш очищает владелец
    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            if (JToken.Parse(File.ReadAllText(_path, Encoding.UTF8)) is not JObject root)
            {
                WasBroken = true;
                return;
            }

            ReadTree(root, string.Empty);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _entries.Clear();
            WasBroken = true;
        }
    }

    private void ReadTree(JObject folder, string prefix)
    {
        foreach (JProperty property in folder.Properties())
        {
            string path = prefix + property.Name;
            if (property.Value is JObject child)
            {
                ReadTree(child, path + "/");
            }
            else if (property.Value.Type == JTokenType.String && !string.IsNullOrEmpty((string?)property.Value))
            {
                _entries[path] = new PersistentAssetCacheEntry((string)property.Value!);
            }
        }
    }
}

internal readonly record struct PersistentAssetCacheEntry(string ETag)
{
    public bool Matches(string etag) => string.Equals(etag, ETag, StringComparison.OrdinalIgnoreCase);
}

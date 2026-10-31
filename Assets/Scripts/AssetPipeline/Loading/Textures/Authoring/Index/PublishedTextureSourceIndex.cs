#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kern.AssetPipeline.Loading;

public sealed class PublishedTextureSourceIndex
{
    private readonly Dictionary<string, string> _sources = new(StringComparer.Ordinal);

    public PublishedTextureSourceIndex(IReadOnlyDictionary<string, string> sourceToPublished)
    {
        foreach (KeyValuePair<string, string> entry in sourceToPublished
                     .OrderBy(entry => FormatPriority(entry.Key))
                     .ThenBy(entry => entry.Key, StringComparer.Ordinal))
        {
            _sources.Add(entry.Value, entry.Key);
            if (FormatPriority(entry.Key) != int.MaxValue)
            {
                string withoutExtension = Path.ChangeExtension(entry.Value, null);
                _sources.TryAdd(withoutExtension, entry.Key);
            }
        }
    }

    public string? Resolve(string publishedPath) =>
        _sources.TryGetValue(publishedPath, out string? source) ? source : null;

    private static int FormatPriority(string path) => Path.GetExtension(path) switch
    {
        ".png" => 0,
        ".jpg" or ".jpeg" => 1,
        ".exr" => 2,
        _ => int.MaxValue,
    };
}

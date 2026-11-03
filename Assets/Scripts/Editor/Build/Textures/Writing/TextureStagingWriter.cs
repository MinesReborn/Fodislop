#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kern.Editor;

public static class TextureStagingWriter
{
    public static void CopyRuntimeFiles(
        string source,
        string destination,
        IReadOnlyDictionary<string, string> catalog)
    {
        var copied = new HashSet<string>(StringComparer.Ordinal);
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
                throw new InvalidDataException($"Authored texture has no published identity in the staging catalog: {relative}");
            }

            string target = Path.Combine(destination, published);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
            copied.Add(relative);
        }

        string[] missing = catalog.Keys.Where(path => !copied.Contains(path)).ToArray();
        if (missing.Length > 0)
        {
            throw new FileNotFoundException($"Texture staging catalog references missing source assets: {string.Join(", ", missing)}");
        }
    }
}

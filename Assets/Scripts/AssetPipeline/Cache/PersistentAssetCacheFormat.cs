#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Kern;

internal static class PersistentAssetCacheFormat
{
    internal const int CurrentSchemaVersion = 2;
    internal const string MarkerFileName = ".format-version";

    internal static void EnsureCurrent(string cachePath)
    {
        if (string.IsNullOrWhiteSpace(cachePath))
        {
            throw new ArgumentException("Asset cache path is required.", nameof(cachePath));
        }

        string normalizedPath = Path.GetFullPath(cachePath);
        Directory.CreateDirectory(normalizedPath);

        // Пейлоади и etag-раскладка одинаковы во всех версиях схемы, поэтому
        // миграций, бэкапов и staging нет: маркер просто штампуется текущей
        // версией. Легаси запрещено — старые маркеры не читаются, а
        // перезаписываются.
        string markerPath = Path.Combine(normalizedPath, MarkerFileName);
        if (File.Exists(markerPath))
        {
            int version = ReadVersionMarker(markerPath);
            if (version == CurrentSchemaVersion)
            {
                return;
            }
        }

        File.WriteAllText(
            markerPath,
            CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture) + "\n",
            Encoding.UTF8);
    }

    private static int ReadVersionMarker(string markerPath)
    {
        string text = File.ReadAllText(markerPath, Encoding.UTF8).Trim();
        if (!int.TryParse(
                text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int version))
        {
            throw new InvalidDataException(
                $"Invalid asset cache schema '{text}' in '{markerPath}'.");
        }

        return version;
    }
}

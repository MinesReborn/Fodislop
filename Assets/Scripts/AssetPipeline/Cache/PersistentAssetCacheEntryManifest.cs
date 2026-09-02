#nullable enable

using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Fodinae;

internal readonly record struct PersistentAssetCacheEntryManifest(
    string ETag,
    long Length,
    string Sha256)
{
    private const int EntryFormatVersion = 2;

    public static PersistentAssetCacheEntryManifest Create(byte[] payload, string etag)
    {
        return new PersistentAssetCacheEntryManifest(etag, payload.LongLength, ComputeHash(payload));
    }

    public bool Matches(byte[] payload)
    {
        return payload.LongLength == Length &&
            string.Equals(ComputeHash(payload), Sha256, StringComparison.OrdinalIgnoreCase);
    }

    public string Serialize()
    {
        string encodedEtag = Convert.ToBase64String(Encoding.UTF8.GetBytes(ETag));
        return string.Join(
            "\n",
            EntryFormatVersion.ToString(CultureInfo.InvariantCulture),
            encodedEtag,
            Length.ToString(CultureInfo.InvariantCulture),
            Sha256) + "\n";
    }

    public static bool TryParse(string text, out PersistentAssetCacheEntryManifest manifest)
    {
        manifest = default;
        string[] lines = text.TrimEnd('\r', '\n').Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            lines[index] = lines[index].TrimEnd('\r');
        }

        if (lines.Length != 4 ||
            !int.TryParse(lines[0], NumberStyles.None, CultureInfo.InvariantCulture, out int version) ||
            version != EntryFormatVersion ||
            !long.TryParse(lines[2], NumberStyles.None, CultureInfo.InvariantCulture, out long length) ||
            length <= 0 ||
            lines[3].Length != 64)
        {
            return false;
        }

        try
        {
            string etag = Encoding.UTF8.GetString(Convert.FromBase64String(lines[1]));
            manifest = new PersistentAssetCacheEntryManifest(etag, length, lines[3]);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string ComputeHash(byte[] payload)
    {
        using SHA256 sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(payload)).Replace("-", string.Empty);
    }
}

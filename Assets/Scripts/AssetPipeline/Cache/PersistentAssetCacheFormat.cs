#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Fodinae;

internal static class PersistentAssetCacheFormat
{
    internal const int CurrentSchemaVersion = 2;
    internal const string MarkerFileName = ".format-version";
    internal const string LegacyBackupFileName = ".format-version.v0.backup";
    internal const string VersionOneBackupFileName = ".format-version.v1.backup";
    internal const string MigrationStagingFileName = ".format-version.migrate.tmp";

    internal static void EnsureCurrent(string cachePath)
    {
        if (string.IsNullOrWhiteSpace(cachePath))
        {
            throw new ArgumentException("Asset cache path is required.", nameof(cachePath));
        }

        string normalizedPath = Path.GetFullPath(cachePath);
        Directory.CreateDirectory(normalizedPath);

        string markerPath = Path.Combine(normalizedPath, MarkerFileName);
        string backupPath = Path.Combine(normalizedPath, LegacyBackupFileName);
        string versionOneBackupPath = Path.Combine(normalizedPath, VersionOneBackupFileName);
        string stagingPath = Path.Combine(normalizedPath, MigrationStagingFileName);
        if (File.Exists(markerPath))
        {
            int version = ReadVersionMarker(markerPath);
            if (version == CurrentSchemaVersion)
            {
                DeleteStaleStaging(stagingPath);
                return;
            }

            if (version != 1)
            {
                throw new InvalidDataException(
                    $"Unsupported asset cache schema '{version}' in '{markerPath}'; " +
                    $"expected 1 or {CurrentSchemaVersion}.");
            }

            if (!File.Exists(versionOneBackupPath))
            {
                WriteDurably(versionOneBackupPath, "1\n", createNew: true);
            }
            else
            {
                ValidateBackup(versionOneBackupPath, "1");
            }

            CommitVersionMarker(markerPath, stagingPath, replaceExisting: true);
            return;
        }

        // Schema v0 had no marker and used the same payload/etag layout as v1.
        // Back up that format state, not every potentially multi-gigabyte asset:
        // payload files do not change, so copying them would only freeze startup.
        if (!File.Exists(backupPath))
        {
            WriteDurably(backupPath, "0\n", createNew: true);
        }
        else
        {
            ValidateBackup(backupPath, "0");
        }

        // The staging marker is durable before the atomic rename. A crash at
        // any point leaves all cache payloads untouched; the next call can
        // safely repeat this metadata-only commit.
        CommitVersionMarker(markerPath, stagingPath, replaceExisting: false);
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

    private static void ValidateBackup(string backupPath, string expectedVersion)
    {
        string text = File.ReadAllText(backupPath, Encoding.UTF8).Trim();
        if (!string.Equals(text, expectedVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Invalid asset cache migration backup '{backupPath}'.");
        }
    }

    private static void WriteDurably(
        string path,
        string value,
        bool createNew)
    {
        byte[] payload = Encoding.UTF8.GetBytes(value);
        using var stream = new FileStream(
            path,
            createNew ? FileMode.CreateNew : FileMode.Create,
            FileAccess.Write,
            FileShare.None);
        stream.Write(payload, 0, payload.Length);
        stream.Flush(flushToDisk: true);
    }

    private static void CommitVersionMarker(
        string markerPath,
        string stagingPath,
        bool replaceExisting)
    {
        DeleteStaleStaging(stagingPath);
        WriteDurably(
            stagingPath,
            CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture) + "\n",
            createNew: true);
        if (replaceExisting)
        {
            File.Replace(stagingPath, markerPath, null);
            return;
        }

        File.Move(stagingPath, markerPath);
    }

    private static void DeleteStaleStaging(string stagingPath)
    {
        if (File.Exists(stagingPath))
        {
            File.Delete(stagingPath);
        }
    }
}

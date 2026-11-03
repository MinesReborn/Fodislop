#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Kern.Networking.Offline;
public sealed class DummyTokenStore
{
    private readonly string _path;
    private readonly string? _legacyPath;

    public DummyTokenStore()
        : this(
            Path.Combine(Application.persistentDataPath, "DummyServer/server_tokens.json"),
            Path.Combine(Application.temporaryCachePath, "server_tokens.json"))
    {
    }

    internal DummyTokenStore(string path)
    {
        _path = path;
        _legacyPath = null;
    }

    private DummyTokenStore(string path, string legacyPath)
        : this(path)
    {
        _legacyPath = legacyPath;
    }

    public HashSet<string> Load()
    {
        try
        {
            string sourcePath = File.Exists(_path)
                ? _path
                : _legacyPath != null && File.Exists(_legacyPath)
                    ? _legacyPath
                    : string.Empty;
            if (sourcePath.Length == 0)
            {
                return new HashSet<string>();
            }

            string json = File.ReadAllText(sourcePath);
            List<string>? tokens = JsonConvert.DeserializeObject<List<string>>(json);
            var result = tokens == null ? new HashSet<string>() : new HashSet<string>(tokens);
            if (sourcePath != _path && TrySave(result))
            {
                DeleteLegacyFile();
            }
            else if (sourcePath == _path)
            {
                // The persistent location is authoritative. Remove a stale copy
                // left by the older temporary-cache location after it is read.
                DeleteLegacyFile();
            }

            return result;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[DummyTokenStore] Failed to load tokens: {exception.Message}");
            return new HashSet<string>();
        }
    }

    public void Save(IEnumerable<string> tokens)
    {
        TrySave(tokens);
    }

    private bool TrySave(IEnumerable<string> tokens)
    {
        string? directory = Path.GetDirectoryName(_path);
        string temporaryPath = _path + ".tmp";
        try
        {
            if (directory == null)
            {
                throw new InvalidOperationException("Dummy token path has no parent directory.");
            }

            Directory.CreateDirectory(directory);
            string json = JsonConvert.SerializeObject(new List<string>(tokens));
            File.WriteAllText(temporaryPath, json);
            if (File.Exists(_path))
            {
                File.Replace(temporaryPath, _path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, _path);
            }

            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[DummyTokenStore] Failed to save tokens: {exception.Message}");
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Debug.LogWarning($"[DummyTokenStore] Failed to remove temporary token file: {exception.Message}");
            }
        }
    }

    private void DeleteLegacyFile()
    {
        if (_legacyPath == null || !File.Exists(_legacyPath))
        {
            return;
        }

        try
        {
            File.Delete(_legacyPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Debug.LogWarning($"[DummyTokenStore] Failed to remove migrated token file: {exception.Message}");
        }
    }
}

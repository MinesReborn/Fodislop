#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Fodinae.Core.Interfaces;
using UnityEngine;

namespace Fodinae.Core;

internal sealed class ClientConfigRepository
{
    private readonly string _configPath;

    public ClientConfigRepository(string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            throw new ArgumentException("Config path must not be empty.", nameof(configPath));
        }

        _configPath = configPath;
    }

    public string ConfigPath => _configPath;

    public bool Exists => File.Exists(_configPath);

    public ClientConfig Load()
    {
        string json;
        try
        {
            json = File.ReadAllText(_configPath);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                $"Failed to read client config '{_configPath}'.",
                ex);
        }

        json = RenameLegacyKeys(json);
        ClientConfig config = JsonUtility.FromJson<ClientConfig>(json) ??
            throw new InvalidDataException($"Client config '{_configPath}' is empty or invalid.");
        if (config.SchemaVersion < ClientConfig.CurrentSchemaVersion)
        {
            HydrateLegacySections(json, config);
        }

        ValidateCurrentSchemaPresence(json, config);
        return config;
    }

    public void Save(ClientConfig config, string? backupPath = null)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        string directory = Path.GetDirectoryName(_configPath) ??
            throw new InvalidOperationException("Client config path has no parent directory.");
        string temporaryPath = _configPath + ".tmp";
        try
        {
            Directory.CreateDirectory(directory);
            string json = JsonUtility.ToJson(config, prettyPrint: true);
            byte[] payload = Encoding.UTF8.GetBytes(json);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None))
            {
                stream.Write(payload, 0, payload.Length);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_configPath))
            {
                File.Replace(temporaryPath, _configPath, backupPath);
            }
            else
            {
                File.Move(temporaryPath, _configPath);
            }
        }
        catch (Exception ex)
        {
            throw new IOException($"Failed to save client config '{_configPath}'.", ex);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string RenameLegacyKeys(string json)
    {
        return json
            .Replace("\"UiScale\"", "\"UIScale\"")
            .Replace("\"UiVolume\"", "\"UIVolume\"");
    }

    private static void HydrateLegacySections(string json, ClientConfig config)
    {
        LegacyClientSettings legacy = JsonUtility.FromJson<LegacyClientSettings>(json) ??
            throw new InvalidDataException("Legacy client settings are invalid.");
        config.Audio = new AudioSettings
        {
            MasterVolume = legacy.MasterVolume,
            SfxVolume = legacy.SfxVolume,
            MusicVolume = legacy.MusicVolume,
            AmbienceVolume = legacy.AmbienceVolume,
            VoiceVolume = legacy.VoiceVolume,
            UIVolume = legacy.UIVolume,
            MuteInBackground = legacy.MuteAudioInBackground,
        };
        config.Display = new DisplaySettings
        {
            ResolutionWidth = legacy.ResolutionWidth,
            ResolutionHeight = legacy.ResolutionHeight,
            RefreshRate = legacy.RefreshRate,
            FullScreenMode = legacy.FullScreenMode,
            VSync = legacy.VSync,
            HDREnabled = legacy.HdrEnabled,
            TargetFrameRate = legacy.TargetFrameRate,
        };
        config.Interface = new InterfaceSettings
        {
            UIScale = legacy.UIScale,
            Language = legacy.Language,
            ControlScheme = legacy.ControlScheme,
        };
        config.Accessibility = new AccessibilitySettings
        {
            ColorblindMode = legacy.ColorblindMode,
            ReducePhotosensitivity = legacy.ReducePhotosensitivity,
        };
        config.Connection = new ConnectionSettings
        {
            UseDummyConnection = legacy.UseDummyConnection,
            ServerHost = legacy.ServerHost,
            ServerPort = legacy.ServerPort,
        };
    }

    [Serializable]
    private sealed class LegacyClientSettings
    {
        public float MasterVolume;
        public float SfxVolume;
        public float MusicVolume;
        public float AmbienceVolume;
        public float VoiceVolume;
        public float UIVolume;
        public bool MuteAudioInBackground = true;
        public float UIScale;
        public string Language = "ru";
        public int ControlScheme;
        public int ResolutionWidth;
        public int ResolutionHeight;
        public int RefreshRate;
        public int FullScreenMode = 1;
        public bool VSync = true;
        public bool HdrEnabled = ProjectRuntimeContracts.ClientConfiguration.DefaultHDREnabled;
        public int TargetFrameRate = -1;
        public int ColorblindMode;
        public bool ReducePhotosensitivity;
        public bool UseDummyConnection = ProjectRuntimeContracts.ClientConfiguration.DefaultUseDummyConnection;
        public string ServerHost = ProjectRuntimeContracts.ClientConfiguration.DefaultServerHost;
        public int ServerPort = ProjectRuntimeContracts.ClientConfiguration.DefaultServerPort;
    }

    private static void ValidateCurrentSchemaPresence(string json, ClientConfig config)
    {
        if (config.SchemaVersion != ClientConfig.CurrentSchemaVersion)
        {
            // Historical schemas intentionally contain fewer fields. Their
            // completeness is established by the ordered migration pipeline.
            return;
        }

        Type[] persistedTypes =
        [
            typeof(ClientConfig),
            typeof(AudioSettings),
            typeof(DisplaySettings),
            typeof(InterfaceSettings),
            typeof(AccessibilitySettings),
            typeof(ConnectionSettings),
        ];
        string[] missingFields = persistedTypes
            .SelectMany(type => type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            .Select(field => field.Name)
            .Where(fieldName => !Regex.IsMatch(
                json,
                $"\\\"{Regex.Escape(fieldName)}\\\"\\s*:",
                RegexOptions.CultureInvariant))
            .ToArray();
        if (missingFields.Length > 0)
        {
            throw new InvalidDataException(
                $"Current client config '{config.SchemaVersion}' is incomplete; missing field(s): " +
                string.Join(", ", missingFields) + ".");
        }
    }
}

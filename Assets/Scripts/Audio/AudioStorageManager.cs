using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Fodinae.Assets.Scripts.Audio
{
    public class AudioStorageManager : MonoBehaviour
    {
        private static AudioStorageManager _instance;
        public static AudioStorageManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<AudioStorageManager>();
                    if (_instance == null)
                    {
                        var go = new GameObject("[AudioStorageManager]");
                        _instance = go.AddComponent<AudioStorageManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        [Header("Audio Storage Configuration")]
        [SerializeField] private bool _enableDebugLogging = true;

        private readonly ConcurrentDictionary<string, byte[]> _audioCache = new();
        private string _audioFolderPath = string.Empty;
        private bool _folderInitialized = false;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public async UniTask<byte[]> GetAudioData(string filename)
        {
            if (string.IsNullOrEmpty(filename))
            {
                Debug.LogError("[AudioStorageManager] Cannot load audio: filename is null or empty");
                return null;
            }

            var normalizedFilename = filename.StartsWith("/") ? filename.Substring(1) : filename;

            if (_audioCache.TryGetValue(normalizedFilename, out var cachedData))
            {
                if (_enableDebugLogging)
                    Debug.Log($"[AudioStorageManager] Cache hit for: {normalizedFilename}");
                return cachedData;
            }

            var audioData = await LoadAudioFromStorage(normalizedFilename);

            if (audioData != null)
            {
                _audioCache.TryAdd(normalizedFilename, audioData);
                if (_enableDebugLogging)
                    Debug.Log($"[AudioStorageManager] Loaded audio from storage: {normalizedFilename}");
                return audioData;
            }

            if (_enableDebugLogging)
                Debug.LogWarning($"[AudioStorageManager] Audio not found: {normalizedFilename}");

            return null;
        }

        private async UniTask<byte[]> LoadAudioFromStorage(string filename)
        {
            try
            {
                if (!_folderInitialized)
                    InitializeAudioFolderPath();

                if (string.IsNullOrEmpty(_audioFolderPath) || !Directory.Exists(_audioFolderPath))
                {
                    if (_enableDebugLogging)
                        Debug.LogWarning($"[AudioStorageManager] Audio folder not found: {_audioFolderPath}");
                    return null;
                }

                // Strip the "audio/" prefix — it's a naming convention, not a real subfolder.
                // Files are stored directly under the audio folder (e.g. Assets/Audio/death.wav).
                var pathFilename = filename.StartsWith("audio/") ? filename.Substring(6) : filename;

                var fullPath = Path.Combine(_audioFolderPath, pathFilename);

                if (!File.Exists(fullPath))
                {
                    string directory = Path.GetDirectoryName(fullPath);
                    string filenameWithoutExtension = Path.GetFileNameWithoutExtension(fullPath);

                    if (Directory.Exists(directory))
                    {
                        var files = Directory.GetFiles(directory, filenameWithoutExtension + ".*")
                            .Where(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                            .ToArray();

                        if (files.Length > 0)
                        {
                            fullPath = files[0];
                            if (_enableDebugLogging)
                                Debug.Log($"[AudioStorageManager] Fuzzy matched {filename} to {fullPath}");
                        }
                    }
                }

                if (!File.Exists(fullPath))
                {
                    if (_enableDebugLogging)
                        Debug.Log($"[AudioStorageManager] Audio file not found: {fullPath}");
                    return null;
                }

                using var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
                var buffer = new byte[fileStream.Length];
                await fileStream.ReadAsync(buffer, 0, buffer.Length);
                return buffer;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AudioStorageManager] Failed to load audio from storage: {ex.Message}");
                return null;
            }
        }

        private void InitializeAudioFolderPath()
        {
            if (_folderInitialized) return;

            try
            {
                var possiblePaths = new[]
                {
                    Path.Combine(Application.dataPath, "Audio"),
                    Path.Combine(Application.dataPath, "../Audio"),
                    Path.Combine(Application.dataPath, "..", "Audio"),
                    Path.Combine(Application.persistentDataPath, "Audio")
                };

                foreach (var path in possiblePaths)
                {
                    if (Directory.Exists(path))
                    {
                        _audioFolderPath = path;
                        if (_enableDebugLogging)
                            Debug.Log($"[AudioStorageManager] Using audio folder: {_audioFolderPath}");
                        _folderInitialized = true;
                        return;
                    }
                }

                var preferredPath = Path.Combine(Application.dataPath, "Audio");
                try
                {
                    Directory.CreateDirectory(preferredPath);
                    _audioFolderPath = preferredPath;
                    if (_enableDebugLogging)
                        Debug.Log($"[AudioStorageManager] Created audio folder: {_audioFolderPath}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AudioStorageManager] Could not create audio folder: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AudioStorageManager] Failed to initialize audio folder: {ex.Message}");
            }

            _folderInitialized = true;
        }

        public void ClearCache()
        {
            _audioCache.Clear();
            if (_enableDebugLogging)
                Debug.Log("[AudioStorageManager] Audio cache cleared");
        }

        public string GetAudioFolderPath()
        {
            if (!_folderInitialized)
                InitializeAudioFolderPath();
            return _audioFolderPath;
        }

        public bool HasAudio(string filename)
        {
            if (!_folderInitialized)
                InitializeAudioFolderPath();

            if (string.IsNullOrEmpty(_audioFolderPath) || !Directory.Exists(_audioFolderPath))
                return false;

            var normalizedFilename = filename.StartsWith("/") ? filename.Substring(1) : filename;
            var fullPath = Path.Combine(_audioFolderPath, normalizedFilename);
            return File.Exists(fullPath);
        }
    }
}

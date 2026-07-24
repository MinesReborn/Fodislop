using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fodinae.Scripts.Audio.Core;
using UnityEngine;

namespace Fodinae.Scripts.Audio.Backend
{
    /// <summary>
    /// FMOD Studio аудио-бэкенд с диск-стримингом банков и селективной загрузкой сэмплов в ОЗУ.
    ///
    /// Оптимизация памяти:
    /// 1. Банки загружаются через loadBankFile с диска (persistentDataPath кэш или StreamingAssets).
    /// 2. Метаданные весят единицы КБ.
    /// 3. Сэмплы загружаются в RAM только для активно воспроизводимых событий через loadSampleData().
    /// </summary>
    public sealed class FmodAudioBackend
    {
        private AudioSystem _system;
        private readonly Dictionary<AudioBusType, FMOD.Studio.Bus> _fmodBuses = new();
        private readonly ConcurrentDictionary<string, FMOD.Studio.Bank> _loadedBanks = new(StringComparer.OrdinalIgnoreCase);

        private const string BANK_PATH = "banks";

        private static readonly string[] _requiredBanks =
        {
            "Master",
            "Master.strings"
        };

        private static readonly FMOD.VECTOR ForwardVector = new() { x = 0f, y = 0f, z = 1f };
        private static readonly FMOD.VECTOR UpVector = new() { x = 0f, y = 1f, z = 0f };

        public void Initialize(AudioSystem system)
        {
            _system = system;
            LoadRequiredBanksAsync().Forget();
        }

        public async UniTaskVoid LoadRequiredBanksAsync()
        {
            foreach (var bankName in _requiredBanks)
            {
                await EnsureBankLoadedAsync(bankName);
            }

            MapBuses();
        }

        /// <summary>
        /// Гарантирует наличие и загрузку .bank файла с диска (CDN cache -> StreamingAssets fallback).
        /// </summary>
        public async UniTask<bool> EnsureBankLoadedAsync(string bankName)
        {
            var cleanBankName = bankName.Replace(".bank", string.Empty);
            if (_loadedBanks.ContainsKey(cleanBankName))
            {
                return true;
            }

            string bankFilePath = null;
            var localPath = System.IO.Path.Combine(Application.streamingAssetsPath, "Audio", $"{cleanBankName}.bank");

            if (System.IO.File.Exists(localPath))
            {
                bankFilePath = localPath;
            }
            else if (ClientAssetLoader.Instance != null)
            {
                var relativeRemotePath = $"{BANK_PATH}/{cleanBankName}.bank";
                try
                {
                    bankFilePath = await ClientAssetLoader.Instance.GetAssetPathAsync(relativeRemotePath);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FmodAudioBackend] Ошибка загрузки банка '{cleanBankName}' с CDN: {ex.Message}");
                }
            }

            if (string.IsNullOrEmpty(bankFilePath) || !System.IO.File.Exists(bankFilePath))
            {
                Debug.LogWarning($"[FmodAudioBackend] Банк '{cleanBankName}' не найден ни локально, ни в кэше CDN");
                return false;
            }

            FMOD.RESULT result = FMODUnity.RuntimeManager.StudioSystem.loadBankFile(
                bankFilePath,
                FMOD.Studio.LOAD_BANK_FLAGS.NORMAL,
                out var bank);

            if (result == FMOD.RESULT.OK)
            {
                _loadedBanks[cleanBankName] = bank;
                Debug.Log($"[FmodAudioBackend] Успешно загружен банк '{cleanBankName}' из: {bankFilePath}");
                return true;
            }
            else if (result == FMOD.RESULT.ERR_EVENT_ALREADY_LOADED)
            {
                return true;
            }

            Debug.LogWarning($"[FmodAudioBackend] Ошибка loadBankFile для '{cleanBankName}': {result}");
            return false;
        }

        public void UnloadBank(string bankName)
        {
            var cleanBankName = bankName.Replace(".bank", string.Empty);
            if (_loadedBanks.TryGetValue(cleanBankName, out var bank))
            {
                bank.unload();
                _loadedBanks.TryRemove(cleanBankName, out _);
                Debug.Log($"[FmodAudioBackend] Банк '{cleanBankName}' выгружен из памяти.");
            }
        }

        private void MapBuses()
        {
            var busPaths = new Dictionary<AudioBusType, string>
            {
                { AudioBusType.Master,   "bus:/" },
                { AudioBusType.SFX,      "bus:/sfx" },
                { AudioBusType.Music,    "bus:/music" },
                { AudioBusType.Voice,    "bus:/voice" },
                { AudioBusType.Ambience, "bus:/ambience" },
                { AudioBusType.UI,       "bus:/ui" },
            };

            foreach (var kvp in busPaths)
            {
                if (FMODUnity.RuntimeManager.StudioSystem.getBus(kvp.Value, out var bus) == FMOD.RESULT.OK)
                {
                    _fmodBuses[kvp.Key] = bus;
                }
                else
                {
                    Debug.LogWarning($"[FmodAudioBackend] Шина '{kvp.Value}' не найдена в FMOD Studio.");
                }
            }

            _system?.ApplySavedBusVolumes();
        }

        public float GetBusVolume(AudioBusType type)
        {
            if (_fmodBuses.TryGetValue(type, out var bus))
            {
                bus.getVolume(out float volume);
                return volume;
            }

            return 1f;
        }

        public void SetBusVolume(AudioBusType type, float volume)
        {
            if (_fmodBuses.TryGetValue(type, out var bus))
            {
                bus.setVolume(Mathf.Clamp01(volume));
            }
        }

        public AudioPlaybackHandle CreateVoice(string eventName, AudioLayer layer, Vector3? worldPosition, GameObject targetGameObject = null)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                return null;
            }

            string fmodPath = eventName.StartsWith("event:/", StringComparison.OrdinalIgnoreCase) || eventName.StartsWith("snapshot:/", StringComparison.OrdinalIgnoreCase)
                ? eventName
                : $"event:/{eventName}";

            if (FMODUnity.RuntimeManager.StudioSystem.getEvent(fmodPath, out var eventDescription) != FMOD.RESULT.OK)
            {
                Debug.LogWarning($"[FmodAudioBackend] Описание события '{fmodPath}' не найдено.");
                return null;
            }

            FMOD.RESULT instResult = eventDescription.createInstance(out var instance);
            if (instResult != FMOD.RESULT.OK || !instance.isValid())
            {
                Debug.LogWarning($"[FmodAudioBackend] Не удалось создать экземпляр события '{fmodPath}': {instResult}");
                return null;
            }

            if (layer.IsSpatial)
            {
                if (targetGameObject != null)
                {
                    FMODUnity.RuntimeManager.AttachInstanceToGameObject(instance, targetGameObject);
                }
                else if (worldPosition.HasValue)
                {
                    var pos = worldPosition.Value;
                    instance.set3DAttributes(new FMOD.ATTRIBUTES_3D
                    {
                        position = new FMOD.VECTOR { x = pos.x, y = pos.y, z = 0f },
                        forward = ForwardVector,
                        up = UpVector,
                    });
                }
            }

            instance.setVolume(layer.Volume);
            instance.setPitch(layer.Pitch);
            instance.start();

            return new AudioPlaybackHandle(instance, layer.Bus);
        }

        public AudioPlaybackHandle PlaySnapshot(string snapshotPath)
        {
            string fullPath = snapshotPath.StartsWith("snapshot:/", StringComparison.OrdinalIgnoreCase) || snapshotPath.StartsWith("event:/", StringComparison.OrdinalIgnoreCase)
                ? snapshotPath
                : $"snapshot:/{snapshotPath}";

            if (FMODUnity.RuntimeManager.StudioSystem.getEvent(fullPath, out var eventDescription) != FMOD.RESULT.OK)
            {
                Debug.LogWarning($"[FmodAudioBackend] Snapshot '{fullPath}' не найден.");
                return null;
            }

            if (eventDescription.createInstance(out var instance) == FMOD.RESULT.OK && instance.isValid())
            {
                instance.start();
                return new AudioPlaybackHandle(instance, AudioBusType.Master);
            }

            return null;
        }

        public void SetGlobalParameter(string name, float value)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            FMODUnity.RuntimeManager.StudioSystem.setParameterByName(name, value);
        }

        public void Update()
        {
            // FMOD Studio C++ engine обновляет внутренние состояния нативно — внешних вызовов не требуется.
        }

        public void Shutdown()
        {
            foreach (var bank in _loadedBanks.Values)
            {
                bank.unload();
            }

            _loadedBanks.Clear();
        }
    }
}

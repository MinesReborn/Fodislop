using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fodinae.Scripts.Audio.Core;
using UnityEngine;

namespace Fodinae.Scripts.Audio.Backend
{
    /// <summary>
    /// FMOD Studio аудио-бэкенд с загрузкой банков с сервера.
    ///
    /// Для MMO: банки .bank скачиваются через ClientAssetLoader, кешируются (ETag),
    /// и загружаются в FMOD через loadBankMemory. Фоллбек: StreamingAssets/Audio/ для dev.
    ///
    /// Дакинг, VoiceLimit и приоритеты — всё нативно в FMOD Studio.
    ///
    /// Структура:
    /// <code>
    /// FodinaeAudio/Build/Desktop/*.bank   ← FMOD билдит → CDN игры
    /// Assets/StreamingAssets/Audio/       ← локальный dev-фоллбек
    /// </code>
    /// </summary>
    public sealed class FmodAudioBackend
    {
        private AudioSystem _system;
        private readonly Dictionary<int, FMOD.Studio.EventInstance> _voices = new();
        private readonly Dictionary<AudioBusType, FMOD.Studio.Bus> _fmodBuses = new();
        private readonly List<FMOD.Studio.Bank> _loadedBanks = new();
        private readonly List<int> _finishedVoiceIds = new();
        private int _nextHandleId;

        private const string BANKPATH = "banks";

        private static readonly string[] _requiredBanks =
        {
            "Master",
            "Master.strings",
        };

        private static readonly FMOD.VECTOR ForwardVector = new() { x = 0f, y = 0f, z = 1f };
        private static readonly FMOD.VECTOR UpVector = new() { x = 0f, y = 1f, z = 0f };

        public void Initialize(AudioSystem system)
        {
            _system = system;
            LoadAllBanks().Forget();
        }

        private async UniTaskVoid LoadAllBanks()
        {
            var loader = ClientAssetLoader.Instance;
            if (loader == null)
            {
                Debug.LogError("[FmodAudioBackend] ClientAssetLoader не доступен — банки не загружены");
                return;
            }

            foreach (var bankName in _requiredBanks)
            {
                byte[] bankBytes = null;
                var localPath = System.IO.Path.Combine(Application.streamingAssetsPath, "Audio", $"{bankName}.bank");

                if (System.IO.File.Exists(localPath))
                {
                    try
                    {
                        bankBytes = System.IO.File.ReadAllBytes(localPath);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[FmodAudioBackend] Не удалось прочитать локальный банк '{localPath}': {ex.Message}");
                    }
                }

                if (bankBytes == null || bankBytes.Length == 0)
                {
                    var bankFile = $"{BANKPATH}/{bankName}.bank";
                    try
                    {
                        bankBytes = await loader.GetAssetBytesAsync(bankFile, timeoutSeconds: 5);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[FmodAudioBackend] Ошибка сети при запросе банка '{bankName}': {ex.Message}");
                    }
                }

                // Check for valid bank data (not PNG fallback bytes 0x89 0x50 0x4E 0x47)
                if (bankBytes != null && bankBytes.Length > 8 && !(bankBytes[0] == 0x89 && bankBytes[1] == 0x50))
                {
                    LoadBankFromMemory(bankName, bankBytes);
                }
                else
                {
                    Debug.LogWarning($"[FmodAudioBackend] Банк '{bankName}' не найден или не является валидным FMOD банком");
                }
            }

            MapBuses();
        }

        private void LoadBankFromMemory(string bankName, byte[] bankData)
        {
            FMOD.RESULT result = FMODUnity.RuntimeManager.StudioSystem.loadBankMemory(
                bankData,
                FMOD.Studio.LOAD_BANK_FLAGS.NORMAL,
                out var bank);

            if (result == FMOD.RESULT.OK)
            {
                _loadedBanks.Add(bank);
            }
            else if (result == FMOD.RESULT.ERR_EVENT_ALREADY_LOADED)
            {
                // Bank was already automatically loaded by FMOD Unity RuntimeManager
            }
            else
            {
                Debug.LogWarning($"[FmodAudioBackend] loadBankMemory '{bankName}': {result}");
            }
        }

        private void MapBuses()
        {
            var busPaths = new Dictionary<AudioBusType, string>
            {
                { AudioBusType.Master,    "bus:/" },
                { AudioBusType.Sfx,       "bus:/sfx" },
                { AudioBusType.Music,     "bus:/music" },
                { AudioBusType.Voice,     "bus:/voice" },
                { AudioBusType.Ambience,  "bus:/ambience" },
                { AudioBusType.Ui,        "bus:/ui" },
            };

            FMODUnity.RuntimeManager.StudioSystem.getBus("bus:/", out var masterBus);

            foreach (var kvp in busPaths)
            {
                if (FMODUnity.RuntimeManager.StudioSystem.getBus(kvp.Value, out var bus) == FMOD.RESULT.OK)
                {
                    _fmodBuses[kvp.Key] = bus;
                }
                else
                {
                    _fmodBuses[kvp.Key] = masterBus;
                }
            }
        }

        public AudioPlaybackHandle CreateVoice(AudioEvent evt, AudioLayer layer, Vector3? worldPosition)
        {
            string fmodPath = $"event:/{evt.Name}";

            FMOD.Studio.EventInstance instance;
            try
            {
                instance = FMODUnity.RuntimeManager.CreateInstance(fmodPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FmodAudioBackend] FMOD event '{fmodPath}' not found or failed to load: {ex.Message}");
                return null;
            }

            if (!instance.isValid())
            {
                Debug.LogWarning($"[FmodAudioBackend] Событие '{fmodPath}' не найдено");
                return null;
            }

            if (layer.IsSpatial && worldPosition.HasValue)
            {
                var pos = worldPosition.Value;
                instance.set3DAttributes(new FMOD.ATTRIBUTES_3D
                {
                    position = new FMOD.VECTOR { x = pos.x, y = pos.y, z = 0f },
                    forward = ForwardVector,
                    up = UpVector,
                });
            }

            float effectiveVolume = layer.Volume * _system.GetBus(layer.Bus).EffectiveVolume;
            instance.setVolume(effectiveVolume);
            instance.setPitch(layer.Pitch * _system.GetBus(layer.Bus).Pitch);
            instance.start();

            var handleId = _nextHandleId++;
            var handle = new AudioPlaybackHandle(handleId, _system.GetBus(layer.Bus))
            {
                _isPlayingFunc = h => _voices.TryGetValue(h.HandleId, out var inst) && IsInstancePlaying(inst),
                _stopAction = (h, fade) => StopVoice(h, fade),
                _positionAction = (h, pos) => SetVoicePosition(h, pos),
                _volumeAction = (h, vol) => SetVoiceVolume(h, vol),
                _pitchAction = (h, pit) => SetVoicePitch(h, pit),
            };

            _voices[handleId] = instance;
            _system.GetBus(layer.Bus).ActiveVoiceCount++;

            return handle;
        }

        public void StopVoice(AudioPlaybackHandle handle, float fadeOut)
        {
            if (!_voices.TryGetValue(handle.HandleId, out var instance))
            {
                return;
            }

            var mode = fadeOut > 0f ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT : FMOD.Studio.STOP_MODE.IMMEDIATE;
            instance.stop(mode);
            instance.release();
            _voices.Remove(handle.HandleId);
            _system.GetBus(handle.Bus.BusType).ActiveVoiceCount--;
        }

        public void SetVoicePosition(AudioPlaybackHandle handle, Vector3 worldPosition)
        {
            if (_voices.TryGetValue(handle.HandleId, out var instance))
            {
                instance.set3DAttributes(new FMOD.ATTRIBUTES_3D
                {
                    position = new FMOD.VECTOR { x = worldPosition.x, y = worldPosition.y, z = 0f },
                    forward = ForwardVector,
                    up = UpVector,
                });
            }
        }

        public void SetVoiceVolume(AudioPlaybackHandle handle, float volume)
        {
            if (_voices.TryGetValue(handle.HandleId, out var instance))
            {
                instance.setVolume(volume * _system.GetBus(handle.Bus.BusType).EffectiveVolume);
            }
        }

        public void SetVoicePitch(AudioPlaybackHandle handle, float pitch)
        {
            if (_voices.TryGetValue(handle.HandleId, out var instance))
            {
                instance.setPitch(pitch);
            }
        }

        public void Update(float deltaTime)
        {
            // Sync bus volumes to FMOD
            foreach (var (busType, fmodBus) in _fmodBuses)
            {
                var audioBus = _system.GetBus(busType);
                fmodBus.setVolume(audioBus.EffectiveVolume);
            }

            // Cleanup voices that finished playing on their own (not stopped via handle)
            _finishedVoiceIds.Clear();
            foreach (var (id, instance) in _voices)
            {
                if (!IsInstancePlaying(instance))
                {
                    _finishedVoiceIds.Add(id);
                }
            }

            foreach (var id in _finishedVoiceIds)
            {
                if (_voices.TryGetValue(id, out var instance))
                {
                    instance.release();
                    _voices.Remove(id);
                }
            }
        }

        public void Shutdown()
        {
            foreach (var instance in _voices.Values)
            {
                instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                instance.release();
            }

            _voices.Clear();

            foreach (var bank in _loadedBanks)
            {
                bank.unload();
            }

            _loadedBanks.Clear();
        }

        private static bool IsInstancePlaying(FMOD.Studio.EventInstance instance)
        {
            instance.getPlaybackState(out var state);
            return state != FMOD.Studio.PLAYBACK_STATE.STOPPED;
        }
    }
}

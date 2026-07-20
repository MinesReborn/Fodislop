#if FMOD
using System.Collections.Generic;
using Fodinae.Scripts.Audio.Core;
using UnityEngine;

namespace Fodinae.Scripts.Audio.Backend
{
    /// <summary>
    /// FMOD Studio аудио-бэкенд.
    ///
    /// Подключается когда проект переходит на FMOD вместо сырых AudioSource.
    /// Для этого нужно:
    ///   1. Установить FMOD Unity Integration package (fmod.com)
    ///   2. Добавить FMOD в Scripting Define Symbols (FMOD)
    ///   3. Закомментировать UnityAudioBackend в AudioSystem.Awake()
    ///   4. Раскомментировать FmodAudioBackend
    ///
    /// AudioEvent.Name отображается на FMOD event path (event:/EventName).
    /// AudioBusType — на FMOD bus (bus:/Sfx, bus:/Music...).
    /// AudioLayer параметры — на event instance parameters (volume, pitch, 3D attributes).
    ///
    /// Вся логика шин (voice limit, ducking, priority) обрабатывается в FMOD Studio нативно.
    /// Здесь только проброс вызовов.
    /// </summary>
    public sealed class FmodAudioBackend : IAudioBackend
    {
        private AudioSystem _system;
        private readonly Dictionary<int, FMOD.Studio.EventInstance> _voices = new();
        private readonly Dictionary<AudioBusType, FMOD.Studio.Bus> _fmodBuses = new();
        private int _nextHandleId;

        public void Initialize(AudioSystem system)
        {
            _system = system;

            LoadBank("Master");
            LoadBank("Master.strings");
            LoadBank("SFX");
            LoadBank("Music");

            var busPaths = new Dictionary<AudioBusType, string>
            {
                { AudioBusType.Master,    "bus:/" },
                { AudioBusType.Sfx,       "bus:/SFX" },
                { AudioBusType.Music,     "bus:/Music" },
                { AudioBusType.Voice,     "bus:/Voice" },
                { AudioBusType.Ambience,  "bus:/Ambience" },
                { AudioBusType.Ui,        "bus:/UI" },
                { AudioBusType.Narrative, "bus:/Narrative" },
            };

            foreach (var kvp in busPaths)
            {
                FMOD.Studio.Bus bus;
                var result = FMODUnity.RuntimeManager.StudioSystem.getBus(kvp.Value, out bus);
                if (result == FMOD.RESULT.OK)
                {
                    _fmodBuses[kvp.Key] = bus;
                }
                else
                {
                    Debug.LogWarning($"[FmodAudioBackend] Шина '{kvp.Value}' не найдена в FMOD Studio — {result}");
                }
            }
        }

        public AudioPlaybackHandle CreateVoice(AudioEvent evt, AudioLayer layer, string assetPath, Vector3? worldPosition)
        {
            string fmodPath = $"event:/{evt.Name}";

            FMOD.Studio.EventInstance instance;
            var result = FMODUnity.RuntimeManager.CreateInstance(fmodPath, out instance);

            if (result != FMOD.RESULT.OK)
            {
                Debug.LogWarning($"[FmodAudioBackend] Событие '{fmodPath}' не найдено — {result}");
                return null;
            }

            if (layer.IsSpatial && worldPosition.HasValue)
            {
                var pos = worldPosition.Value;
                instance.set3DAttributes(new FMOD.ATTRIBUTES_3D
                {
                    position = new FMOD.VECTOR { x = pos.x, y = pos.y, z = 0f },
                    forward  = new FMOD.VECTOR { x = 0f, y = 0f, z = 1f },
                    up       = new FMOD.VECTOR { x = 0f, y = 1f, z = 0f },
                });
            }

            float effectiveVolume = layer.Volume * _system.GetBus(layer.Bus).EffectiveVolume;
            instance.setVolume(effectiveVolume);
            instance.setPitch(layer.Pitch * _system.GetBus(layer.Bus).Pitch);
            instance.start();

            var handle = new AudioPlaybackHandle(_nextHandleId, _system.GetBus(layer.Bus))
            {
                Priority = layer.Priority,
                StartTime = Time.unscaledTime,
                _isPlayingFunc  = h => _voices.TryGetValue(h.HandleId, out var inst) && IsInstancePlaying(inst),
                _stopAction     = (h, fade) => StopVoice(h, fade),
                _positionAction = (h, pos) => SetVoicePosition(h, pos),
                _volumeAction   = (h, vol) => SetVoiceVolume(h, vol),
                _pitchAction    = (h, pit) => SetVoicePitch(h, pit),
            };

            _voices[_nextHandleId] = instance;
            _system.GetBus(layer.Bus)._activeVoices++;
            _nextHandleId++;

            return handle;
        }

        public void StopVoice(AudioPlaybackHandle handle, float fadeOut)
        {
            if (!_voices.TryGetValue(handle.HandleId, out var instance))
                return;

            var mode = fadeOut > 0f ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT : FMOD.Studio.STOP_MODE.IMMEDIATE;
            instance.stop(mode);
            instance.release();
            _voices.Remove(handle.HandleId);
            _system.GetBus(handle.Bus.BusType)._activeVoices--;
        }

        public void SetVoicePosition(AudioPlaybackHandle handle, Vector3 worldPosition)
        {
            if (_voices.TryGetValue(handle.HandleId, out var instance))
            {
                instance.set3DAttributes(new FMOD.ATTRIBUTES_3D
                {
                    position = new FMOD.VECTOR { x = worldPosition.x, y = worldPosition.y, z = 0f },
                });
            }
        }

        public void SetVoiceVolume(AudioPlaybackHandle handle, float volume)
        {
            if (_voices.TryGetValue(handle.HandleId, out var instance))
                instance.setVolume(volume * _system.GetBus(handle.Bus.BusType).EffectiveVolume);
        }

        public void SetVoicePitch(AudioPlaybackHandle handle, float pitch)
        {
            if (_voices.TryGetValue(handle.HandleId, out var instance))
                instance.setPitch(pitch);
        }

        public bool IsPlaying(AudioPlaybackHandle handle)
        {
            if (!_voices.TryGetValue(handle.HandleId, out var instance))
                return false;

            FMOD.Studio.PLAYBACK_STATE state;
            instance.getPlaybackState(out state);
            return state == FMOD.Studio.PLAYBACK_STATE.PLAYING || state == FMOD.Studio.PLAYBACK_STATE.STARTING;
        }

        public void Update(float deltaTime)
        {
            foreach (var (busType, fmodBus) in _fmodBuses)
            {
                var audioBus = _system.GetBus(busType);
                fmodBus.setVolume(audioBus.EffectiveVolume);
            }

            // Дакинг, лимиты голосов и приоритеты — всё нативно в FMOD Studio.
        }

        public void Shutdown()
        {
            foreach (var instance in _voices.Values)
            {
                instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                instance.release();
            }

            _voices.Clear();
        }

        private static void LoadBank(string bankName)
        {
            try
            {
                FMODUnity.RuntimeManager.LoadBank(bankName, true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FmodAudioBackend] Банк '{bankName}': {ex.Message}");
            }
        }

        private static bool IsInstancePlaying(FMOD.Studio.EventInstance instance)
        {
            FMOD.Studio.PLAYBACK_STATE state;
            instance.getPlaybackState(out state);
            return state != FMOD.Studio.PLAYBACK_STATE.STOPPED;
        }
    }
}
#endif

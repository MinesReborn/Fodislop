using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fodinae.Scripts.Audio.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Fodinae.Scripts.Audio.Backend
{
    /// <summary>
    /// Стандартный аудио-бэкенд на UnityEngine.AudioSource.
    ///
    /// Создаёт по одному GameObject с AudioSource на каждый голос, применяет параметры
    /// шины (Volume, Pitch, EffectiveVolume) и слоя (IsSpatial, MinDistance, MaxDistance).
    ///
    /// Не использует Unity AudioMixer — весь микс считается в коде через систему шин.
    /// Это даёт полный контроль над уровнями, дакингом и лимитами голосов без зависимости от
    /// конкретного микшера (FMOD/Wwise/Unity).
    /// </summary>
    public sealed class UnityAudioBackend : IAudioBackend
    {
        private AudioSystem _system;
        private GameObject _poolRoot;
        private int _nextHandleId;

        private readonly Dictionary<int, Voice> _voices = new();
        private readonly List<int> _toRemove = new();

        public void Initialize(AudioSystem system)
        {
            _system = system;
            _poolRoot = new GameObject("[AudioBackend_Voices]");
            _poolRoot.transform.SetParent(system.transform);
            Object.DontDestroyOnLoad(_poolRoot);
        }

        public AudioPlaybackHandle CreateVoice(AudioEvent evt, AudioLayer layer, string assetPath, Vector3? worldPosition)
        {
            var go = new GameObject($"Voice_{assetPath}_{_nextHandleId}");
            go.transform.SetParent(_poolRoot.transform);

            if (worldPosition.HasValue)
                go.transform.position = worldPosition.Value;

            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = layer.IsSpatial ? 1f : 0f;
            source.minDistance = layer.MinDistance;
            source.maxDistance = layer.MaxDistance;
            source.volume = layer.Volume * _system.GetBus(layer.Bus).EffectiveVolume;
            source.pitch = layer.Pitch * _system.GetBus(layer.Bus).Pitch;
            source.outputAudioMixerGroup = null; // не используем Unity AudioMixer

            var handle = new AudioPlaybackHandle(_nextHandleId, _system.GetBus(layer.Bus))
            {
                Priority = layer.Priority,
                StartTime = Time.unscaledTime,
                _isPlayingFunc  = h => _voices.TryGetValue(h.HandleId, out var v) && v.Source != null && v.Source.isPlaying,
                _stopAction     = (h, fade) => StopVoice(h, fade),
                _positionAction = (h, pos) => SetVoicePosition(h, pos),
                _volumeAction   = (h, vol) => SetVoiceVolume(h, vol),
                _pitchAction    = (h, pit) => SetVoicePitch(h, pit),
            };

            var voice = new Voice
            {
                Handle = handle,
                GameObject = go,
                Source = source,
                Bus = layer.Bus,
                IsLoaded = false,
                IsSpatial = layer.IsSpatial,
            };

            _voices[_nextHandleId] = voice;
            _system.GetBus(layer.Bus)._activeVoices++;
            _nextHandleId++;

            LoadAndPlayAsync(voice, assetPath, layer).Forget();
            return handle;
        }

        private async UniTaskVoid LoadAndPlayAsync(Voice voice, string assetPath, AudioLayer layer)
        {
            try
            {
                var loader = ClientAssetLoader.Instance;
                if (loader == null)
                {
                    DisposeVoice(voice.Handle.HandleId);
                    return;
                }

                var clip = await loader.GetAudioAsync(assetPath, timeoutSeconds: 10);

                if (voice.Handle._disposed || voice.Source == null)
                    return;

                if (clip == null)
                {
                    Debug.LogWarning($"[UnityAudioBackend] Не удалось загрузить {assetPath}");
                    DisposeVoice(voice.Handle.HandleId);
                    return;
                }

                voice.Source.clip = clip;
                voice.Clip = clip;
                voice.IsLoaded = true;
                voice.Source.Play();

                // Храним клип для авто-очистки при остановке голоса
                // (Unity не освобождает AudioClip пока на него есть ссылка).
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityAudioBackend] Ошибка загрузки {assetPath}: {ex.Message}");
                DisposeVoice(voice.Handle.HandleId);
            }
        }

        public void StopVoice(AudioPlaybackHandle handle, float fadeOut)
        {
            if (!_voices.TryGetValue(handle.HandleId, out var voice))
                return;

            if (fadeOut <= 0f)
            {
                DisposeVoice(handle.HandleId);
                return;
            }

            // Плавное затухание: ставим источник на fade-out через корутину внутри бэкенда.
            voice.FadeOutRemaining = fadeOut;
            voice.FadeOutDuration = fadeOut;
        }

        public void SetVoicePosition(AudioPlaybackHandle handle, Vector3 worldPosition)
        {
            if (_voices.TryGetValue(handle.HandleId, out var voice) && voice.GameObject != null)
                voice.GameObject.transform.position = worldPosition;
        }

        public void SetVoiceVolume(AudioPlaybackHandle handle, float volume)
        {
            if (_voices.TryGetValue(handle.HandleId, out var voice) && voice.Source != null)
                voice.Source.volume = volume;
        }

        public void SetVoicePitch(AudioPlaybackHandle handle, float pitch)
        {
            if (_voices.TryGetValue(handle.HandleId, out var voice) && voice.Source != null)
                voice.Source.pitch = pitch;
        }

        public bool IsPlaying(AudioPlaybackHandle handle)
        {
            if (!_voices.TryGetValue(handle.HandleId, out var voice))
                return false;
            return voice.Source != null && voice.Source.isPlaying;
        }

        public void Update(float deltaTime)
        {
            // Обновляем дакинг: для каждой шины с активными голосами — накатываем гейн
            // на шины-жертвы.
            foreach (var bus in _system.GetAllBuses())
            {
                if (bus.ActiveVoiceCount > 0)
                {
                    foreach (var (targetType, state) in bus._duckingTargets)
                    {
                        var targetBus = _system.GetBus(targetType);
                        // Атака: если на шине-источнике есть голоса — давим.
                        float targetGain = 0f; // dB to linear
                        state._currentGain = Mathf.MoveTowards(
                            state._currentGain,
                            targetGain,
                            deltaTime / state.Attack);

                        targetBus.RegisterDuckSource(bus.BusType, state);
                    }
                }
                else
                {
                    foreach (var (targetType, state) in bus._duckingTargets)
                    {
                        // Релиз: голосов нет — отпускаем.
                        state._currentGain = Mathf.MoveTowards(
                            state._currentGain,
                            1f,
                            deltaTime / state.Release);

                        if (Mathf.Approximately(state._currentGain, 1f))
                        {
                            var targetBus = _system.GetBus(targetType);
                            bus.StopDucking(targetBus);
                        }
                    }
                }
            }

            // Обновляем громкость голосов с учётом EffectiveVolume шины
            _toRemove.Clear();
            foreach (var (id, voice) in _voices)
            {
                if (voice.Handle._disposed)
                {
                    _toRemove.Add(id);
                    continue;
                }

                if (voice.Source == null)
                {
                    _toRemove.Add(id);
                    continue;
                }

                // Фейд-аут
                if (voice.FadeOutRemaining > 0f)
                {
                    voice.FadeOutRemaining -= deltaTime;
                    float t = Mathf.Clamp01(voice.FadeOutRemaining / voice.FadeOutDuration);
                    voice.Source.volume = voice.BaseVolume * t * _system.GetBus(voice.Bus).EffectiveVolume;

                    if (voice.FadeOutRemaining <= 0f)
                    {
                        _toRemove.Add(id);
                        continue;
                    }
                }
                else
                {
                    voice.Source.volume = voice.BaseVolume * _system.GetBus(voice.Bus).EffectiveVolume;
                }

                // Проверка: клип отыграл?
                if (voice.IsLoaded && voice.Source.clip != null && !voice.Source.isPlaying &&
                    Time.unscaledTime - voice.Handle.StartTime > voice.Source.clip.length)
                {
                    _toRemove.Add(id);
                }
            }

            foreach (var id in _toRemove)
                DisposeVoice(id);

            // Применяем лимиты голосов
            foreach (var bus in _system.GetAllBuses())
            {
                if (bus.VoiceLimit <= 0) continue;
                if (bus.ActiveVoiceCount <= bus.VoiceLimit) continue;

                // Крадём голоса
                var toSteal = bus.ActiveVoiceCount - bus.VoiceLimit;
                var busHandleIds = new List<int>();
                foreach (var (id, voice) in _voices)
                {
                    if (voice.Bus == bus.BusType && !voice.Handle._disposed)
                        busHandleIds.Add(id);
                }

                if (bus.StealMode == VoiceStealMode.Oldest)
                {
                    busHandleIds.Sort((a, b) => _voices[a].Handle.StartTime.CompareTo(_voices[b].Handle.StartTime));
                }
                else
                {
                    busHandleIds.Sort((a, b) => _voices[a].Handle.Priority.CompareTo(_voices[b].Handle.Priority));
                }

                for (int i = 0; i < Mathf.Min(toSteal, busHandleIds.Count); i++)
                {
                    _voices[busHandleIds[i]].Handle.Stop();
                    _toRemove.Add(busHandleIds[i]);
                }
            }
        }

        public void Shutdown()
        {
            foreach (var id in _voices.Keys)
                DisposeVoice(id);
            _voices.Clear();

            if (_poolRoot != null)
                Object.Destroy(_poolRoot);
        }

        private void DisposeVoice(int handleId)
        {
            if (!_voices.TryGetValue(handleId, out var voice))
                return;

            voice.Handle._disposed = true;
            _system.GetBus(voice.Bus)._activeVoices--;

            if (voice.Source != null)
            {
                if (voice.Source.isPlaying)
                    voice.Source.Stop();
                Object.Destroy(voice.Source);
            }

            if (voice.Clip != null)
                Object.Destroy(voice.Clip);

            if (voice.GameObject != null)
                Object.Destroy(voice.GameObject);

            _voices.Remove(handleId);
        }

        private sealed class Voice
        {
            public AudioPlaybackHandle Handle;
            public GameObject GameObject;
            public AudioSource Source;
            public AudioClip Clip;
            public AudioBusType Bus;
            public bool IsLoaded;
            public bool IsSpatial;
            public float BaseVolume = 1f;
            public float FadeOutRemaining;
            public float FadeOutDuration;
        }
    }
}

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Fodinae.Scripts.Audio.Core;
using Fodinae.Scripts.Audio.Data;
using Fodinae.Scripts.Core;
using UnityEngine;

namespace Fodinae.Scripts.Audio.Backend
{
    /// <summary>
    /// Точка входа в аудио-домен — синглтон, висящий в DontDestroyOnLoad.
    ///
    /// Использует FmodAudioBackend для проигрывания FMOD Studio событий.
    /// Все события адресуются по строковому имени, соответствующему FMOD event path без prefix event:/.
    ///
    /// Пример: Play("sfx_dig") → FMOD event:/sfx_dig
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Gracefully catch startup exceptions to prevent game crash.")]
    public sealed class AudioSystem : SingletonMonoBehaviour<AudioSystem>
    {
        private readonly Dictionary<AudioBusType, AudioBus> _buses = new();
        private readonly Dictionary<string, AudioEvent> _events = new(System.StringComparer.OrdinalIgnoreCase);

        [SerializeField]
        [Tooltip("Библиотека аудио-событий. События регистрируются автоматически при старте.")]
        private AudioLibrary _audioLibrary;

        private FmodAudioBackend _backend;

        // ═══════════════════════════════════════════════════════════
        //  Public API
        // ═══════════════════════════════════════════════════════════

        public AudioBus GetBus(AudioBusType type)
        {
            return _buses.TryGetValue(type, out var bus) ? bus : _buses[AudioBusType.Master];
        }

        public IEnumerable<AudioBus> GetAllBuses() => _buses.Values;

        public void Register(AudioEvent evt)
        {
            if (evt == null || string.IsNullOrEmpty(evt.Name))
            {
                return;
            }

            _events[evt.Name] = evt;
        }

        public void RegisterAll(IEnumerable<AudioEvent> events)
        {
            foreach (var evt in events)
            {
                Register(evt);
            }
        }

        /// <summary>
        /// Найти зарегистрированное событие по имени или автоматически зарегистрировать
        /// событие FMOD с соответствующим дефолтным слоем (Music, Ui, Sfx).
        /// </summary>
        public AudioEvent FindEvent(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            // Strip event:/ or event: prefix for lookup
            string lookupName = name;
            if (lookupName.StartsWith("event:/", System.StringComparison.OrdinalIgnoreCase))
            {
                lookupName = lookupName.Substring("event:/".Length);
            }
            else if (lookupName.StartsWith("event:", System.StringComparison.OrdinalIgnoreCase))
            {
                lookupName = lookupName.Substring("event:".Length);
            }

            if (_events.TryGetValue(lookupName, out var evt))
            {
                return evt;
            }

            // Also check alternate name (sfx_bz <-> sfx/bz)
            string alternateName = lookupName;
            if (lookupName.StartsWith("sfx_", System.StringComparison.OrdinalIgnoreCase))
            {
                alternateName = "sfx/" + lookupName.Substring("sfx_".Length);
            }
            else if (lookupName.StartsWith("sfx/", System.StringComparison.OrdinalIgnoreCase))
            {
                alternateName = "sfx_" + lookupName.Substring("sfx/".Length);
            }

            if (_events.TryGetValue(alternateName, out var altEvt))
            {
                return altEvt;
            }

            AudioLayer defaultLayer = AudioLayer.SfxDefault();
            if (lookupName.StartsWith("music/", System.StringComparison.OrdinalIgnoreCase) || lookupName.StartsWith("music_", System.StringComparison.OrdinalIgnoreCase))
            {
                defaultLayer = AudioLayer.MusicDefault();
            }
            else if (lookupName.StartsWith("ui/", System.StringComparison.OrdinalIgnoreCase) || lookupName.StartsWith("ui_", System.StringComparison.OrdinalIgnoreCase))
            {
                defaultLayer = AudioLayer.UiDefault();
            }

            var dynamicEvt = AudioEvent.Create(lookupName, defaultLayer);
            _events[lookupName] = dynamicEvt;
            _events[alternateName] = dynamicEvt;
            return dynamicEvt;
        }

        /// <summary>Воспроизвести событие по имени с опциональной 3D-позицией.</summary>
        public AudioPlaybackHandle Play(string eventName, Vector3? worldPosition = null, AudioLayer? overrideLayer = null, float? overrideVolume = null)
        {
            var evt = FindEvent(eventName);
            return PlayEvent(evt, worldPosition, overrideLayer, overrideVolume);
        }

        public AudioPlaybackHandle PlayEvent(AudioEvent evt, Vector3? worldPosition = null, AudioLayer? overrideLayer = null, float? overrideVolume = null)
        {
            if (evt == null)
            {
                return null;
            }

            var layer = overrideLayer ?? evt.DefaultLayer;

            if (evt.PitchVariation > 0f)
            {
                float variation = UnityEngine.Random.Range(-evt.PitchVariation, evt.PitchVariation);
                layer = new AudioLayer
                {
                    Bus = layer.Bus,
                    Volume = overrideVolume ?? evt.DefaultVolume,
                    Pitch = layer.Pitch + variation,
                    IsSpatial = layer.IsSpatial,
                };
            }
            else if (overrideVolume.HasValue)
            {
                layer = new AudioLayer
                {
                    Bus = layer.Bus,
                    Volume = overrideVolume.Value,
                    Pitch = layer.Pitch,
                    IsSpatial = layer.IsSpatial,
                };
            }

            return _backend?.CreateVoice(evt, layer, worldPosition);
        }

        /// <summary>Воспроизвести 3D-событие на заданной позиции в мире.</summary>
        public AudioPlaybackHandle PlayAt(string eventName, Vector3 worldPosition, AudioLayer? layer = null, float? volume = null)
            => Play(eventName, worldPosition, layer, volume);

        /// <summary>Воспроизвести 2D-событие (без пространственного позиционирования).</summary>
        public AudioPlaybackHandle Play2D(string eventName, AudioLayer? layer = null, float? volume = null)
            => Play(eventName, null, layer, volume);

        // ═══════════════════════════════════════════════════════════
        //  Protected Lifecycle Methods
        // ═══════════════════════════════════════════════════════════

        protected override void OnAwake()
        {
            CreateBuses();

            if (_audioLibrary != null)
            {
                RegisterAll(_audioLibrary.Events);
            }

            // Load saved volume settings from PlayerPrefs
            GetBus(AudioBusType.Music).Volume = PlayerPrefs.GetFloat("Audio_Ambient", 0.5f);
            GetBus(AudioBusType.Sfx).Volume = PlayerPrefs.GetFloat("Audio_Sfx", 1f);

            _backend = new FmodAudioBackend();
            _backend.Initialize(this);

            // Play ambient background music
            try
            {
                Play2D("music/ambient_bg", AudioLayer.MusicDefault(), 0.5f);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[AudioSystem] Не удалось загрузить эмбиент: {ex.Message}");
            }
        }

        protected override void OnDestroyed()
        {
            _backend?.Shutdown();
            _backend = null;
        }

        // ═══════════════════════════════════════════════════════════
        //  Private Methods
        // ═══════════════════════════════════════════════════════════

        private void Update()
        {
            _backend?.Update(Time.unscaledDeltaTime);
        }

        private void CreateBuses()
        {
            AddBus(new AudioBus(AudioBusType.Master));
            AddBus(new AudioBus(AudioBusType.Sfx));
            AddBus(new AudioBus(AudioBusType.Music));
            AddBus(new AudioBus(AudioBusType.Voice));
            AddBus(new AudioBus(AudioBusType.Ambience));
            AddBus(new AudioBus(AudioBusType.Ui));
        }

        private void AddBus(AudioBus bus)
        {
            _buses[bus.BusType] = bus;
        }
    }
}

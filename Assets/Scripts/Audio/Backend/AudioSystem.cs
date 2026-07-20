using System.Collections.Generic;
using Fodinae.Scripts.Audio.Core;
using Fodinae.Scripts.Utils;
using UnityEngine;

namespace Fodinae.Scripts.Audio.Backend
{
    /// <summary>
    /// Точка входа в аудио-домен — синглтон, висящий в DontDestroyOnLoad.
    ///
    /// <b>ЧТО ЭТО ДЕЛАЕТ:</b>
    /// <list type="bullet">
    ///   <item>Регистрирует шины (Master, Sfx, Music, Voice, Ambience, Ui, Narrative)</item>
    ///   <item>Держит реестр аудио-событий (AudioEvent по имени)</item>
    ///   <item>Принимает вызовы Play() → создаёт голос через бэкенд → возвращает AudioPlaybackHandle</item>
    ///   <item>Каждый кадр обновляет бэкенд (дакинг, лимиты голосов, очистка отыгравших)</item>
    ///   <item>Даёт удобные шорткаты для звукорежиссёра:
    ///     <c>AudioSystem.Play("dig_rock")</c>,
    ///     <c>AudioSystem.PlayAt("explosion", transform.position)</c>
    ///   </item>
    /// </list>
    ///
    /// <b>КАК РАСШИРИТЬ ДО FMOD:</b>
    /// Замени <see cref="_backend"/> на FmodAudioBackend в Awake().
    /// Шины и события остаются те же — бэкенд сам разберётся как их отобразить на FMOD Studio buses/events.
    /// </summary>
    public sealed class AudioSystem : SingletonMonoBehaviour<AudioSystem>
    {
        private IAudioBackend _backend;
        private readonly Dictionary<AudioBusType, AudioBus> _buses = new();
        private readonly Dictionary<string, AudioEvent> _events = new();

        protected override void OnAwake()
        {
            CreateBuses();
            RegisterDefaults();
            _backend = new UnityAudioBackend();
            _backend.Initialize(this);
        }

        private void Update()
        {
            _backend?.Update(Time.unscaledDeltaTime);
        }

        protected override void OnDestroyed()
        {
            _backend?.Shutdown();
            _backend = null;
        }

        // ═══════════════════════════════════════════════════════════
        //  Шины
        // ═══════════════════════════════════════════════════════════

        private void CreateBuses()
        {
            AddBus(new AudioBus(AudioBusType.Master)    { VoiceLimit = 0 });
            AddBus(new AudioBus(AudioBusType.Sfx)       { VoiceLimit = 32 });
            AddBus(new AudioBus(AudioBusType.Music)     { VoiceLimit = 2 });
            AddBus(new AudioBus(AudioBusType.Voice)     { VoiceLimit = 1 });
            AddBus(new AudioBus(AudioBusType.Ambience)  { VoiceLimit = 8 });
            AddBus(new AudioBus(AudioBusType.Ui)        { VoiceLimit = 4 });
            AddBus(new AudioBus(AudioBusType.Narrative) { VoiceLimit = 1 });
        }

        private void AddBus(AudioBus bus)
        {
            _buses[bus.BusType] = bus;
        }

        /// <summary>Регистрирует события без которых игра не запустится (эмбиент, базовые SFX).</summary>
        private void RegisterDefaults()
        {
            // Фоновый эмбиент — загружается AudioManager при старте.
            Register(AudioEvent.Create(
                "ambient_bg",
                new[] { "audio/evil_huge" },
                AudioLayer.MusicDefault()));

            // Примечание: полный список событий регистрируется через AudioLibrary.asset
            // в сцене или через код. Здесь только неубираемый минимум.
        }

        /// <summary>Получить шину по идентификатору.</summary>
        public AudioBus GetBus(AudioBusType type)
        {
            return _buses.TryGetValue(type, out var bus) ? bus : _buses[AudioBusType.Master];
        }

        /// <summary>Все зарегистрированные шины (для итерации бэкендом).</summary>
        public IEnumerable<AudioBus> GetAllBuses() => _buses.Values;

        // ═══════════════════════════════════════════════════════════
        //  Реестр событий
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Зарегистрировать аудио-событие.
        /// Обычно вызывается при старте из AudioLibrary (ScriptableObject) или программно.
        /// </summary>
        public void Register(AudioEvent evt)
        {
            if (evt == null || string.IsNullOrEmpty(evt.Name)) return;
            _events[evt.Name] = evt;
        }

        /// <summary>Массовая регистрация.</summary>
        public void RegisterAll(IEnumerable<AudioEvent> events)
        {
            foreach (var evt in events)
                Register(evt);
        }

        /// <summary>Найти событие по имени. null если не зарегистрировано.</summary>
        public AudioEvent FindEvent(string name)
        {
            return _events.TryGetValue(name, out var evt) ? evt : null;
        }

        // ═══════════════════════════════════════════════════════════
        //  Play — основной API
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Проиграть аудио-событие по имени.
        /// Событие должно быть зарегистрировано через Register().
        /// </summary>
        /// <param name="eventName">Имя зарегистрированного события.</param>
        /// <param name="worldPosition">Позиция в мире (только для пространственных звуков). null = 2D (не-пространственный).</param>
        /// <param name="overrideLayer">Переопределить параметры слоя. null = использовать DefaultLayer события.</param>
        /// <param name="overrideVolume">Переопределить громкость. null = использовать DefaultVolume события.</param>
        /// <returns>Хендл для управления голосом, или null если событие не найдено.</returns>
        public AudioPlaybackHandle Play(string eventName, Vector3? worldPosition = null, AudioLayer? overrideLayer = null, float? overrideVolume = null)
        {
            var evt = FindEvent(eventName);
            if (evt == null)
            {
                Debug.LogWarning($"[AudioSystem] Событие '{eventName}' не зарегистрировано");
                return null;
            }

            return PlayEvent(evt, worldPosition, overrideLayer, overrideVolume);
        }

        /// <summary>Проиграть событие напрямую (без поиска по имени).</summary>
        public AudioPlaybackHandle PlayEvent(AudioEvent evt, Vector3? worldPosition = null, AudioLayer? overrideLayer = null, float? overrideVolume = null)
        {
            if (evt == null || evt.AssetPaths == null || evt.AssetPaths.Length == 0)
                return null;

            var layer = overrideLayer ?? evt.DefaultLayer;
            var assetPath = evt.PickAsset();
            if (string.IsNullOrEmpty(assetPath))
                return null;

            // Применяем вариацию питча
            if (evt.PitchVariation > 0f)
            {
                float variation = Random.Range(-evt.PitchVariation, evt.PitchVariation);
                layer = new AudioLayer
                {
                    Bus = layer.Bus,
                    Volume = overrideVolume ?? evt.DefaultVolume,
                    Pitch = layer.Pitch + variation,
                    Priority = layer.Priority,
                    IsSpatial = layer.IsSpatial,
                    MinDistance = layer.MinDistance,
                    MaxDistance = layer.MaxDistance,
                };
            }
            else if (overrideVolume.HasValue)
            {
                layer = new AudioLayer
                {
                    Bus = layer.Bus,
                    Volume = overrideVolume.Value,
                    Pitch = layer.Pitch,
                    Priority = layer.Priority,
                    IsSpatial = layer.IsSpatial,
                    MinDistance = layer.MinDistance,
                    MaxDistance = layer.MaxDistance,
                };
            }

            return _backend?.CreateVoice(evt, layer, assetPath, worldPosition);
        }

        /// <summary>
        /// Шорткат: проиграть событие в указанной мировой позиции.
        /// <c>AudioSystem.PlayAt("explosion", transform.position)</c>
        /// </summary>
        public AudioPlaybackHandle PlayAt(string eventName, Vector3 worldPosition, AudioLayer? layer = null, float? volume = null)
        {
            return Play(eventName, worldPosition, layer, volume);
        }

        /// <summary>
        /// Шорткат: проиграть не-пространственное событие (UI, музыка).
        /// <c>AudioSystem.Play2D("ui_click")</c>
        /// </summary>
        public AudioPlaybackHandle Play2D(string eventName, AudioLayer? layer = null, float? volume = null)
        {
            return Play(eventName, null, layer, volume);
        }

        /// <summary>
        /// Шорткат для звукорежиссёра: проиграть событие из Transform'а источника.
        /// Позиция обновляется автоматически каждый кадр через AudioSpatial компонент.
        /// </summary>
        public AudioPlaybackHandle PlayAttached(string eventName, Transform followTarget, AudioLayer? layer = null, float? volume = null)
        {
            var handle = Play(eventName, followTarget.position, layer, volume);
            if (handle != null)
            {
                // Клиент должен сам обновлять позицию через handle.SetPosition() в Update.
                // Для удобства можно повесить компонент AudioSpatial на followTarget.
            }

            return handle;
        }
    }
}

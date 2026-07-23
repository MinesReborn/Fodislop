using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Fodinae.Scripts.Audio.Core;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using UnityEngine;

namespace Fodinae.Scripts.Audio.Backend
{
    /// <summary>
    /// Точка входа в аудио-домен — синглтон, висящий в DontDestroyOnLoad.
    ///
    /// Использует FmodAudioBackend для проигрывания FMOD Studio событий.
    /// Все события адресуются по строковому имени, соответствующему FMOD event path без prefix event:/.
    ///
    /// Пример: Play("sfx/dig") → FMOD event:/sfx/dig
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Gracefully catch startup exceptions to prevent game crash.")]
    public sealed class AudioSystem : SingletonMonoBehaviour<AudioSystem>, IAudioSystem
    {
        private const string TAG = "[AudioSystem]";
        private FmodAudioBackend _backend;

        public float GetBusVolume(AudioBusType type)
        {
            return _backend?.GetBusVolume(type) ?? 1f;
        }

        public void SetBusVolume(AudioBusType type, float volume)
        {
            _backend?.SetBusVolume(type, volume);
        }

        /// <summary>
        /// Динамическая загрузка доп. банков (фич/локаций) с CDN или локального хранилища.
        /// </summary>
        public async Cysharp.Threading.Tasks.UniTask<bool> EnsureBankLoadedAsync(string bankName)
        {
            if (_backend != null)
            {
                return await _backend.EnsureBankLoadedAsync(bankName);
            }

            Debug.LogWarning($"{TAG} Cannot load bank '{bankName}': backend not initialized");
            return false;
        }

        /// <summary>
        /// Выгрузка банка из памяти (вызывать при выходе из зоны / завершении фичи).
        /// </summary>
        public void UnloadBank(string bankName)
        {
            _backend?.UnloadBank(bankName);
        }

        /// <summary>Воспроизвести событие по имени с опциональной 3D-позицией.</summary>
        public AudioPlaybackHandle Play(string eventName, Vector3? worldPosition = null, AudioLayer? overrideLayer = null, float? overrideVolume = null)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                return null;
            }

            var layer = overrideLayer ?? AudioLayer.SFXDefault();
            if (overrideVolume.HasValue)
            {
                layer.Volume = overrideVolume.Value;
            }

            var handle = _backend?.CreateVoice(eventName, layer, worldPosition);
            if (handle == null)
            {
                Debug.LogWarning($"{TAG} Failed to play '{eventName}': backend returned null");
            }

            return handle;
        }

        /// <summary>Воспроизвести 3D-событие с нативной привязкой FMOD к GameObject (позиция/поворот следуют автоматически в C++).</summary>
        public AudioPlaybackHandle PlayAttached(string eventName, GameObject targetGameObject, AudioLayer? overrideLayer = null, float? overrideVolume = null)
        {
            if (string.IsNullOrEmpty(eventName) || targetGameObject == null)
            {
                return null;
            }

            var layer = overrideLayer ?? AudioLayer.SFXDefault();
            if (overrideVolume.HasValue)
            {
                layer.Volume = overrideVolume.Value;
            }

            var handle = _backend?.CreateVoice(eventName, layer, null, targetGameObject);
            if (handle == null)
            {
                Debug.LogWarning($"{TAG} Failed to play attached '{eventName}': backend returned null");
            }

            return handle;
        }

        /// <summary>Воспроизвести FMOD Snapshot (например "snapshot:/Cave_Ambient").</summary>
        public AudioPlaybackHandle PlaySnapshot(string snapshotPath)
        {
            if (string.IsNullOrEmpty(snapshotPath))
            {
                return null;
            }

            var handle = _backend?.PlaySnapshot(snapshotPath);
            if (handle == null)
            {
                Debug.LogWarning($"{TAG} Failed to play snapshot '{snapshotPath}': backend returned null");
            }

            return handle;
        }

        /// <summary>Установить значения глобального FMOD параметра в Studio (например "Depth", "Weather").</summary>
        public void SetGlobalParameter(string parameterName, float value)
        {
            _backend?.SetGlobalParameter(parameterName, value);
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

        /// <summary>
        /// Применяет сохранённые в PlayerPrefs значения громкости для всех 6 шин FMOD Studio.
        /// </summary>
        public void ApplySavedBusVolumes()
        {
            SetBusVolume(AudioBusType.Master, PlayerPrefs.GetFloat("Audio_Master", 1f));
            SetBusVolume(AudioBusType.SFX, PlayerPrefs.GetFloat("Audio_SFX", PlayerPrefs.GetFloat("Audio_Sfx", 1f)));
            SetBusVolume(AudioBusType.Music, PlayerPrefs.GetFloat("Audio_Music", PlayerPrefs.GetFloat("Audio_Ambient", 0.5f)));
            SetBusVolume(AudioBusType.Voice, PlayerPrefs.GetFloat("Audio_Voice", 1f));
            SetBusVolume(AudioBusType.Ambience, PlayerPrefs.GetFloat("Audio_Ambience", 0.7f));
            SetBusVolume(AudioBusType.UI, PlayerPrefs.GetFloat("Audio_UI", 1f));
        }

        protected override void OnAwake()
        {
            ServiceLocator.Register<IAudioSystem>(this);
            _backend = new FmodAudioBackend();
            _backend.Initialize(this);
            ApplySavedBusVolumes();
        }

        protected override void OnDestroyed()
        {
            _backend?.Shutdown();
            _backend = null;
        }

        private void Update()
        {
            _backend?.Update();
        }
    }
}

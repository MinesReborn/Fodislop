#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Audio.Core;
using Kern.Core;
using Kern.Core.Interfaces;
using UnityEngine;
using VContainer;
using UnityAudioSettings = UnityEngine.AudioSettings;

namespace Kern.Audio.Backend
{
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Gracefully catch startup exceptions to prevent game crash.")]
    [DefaultExecutionOrder(-10000)]
    public sealed class AudioSystem : MonoBehaviour, IAudioSystem
    {
        private const string TAG = "[AudioSystem]";

        private FmodAudioBackend _backend = null!;
        private bool _configApplied;
        private bool _configWaitLogged;
        private bool _pausedInBackground;

        [Inject]
        private IClientConfigManager _clientConfig = null!;

        [Inject]
        private IAsyncOperationSupervisor _operations = null!;

        public bool IsInitialized => _backend != null;

        public bool IsDegraded => _backend == null || _backend.IsDegraded;

        public UniTask WaitUntilBanksReadyAsync(CancellationToken cancellationToken = default)
            => _backend.WaitUntilReadyAsync(this, cancellationToken);

        private void Awake()
        {
            _backend = new FmodAudioBackend();
        }

        private void Start()
        {
            _operations.Run(
                "wait_audio_banks",
                cancellationToken => _backend.WaitUntilReadyAsync(this, cancellationToken));
        }

        private void Update()
        {
            if (!_configApplied)
            {
                TryApplySavedBusVolumes();
            }
        }

        private void OnEnable()
        {
            UnityAudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        }

        private void OnDisable()
        {
            UnityAudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
        }

        private void OnDestroy()
        {
            _backend?.StopAll();
        }

        private void OnAudioConfigurationChanged(bool deviceChanged)
        {
            if (!deviceChanged)
            {
                return;
            }

            // Смена устройства сбрасывает громкости шин на авторские: сами
            // шины принадлежат FMOD, пересоздавать их не нужно, а вот наши
            // значения после сброса надо наложить заново.
            Debug.Log($"{TAG} Устройство вывода сменилось — применяю громкости заново.");
            _configApplied = false;
            TryApplySavedBusVolumes();
            _backend.SetPaused(_pausedInBackground);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            bool shouldPause = !hasFocus &&
                _clientConfig?.Config != null &&
                _clientConfig.Config.Audio.MuteInBackground;
            if (_pausedInBackground == shouldPause)
            {
                return;
            }

            _backend?.SetPaused(shouldPause);
            _pausedInBackground = shouldPause;
        }

        public IAudioPlaybackHandle? Play(
            string eventName,
            Vector3? worldPosition = null,
            AudioLayer? overrideLayer = null,
            float? overrideVolume = null)
            => CreateVoice(eventName, worldPosition, null, overrideLayer, overrideVolume);

        public IAudioPlaybackHandle? PlayAttached(
            string eventName,
            GameObject targetGameObject,
            AudioLayer? overrideLayer = null,
            float? overrideVolume = null)
            => targetGameObject == null
                ? null
                : CreateVoice(eventName, null, targetGameObject, overrideLayer, overrideVolume);

        public IAudioPlaybackHandle? PlayAt(
            string eventName,
            Vector3 worldPosition,
            AudioLayer? layer = null,
            float? volume = null)
            => CreateVoice(eventName, worldPosition, null, layer, volume);

        public IAudioPlaybackHandle? Play2D(string eventName, AudioLayer? layer = null, float? volume = null)
            => CreateVoice(eventName, null, null, layer, volume);

        private IAudioPlaybackHandle? CreateVoice(
            string eventName,
            Vector3? worldPosition,
            GameObject? targetGameObject,
            AudioLayer? overrideLayer,
            float? overrideVolume)
        {
            if (string.IsNullOrEmpty(eventName) || _backend == null)
            {
                return null;
            }

            AudioLayer layer = overrideLayer ?? AudioLayer.SFXDefault();
            if (overrideVolume.HasValue)
            {
                layer.Volume = overrideVolume.Value;
            }

            return _backend.CreateVoice(eventName, layer, worldPosition, targetGameObject);
        }

        public float GetBusVolume(AudioBusType type) => _backend?.GetBusVolume(type) ?? 1f;

        public void SetBusVolume(AudioBusType type, float volume)
        {
            _backend?.SetBusVolume(type, volume);
        }

        private void TryApplySavedBusVolumes()
        {
            if (_configApplied)
            {
                return;
            }

            if (_clientConfig?.Config == null)
            {
                if (!_configWaitLogged)
                {
                    Debug.Log($"{TAG} Жду ClientConfigManager, чтобы применить настройки звука.");
                    _configWaitLogged = true;
                }

                return;
            }

            ApplySavedBusVolumes();
        }

        public void ApplySavedBusVolumes()
        {
            if (_clientConfig?.Config == null)
            {
                return;
            }

            ClientConfig config = _clientConfig.Config;
            foreach (AudioBusRegistry.BusBinding binding in AudioBusRegistry.Buses)
            {
                SetBusVolume(binding.Bus, binding.Read(config.Audio));
            }

            _configApplied = true;
        }
    }
}

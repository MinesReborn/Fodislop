#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Audio.Core;
using Kern.Core;
using Kern.Core.Interfaces;
using UnityEngine;
using VContainer;
using UnityAudioSettings = UnityEngine.AudioSettings;

namespace Kern.Infrastructure.Audio
{
    [DefaultExecutionOrder(-10000)]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Kern.Audio.Backend", sourceAssembly: null, sourceClassName: "AudioSystem")]
    public sealed class AudioSystem : MonoBehaviour, IAudioSystem
    {
        private const string TAG = "[AudioSystem]";

        private FmodAudioBackend _backend = null!;
        private bool _configApplied;
        private bool _configWaitLogged;
        private bool _bankWaitStarted;
        private bool _pausedInBackground;
        private int _bankRevision = -1;
        private bool _bankRefreshRunning;

        [Inject]
        private IClientConfigManager _clientConfig = null!;
        [Inject]
        private IAssetLoader _assetLoader = null!;
        [Inject]
        private IAudioBankManifestState _audioBankManifest = null!;

        [Inject]
        private IAsyncOperationSupervisor _operations = null!;

        public bool IsInitialized => _backend != null;

        public bool IsDegraded => _backend == null || _backend.IsDegraded;

        public bool IsBackgroundMuted => _pausedInBackground;

        public UniTask WaitUntilBanksReadyAsync(CancellationToken cancellationToken = default)
            => _backend.WaitUntilReadyAsync(this, _assetLoader, _audioBankManifest, cancellationToken);

        private void Awake()
        {
            _backend = new FmodAudioBackend();
        }

        private void Start()
        {
            TryStartBankWait();
        }

        private void Update()
        {
            TryStartBankWait();
            TryRefreshBanks();
            if (!_configApplied)
            {
                TryApplySavedBusVolumes();
            }
        }

        // Сервер может сменить набор банков посреди игры: изменившиеся перезагружаются, не прерывая её
        private void TryRefreshBanks()
        {
            if (_bankRefreshRunning ||
                _operations == null ||
                _backend == null ||
                !_backend.IsReady ||
                _audioBankManifest.Revision == _bankRevision)
            {
                return;
            }

            _bankRevision = _audioBankManifest.Revision;
            _bankRefreshRunning = true;
            _operations.Run(
                "refresh_audio_banks",
                async cancellationToken =>
                {
                    try
                    {
                        await _backend.RefreshBanksAsync(
                            this,
                            _assetLoader,
                            _audioBankManifest,
                            cancellationToken);
                    }
                    finally
                    {
                        _bankRefreshRunning = false;
                    }
                });
        }

        private void TryStartBankWait()
        {
            if (_bankWaitStarted || _operations == null || _backend == null)
            {
                return;
            }

            _bankWaitStarted = true;
            _operations.Run(
                "wait_audio_banks",
                cancellationToken => _backend.WaitUntilReadyAsync(
                    this,
                    _assetLoader,
                    _audioBankManifest,
                    cancellationToken));
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

            AudioLayer layer = overrideLayer ?? AudioLayer.SoundEffectsDefault();
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

        public void StopBus(AudioBusType type, float fadeOut = 0f)
        {
            _backend?.StopBus(type, fadeOut > 0f);
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

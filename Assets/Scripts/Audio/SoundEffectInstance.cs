using System;
using Cysharp.Threading.Tasks;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Scripts.Audio
{
    public class SoundEffectInstance
    {
        private const float UNLOADED_TIMEOUT = 15f;

        private readonly SFX _sfxType;
        private readonly float _requestStartTime;
        private readonly string _filename;
        private readonly float _volume;

        private AudioClip _clip;
        private GameObject _audioObject;
        private AudioSource _source;
        private bool _isLoaded;
        private bool _isDisposed;

        public bool IsDisposed => _isDisposed;
        public bool IsLoaded => _isLoaded;

        private float ElapsedTime => Time.realtimeSinceStartup - _requestStartTime;
        private bool IsTimedOut => !_isLoaded && ElapsedTime > UNLOADED_TIMEOUT;
        private bool IsFinished => _isLoaded && _clip != null && ElapsedTime >= _clip.length;

        public SoundEffectInstance(SFX sfxType, string filename, float volume)
        {
            _sfxType = sfxType;
            _filename = filename;
            _volume = volume;
            _requestStartTime = Time.realtimeSinceStartup;

            _audioObject = new GameObject($"SFX_{sfxType}_{_requestStartTime:F3}");
            _source = _audioObject.AddComponent<AudioSource>();
            _source.volume = volume;
            _source.playOnAwake = false;
            _source.loop = false;
            UnityEngine.Object.DontDestroyOnLoad(_audioObject);

            LoadAsync().Forget();
        }

        private async UniTaskVoid LoadAsync()
        {
            try
            {
                byte[] audioBytes = await ClientAssetLoader.Instance.GetAssetBytesAsync(
                    _filename,
                    timeoutSeconds: (int)UNLOADED_TIMEOUT
                );

                if (_isDisposed)
                    return;

                if (audioBytes == null || audioBytes.Length == 0)
                {
                    Debug.LogWarning($"[SoundEffectInstance] No audio data received for {_sfxType} ({_filename})");
                    Dispose();
                    return;
                }

                _clip = WavUtility.ToAudioClip(audioBytes, $"SFX_{_sfxType}");

                if (_isDisposed || _clip == null)
                {
                    Dispose();
                    return;
                }

                _isLoaded = true;

                // If the virtual play time already exceeds the clip duration, discard
                float elapsed = ElapsedTime;
                if (elapsed >= _clip.length)
                {
                    Dispose();
                    return;
                }

                // Play from the correct time offset (seek to virtual position)
                _source.clip = _clip;
                _source.time = Mathf.Clamp(elapsed, 0f, _clip.length - 0.001f);
                _source.Play();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SoundEffectInstance] Failed to load SFX {_sfxType}: {ex.Message}");
                if (!_isDisposed)
                    Dispose();
            }
        }

        public void Update()
        {
            if (_isDisposed) return;

            // Unloaded timeout: 15s without data → destroy
            if (!_isLoaded && IsTimedOut)
            {
                Debug.Log($"[SoundEffectInstance] Timeout for {_sfxType} (not loaded within {UNLOADED_TIMEOUT}s)");
                Dispose();
                return;
            }

            // Loaded: destroy when virtual play time exceeds clip duration
            if (_isLoaded && _clip != null && IsFinished)
            {
                Dispose();
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            if (_source != null)
            {
                if (_source.isPlaying)
                    _source.Stop();
                UnityEngine.Object.Destroy(_source);
            }

            if (_audioObject != null)
            {
                UnityEngine.Object.Destroy(_audioObject);
                _audioObject = null;
            }

            if (_clip != null)
            {
                UnityEngine.Object.Destroy(_clip);
                _clip = null;
            }
        }
    }
}

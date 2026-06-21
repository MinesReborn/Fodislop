using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Scripts.Audio
{
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        private AudioSource _ambientSource;
        private readonly List<SoundEffectInstance> _activeInstances = new();
        private readonly List<SoundEffectInstance> _pendingRemove = new();

        private float _ambientVolume = 0.5f;
        private float _sfxVolume = 1f;

        public float AmbientVolume
        {
            get => _ambientVolume;
            set
            {
                _ambientVolume = Mathf.Clamp01(value);
                if (_ambientSource != null)
                    _ambientSource.volume = _ambientVolume;
                PlayerPrefs.SetFloat("Audio_Ambient", _ambientVolume);
                PlayerPrefs.Save();
            }
        }

        public float SfxVolume
        {
            get => _sfxVolume;
            set
            {
                _sfxVolume = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat("Audio_Sfx", _sfxVolume);
                PlayerPrefs.Save();
            }
        }

        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _ambientSource = gameObject.AddComponent<AudioSource>();
            _ambientSource.loop = true;
            _ambientSource.playOnAwake = false;

            _ambientVolume = PlayerPrefs.GetFloat("Audio_Ambient", 0.5f);
            _sfxVolume = PlayerPrefs.GetFloat("Audio_Sfx", 1f);

            LoadAmbientAsync().Forget();
        }

        private async UniTaskVoid LoadAmbientAsync()
        {
            try
            {
                var audioBytes = await ClientAssetLoader.Instance.GetAssetBytesAsync(
                    "audio/evil_huge",
                    timeoutSeconds: 30
                );

                if (audioBytes == null || audioBytes.Length == 0)
                {
                    Debug.LogWarning("[AudioManager] Failed to load ambient audio from server");
                    return;
                }

                var clip = WavUtility.ToAudioClip(audioBytes, "Ambient_evil_huge");
                if (clip != null)
                {
                    _ambientSource.clip = clip;
                    _ambientSource.volume = _ambientVolume;
                    _ambientSource.Play();
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[AudioManager] Failed to load ambient: {ex.Message}");
            }
        }

        public void PlaySfx(SFX type)
        {
            var filename = $"audio/{type.ToString().ToLowerInvariant()}";
            var instance = new SoundEffectInstance(type, filename, _sfxVolume);
            _activeInstances.Add(instance);
        }

        void Update()
        {
            for (int i = _activeInstances.Count - 1; i >= 0; i--)
            {
                var instance = _activeInstances[i];
                instance.Update();
                if (instance.IsDisposed)
                {
                    _activeInstances.RemoveAt(i);
                }
            }
        }

        void OnDestroy()
        {
            foreach (var instance in _activeInstances)
                instance.Dispose();
            _activeInstances.Clear();
        }
    }
}

using UnityEngine;

namespace Fodinae.Assets.Scripts.Audio
{
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        private AudioSource _ambientSource;
        private AudioSource _sfxSource;

        private float _ambientVolume = 0.5f;
        private float _sfxVolume = 1f;

        public float AmbientVolume
        {
            get => _ambientVolume;
            set
            {
                _ambientVolume = Mathf.Clamp01(value);
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
                _sfxSource.volume = _sfxVolume;
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

            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.loop = true;
            _sfxSource.playOnAwake = false;

            _ambientVolume = PlayerPrefs.GetFloat("Audio_Ambient", 0.5f);
            _sfxVolume = PlayerPrefs.GetFloat("Audio_Sfx", 1f);

            LoadAndPlay();
        }

        private void LoadAndPlay()
        {
            var ambientClip = Resources.Load<AudioClip>("Audio/evil_huge");
            if (ambientClip != null)
            {
                _ambientSource.clip = ambientClip;
                _ambientSource.volume = _ambientVolume;
                _ambientSource.Play();
            }
            else
            {
                Debug.LogWarning("[AudioManager] Audio/evil_huge не найден в Resources");
            }

            var sfxClip = Resources.Load<AudioClip>("Audio/mining");
            if (sfxClip != null)
            {
                _sfxSource.clip = sfxClip;
                _sfxSource.volume = _sfxVolume;
                _sfxSource.Play();
            }
            else
            {
                Debug.LogWarning("[AudioManager] Audio/mining не найден в Resources");
            }
        }
    }
}

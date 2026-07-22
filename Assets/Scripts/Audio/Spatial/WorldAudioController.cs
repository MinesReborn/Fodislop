using Fodinae.Scripts.Audio.Backend;
using Fodinae.Scripts.Audio.Core;
using Fodinae.Scripts.Game.Managers;
using UnityEngine;

namespace Fodinae.Scripts.Audio.Spatial
{
    /// <summary>
    /// Контроллер звукового сопровождения локации и игрового мира.
    ///
    /// Отвечает за реакцию на смену сцен, готовность локаций и атмосферный эмбиент.
    /// Не зависит от низкоуровневой работы с картой (MapManager) и от инфраструктурных серверов.
    /// </summary>
    public sealed class WorldAudioController : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Имя стартового музыкального ивента мира.")]
        private string _worldMusicEvent = "music/evil_huge";

        [SerializeField]
        [Tooltip("Громкость музыки при старте локации.")]
        [Range(0f, 1f)]
        private float _musicVolume = 0.5f;

        private AudioPlaybackHandle _musicHandle;

        private void Start()
        {
            if (MapManager.Instance != null)
            {
                MapManager.Instance.OnWorldInitialized += OnWorldInitialized;
            }

            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnWorldLoaded += OnWorldLoaded;
            }
        }

        private void OnDestroy()
        {
            if (MapManager.InstanceIfExists != null)
            {
                MapManager.InstanceIfExists.OnWorldInitialized -= OnWorldInitialized;
            }

            if (GameManager.InstanceIfExists != null)
            {
                GameManager.InstanceIfExists.OnWorldLoaded -= OnWorldLoaded;
            }

            if (_musicHandle != null)
            {
                _musicHandle.Stop();
                _musicHandle = null;
            }
        }

        private void OnWorldInitialized()
        {
            PlayWorldMusic();
        }

        private void OnWorldLoaded()
        {
            PlayWorldMusic();
        }

        private void PlayWorldMusic()
        {
            if (AudioSystem.Instance == null || string.IsNullOrEmpty(_worldMusicEvent))
            {
                return;
            }

            if (_musicHandle != null)
            {
                _musicHandle.Stop();
                _musicHandle = null;
            }

            _musicHandle = AudioSystem.Instance.Play2D(_worldMusicEvent, AudioLayer.MusicDefault(), _musicVolume);
        }
    }
}

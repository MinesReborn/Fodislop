using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fodinae.Scripts.Audio.Backend;
using Fodinae.Scripts.Audio.Core;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Scripts.Audio
{
    /// <summary>
    /// Адаптер старого аудио-кода к новой системе AudioSystem.
    ///
    /// Сохраняет обратную совместимость — весь старый код продолжает работать
    /// через AudioManager.Instance и PlaySfx(). Фоновый эмбиент теперь
    /// играет через AudioSystem.Play2D() и управляется шиной Music.
    ///
    /// Для полного перехода на новый API — замени вызовы AudioManager.Instance.PlaySfx(type)
    /// на AudioSystem.Play("имя_события").
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        private readonly List<SoundEffectInstance> _activeInstances = new();
        private float _ambientVolume = 0.5f;
        private float _sfxVolume = 1f;

        public float AmbientVolume
        {
            get => _ambientVolume;
            set
            {
                _ambientVolume = Mathf.Clamp01(value);
                AudioSystem.Instance.GetBus(AudioBusType.Music).Volume = _ambientVolume;
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
                AudioSystem.Instance.GetBus(AudioBusType.Sfx).Volume = _sfxVolume;
                PlayerPrefs.SetFloat("Audio_Sfx", _sfxVolume);
                PlayerPrefs.Save();
            }
        }

        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _ambientVolume = PlayerPrefs.GetFloat("Audio_Ambient", 0.5f);
            _sfxVolume = PlayerPrefs.GetFloat("Audio_Sfx", 1f);

            // Пробрасываем громкости в шины новой системы
            AudioSystem.Instance.GetBus(AudioBusType.Music).Volume = _ambientVolume;
            AudioSystem.Instance.GetBus(AudioBusType.Sfx).Volume = _sfxVolume;

            LoadAmbientAsync().Forget();
        }

        private async UniTaskVoid LoadAmbientAsync()
        {
            try
            {
                // Загружаем эмбиент через новую систему — она сама загрузит файл и применит шину.
                AudioSystem.Instance.Play2D(
                    "ambient_bg",
                    AudioLayer.MusicDefault(),
                    0.5f);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AudioManager] Не удалось загрузить эмбиент: {ex.Message}");
            }
        }

        /// <summary>
        /// Проиграть звуковой эффект — старый API, сохранён для обратной совместимости.
        /// Пробрасывает вызов в новую AudioSystem, отображая SFX-тип на имя события.
        /// </summary>
        public void PlaySfx(SFX type)
        {
            var pool = SFXPool.Instance;
            if (pool != null)
            {
                pool.Play(type, _sfxVolume);
                return;
            }

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

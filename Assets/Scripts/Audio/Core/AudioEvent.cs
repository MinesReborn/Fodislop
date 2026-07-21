using System;
using UnityEngine;

namespace Fodinae.Scripts.Audio.Core
{
    /// <summary>
    /// Именованное аудио-событие — семантическая обёртка над FMOD Studio event.
    ///
    /// Код говорит <c>AudioSystem.Play("dig_rock")</c> — система строит FMOD path
    /// как <c>event:/dig_rock</c> и запускает через FmodAudioBackend.
    ///
    /// PitchVariation позволяет рандомизировать питч для разнообразия одинаковых звуков.
    /// </summary>
    [Serializable]
    public sealed class AudioEvent
    {
        /// <summary>Уникальное имя события. Совпадает с именем FMOD event (без префикса event:/).</summary>
        public string Name;

        /// <summary>Слой по умолчанию. Можно переопределить при вызове Play().</summary>
        public AudioLayer DefaultLayer = AudioLayer.SfxDefault();

        /// <summary>
        /// Громкость по умолчанию (линейная, 1.0 = без изменений).
        /// Можно переопределить при вызове Play().
        /// </summary>
        [Range(0f, 2f)]
        public float DefaultVolume = 1f;

        /// <summary>
        /// Вариация питча: финальный питч = DefaultLayer.Pitch ± Random.Range(-PitchVariation, PitchVariation).
        /// 0 = без вариации. 0.1 = ±10%.
        /// </summary>
        [Range(0f, 0.5f)]
        public float PitchVariation;

        /// <summary>Создать событие программно.</summary>
        public static AudioEvent Create(string name, AudioLayer layer)
        {
            return new AudioEvent
            {
                Name = name,
                DefaultLayer = layer,
                DefaultVolume = 1f,
            };
        }
    }
}

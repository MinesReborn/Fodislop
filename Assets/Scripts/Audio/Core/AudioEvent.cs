using System;
using UnityEngine;

namespace Fodinae.Scripts.Audio.Core
{
    /// <summary>
    /// Именованное семантическое аудио-событие — то что звукорежиссёр говорит «проиграть».
    ///
    /// Вместо «загрузи файл audio/bz.wav с громкостью 0.7 на шину Sfx» код говорит:
    /// <c>AudioSystem.Play(AudioEvent.FromLibrary("dig_rock"))</c>.
    ///
    /// Событие ссылается на один или несколько аудио-ассетов и содержит дефолтные параметры
    /// слоя, громкости, рандомизации питча.  Это мост между нарративным дизайном («здесь должен быть звук копки»)
    /// и технической реализацией («файл bz.wav, громкость 0.7, небольшая вариация питча»).
    ///
    /// <para>
    /// <b>Как расширить под FMOD:</b> добавить поле <c>string FmodEventPath</c>.
    /// Бэкенд проверит его и вызовет <c>FMODUnity.RuntimeManager.PlayOneShot(eventPath)</c> вместо загрузки AudioClip.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class AudioEvent
    {
        /// <summary>Уникальное имя для поиска в <see cref="AudioSystem"/>.</summary>
        public string Name;

        /// <summary>
        /// Пути к аудио-файлам (без расширения).  При игре выбирается случайный.
        /// Пример: <c>["audio/dig_rock_01", "audio/dig_rock_02", "audio/dig_rock_03"]</c>.
        /// </summary>
        public string[] AssetPaths;

        /// <summary>Слой по умолчанию. Можно переопределить при вызове Play().</summary>
        public AudioLayer DefaultLayer = AudioLayer.SfxDefault();

        /// <summary>
        /// Громкость по умолчанию (линейная, 1.0 = как в файле).
        /// Можно переопределить при вызове Play().
        /// </summary>
        [Range(0f, 2f)]
        public float DefaultVolume = 1f;

        /// <summary>
        /// Вариация питча: финальный питч = DefaultLayer.Pitch ± PitchVariation * Random.Range(-1,1).
        /// 0 = без вариации.  0.1 = ±10%.
        /// </summary>
        [Range(0f, 0.5f)]
        public float PitchVariation;

        /// <summary>
        /// Если true, при вызове Play() несколько раз подряд используется round-robin по AssetPaths
        /// вместо случайного выбора.  Полезно для вариаций шагов и выстрелов чтобы избежать повторений.
        /// </summary>
        public bool UseRoundRobin;

        /// <summary>Создать событие программно (альтернатива ScriptableObject).</summary>
        public static AudioEvent Create(string name, string[] assetPaths, AudioLayer layer)
        {
            return new AudioEvent
            {
                Name = name,
                AssetPaths = assetPaths,
                DefaultLayer = layer,
                DefaultVolume = 1f,
            };
        }

        /// <summary>Выбрать путь к ассету согласно стратегии (random или round-robin).</summary>
        public string PickAsset()
        {
            if (AssetPaths == null || AssetPaths.Length == 0)
                return null;

            if (AssetPaths.Length == 1)
                return AssetPaths[0];

            if (UseRoundRobin)
            {
                _roundRobinIndex = (_roundRobinIndex + 1) % AssetPaths.Length;
                return AssetPaths[_roundRobinIndex];
            }

            return AssetPaths[UnityEngine.Random.Range(0, AssetPaths.Length)];
        }

        private int _roundRobinIndex = -1;
    }
}

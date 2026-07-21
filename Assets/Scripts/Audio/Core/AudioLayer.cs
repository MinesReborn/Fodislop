using UnityEngine;

namespace Fodinae.Scripts.Audio.Core
{
    /// <summary>
    /// Тип аудио-шины — определяет куда маршрутизируется звук в FMOD Studio.
    /// Каждый тип соответствует шине (bus) в FMOD Studio: bus:/sfx, bus:/music, etc.
    /// </summary>
    public enum AudioBusType
    {
        /// <summary>Мастер-шина — все шины в конечном счёте идут сюда.</summary>
        Master = 0,

        /// <summary>Игровые звуки: копка, взрывы, шаги, выстрелы, механизмы.</summary>
        SFX = 10,

        /// <summary>Музыка / саундтрек. Длинные треки, стерео, без пространственного позиционирования.</summary>
        Music = 20,

        /// <summary>Голос персонажа / нарратив / диалоговая система.</summary>
        Voice = 30,

        /// <summary>Эмбиент окружения: ветер, гул, лава. Обычно зациклено.</summary>
        Ambience = 40,

        /// <summary>Звуки интерфейса и системных уведомлений: клики, открытие инвентаря, достижения.</summary>
        UI = 50,
    }

    /// <summary>
    /// Параметры воспроизведения звука поверх FMOD.
    /// Distance attenuation, spatialization и voice stealing настраиваются нативно в FMOD Studio.
    /// Здесь только то что может меняться в рантайме: шина, громкость, питч, приоритет.
    /// </summary>
    [System.Serializable]
    public struct AudioLayer
    {
        /// <summary>Шина через которую идёт звук. По умолчанию = <see cref="AudioBusType.SFX"/>.</summary>
        [Tooltip("Шина микшера: SFX, Music, Voice, Ambience, UI.")]
        public AudioBusType Bus;

        /// <summary>
        /// Линейный множитель громкости поверх громкости шины.
        /// 1.0 = громкость шины, 0.5 = -6 dB.
        /// </summary>
        [Range(0f, 2f)]
        public float Volume;

        /// <summary>
        /// Множитель питча. 1.0 = без изменений.
        /// Используется для рандомизации вариаций одного звука.
        /// </summary>
        [Range(0.01f, 4f)]
        public float Pitch;

        /// <summary>
        /// true = 3D пространственный звук (позиция передаётся в FMOD set3DAttributes).
        /// false = 2D (без пространственного позиционирования, UI/Music).
        /// </summary>
        [Tooltip("Пространственный звук: позиция передаётся в FMOD.")]
        public bool IsSpatial;

        /// <summary>Фабрика: игровой SFX — пространственный, стандартная шина SFX.</summary>
        public static AudioLayer SFXDefault() => new()
        {
            Bus = AudioBusType.SFX,
            Volume = 1f,
            Pitch = 1f,
            IsSpatial = true,
        };

        /// <summary>Фабрика: не-пространственный UI-звук.</summary>
        public static AudioLayer UIDefault() => new()
        {
            Bus = AudioBusType.UI,
            Volume = 1f,
            Pitch = 1f,
            IsSpatial = false,
        };

        /// <summary>Фабрика: музыка — всегда стерео, без условий.</summary>
        public static AudioLayer MusicDefault() => new()
        {
            Bus = AudioBusType.Music,
            Volume = 1f,
            Pitch = 1f,
            IsSpatial = false,
        };

        /// <summary>Фабрика: голос / диалог.</summary>
        public static AudioLayer VoiceDefault() => new()
        {
            Bus = AudioBusType.Voice,
            Volume = 1f,
            Pitch = 1f,
            IsSpatial = true,
        };
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace Fodinae.Scripts.Audio.Core
{
    /// <summary>
    /// Логическая аудио-шина — именованный канал, через который каждый звук проходит перед выходом на железо.
    ///
    /// Шина держит свою громкость, питч, лимит голосов и отношения дакинга с другими шинами.
    /// Это основа и для микширования, и для аудио-повествования:
    /// <list type="bullet">
    ///   <item><b>Режиссура:</b> когда персонаж говорит — просадить SFX и Ambience</item>
    ///   <item><b>Дизайн:</b> разделить звуки UI и мира чтобы ползунками рулить раздельно</item>
    ///   <item><b>FMOD-миграция:</b> обернуть VCA/VCA-group в этот класс, API останется тем же</item>
    ///   <item><b>Отладка:</b> видеть уровни по шинам, считать активные голоса, смотреть кто кого душит</item>
    /// </list>
    /// </summary>
    public sealed class AudioBus
    {
        /// <summary>Идентификатор шины для поиска в <see cref="AudioSystem"/>.</summary>
        public AudioBusType BusType { get; }

        /// <summary>
        /// Линейная громкость [0..+∞).  Default = 1.0.
        /// Поставь 0 чтобы заглушить категорию, 0.5 = −6 dB.
        /// Применяется мультипликативно с дакингом.
        /// </summary>
        public float Volume
        {
            get => _volume;
            set => _volume = Mathf.Max(0f, value);
        }

        /// <summary>
        /// Множитель питча на каждый голос шины.
        /// 0.5 = октава вниз (half-speed), 2.0 = октава вверх (double-speed).  1.0 = без изменений.
        /// </summary>
        public float Pitch
        {
            get => _pitch;
            set => _pitch = Mathf.Clamp(value, 0.01f, 4f);
        }

        /// <summary>
        /// Максимум одновременных голосов на шине.
        /// При превышении крадётся самый тихий или старый голос (см. <see cref="StealMode"/>).
        /// 0 = безлимит.  Дефолты: Sfx=32, Voice=1, Music=2.
        /// </summary>
        public int VoiceLimit { get; set; }

        /// <summary>Режим кражи голосов при превышении <see cref="VoiceLimit"/>.</summary>
        public VoiceStealMode StealMode { get; set; } = VoiceStealMode.Quietest;

        /// <summary>Сколько голосов сейчас активно на шине (только чтение).</summary>
        public int ActiveVoiceCount => _activeVoices;

        /// <summary>
        /// Финальная громкость после применения родительской шины, дакинга и мьюта.
        /// Бэкенд читает это поле чтобы выставить AudioSource.volume.
        /// </summary>
        public float EffectiveVolume
        {
            get
            {
                if (_muted) return 0f;
                float v = _volume;
                foreach (var kvp in _duckSources)
                    v *= kvp.Value._currentGain;
                return v;
            }
        }

        internal int _activeVoices;
        internal float _volume = 1f;
        internal float _pitch = 1f;
        internal bool _muted;

        /// <summary>Создаётся только через <see cref="AudioSystem"/>. Не инстанциируй руками.</summary>
        public AudioBus(AudioBusType busType)
        {
            BusType = busType;
        }

        /// <summary>
        /// Мьют/анмьют шины.  Голоса не останавливаются — продолжают играть молча.
        /// При анмьюте звук восстанавливается мгновенно.
        /// </summary>
        public void SetMute(bool muted)
        {
            _muted = muted;
        }

        /// <summary>
        /// Приказать этой шине просадить другую шину когда на ней есть активные голоса.
        ///
        /// Это ядро дакинга / side-chain:
        ///   Voice → просадить Sfx, Explosion → просадить Ambience, MenuOpen → просадить World.
        ///
        /// Гейн плавно нарастает за <paramref name="attackTime"/> когда появляется голос,
        /// и отпускается за <paramref name="releaseTime"/> когда последний голос на этой шине замолкает.
        ///
        /// <param name="target">Шина которую просаживаем (Sfx, Ambience...).</param>
        /// <param name="duckingDb">Глубина просадки в децибелах (отрицательное число).  −6 = полгромкости линейно.  0 = убрать дакинг.</param>
        /// <param name="attackTime">Секунд до полной глубины дакинга.</param>
        /// <param name="releaseTime">Секунд восстановления полной громкости.</param>
        /// </summary>
        public void DuckBus(AudioBus target, float duckingDb, float attackTime, float releaseTime)
        {
            _duckingTargets[target.BusType] = new DuckState
            {
                Target    = target,
                DuckingDb = Mathf.Min(0f, duckingDb),
                Attack    = Mathf.Max(0.001f, attackTime),
                Release   = Mathf.Max(0.001f, releaseTime),
            };
        }

        /// <summary>Убрать дакинг с указанной шины.</summary>
        public void StopDucking(AudioBus target)
        {
            _duckingTargets.Remove(target.BusType);
        }

        internal readonly Dictionary<AudioBusType, DuckState> _duckingTargets = new();
        private readonly Dictionary<AudioBusType, DuckState> _duckSources = new();

        /// <summary>Зарегистрировать что другая шина душит эту. Вызывается AudioSystem.</summary>
        internal void RegisterDuckSource(AudioBusType sourceBus, DuckState state)
        {
            _duckSources[sourceBus] = state;
        }

        internal sealed class DuckState
        {
            public AudioBus Target;
            public float    DuckingDb;
            public float    Attack;
            public float    Release;
            public float    _currentGain = 1f;
        }
    }

    /// <summary>
    /// Как <see cref="AudioBus"/> крадёт голоса когда <see cref="AudioBus.VoiceLimit"/> превышен.
    /// </summary>
    public enum VoiceStealMode
    {
        /// <summary>Красть голос с наименьшим <see cref="AudioLayer.Priority"/>. При равенстве — самый старый.</summary>
        Quietest,

        /// <summary>Красть самый старый голос, независимо от приоритета.</summary>
        Oldest,
    }
}

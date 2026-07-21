using UnityEngine;

namespace Fodinae.Scripts.Audio.Core
{
    /// <summary>
    /// Аудио-шина — именованный канал, через который каждый звук проходит перед выходом на железо.
    ///
    /// Шина держит громкость, питч и мьют. Всё остальное (дакинг, VoiceLimit, приоритеты,
    /// VoiceSteal) обрабатывается нативно в FMOD Studio. Здесь только параметры для проброса
    /// в FMOD через FmodAudioBackend.Update().
    /// </summary>
    public sealed class AudioBus
    {
        private float _volume = 1f;
        private float _pitch = 1f;
        private bool _muted;

        public AudioBus(AudioBusType busType)
        {
            BusType = busType;
        }

        public AudioBusType BusType { get; }

        public float Volume
        {
            get => _volume;
            set => _volume = Mathf.Max(0f, value);
        }

        public float Pitch
        {
            get => _pitch;
            set => _pitch = Mathf.Clamp(value, 0.01f, 4f);
        }

        /// <summary>Финальная громкость для проброса в FMOD (учитывает мьют).</summary>
        public float EffectiveVolume => _muted ? 0f : _volume;

        /// <summary>Сколько голосов сейчас активно на этой шине.</summary>
        public int ActiveVoiceCount { get; internal set; }

        public bool Muted => _muted;

        public void SetMute(bool muted)
        {
            _muted = muted;
        }
    }
}

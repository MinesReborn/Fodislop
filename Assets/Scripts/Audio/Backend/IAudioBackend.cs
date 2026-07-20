using Fodinae.Scripts.Audio.Core;
using UnityEngine;

namespace Fodinae.Scripts.Audio.Backend
{
    /// <summary>
    /// Интерфейс аудио-бэкенда — слой абстракции между доменом звука и конкретным рендерером.
    ///
    /// Реализации:
    /// <list type="bullet">
    ///   <item><see cref="UnityAudioBackend"/> — стандартный AudioSource, работает из коробки</item>
    ///   <item><b>FmodAudioBackend</b> (будущее) — FMOD Studio, грузит банки, рулит событиями</item>
    ///   <item><b>WwiseBackend</b> (будущее) — Audiokinetic Wwise</item>
    /// </list>
    ///
    /// Каждая реализация регистрируется в <see cref="AudioSystem"/> и получает вызовы
    /// для создания/остановки/обновления голосов и шин.
    /// </summary>
    public interface IAudioBackend
    {
        /// <summary>Инициализация бэкенда. Вызывается при старте AudioSystem.</summary>
        void Initialize(AudioSystem system);

        /// <summary>
        /// Создать голос — загрузить AudioClip, выделить AudioSource (или эквивалент в FMOD),
        /// применить параметры слоя и шины, начать воспроизведение.
        /// </summary>
        /// <param name="evt">Событие которое играем.</param>
        /// <param name="layer">Параметры слоя (шина, громкость, питч, пространственность).</param>
        /// <param name="assetPath">Путь к аудио-файлу без расширения.</param>
        /// <param name="worldPosition">Позиция в мире (используется только если layer.IsSpatial).</param>
        /// <returns>Хендл для управления голосом, или null если не удалось создать.</returns>
        AudioPlaybackHandle CreateVoice(AudioEvent evt, AudioLayer layer, string assetPath, Vector3? worldPosition);

        /// <summary>Остановить голос с плавным затуханием.</summary>
        void StopVoice(AudioPlaybackHandle handle, float fadeOut);

        /// <summary>Обновить мировой позицию голоса.</summary>
        void SetVoicePosition(AudioPlaybackHandle handle, Vector3 worldPosition);

        /// <summary>Обновить громкость голоса.</summary>
        void SetVoiceVolume(AudioPlaybackHandle handle, float volume);

        /// <summary>Обновить питч голоса.</summary>
        void SetVoicePitch(AudioPlaybackHandle handle, float pitch);

        /// <summary>Играет ли голос сейчас.</summary>
        bool IsPlaying(AudioPlaybackHandle handle);

        /// <summary>
        /// Вызывается каждый кадр из AudioSystem.Update().
        /// Бэкенд должен обновить дакинг, проверить отыгравшие голоса, применить VoiceLimit.
        /// </summary>
        void Update(float deltaTime);

        /// <summary>Полная очистка — остановить все голоса, освободить ресурсы.</summary>
        void Shutdown();
    }
}

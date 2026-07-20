using Fodinae.Scripts.Audio.Core;
using Fodinae.Scripts.Audio.Backend;
using UnityEngine;

namespace Fodinae.Scripts.Audio.Spatial
{
    /// <summary>
    /// Вешается на любой GameObject чтобы он излучал пространственный звук.
    ///
    /// Автоматически обновляет позицию хендла каждый кадр — звук следует за объектом.
    /// Можно использовать для:
    /// <list type="bullet">
    ///   <item>Роботов (звук шагов/двигателя следует за ботом)</item>
    ///   <item>Машин/механизмов (петляющий звук привязан к трансформу)</item>
    ///   <item>Эффектов (взрыв с затуханием по расстоянию)</item>
    /// </list>
    ///
    /// <b>Как использовать:</b>
    /// <code>
    /// // На роботе:
    /// var spatial = robot.AddComponent&lt;AudioSpatial&gt;();
    /// spatial.Play("robot_idle", AudioLayer.SfxDefault());
    ///
    /// // Позже:
    /// spatial.SetEvent("robot_alert"); // сменить звук
    /// spatial.Stop(0.5f);             // остановить с фейдом
    /// </code>
    /// </summary>
    [RequireComponent(typeof(Transform))]
    public sealed class AudioSpatial : MonoBehaviour
    {
        [Tooltip("Имя аудио-события.  Можно сменить на лету через SetEvent().")]
        [SerializeField] private string _eventName;

        [Tooltip("Слой.  Если не задан — используется DefaultLayer из AudioEvent.")]
        [SerializeField] private AudioLayer _layer = AudioLayer.SfxDefault();

        [Tooltip("Громкость.  Если 0 или меньше — используется DefaultVolume из AudioEvent.")]
        [SerializeField, Range(0f, 2f)] private float _volume;

        [Tooltip("Играть автоматически при Start().")]
        [SerializeField] private bool _playOnStart = true;

        private AudioPlaybackHandle _handle;

        private void Start()
        {
            if (_playOnStart && !string.IsNullOrEmpty(_eventName))
                PlayCurrent();
        }

        private void Update()
        {
            if (_handle != null && !_handle._disposed)
                _handle.SetPosition(transform.position);
        }

        private void OnDestroy()
        {
            _handle?.Stop();
            _handle = null;
        }

        /// <summary>Начать проигрывание текущего события.</summary>
        public void PlayCurrent()
        {
            if (string.IsNullOrEmpty(_eventName))
                return;

            Stop();
            float? vol = _volume > 0f ? _volume : null;
            _handle = AudioSystem.Instance.Play(_eventName, transform.position, _layer, vol);
        }

        /// <summary>Сменить событие на лету (старое останавливается, новое стартует).</summary>
        public void SetEvent(string eventName, AudioLayer? layer = null, float? volume = null)
        {
            _eventName = eventName;
            if (layer.HasValue) _layer = layer.Value;
            if (volume.HasValue) _volume = volume.Value;
            PlayCurrent();
        }

        /// <summary>Остановить с фейд-аутом.</summary>
        public void Stop(float fadeOut = 0f)
        {
            _handle?.Stop(fadeOut);
            _handle = null;
        }

        /// <summary>Играет ли сейчас звук.</summary>
        public bool IsPlaying => _handle != null && !_handle._disposed && _handle.IsPlaying;
    }
}

using Fodinae.Scripts.Audio.Backend;
using Fodinae.Scripts.Audio.Core;
using UnityEngine;

namespace Fodinae.Scripts.Audio.Spatial
{
    /// <summary>
    /// Аудио-зона — триггерный регион меняющий громкость шины когда игрок входит/выходит.
    ///
    /// Примеры использования:
    /// <list type="bullet">
    ///   <item><b>Пещера:</b> войдя в коллайдер — приглушить шину Ambience на 6 dB</item>
    ///   <item><b>Под водой:</b> приглушить Sfx, оставить только низкие частоты</item>
    ///   <item><b>Меню паузы:</b> при открытии меню — просадить шину World на 12 dB</item>
    ///   <item><b>Катсцена:</b> заглушить всё кроме Voice и Music</item>
    /// </list>
    ///
    /// <b>Как повесить:</b>
    /// 1. Создай GameObject с Collider2D (IsTrigger = true)
    /// 2. Добавь компонент AudioZone
    /// 3. Настрой параметры: TargetBus, VolumeMultiplier, TransitionTime
    /// 4. При входе игрока в коллайдер — громкость шины плавно изменится.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class AudioZone : MonoBehaviour
    {
        [Tooltip("На какую шину действует зона.")]
        [SerializeField]
        private AudioBusType _targetBus = AudioBusType.Ambience;

        [Tooltip("Модификатор громкости. 1.0 = без изменений, 0.5 = полгромкости, 0.0 = тишина.")]
        [SerializeField]
        [Range(0f, 1f)]
        private float _volumeMultiplier = 0.5f;

        [Tooltip("Время перехода громкости (секунды).")]
        [SerializeField]
        [Min(0f)]
        private float _transitionTime = 1f;

        [Tooltip("Сколько игроков должны быть в зоне чтобы эффект работал (обычно 1).")]
        [SerializeField]
        [Min(1)]
        private int _requiredPlayers = 1;

        private AudioBus _bus;
        private int _playersInside;
        private bool _active;

        // Fixed start/target volumes captured at moment transition begins
        private float _transitionStartVolume;
        private float _transitionTargetVolume;
        private float _transitionElapsed;
        private bool _inTransition;

        private float _originalVolume;

        private void Start()
        {
            if (AudioSystem.Instance == null)
            {
                return;
            }

            _bus = AudioSystem.Instance.GetBus(_targetBus);
            _originalVolume = _bus.Volume;
        }

        private void Update()
        {
            if (_bus == null || !_inTransition)
            {
                return;
            }

            _transitionElapsed += Time.unscaledDeltaTime;
            float t = _transitionTime > 0f ? Mathf.Clamp01(_transitionElapsed / _transitionTime) : 1f;
            _bus.Volume = Mathf.Lerp(_transitionStartVolume, _transitionTargetVolume, t);

            if (t >= 1f)
            {
                _bus.Volume = _transitionTargetVolume;
                _inTransition = false;
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!other.CompareTag("Player"))
            {
                return;
            }

            _playersInside++;
            if (_playersInside >= _requiredPlayers && !_active)
            {
                _active = true;
                BeginTransition(_originalVolume * _volumeMultiplier);
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (!other.CompareTag("Player"))
            {
                return;
            }

            _playersInside = Mathf.Max(0, _playersInside - 1);
            if (_playersInside < _requiredPlayers && _active)
            {
                _active = false;
                BeginTransition(_originalVolume);
            }
        }

        private void BeginTransition(float targetVolume)
        {
            if (_bus == null)
            {
                return;
            }

            _transitionStartVolume = _bus.Volume;
            _transitionTargetVolume = targetVolume;
            _transitionElapsed = 0f;
            _inTransition = true;
        }
    }
}

using Fodinae.Scripts.Audio.Backend;
using Fodinae.Scripts.Audio.Core;
using UnityEngine;

namespace Fodinae.Scripts.Audio.Spatial
{
    /// <summary>
    /// Аудио-зона — триггерный регион, задействующий FMOD Snapshot при входе игрока.
    ///
    /// Нативно меняет акустику, дакинг и фильтры шин в FMOD Studio без принудительной C#-мутации
    /// громкостей микшера (что предотвращает сброс пользовательских настроек из PauseMenu).
    ///
    /// Примеры использования:
    /// <list type="bullet">
    ///   <item><b>Кристальная жила:</b> snapshot:/Crystal_Zone — усиливает высокочастотные резонансы SFX, снижает Ambience</item>
    ///   <item><b>Вулканическая зона:</b> snapshot:/Volcano_Zone — добавляет низкочастотный Reverb, нагнетает Ambience</item>
    ///   <item><b>Пак (здание):</b> snapshot:/Pack_Interior — приглушает внешний Ambience, поднимает акустику помещения</item>
    /// </list>
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class AudioZone : MonoBehaviour
    {
        [Tooltip("Путь к FMOD Snapshot (например snapshot:/Crystal_Zone, snapshot:/Volcano_Zone, snapshot:/Pack_Interior).")]
        [SerializeField]
        private string _snapshotPath = "snapshot:/Crystal_Zone";

        [Tooltip("Опциональное имя FMOD Global Parameter для установки при входе.")]
        [SerializeField]
        private string _globalParameterName;

        [Tooltip("Значение FMOD Global Parameter при входе.")]
        [SerializeField]
        private float _parameterValueOnEnter = 1f;

        [Tooltip("Значение FMOD Global Parameter при выходе.")]
        [SerializeField]
        private float _parameterValueOnExit;

        [Tooltip("Сколько игроков должны быть в зоне чтобы эффект работал (обычно 1).")]
        [SerializeField]
        [Min(1)]
        private int _requiredPlayers = 1;

        private int _playersInside;
        private bool _active;
        private AudioPlaybackHandle _activeSnapshot;

        private void OnDestroy()
        {
            StopSnapshot();
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
                StartSnapshot();
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
                StopSnapshot();
            }
        }

        private void StartSnapshot()
        {
            if (AudioSystem.Instance == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(_snapshotPath))
            {
                _activeSnapshot = AudioSystem.Instance.PlaySnapshot(_snapshotPath);
            }

            if (!string.IsNullOrEmpty(_globalParameterName))
            {
                AudioSystem.Instance.SetGlobalParameter(_globalParameterName, _parameterValueOnEnter);
            }
        }

        private void StopSnapshot()
        {
            if (_activeSnapshot != null)
            {
                _activeSnapshot.Stop(fadeOut: 0.5f);
                _activeSnapshot = null;
            }

            if (!string.IsNullOrEmpty(_globalParameterName) && AudioSystem.Instance != null)
            {
                AudioSystem.Instance.SetGlobalParameter(_globalParameterName, _parameterValueOnExit);
            }
        }
    }
}

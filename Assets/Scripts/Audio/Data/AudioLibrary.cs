using System.Collections.Generic;
using Fodinae.Scripts.Audio.Core;
using UnityEngine;

namespace Fodinae.Scripts.Audio.Data
{
    /// <summary>
    /// ScriptableObject-библиотека аудио-событий — главный инструмент звукорежиссёра.
    ///
    /// Создаётся в редакторе: ПКМ в Project → Create → Audio → Audio Library.
    /// Здесь звукорежиссёр регистрирует все события игры:
    /// <list type="bullet">
    ///   <item>Имя события (dig_rock, explosion_large, ui_click, ambient_cave...)</item>
    ///   <item>Файлы-источники (.wav/.ogg, несколько для вариаций)</item>
    ///   <item>Дефолтные параметры слоя (шина, громкость, пространственность, питч)</item>
    ///   <item>Стратегию выбора файла: случайный или round-robin</item>
    /// </list>
    ///
    /// <b>Как звукорежиссёр работает с библиотекой:</b>
    /// <code>
    /// 1. Открывает AudioLibrary.asset в инспекторе
    /// 2. Добавляет новое событие: имя "dig_rock", файлы ["Audio/dig_01", "Audio/dig_02"]
    /// 3. Выбирает слой: Sfx, громкость 0.8, пространственный (галочка IsSpatial)
    /// 4. Всё — программист пишет AudioSystem.Play("dig_rock")
    /// </code>
    ///
    /// <b>Как программист подключает:</b>
    /// <code>
    /// [SerializeField] private AudioLibrary _audioLibrary;
    /// void Start() { AudioSystem.Instance.RegisterAll(_audioLibrary.Events); }
    /// </code>
    ///
    /// При переходе на FMOD — события отображаются на FMOD event path (event:/EventName).
    /// Структура та же, меняется только бэкенд.
    /// </summary>
    [CreateAssetMenu(fileName = "AudioLibrary", menuName = "Audio/Audio Library")]
    public sealed class AudioLibrary : ScriptableObject
    {
        /// <summary>Все зарегистрированные события.</summary>
        [SerializeField]
        [Tooltip("Аудио-события игры. Каждое — именованная звуковая сущность с файлами и параметрами.")]
        private AudioEvent[] _events = System.Array.Empty<AudioEvent>();

        /// <summary>
        /// События в виде перечислимой коллекции.
        /// Используется AudioSystem.RegisterAll().
        /// </summary>
        public IEnumerable<AudioEvent> Events => _events;

        /// <summary>Найти событие по имени. null если не найдено.</summary>
        public AudioEvent Find(string name)
        {
            if (_events == null)
            {
                return null;
            }

            foreach (var e in _events)
            {
                if (e != null && e.Name == name)
                {
                    return e;
                }
            }

            return null;
        }

        /// <summary>Количество событий в библиотеке.</summary>
        public int Count => _events?.Length ?? 0;

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Проверка на дубликаты имён и пустые ссылки
            if (_events == null)
            {
                return;
            }

            var names = new HashSet<string>();
            for (int i = 0; i < _events.Length; i++)
            {
                var evt = _events[i];
                if (evt == null)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(evt.Name))
                {
                    Debug.LogWarning($"[AudioLibrary] Событие #{i} имеет пустое имя");
                    continue;
                }

                if (!names.Add(evt.Name))
                {
                    Debug.LogWarning($"[AudioLibrary] Дубликат имени события: '{evt.Name}' (индекс {i})");
                }
            }
        }
#endif
    }
}

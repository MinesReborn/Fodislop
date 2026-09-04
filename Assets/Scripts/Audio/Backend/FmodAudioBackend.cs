#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Audio.Core;
using Fodinae.Core;
using UnityEngine;

namespace Fodinae.Audio.Backend
{
    /// <summary>
    /// Граница с FMOD: шины, голоса, пауза, готовность банков.
    /// </summary>
    /// <remarks>
    /// ЧТО ОТСЮДА УШЛО И ПОЧЕМУ. Здесь жила своя загрузка банков: поиск
    /// файла в StreamingAssets и в кэше, проверка сигнатуры RIFF/FEV,
    /// вызов loadBankFile, ожидание состояния, заказ сэмплов, множество
    /// «недоступных» банков, выгрузка — около четырёхсот строк.
    ///
    /// Всё это FMOD for Unity делает сам. В FMODStudioSettings стоит
    /// BankLoadType = All и MasterBanks = [Master], то есть RuntimeManager
    /// грузит банки при инициализации. Наш код грузил их вторым заходом —
    /// настолько, что в нём была отдельная ветка на ERR_EVENT_ALREADY_LOADED,
    /// то есть на собственное дублирование.
    ///
    /// Плата за дубль была не теоретической. Готовность считалась по нашему
    /// проходу, а он объявлял её по факту ЗАКАЗА сэмплов, а не их загрузки;
    /// первый же звук отбрасывался проверкой резидентности и молчал. Плюс
    /// подгрузка «фиче-банка» по категории события: имя банка бралось из
    /// префикса пути, для music/evil_huge получалось «music», банка с таким
    /// именем нет и не было, и категория помечалась недоступной навсегда.
    ///
    /// Готовность теперь спрашивается у FMOD: банки загружены и ни один
    /// сэмпл не догружается.
    /// </remarks>
    public sealed class FmodAudioBackend
    {
        private readonly Dictionary<AudioBusType, FMOD.Studio.Bus> _buses = new();
        private readonly HashSet<string> _reportedMissingEvents = new(StringComparer.OrdinalIgnoreCase);
        private bool _paused;
        private bool _busesMapped;
        private bool _degraded;

        private static readonly FMOD.VECTOR ForwardVector = new() { x = 0f, y = 0f, z = 1f };
        private static readonly FMOD.VECTOR UpVector = new() { x = 0f, y = 1f, z = 0f };

        /// <summary>Банки не приехали: игра идёт, звука нет.</summary>
        public bool IsDegraded => _degraded;

        /// <summary>
        /// Завершается, когда банки загружены и сэмплы дозагружены.
        /// </summary>
        /// <remarks>
        /// Ждать нужно оба условия. HaveAllBanksLoaded говорит только про
        /// метаданные; событие с незагруженным сэмплом стартует тишиной.
        /// На это обещание опираются переходы сцен, поэтому обещать раньше
        /// времени — значит терять первые звуки сцены.
        /// </remarks>
        public async UniTask WaitUntilReadyAsync(AudioSystem system, CancellationToken cancellationToken)
        {
            const int maxWaitFrames = 1800;
            for (int frame = 0; frame < maxWaitFrames; frame++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (FMODUnity.RuntimeManager.HaveAllBanksLoaded &&
                    !FMODUnity.RuntimeManager.AnySampleDataLoading())
                {
                    MapBuses(system);
                    return;
                }

                await UniTask.Yield(cancellationToken);
            }

            _degraded = true;
            Debug.LogWarning(
                "[FmodAudioBackend] Банки FMOD не догрузились за отведённое время; игра идёт без звука.");
        }

        /// <summary>
        /// Раскладывает шины и применяет сохранённые громкости.
        /// </summary>
        /// <remarks>
        /// Пути живут на значениях <see cref="AudioBusType"/>, а не списком
        /// здесь: забытая строка в таком списке означала бы шину без звука
        /// без единой жалобы.
        /// </remarks>
        private void MapBuses(AudioSystem system)
        {
            if (_busesMapped)
            {
                return;
            }

            _busesMapped = true;
            foreach (AudioBusRegistry.BusBinding binding in AudioBusRegistry.Buses)
            {
                if (FMODUnity.RuntimeManager.StudioSystem.getBus(binding.Path, out FMOD.Studio.Bus bus) ==
                    FMOD.RESULT.OK)
                {
                    _buses[binding.Bus] = bus;
                }
                else
                {
                    Debug.LogWarning(
                        $"[FmodAudioBackend] Шина '{binding.Path}' ({binding.Bus}) не найдена в банках FMOD.");
                }
            }

            system.ApplySavedBusVolumes();
            SetPaused(_paused);
        }

        public float GetBusVolume(AudioBusType type)
        {
            if (!_buses.TryGetValue(type, out FMOD.Studio.Bus bus))
            {
                return 1f;
            }

            bus.getVolume(out float volume);
            return volume;
        }

        public void SetBusVolume(AudioBusType type, float volume)
        {
            if (_buses.TryGetValue(type, out FMOD.Studio.Bus bus))
            {
                bus.setVolume(Mathf.Clamp01(volume));
            }
        }

        public void SetPaused(bool paused)
        {
            _paused = paused;
            if (_buses.TryGetValue(AudioBusType.Master, out FMOD.Studio.Bus masterBus))
            {
                masterBus.setPaused(paused);
            }
        }

        /// <summary>
        /// Создаёт и запускает голос.
        /// </summary>
        /// <remarks>
        /// Проверки резидентности сэмплов здесь нет намеренно. Она была, и
        /// именно она молча съедала первый звук: FMOD грузит сэмплы в фоне,
        /// а отказ выглядел как обычный null. Событие, чьи сэмплы ещё едут,
        /// FMOD корректно доигрывает сам, а ждать их загрузки — работа
        /// <see cref="WaitUntilReadyAsync"/>.
        /// </remarks>
        public AudioPlaybackHandle? CreateVoice(
            string eventName,
            AudioLayer layer,
            Vector3? worldPosition,
            GameObject? targetGameObject = null)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                return null;
            }

            string fmodPath = eventName.StartsWith("event:/", StringComparison.OrdinalIgnoreCase)
                ? eventName
                : $"event:/{eventName}";

            if (FMODUnity.RuntimeManager.StudioSystem.getEvent(fmodPath, out FMOD.Studio.EventDescription description) !=
                FMOD.RESULT.OK)
            {
                if (_reportedMissingEvents.Add(fmodPath))
                {
                    Debug.LogWarning(
                        $"[FmodAudioBackend] Событие '{fmodPath}' отсутствует в загруженных банках.");
                }

                return null;
            }

            if (description.createInstance(out FMOD.Studio.EventInstance instance) != FMOD.RESULT.OK ||
                !instance.isValid())
            {
                Debug.LogWarning($"[FmodAudioBackend] Не удалось создать экземпляр события '{fmodPath}'.");
                return null;
            }

            if (layer.IsSpatial)
            {
                if (targetGameObject != null)
                {
                    FMODUnity.RuntimeManager.AttachInstanceToGameObject(instance, targetGameObject);
                }
                else if (worldPosition.HasValue)
                {
                    Vector3 position = worldPosition.Value;
                    instance.set3DAttributes(new FMOD.ATTRIBUTES_3D
                    {
                        position = new FMOD.VECTOR { x = position.x, y = position.y, z = 0f },
                        forward = ForwardVector,
                        up = UpVector,
                    });
                }
            }

            instance.setVolume(layer.Volume);
            instance.setPitch(layer.Pitch);
            instance.start();
            instance.release();
            return new AudioPlaybackHandle(instance, layer.Bus);
        }
    }
}

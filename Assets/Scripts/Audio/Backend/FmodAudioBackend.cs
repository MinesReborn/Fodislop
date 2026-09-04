#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Audio.Core;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using UnityEngine;
using VContainer;

namespace Fodinae.Audio.Backend
{
    /// <summary>
    /// Воспроизведение через FMOD Studio: голоса, шины, снэпшоты, глобальные
    /// параметры. Добыча и загрузка банков живёт в <see cref="FmodBankLibrary"/>,
    /// сюда она приходит уже готовой.
    /// </summary>
    public sealed class FmodAudioBackend
    {
        private AudioSystem _system = null!;
        private FmodBankLibrary _banks = null!;

        public void Initialize(
            AudioSystem system,
            IAssetLoader assetLoader,
            IPersistentAssetCache persistentCache,
            IAsyncOperationSupervisor operations)
        {
            _system = system;
            _banks = new FmodBankLibrary(assetLoader, persistentCache, MapBuses);
            operations.Run("load_required_audio_banks", _banks.LoadRequiredBanksAsync);
        }

        private readonly Dictionary<AudioBusType, FMOD.Studio.Bus> _fmodBuses = new();
        private readonly HashSet<string> _reportedMissingEvents = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _reportedInstanceFailures = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _reportedUnloadedSamples = new(StringComparer.OrdinalIgnoreCase);
        private bool _paused;

        private static readonly FMOD.VECTOR ForwardVector = new() { x = 0f, y = 0f, z = 1f };
        private static readonly FMOD.VECTOR UpVector = new() { x = 0f, y = 1f, z = 0f };

        public bool IsDegraded => _banks.IsDegraded;

        public UniTask WaitUntilBanksReadyAsync(CancellationToken cancellationToken = default)
            => _banks.WaitUntilBanksReadyAsync(cancellationToken);

        public UniTask<bool> EnsureBankLoadedAsync(string bankName)
            => _banks.EnsureBankLoadedAsync(bankName);

        public void UnloadBank(string bankName) => _banks.UnloadBank(bankName);

        private void MapBuses()
        {
            // Пути живут на значениях AudioBusType, а не списком здесь: раньше
            // забытая строка в этом словаре означала шину без звука без единой
            // жалобы.
            foreach (AudioBusRegistry.BusBinding binding in AudioBusRegistry.Buses)
            {
                if (FMODUnity.RuntimeManager.StudioSystem.getBus(binding.Path, out var bus) == FMOD.RESULT.OK)
                {
                    _fmodBuses[binding.Bus] = bus;
                }
                else
                {
                    Debug.LogWarning(
                        $"[FmodAudioBackend] Шина '{binding.Path}' ({binding.Bus}) не найдена в банках FMOD.");
                }
            }

            _system?.ApplySavedBusVolumes();
            SetPaused(_paused);
        }

        public float GetBusVolume(AudioBusType type)
        {
            if (_fmodBuses.TryGetValue(type, out var bus))
            {
                bus.getVolume(out float volume);
                return volume;
            }

            return 1f;
        }

        public void SetBusVolume(AudioBusType type, float volume)
        {
            if (_fmodBuses.TryGetValue(type, out var bus))
            {
                bus.setVolume(Mathf.Clamp01(volume));
            }
        }

        public void SetPaused(bool paused)
        {
            _paused = paused;
            if (_fmodBuses.TryGetValue(AudioBusType.Master, out FMOD.Studio.Bus masterBus))
            {
                masterBus.setPaused(paused);
            }
        }

        public AudioPlaybackHandle? CreateVoice(string eventName, AudioLayer layer, Vector3? worldPosition, GameObject? targetGameObject = null)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                return null;
            }

            string fmodPath = eventName.StartsWith("event:/", StringComparison.OrdinalIgnoreCase) || eventName.StartsWith("snapshot:/", StringComparison.OrdinalIgnoreCase)
                ? eventName
                : $"event:/{eventName}";

            if (FMODUnity.RuntimeManager.StudioSystem.getEvent(fmodPath, out var eventDescription) != FMOD.RESULT.OK)
            {
                if (_reportedMissingEvents.Add(fmodPath))
                {
                    Debug.LogWarning(
                        $"[FmodAudioBackend] FMOD event '{fmodPath}' was not found in the loaded banks.");
                }

                return null;
            }

            // Streaming events load their audio after start and must not be
            // rejected for having no resident sample data yet.
            eventDescription.getSampleLoadingState(out FMOD.Studio.LOADING_STATE sampleState);
            eventDescription.isStream(out bool isStream);
            if (!isStream && sampleState != FMOD.Studio.LOADING_STATE.LOADED)
            {
                // Отказ по незагруженным сэмплам обязан называть себя. Событие
                // есть в банке, путь верный, вызывающий получает null и
                // считает, что звук отыграл — а звука нет и не будет: банк
                // грузит сэмплы асинхронно, и повторной попытки здесь никто
                // не делает.
                if (_reportedUnloadedSamples.Add(fmodPath))
                {
                    Debug.LogWarning(
                        $"[FmodAudioBackend] Событие '{fmodPath}' найдено, но его сэмплы " +
                        $"в состоянии {sampleState}, а не LOADED. Звук не прозвучал.");
                }

                return null;
            }

            FMOD.RESULT instResult = eventDescription.createInstance(out var instance);
            if (instResult != FMOD.RESULT.OK || !instance.isValid())
            {
                // A broken event referenced by a frequently played SFX would
                // otherwise warn on every playback attempt.
                if (_reportedInstanceFailures.Add(fmodPath))
                {
                    Debug.LogWarning($"[FmodAudioBackend] Не удалось создать экземпляр события '{fmodPath}': {instResult}");
                }

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
                    var pos = worldPosition.Value;
                    instance.set3DAttributes(new FMOD.ATTRIBUTES_3D
                    {
                        position = new FMOD.VECTOR { x = pos.x, y = pos.y, z = 0f },
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

        public AudioPlaybackHandle? PlaySnapshot(string snapshotPath)
        {
            string fullPath = snapshotPath.StartsWith("snapshot:/", StringComparison.OrdinalIgnoreCase) || snapshotPath.StartsWith("event:/", StringComparison.OrdinalIgnoreCase)
                ? snapshotPath
                : $"snapshot:/{snapshotPath}";

            if (FMODUnity.RuntimeManager.StudioSystem.getEvent(fullPath, out var eventDescription) != FMOD.RESULT.OK)
            {
                return null;
            }

            eventDescription.getSampleLoadingState(out FMOD.Studio.LOADING_STATE sampleState);
            if (sampleState != FMOD.Studio.LOADING_STATE.LOADED)
            {
                return null;
            }

            if (eventDescription.createInstance(out var instance) == FMOD.RESULT.OK && instance.isValid())
            {
                instance.start();
                instance.release();
                return new AudioPlaybackHandle(instance, AudioBusType.Master);
            }

            return null;
        }

        public void SetGlobalParameter(string name, float value)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            FMODUnity.RuntimeManager.StudioSystem.setParameterByName(name, value);
        }

        public void Update()
        {
            // FMOD Studio C++ engine обновляет внутренние состояния нативно — внешних вызовов не требуется.
        }

        public void Shutdown()
        {
            _banks.UnloadAll();
        }
    }
}

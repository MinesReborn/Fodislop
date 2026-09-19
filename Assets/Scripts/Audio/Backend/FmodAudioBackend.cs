#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Audio.Core;
using Kern.Core;
using UnityEngine;

namespace Kern.Audio.Backend;
public sealed class FmodAudioBackend
{
    private readonly Dictionary<AudioBusType, FMOD.Studio.Bus> _buses = new();
    private readonly HashSet<string> _reportedMissingEvents = new(StringComparer.OrdinalIgnoreCase);
    private bool _paused;
    private bool _busesMapped;
    private bool _degraded;

    private const string MasterBusPath = "bus:/";

    private static readonly FMOD.VECTOR _ForwardVector = new() { x = 0f, y = 0f, z = 1f };
    private static readonly FMOD.VECTOR _UpVector = new() { x = 0f, y = 1f, z = 0f };

    public bool IsDegraded => _degraded;

    public async UniTask WaitUntilReadyAsync(AudioSystem system, CancellationToken cancellationToken)
    {
        // Unity's test runner is batch-mode and does not advance FMOD's sample
        // loading lifecycle. Waiting on the native loading flag there can
        // block the scene transition forever, even though audio is explicitly
        // optional for the disposable test process.
        if (Application.isBatchMode)
        {
            _degraded = true;
            return;
        }

        const double maxWaitSeconds = 30d;
        double deadline = Time.realtimeSinceStartupAsDouble + maxWaitSeconds;
        while (Time.realtimeSinceStartupAsDouble < deadline)
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

    // FMOD RuntimeManager живёт дольше Bootstrap. Экземпляры событий
    // отпускаются сразу после старта, поэтому зацикленная музыка и эмбиент
    // играют, пока их не остановят явно, — в том числе после выхода из игры в
    // редакторе и между PlayMode-тестами. Пауза мастер-шины тоже переживала
    // Bootstrap и глушила следующий запуск.
    public void StopAll()
    {
        _paused = false;
        _buses.Clear();
        _busesMapped = false;
        if (!FMODUnity.RuntimeManager.IsInitialized)
        {
            return;
        }

        if (FMODUnity.RuntimeManager.StudioSystem.getBus(MasterBusPath, out FMOD.Studio.Bus master) == FMOD.RESULT.OK)
        {
            master.stopAllEvents(FMOD.Studio.STOP_MODE.IMMEDIATE);
            master.setPaused(false);
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
                    forward = _ForwardVector,
                    up = _UpVector,
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

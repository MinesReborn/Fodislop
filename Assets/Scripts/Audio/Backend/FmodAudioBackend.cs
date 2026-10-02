#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Audio.Core;
using Kern.Core;
using Kern.Core.Interfaces.Diagnostics;
using UnityEngine;

namespace Kern.Audio.Backend;
public sealed class FmodAudioBackend
{
    private readonly Dictionary<AudioBusType, FMOD.Studio.Bus> _buses = new();
    private readonly HashSet<string> _reportedMissingEvents = new(StringComparer.OrdinalIgnoreCase);
    private FMOD.Studio.EventInstance _activeMusicInstance;
    private bool _paused;
    private bool _busesMapped;
    private bool _degraded;
    private FMOD.Studio.EventDescription _digEventDescription;
    private bool _digSamplesRequested;

    private const string MasterBusPath = "bus:/";
    private const string DigEventPath = "event:/sfx/bz";

    private static readonly FMOD.VECTOR s_forwardVector = new() { x = 0f, y = 0f, z = 1f };
    private static readonly FMOD.VECTOR s_upVector = new() { x = 0f, y = 1f, z = 0f };

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
            if (FMODUnity.RuntimeManager.HaveAllBanksLoaded)
            {
                RequestDigSamples();
                FMOD.RESULT result = _digEventDescription.getSampleLoadingState(out FMOD.Studio.LOADING_STATE state);
                if (result != FMOD.RESULT.OK || state == FMOD.Studio.LOADING_STATE.ERROR)
                {
                    throw new InvalidOperationException($"Failed to preload '{DigEventPath}': {result}, {state}.");
                }

                if (state == FMOD.Studio.LOADING_STATE.LOADED &&
                    !FMODUnity.RuntimeManager.AnySampleDataLoading())
                {
                    MapBuses(system);
                    return;
                }
            }

            await UniTask.Yield(cancellationToken);
        }

        _degraded = true;
        Debug.LogWarning(
            "[FmodAudioBackend] Банки FMOD не догрузились за отведённое время; игра идёт без звука.");
    }

    private void RequestDigSamples()
    {
        if (_digSamplesRequested)
        {
            return;
        }

        FMOD.RESULT result = FMODUnity.RuntimeManager.StudioSystem.getEvent(
            DigEventPath, out FMOD.Studio.EventDescription description);
        if (result != FMOD.RESULT.OK)
        {
            throw new InvalidOperationException($"Required digging sound '{DigEventPath}' is missing: {result}.");
        }

        // Bank readiness covers metadata. AutomaticSampleLoading is disabled,
        // so explicitly start this asynchronous load before gameplay instead
        // of leaving it to the first createInstance. Keep its reference until
        // teardown so completion of a one-shot cannot unload the next dig's data.
        result = description.loadSampleData();
        if (result != FMOD.RESULT.OK)
        {
            throw new InvalidOperationException($"Cannot preload '{DigEventPath}': {result}.");
        }

        _digEventDescription = description;
        _digSamplesRequested = true;
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

    public void StopBus(AudioBusType type, bool allowFadeOut = true)
    {
        if (_buses.TryGetValue(type, out FMOD.Studio.Bus bus))
        {
            bus.stopAllEvents(allowFadeOut ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT : FMOD.Studio.STOP_MODE.IMMEDIATE);
        }
    }

    // FMOD RuntimeManager живёт дольше Bootstrap. Экземпляры событий
    // отпускаются сразу после старта, поэтому зацикленная музыка и эмбиент
    // играют, пока их не остановят явно, — в том числе после выхода из игры в
    // редакторе и между PlayMode-тестами. Пауза мастер-шины тоже переживала
    // Bootstrap и глушила следующий запуск.
    public void StopAll()
    {
        if (_digSamplesRequested)
        {
            if (_digEventDescription.isValid())
            {
                _digEventDescription.unloadSampleData();
            }

            _digEventDescription = default;
            _digSamplesRequested = false;
        }

        _paused = false;
        _buses.Clear();
        _busesMapped = false;
        if (_activeMusicInstance.isValid())
        {
            _activeMusicInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            _activeMusicInstance.release();
            _activeMusicInstance.clearHandle();
        }

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

        long callStart = System.Diagnostics.Stopwatch.GetTimestamp();
        FMOD.RESULT lookupResult = FMODUnity.RuntimeManager.StudioSystem.getEvent(
            fmodPath, out FMOD.Studio.EventDescription description);
        RecordSlowCall("getEvent", fmodPath, callStart);
        if (lookupResult != FMOD.RESULT.OK)
        {
            if (_reportedMissingEvents.Add(fmodPath))
            {
                Debug.LogWarning(
                    $"[FmodAudioBackend] Событие '{fmodPath}' отсутствует в загруженных банках.");
            }

            return null;
        }

        if (layer.Bus != AudioBusType.Music)
        {
            const int maxVoicesPerEvent = 8;
            callStart = System.Diagnostics.Stopwatch.GetTimestamp();
            FMOD.RESULT countResult = description.getInstanceCount(out int instanceCount);
            RecordSlowCall("getInstanceCount", fmodPath, callStart);
            if (countResult == FMOD.RESULT.OK && instanceCount >= maxVoicesPerEvent)
            {
                return null;
            }
        }

        callStart = System.Diagnostics.Stopwatch.GetTimestamp();
        FMOD.RESULT createResult = description.createInstance(out FMOD.Studio.EventInstance instance);
        RecordSlowCall("createInstance", fmodPath, callStart);
        if (createResult != FMOD.RESULT.OK ||
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
                    forward = s_forwardVector,
                    up = s_upVector,
                });
            }
        }

        if (layer.Bus == AudioBusType.Music)
        {
            if (_activeMusicInstance.isValid())
            {
                _activeMusicInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                _activeMusicInstance.release();
                _activeMusicInstance.clearHandle();
            }

            if (_buses.TryGetValue(AudioBusType.Music, out FMOD.Studio.Bus musicBus))
            {
                musicBus.stopAllEvents(FMOD.Studio.STOP_MODE.IMMEDIATE);
            }

            _activeMusicInstance = instance;
        }

        instance.setVolume(layer.Volume);
        instance.setPitch(layer.Pitch);
        callStart = System.Diagnostics.Stopwatch.GetTimestamp();
        instance.start();
        RecordSlowCall("start", fmodPath, callStart);
        instance.release();
        return new AudioPlaybackHandle(instance, layer.Bus);
    }

    private static void RecordSlowCall(string operation, string eventPath, long started)
    {
        double milliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) *
            1000.0 / System.Diagnostics.Stopwatch.Frequency;
        if (milliseconds >= 2.0)
        {
            FrameEventLog.Record($"FMOD {operation}: {eventPath} {milliseconds:F1} мс");
        }
    }
}

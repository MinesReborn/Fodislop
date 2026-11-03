#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Audio.Core;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Interfaces.Diagnostics;
using UnityEngine;

using Kern.Diagnostics.Frames;
using MinesServer.Networking.Server.Packets.Connection;

namespace Kern.Infrastructure.Audio;
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
    private readonly Dictionary<string, LoadedBank> _persistentBanks = new(StringComparer.Ordinal);
    private bool _ready;

    private const string MasterBusPath = "bus:/";
    private const string DigEventPath = "event:/sfx/bz";

    private static readonly FMOD.VECTOR s_forwardVector = new() { x = 0f, y = 0f, z = 1f };
    private static readonly FMOD.VECTOR s_upVector = new() { x = 0f, y = 1f, z = 0f };

    public bool IsDegraded => _degraded;

    /// <summary>Банки загружены и шины сопоставлены: дальше набор банков можно сверять на лету.</summary>
    public bool IsReady => _ready;

    public async UniTask WaitUntilReadyAsync(
        AudioSystem system,
        IAssetLoader assetLoader,
        IAudioBankManifestState audioBankManifest,
        CancellationToken cancellationToken)
    {
        try
        {
            await WaitUntilReadyCoreAsync(system, assetLoader, audioBankManifest, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _degraded = true;
            Debug.LogWarning(
                $"[FmodAudioBackend] Audio initialization failed; continuing without complete audio. {exception}");
        }
    }

    private async UniTask WaitUntilReadyCoreAsync(
        AudioSystem system,
        IAssetLoader assetLoader,
        IAudioBankManifestState audioBankManifest,
        CancellationToken cancellationToken)
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
                if (!audioBankManifest.TryGetBanks(out IReadOnlyList<AssetsDefinitionPacket> banks))
                {
                    await UniTask.Yield(cancellationToken);
                    continue;
                }

                await SyncPersistentBanksAsync(assetLoader, banks, cancellationToken);

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
                    _ready = true;
                    return;
                }
            }

            await UniTask.Yield(cancellationToken);
        }

        _degraded = true;
        Debug.LogWarning(
            "[FmodAudioBackend] Банки FMOD не догрузились за отведённое время; игра идёт без звука.");
    }

    /// <summary>
    /// Сервер сменил набор банков посреди игры: выгружает исчезнувшие и изменившиеся, загружает новые и заново
    /// привязывает события и шины, потому что описания из прежних банков больше не действительны.
    /// </summary>
    public async UniTask RefreshBanksAsync(
        AudioSystem system,
        IAssetLoader assetLoader,
        IAudioBankManifestState audioBankManifest,
        CancellationToken cancellationToken)
    {
        if (!_ready || !audioBankManifest.TryGetBanks(out IReadOnlyList<AssetsDefinitionPacket> banks))
        {
            return;
        }

        if (!await SyncPersistentBanksAsync(assetLoader, banks, cancellationToken))
        {
            return;
        }

        _digSamplesRequested = false;
        RequestDigSamples();
        _busesMapped = false;
        _buses.Clear();
        MapBuses(system);
    }

    // Приводит загруженные банки к набору сервера. Возвращает true, если что-то изменилось
    private async UniTask<bool> SyncPersistentBanksAsync(
        IAssetLoader assetLoader,
        IReadOnlyList<AssetsDefinitionPacket> registeredBanks,
        CancellationToken cancellationToken)
    {
        AssetsDefinitionPacket[] banks = registeredBanks
            .OrderBy(bank => bank.Filename.EndsWith(".strings.bank", StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(bank => bank.Filename, StringComparer.Ordinal)
            .ToArray();
        if (banks.Length == 0)
        {
            throw new FileNotFoundException(
                "No FMOD banks are registered in the WorldInit asset manifest.");
        }

        var desired = new HashSet<string>(StringComparer.Ordinal);
        foreach (AssetsDefinitionPacket bank in banks)
        {
            desired.Add(bank.Filename);
        }

        bool changed = false;
        var stale = new List<string>();
        foreach (KeyValuePair<string, LoadedBank> loaded in _persistentBanks)
        {
            AssetsDefinitionPacket? wanted = null;
            foreach (AssetsDefinitionPacket bank in banks)
            {
                if (string.Equals(bank.Filename, loaded.Key, StringComparison.Ordinal))
                {
                    wanted = bank;
                    break;
                }
            }

            if (wanted is not { } match ||
                !string.Equals(match.ETag, loaded.Value.ETag, StringComparison.OrdinalIgnoreCase))
            {
                stale.Add(loaded.Key);
            }
        }

        // Файлы новых и изменившихся банков берутся до выгрузки старых: сбой загрузки не должен оставить звук без банков
        var toLoad = new List<(AssetsDefinitionPacket Bank, string Path)>();
        foreach (AssetsDefinitionPacket bank in banks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_persistentBanks.ContainsKey(bank.Filename) && !stale.Contains(bank.Filename))
            {
                continue;
            }

            string path = await assetLoader.GetAssetPathAsync(bank.Filename, cancellationToken);
            toLoad.Add((bank, path));
        }

        foreach (string filename in stale)
        {
            if (_persistentBanks.Remove(filename, out LoadedBank unloaded) && unloaded.Bank.isValid())
            {
                unloaded.Bank.unload();
            }

            changed = true;
        }

        FMOD.Studio.System studioSystem = FMODUnity.RuntimeManager.StudioSystem;
        foreach ((AssetsDefinitionPacket bank, string path) in toLoad)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string bankName = Path.GetFileNameWithoutExtension(bank.Filename);

            // Банк с этим именем мог загрузить сам FMOD Unity при старте
            FMODUnity.RuntimeManager.UnloadBank(bankName);
            FMOD.RESULT result = studioSystem.loadBankFile(
                path,
                FMOD.Studio.LOAD_BANK_FLAGS.NORMAL,
                out FMOD.Studio.Bank loadedBank);
            if (result != FMOD.RESULT.OK)
            {
                throw new InvalidOperationException(
                    $"Could not load cached FMOD bank '{bankName}' at '{path}': {result}.");
            }

            _persistentBanks[bank.Filename] = new LoadedBank(bank.ETag, loadedBank);
            changed = true;
        }

        return changed;
    }

    private void UnloadPersistentBanks()
    {
        if (!FMODUnity.RuntimeManager.IsInitialized)
        {
            _persistentBanks.Clear();
            return;
        }

        foreach (LoadedBank loaded in _persistentBanks.Values)
        {
            if (loaded.Bank.isValid())
            {
                loaded.Bank.unload();
            }
        }

        _persistentBanks.Clear();
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
        UnloadPersistentBanks();

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
        FMOD.Studio.EventDescription description;
        FMOD.RESULT lookupResult;
        if (_digSamplesRequested && _digEventDescription.isValid() &&
            string.Equals(fmodPath, DigEventPath, StringComparison.OrdinalIgnoreCase))
        {
            description = _digEventDescription;
            lookupResult = FMOD.RESULT.OK;
        }
        else
        {
            lookupResult = FMODUnity.RuntimeManager.StudioSystem.getEvent(fmodPath, out description);
        }
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

    private readonly struct LoadedBank
    {
        public LoadedBank(string eTag, FMOD.Studio.Bank bank)
        {
            ETag = eTag;
            Bank = bank;
        }

        public string ETag { get; }

        public FMOD.Studio.Bank Bank { get; }
    }
}

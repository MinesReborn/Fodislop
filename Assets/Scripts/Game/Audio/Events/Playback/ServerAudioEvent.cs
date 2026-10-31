#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Interfaces;
using Kern.World;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.World;
using MinesServer.Networking.Contracts.Packets;
using UnityEngine;
using Kern.Game.Audio.Diagnostics;

using Kern.Game.Visual.Effects.Events.Playback;
using Kern.Game.Visual.Effects.Events.Parameters;
using Kern.Game.Visual.Effects.Cells;
using Kern.Game.Visual.Effects.Pooling;

namespace Kern.Game.Audio.Events.Playback;
public sealed class ServerAudioEvent : IDisposable, IServerWorldEffect
{
    private readonly AudioEventType? _audioEventType;
    private readonly IAudioSystem _audioSystem;
    private readonly ServerVisualEffectPlayback _visualPlayback;

    public ServerAudioEvent(
        AudioPacket packet,
        IVisualEffectSlot? slot,
        IRobotService robotService,
        ILocalPlayerState localPlayer,
        IAudioSystem audioSystem,
        IAssetLoader assetLoader,
        MapManager mapManager,
        IVisualEffectService visualEffectPool,
        IAsyncOperationSupervisor operations)
        : this(
            packet.EventType,
            packet.EventType.ToString(),
            packet.TargetBotId,
            packet.X,
            packet.Y,
            packet.Parameters,
            slot,
            robotService,
            localPlayer,
            audioSystem,
            assetLoader,
            mapManager,
            visualEffectPool,
            operations)
    {
    }

    public ServerAudioEvent(
        VisualPacket packet,
        IVisualEffectSlot? slot,
        IRobotService robotService,
        ILocalPlayerState localPlayer,
        IAudioSystem audioSystem,
        IAssetLoader assetLoader,
        MapManager mapManager,
        IVisualEffectService visualEffectPool,
        IAsyncOperationSupervisor operations)
        : this(
            null,
            packet.EffectType.ToString(),
            packet.TargetBotId,
            packet.X,
            packet.Y,
            packet.Parameters,
            slot,
            robotService,
            localPlayer,
            audioSystem,
            assetLoader,
            mapManager,
            visualEffectPool,
            operations)
    {
    }

    private ServerAudioEvent(
        AudioEventType? audioEventType,
        string visualEffectName,
        ushort targetBotId,
        ushort sourceX,
        ushort sourceY,
        IReadOnlyList<StringPairPacket> parameters,
        IVisualEffectSlot? slot,
        IRobotService robotService,
        ILocalPlayerState localPlayer,
        IAudioSystem audioSystem,
        IAssetLoader assetLoader,
        MapManager mapManager,
        IVisualEffectService visualEffectPool,
        IAsyncOperationSupervisor operations)
    {
        _audioEventType = audioEventType;
        _audioSystem = audioSystem;
        VisualEffectParameters parsedParams = VisualEffectParameters.Parse(parameters);
        _visualPlayback = new ServerVisualEffectPlayback(
            visualEffectName,
            targetBotId,
            sourceX,
            sourceY,
            parsedParams,
            slot,
            robotService,
            localPlayer,
            assetLoader,
            mapManager,
            visualEffectPool);
        if (_audioEventType is AudioEventType eventType)
        {
            PlayAudio(eventType, _visualPlayback.IntendedWorldPosition);
        }

        _visualPlayback.StartLoading(operations);
    }

    public bool IsDisposed => _visualPlayback.IsDisposed;

    public void Update() => _visualPlayback.Update();

    public void Dispose() => _visualPlayback.Dispose();

    private void PlayAudio(AudioEventType audioEventType, Vector3 worldPosition)
    {
        string eventName = AudioEventNames.Get(audioEventType);
        long audioStart = System.Diagnostics.Stopwatch.GetTimestamp();
        IAudioPlaybackHandle? handle;
        if (CellDestructionEffect.IsCellDestruction(audioEventType))
        {
            var layer = Kern.Audio.Core.AudioLayer.SoundEffectsDefault();
            layer.IsSpatial = false; // Original SoundManager call: spatialBlend=0, pitch=1.
            handle = _audioSystem.Play2D(eventName, layer);
        }
        else
        {
            handle = _audioSystem.PlayAt(eventName, worldPosition);
        }
        double milliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - audioStart) * 1000.0 /
            System.Diagnostics.Stopwatch.Frequency;
        if (milliseconds >= 2.0)
        {
            Kern.Diagnostics.Frames.FrameEventLog.Record($"аудио-событие: {eventName} {milliseconds:F1} мс");
        }
        if (handle == null)
        {
            UnavailableAudioEventLog.LogOnce(
                eventName,
                $"[ServerAudioEvent] FMOD could not play required event '{eventName}'.",
                LogType.Error);
        }
    }

}

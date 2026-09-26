#nullable enable

using System;
using System.Collections.Generic;
using Kern.Audio.Backend;
using Kern.Core.Interfaces;
using Kern.Game.Managers;
using Kern.World;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.World;
using MinesServer.Networking.Shared.Packets;
using UnityEngine;

namespace Kern.Game;
public sealed class ServerAudioEvent : IDisposable
{
    private readonly SFX? _audioEffectType;
    private readonly IAudioSystem _audioSystem;
    private readonly ServerAudioVisualPlayback _visualPlayback;

    public ServerAudioEvent(
        AudioPacket packet,
        IVfxSlot? slot,
        IRobotService robotService,
        IAudioSystem audioSystem,
        IAssetLoader assetLoader,
        MapManager mapManager,
        IVfxService vfxPool,
        IAsyncOperationSupervisor operations)
        : this(
            packet.EffectType,
            packet.EffectType.ToString(),
            packet.TargetBotId,
            packet.X,
            packet.Y,
            packet.Parameters,
            slot,
            robotService,
            audioSystem,
            assetLoader,
            mapManager,
            vfxPool,
            operations)
    {
    }

    public ServerAudioEvent(
        VFXPacket packet,
        IVfxSlot? slot,
        IRobotService robotService,
        IAudioSystem audioSystem,
        IAssetLoader assetLoader,
        MapManager mapManager,
        IVfxService vfxPool,
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
            audioSystem,
            assetLoader,
            mapManager,
            vfxPool,
            operations)
    {
    }

    private ServerAudioEvent(
        SFX? audioEffectType,
        string visualEffectName,
        ushort targetBotId,
        ushort sourceX,
        ushort sourceY,
        IReadOnlyList<StringPairPacket> parameters,
        IVfxSlot? slot,
        IRobotService robotService,
        IAudioSystem audioSystem,
        IAssetLoader assetLoader,
        MapManager mapManager,
        IVfxService vfxPool,
        IAsyncOperationSupervisor operations)
    {
        _audioEffectType = audioEffectType;
        _audioSystem = audioSystem;
        ServerAudioParameters parsedParams = ServerAudioParameters.Parse(parameters);
        _visualPlayback = new ServerAudioVisualPlayback(
            visualEffectName,
            targetBotId,
            sourceX,
            sourceY,
            parsedParams,
            slot,
            robotService,
            assetLoader,
            mapManager,
            vfxPool);
        if (_audioEffectType is SFX effectType)
        {
            PlayAudio(effectType, _visualPlayback.IntendedWorldPosition);
        }

        _visualPlayback.StartLoading(operations);
    }

    public bool IsDisposed => _visualPlayback.IsDisposed;

    public void Update() => _visualPlayback.Update();

    public void Dispose() => _visualPlayback.Dispose();

    private void PlayAudio(SFX effectType, Vector3 worldPosition)
    {
        string eventName = SfxEventNames.Get(effectType);
        long audioStart = System.Diagnostics.Stopwatch.GetTimestamp();
        _audioSystem.PlayAt(eventName, worldPosition);
        ServerAudioVisualPlayback.RecordIfSlow(eventName, audioStart);
    }

}

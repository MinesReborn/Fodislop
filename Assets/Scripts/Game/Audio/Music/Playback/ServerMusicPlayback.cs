#nullable enable

using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Audio.Core;
using Kern.Game.Audio.Diagnostics;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using UnityEngine;

namespace Kern.Game.Audio.Music.Playback;

internal sealed class ServerMusicPlayback
{
    private const string TAG = "[ServerMusicPlayback]";
    private const string MusicEventName = "music/evil_huge";
    private readonly IAudioSystem _audioSystem;
    private readonly IAsyncOperationSupervisor _operations;
    private readonly IWorldPresentationState _presentationState;
    private IAudioPlaybackHandle? _currentMusicHandle;
    private bool _isMusicStarting;
    private bool _hasPendingMusic;
    private ulong _pendingMusicTransitionId;

    public ServerMusicPlayback(IAudioSystem audioSystem, IAsyncOperationSupervisor operations, IWorldPresentationState presentationState)
    {
        _audioSystem = audioSystem;
        _operations = operations;
        _presentationState = presentationState;
    }

    internal void Play()
    {
        if (!_presentationState.IsStable)
        {
            _hasPendingMusic = true;
            _pendingMusicTransitionId = _presentationState.Current.TransitionId;
            return;
        }

        StartMusicWhenReady();
    }
    private async UniTask PlayMusicWhenAudioReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _audioSystem.WaitUntilBanksReadyAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (!_presentationState.IsStable)
            {
                _hasPendingMusic = true;
                _pendingMusicTransitionId = _presentationState.Current.TransitionId;
                return;
            }

            if (_currentMusicHandle != null && _currentMusicHandle.IsPlaying)
            {
                return;
            }

            StopMusic(0f);
            _currentMusicHandle = _audioSystem.Play2D(MusicEventName, AudioLayer.MusicDefault());
            if (_currentMusicHandle == null)
            {
                UnavailableAudioEventLog.LogOnce(
                    MusicEventName,
                    $"{TAG} Музыка '{MusicEventName}' не запустилась.",
                    LogType.Warning);
            }
        }
        finally
        {
            _isMusicStarting = false;
        }
    }

    private void StartMusicWhenReady()
    {
        if (_isMusicStarting || (_currentMusicHandle != null && _currentMusicHandle.IsPlaying))
        {
            return;
        }

        _isMusicStarting = true;
        _operations.Run("play_server_music", PlayMusicWhenAudioReadyAsync);
    }

    internal void HandlePresentationChanged(WorldPresentationSnapshot snapshot)
    {
        if (snapshot.Phase == WorldPresentationPhase.Preparing)
        {
            if (_hasPendingMusic && _pendingMusicTransitionId != snapshot.TransitionId)
            {
                _hasPendingMusic = false;
            }

            return;
        }

        if (snapshot.Phase == WorldPresentationPhase.Stable &&
            _hasPendingMusic && _pendingMusicTransitionId == snapshot.TransitionId)
        {
            _hasPendingMusic = false;
            StartMusicWhenReady();
        }
    }

    internal void StopMusic(float fadeOut = 0.5f)
    {
        if (_currentMusicHandle != null)
        {
            if (_currentMusicHandle.IsPlaying)
            {
                _currentMusicHandle.Stop(fadeOut);
            }

            _currentMusicHandle = null;
        }
    }
}

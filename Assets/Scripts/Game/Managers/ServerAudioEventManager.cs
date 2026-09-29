#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Audio.Core;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Game;
using Kern.World;
using Kern.World.Terrain;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;
using VContainer;

namespace Kern.Game.Managers
{
    public class ServerAudioEventManager : MonoBehaviour, IServerAudioService, IServerVfxService
    {
        private const string TAG = "[ServerAudioEventManager]";

        private const string MusicEventName = "music/evil_huge";
        private readonly List<ServerAudioEvent> _activeEffects = new();
        private readonly Dictionary<(global::MinesServer.Data.SFX Effect, ushort Bot, ushort X, ushort Y), float> _lastSfxTimes = new();
        private IAudioPlaybackHandle? _currentMusicHandle;
        private bool _isMusicStarting;

        [Inject]
        private IVfxService _vfxService = null!;

        [Inject]
        private IRobotService _robotService = null!;

        [Inject]
        private IAudioSystem _audioSystem = null!;

        [Inject]
        private IAssetLoader _assetLoader = null!;

        [Inject]
        private MapManager _mapManager = null!;

        [Inject]
        private VfxPool _vfxPool = null!;
        [Inject]
        private IAsyncOperationSupervisor _operations = null!;

        public void PlayEffect(AudioPacket packet)
        {
            if (packet.EffectType == global::MinesServer.Data.SFX.Music)
            {
                if (_isMusicStarting || (_currentMusicHandle != null && _currentMusicHandle.IsPlaying))
                {
                    return;
                }

                _isMusicStarting = true;
                _operations.Run("play_server_music", PlayMusicWhenAudioReadyAsync);
                return;
            }

            var sfxKey = (packet.EffectType, packet.TargetBotId, packet.X, packet.Y);
            if (_lastSfxTimes.TryGetValue(sfxKey, out float lastTime) && Time.time - lastTime < 0.04f)
            {
                return;
            }

            _lastSfxTimes[sfxKey] = Time.time;
            if (_lastSfxTimes.Count > 128)
            {
                _lastSfxTimes.Clear();
            }

            var vfxType = MapAudioToVFX(packet.EffectType);
            long acquireStart = System.Diagnostics.Stopwatch.GetTimestamp();
            IVfxSlot? slot = _vfxService.Acquire(vfxType);
            double acquireMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - acquireStart) * 1000.0 /
                System.Diagnostics.Stopwatch.Frequency;
            if (acquireMilliseconds >= 2.0)
            {
                Kern.Core.Interfaces.Diagnostics.FrameEventLog.Record(
                    $"звуковое событие: VFX-слот {vfxType} {acquireMilliseconds:F1} мс");
            }

            var effect = new ServerAudioEvent(
                packet,
                slot,
                _robotService,
                _audioSystem,
                _assetLoader,
                _mapManager,
                _vfxPool,
                _operations);
            _activeEffects.Add(effect);
        }

        public void PlayEffect(VFXPacket packet)
        {
            VfxType vfxType = packet.EffectType switch
            {
                global::MinesServer.Data.VFX.Bz => VfxType.Bz,
                global::MinesServer.Data.VFX.Death => VfxType.Death,
                _ => VfxType.Custom,
            };
            IVfxSlot? slot = _vfxService.Acquire(vfxType);

            var effect = new ServerAudioEvent(
                packet,
                slot,
                _robotService,
                _audioSystem,
                _assetLoader,
                _mapManager,
                _vfxPool,
                _operations);
            _activeEffects.Add(effect);
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

                if (_currentMusicHandle != null && _currentMusicHandle.IsPlaying)
                {
                    return;
                }

                StopMusic(0f);
                _currentMusicHandle = _audioSystem.Play2D(MusicEventName, AudioLayer.MusicDefault());
                if (_currentMusicHandle == null)
                {
                    Debug.LogWarning($"{TAG} Музыка '{MusicEventName}' не запустилась.");
                }
            }
            finally
            {
                _isMusicStarting = false;
            }
        }

        private void StopMusic(float fadeOut = 0.5f)
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

        private static VfxType MapAudioToVFX(global::MinesServer.Data.SFX audioType)
        {
            // Enum is logically fixed on client, but server can extend it at any time.
            // Unknown values must NOT be silently dropped — they should flow through
            // as Custom so client can request/display them by numeric id rather than
            // treating them as "no effect".
            return audioType switch
            {
                global::MinesServer.Data.SFX.Bz => VfxType.Bz,
                global::MinesServer.Data.SFX.Destroy => VfxType.Destroy,
                global::MinesServer.Data.SFX.Death => VfxType.Death,
                _ => VfxType.Custom,
            };
        }

        public void ClearAllEffects()
        {
            StopMusic();
            int count = _activeEffects.Count;
            foreach (var effect in _activeEffects)
            {
                effect.Dispose();
            }

            _activeEffects.Clear();
            if (count > 0)
            {
                Debug.Log($"{TAG} Cleared {count} active effects");
            }
        }

        protected void OnDestroy()
        {
            ClearAllEffects();
            _audioSystem?.StopBus(AudioBusType.Music, 0.2f);
        }

        protected void Update()
        {
            if (_activeEffects.Count == 0)
            {
                return;
            }

            for (int i = _activeEffects.Count - 1; i >= 0; i--)
            {
                var effect = _activeEffects[i];
                effect.Update();
                if (effect.IsDisposed)
                {
                    _activeEffects.RemoveAt(i);
                }
            }
        }
    }
}

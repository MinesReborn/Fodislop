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
    public class ServerAudioEventManager : MonoBehaviour, IServerAudioService
    {
        private const string TAG = "[ServerAudioEventManager]";

        private const string MusicEventName = "music/evil_huge";
        private readonly List<ServerAudioEvent> _activeEffects = new();

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
                _operations.Run("play_server_music", PlayMusicWhenAudioReadyAsync);
                return;
            }

            var vfxType = MapAudioToVFX(packet.EffectType);
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
            await _audioSystem.WaitUntilBanksReadyAsync(cancellationToken);
            if (_audioSystem.Play2D(MusicEventName, AudioLayer.MusicDefault()) == null)
            {
                Debug.LogWarning($"{TAG} Музыка '{MusicEventName}' не запустилась.");
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

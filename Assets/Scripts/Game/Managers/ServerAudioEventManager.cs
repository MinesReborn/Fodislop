#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Audio.Core;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game;
using Fodinae.World;
using Fodinae.World.Terrain;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;
using VContainer;

namespace Fodinae.Game.Managers
{
    public class ServerAudioEventManager : MonoBehaviour, IServerAudioService
    {
        private const string TAG = "[ServerAudioEventManager]";

        /// <summary>Единственное музыкальное событие в банке.</summary>
        private const string MusicEventName = "music/evil_huge";
        private readonly List<ServerAudioEvent> _activeEffects = new();

        [Inject]
        private IVFXService _vfxService = null!;

        [Inject]
        private IRobotService _robotService = null!;

        [Inject]
        private IAudioSystem _audioSystem = null!;

        [Inject]
        private IAssetLoader _assetLoader = null!;

        [Inject]
        private MapManager _mapManager = null!;

        [Inject]
        private VFXPool _vfxPool = null!;
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
            IVFXSlot? slot = _vfxService.Acquire(vfxType);

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

        /// <summary>
        /// Заказывает музыку, дождавшись готовности банков.
        /// </summary>
        /// <remarks>
        /// Музыку сервер заказывает один раз за вход в мир, и заказ приходит
        /// в том же потоке пакетов, что и инициализация мира — то есть
        /// раньше, чем FMOD успевает догрузить сэмплы. Немедленный вызов
        /// отбрасывался в бэкенде по состоянию сэмплов и не повторялся
        /// никогда: одноразовый SFX закажут снова, а трек — нет.
        /// </remarks>
        private async UniTask PlayMusicWhenAudioReadyAsync(CancellationToken cancellationToken)
        {
            await _audioSystem.WaitUntilBanksReadyAsync(cancellationToken);
            if (_audioSystem.Play2D(MusicEventName, AudioLayer.MusicDefault()) == null)
            {
                Debug.LogWarning($"{TAG} Музыка '{MusicEventName}' не запустилась.");
            }
        }

        private static VFXType MapAudioToVFX(global::MinesServer.Data.SFX audioType)
        {
            // Enum is logically fixed on client, but server can extend it at any time.
            // Unknown values must NOT be silently dropped — they should flow through
            // as Custom so client can request/display them by numeric id rather than
            // treating them as "no effect".
            return audioType switch
            {
                global::MinesServer.Data.SFX.Bz => VFXType.Bz,
                global::MinesServer.Data.SFX.Destroy => VFXType.Destroy,
                global::MinesServer.Data.SFX.Death => VFXType.Death,
                _ => VFXType.Custom,
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

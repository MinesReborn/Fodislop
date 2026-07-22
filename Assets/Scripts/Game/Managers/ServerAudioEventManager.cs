using System.Collections.Generic;
using Fodinae.Scripts.Audio;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.World;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;

using AudioPacket = MinesServer.Networking.Server.Packets.World.SFXPacket;

namespace Fodinae.Scripts.Game.Managers
{
    public class ServerAudioEventManager : SingletonMonoBehaviour<ServerAudioEventManager>
    {
        private const string TAG = "[ServerAudioEventManager]";
        private readonly List<ServerAudioEvent> _activeEffects = new();

        public void PlayEffect(AudioPacket packet)
        {
            var slot = VFXPool.Instance != null ? VFXPool.Instance.Acquire(packet.EffectType) : null;

            var effect = new ServerAudioEvent(packet, slot);
            _activeEffects.Add(effect);
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

        protected override void OnDestroyed()
        {
            ClearAllEffects();
        }

        protected override void OnApplicationQuitting()
        {
            ClearAllEffects();
        }

        protected void Update()
        {
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

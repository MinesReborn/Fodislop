using System.Collections.Generic;
using Fodinae.Scripts.Audio;
using Fodinae.Scripts.Utils;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;

namespace Fodinae.Scripts.Game.Managers
{
    public class SFXEffectManager : SingletonMonoBehaviour<SFXEffectManager>
    {
        private readonly List<SFXEffectInstance> _activeEffects = new();

        public void PlayEffect(SFXPacket packet)
        {
            var slot = SFXPool.Instance?.Acquire(packet.EffectType);
            if (slot == null)
            {
                return;
            }

            var effect = new SFXEffectInstance(packet, slot);
            _activeEffects.Add(effect);
        }

        public void ClearAllEffects()
        {
            foreach (var effect in _activeEffects)
            {
                effect.Dispose();
            }

            _activeEffects.Clear();
        }

        protected override void OnDestroyed()
        {
            ClearAllEffects();
        }

        protected override void OnApplicationQuitting()
        {
            ClearAllEffects();
        }

        private void Update()
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

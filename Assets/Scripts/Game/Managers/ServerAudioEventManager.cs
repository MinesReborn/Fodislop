using System.Collections.Generic;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.World;
using MinesServer.Networking.Server.Packets.World;
using UnityEngine;

namespace Fodinae.Scripts.Game.Managers
{
    public class ServerAudioEventManager : MonoBehaviour
    {
        private static ServerAudioEventManager _instance;
        public static ServerAudioEventManager Instance => _instance;
        public static ServerAudioEventManager InstanceIfExists => _instance;

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

        protected void Awake()
        {
            _instance = this;
        }

        protected void OnDestroy()
        {
            if (_instance != this)
            {
                return;
            }

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

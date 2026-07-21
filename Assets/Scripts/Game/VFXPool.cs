using System;
using System.Collections.Generic;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.World;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Scripts.Game
{
    public class VFXPool : SingletonMonoBehaviour<VFXPool>
    {
        [Serializable]
        public struct PoolConfig
        {
            [SerializeField]
            private SFX _sfxType;

            [SerializeField]
            private int _initialSize;

            public SFX SfxType => _sfxType;

            public int InitialSize => _initialSize;
        }

        [SerializeField]
        private PoolConfig[] _configs;

        [SerializeField]
        private int _defaultInitialSize = 2;

        [SerializeField]
        private float _shrinkDelay = 30f;

        [SerializeField]
        private int _softMaxPerType = 30;

        private readonly Dictionary<SFX, SubPool> _pools = new();

        protected override void OnAwake()
        {
            InitializePools();
        }

        protected override void OnDestroyed()
        {
            foreach (var kvp in _pools)
            {
                TeardownSubPool(kvp.Value);
            }

            _pools.Clear();
        }

        protected void Update()
        {
            var now = Time.realtimeSinceStartup;

            foreach (var kvp in _pools)
            {
                var pool = kvp.Value;
                var activeList = pool.Active;

                for (int i = activeList.Count - 1; i >= 0; i--)
                {
                    var slot = activeList[i];

                    if (slot.GameObject == null)
                    {
                        activeList.RemoveAt(i);
                        continue;
                    }

                    if (slot.IsManagedExternally)
                    {
                        continue;
                    }

                    if ((now - slot.PlayStartTime) > 30f)
                    {
                        ReleaseInternal(pool, slot, i);
                    }
                }

                ShrinkIfIdle(pool, now);
            }
        }

        private void InitializePools()
        {
            if (_configs == null)
            {
                return;
            }

            foreach (var cfg in _configs)
            {
                var pool = GetOrCreateSubPool(cfg.SfxType);
                pool.TargetSize = Mathf.Max(cfg.InitialSize, 1);
                SpawnToTargetSize(pool);
            }
        }

        private SubPool GetOrCreateSubPool(SFX sfxType)
        {
            if (!_pools.TryGetValue(sfxType, out var pool))
            {
                pool = new SubPool
                {
                    SfxType = sfxType,
                    TargetSize = _defaultInitialSize,
                    LastReleaseTime = Time.realtimeSinceStartup,
                };
                _pools[sfxType] = pool;
            }

            return pool;
        }

        public PooledSlot Acquire(SFX sfxType)
        {
            var pool = GetOrCreateSubPool(sfxType);
            var slot = AcquireInternal(pool);
            if (slot == null)
            {
                return null;
            }

            if (slot.GameObject != null)
            {
                slot.GameObject.SetActive(true);
            }

            slot.SfxType = sfxType;
            slot.IsManagedExternally = true;
            slot.PlayStartTime = Time.realtimeSinceStartup;

            return slot;
        }

        public void Release(PooledSlot slot)
        {
            if (slot == null || slot.IsInPool)
            {
                return;
            }

            if (!_pools.TryGetValue(slot.SfxType, out var pool))
            {
                return;
            }

            int idx = pool.Active.IndexOf(slot);
            if (idx < 0)
            {
                return;
            }

            ReleaseInternal(pool, slot, idx);
        }

        private PooledSlot AcquireInternal(SubPool pool)
        {
            PooledSlot slot;

            if (pool.Available.Count > 0)
            {
                slot = pool.Available.Dequeue();
                slot.IsInPool = false;
                pool.Active.Add(slot);
                return slot;
            }

            var total = pool.Available.Count + pool.Active.Count;

            if (total < _softMaxPerType)
            {
                slot = CreatePooledSlot(pool);
                slot.IsInPool = false;
                pool.Active.Add(slot);

                pool.TargetSize = Mathf.Max(pool.TargetSize, total + 1);
                pool.PeakActiveCount = Mathf.Max(pool.PeakActiveCount, pool.Active.Count);

                return slot;
            }

            slot = CreatePooledSlot(pool);
            slot.IsInPool = false;
            pool.Active.Add(slot);

            pool.TargetSize = Mathf.Max(pool.TargetSize, total + 1);
            pool.PeakActiveCount = Mathf.Max(pool.PeakActiveCount, pool.Active.Count);

            return slot;
        }

        private static void ReleaseInternal(SubPool pool, PooledSlot slot, int activeIndex)
        {
            slot.SpriteRenderer.sprite = null;
            slot.SpriteRenderer.color = Color.white;
            slot.SpriteRenderer.enabled = true;
            slot.GameObject.SetActive(false);
            slot.IsManagedExternally = false;
            slot.IsInPool = true;
            pool.Active.RemoveAt(activeIndex);
            pool.Available.Enqueue(slot);
            pool.LastReleaseTime = Time.realtimeSinceStartup;
        }

        private static void TeardownSubPool(SubPool pool)
        {
            while (pool.Available.Count > 0)
            {
                var slot = pool.Available.Dequeue();
                DestroyPooledSlot(slot);
            }

            foreach (var slot in pool.Active)
            {
                DestroyPooledSlot(slot);
            }

            pool.Active.Clear();
        }

        private static void DestroyPooledSlot(PooledSlot slot)
        {
            if (slot.GameObject != null)
            {
                Destroy(slot.GameObject);
            }
        }

        private static PooledSlot CreatePooledSlot(SubPool pool)
        {
            var go = new GameObject($"PooledVFX_{pool.SfxType}");
            go.SetActive(false);

            if (Application.isPlaying)
            {
                DontDestroyOnLoad(go);
            }

            var renderer = go.AddComponent<SpriteRenderer>();

            return new PooledSlot
            {
                SfxType = pool.SfxType,
                GameObject = go,
                SpriteRenderer = renderer,
                PlayStartTime = 0f,
                IsInPool = true,
            };
        }

        private static void SpawnToTargetSize(SubPool pool)
        {
            var total = pool.Available.Count + pool.Active.Count;
            var needed = pool.TargetSize - total;

            for (int i = 0; i < needed; i++)
            {
                var slot = CreatePooledSlot(pool);
                pool.Available.Enqueue(slot);
            }
        }

        private void ShrinkIfIdle(SubPool pool, float now)
        {
            if (pool.Available.Count <= _defaultInitialSize)
            {
                return;
            }

            if ((now - pool.LastReleaseTime) < _shrinkDelay)
            {
                return;
            }

            var target = Mathf.Max(pool.TargetSize, _defaultInitialSize);
            var excess = pool.Available.Count - target;

            for (int i = 0; i < excess && pool.Available.Count > 0; i++)
            {
                var idle = pool.Available.Dequeue();
                DestroyPooledSlot(idle);
            }

            if (pool.TargetSize > _defaultInitialSize)
            {
                pool.TargetSize = Mathf.Max(_defaultInitialSize, pool.TargetSize - 1);
            }
        }

        public sealed class PooledSlot
        {
            public SFX SfxType;
            public GameObject GameObject;
            public SpriteRenderer SpriteRenderer;
            public float PlayStartTime;
            public bool IsInPool;
            public bool IsManagedExternally;
        }

        private sealed class SubPool
        {
            public SFX SfxType;
            public readonly Queue<PooledSlot> Available = new();
            public readonly List<PooledSlot> Active = new();
            public int TargetSize;
            public float LastReleaseTime;
            public int PeakActiveCount;
        }
    }
}

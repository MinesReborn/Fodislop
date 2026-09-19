#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.World;
using Kern.World.Terrain;
using MinesServer.Data;
using UnityEngine;
using VContainer;

namespace Kern.Game
{
    public class VfxPool : MonoBehaviour, IVfxService
    {
        [Serializable]
        public struct PoolConfig
        {
            [SerializeField]
            private VfxType _vfxType;

            [SerializeField]
            private int _initialSize;

            public VfxType VfxType => _vfxType;

            public int InitialSize => _initialSize;
        }

        [SerializeField]
        private PoolConfig[] _configs = Array.Empty<PoolConfig>();

        [SerializeField]
        private int _defaultInitialSize = 2;

        [SerializeField]
        private float _shrinkDelay = 30f;

        [SerializeField]
        private int _softMaxPerType = 30;

        private readonly Dictionary<VfxType, SubPool> _pools = new();
        private readonly List<SubPool> _poolList = new();
        private int _totalActiveVfxCount;

        [Inject]
        private WorldEntityBatchRenderer _entityBatchRenderer = null!;
        [Inject]
        private ISceneObjectFactory _sceneObjects = null!;

        protected void Awake()
        {
        }

        protected void Start()
        {
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            if (_pools.Count == 0 && _sceneObjects != null && _entityBatchRenderer != null)
            {
                InitializePools();
            }
        }

        protected void OnDestroy()
        {
            for (int i = 0; i < _poolList.Count; i++)
            {
                TeardownSubPool(_poolList[i]);
            }

            _pools.Clear();
            _poolList.Clear();
            _totalActiveVfxCount = 0;
        }
        protected void Update()
        {
            if (_totalActiveVfxCount == 0)
            {
                return;
            }

            var now = Time.realtimeSinceStartup;

            for (int p = 0; p < _poolList.Count; p++)
            {
                var pool = _poolList[p];
                var activeList = pool.Active;

                for (int i = activeList.Count - 1; i >= 0; i--)
                {
                    var slot = activeList[i];

                    if (slot.GameObject == null)
                    {
                        activeList.RemoveAt(i);
                        _totalActiveVfxCount = Mathf.Max(0, _totalActiveVfxCount - 1);
                        continue;
                    }

                    if (slot.IsManagedExternally)
                    {
                        continue;
                    }

                    if ((now - slot.PlayStartTime) > 30f)
                    {
                        ReleaseInternal(pool, slot, i);
                        _totalActiveVfxCount = Mathf.Max(0, _totalActiveVfxCount - 1);
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
                var pool = GetOrCreateSubPool(cfg.VfxType);
                pool.TargetSize = Mathf.Max(cfg.InitialSize, 1);
                SpawnToTargetSize(pool);
            }
        }

        private SubPool GetOrCreateSubPool(VfxType vfxType)
        {
            if (!_pools.TryGetValue(vfxType, out var pool))
            {
                pool = new SubPool
                {
                    VfxType = vfxType,
                    TargetSize = _defaultInitialSize,
                    LastReleaseTime = Time.realtimeSinceStartup,
                };
                _pools[vfxType] = pool;
                _poolList.Add(pool);
            }

            return pool;
        }

        public void Preload(VfxType vfxType, int count)
        {
            var pool = GetOrCreateSubPool(vfxType);
            pool.TargetSize = Mathf.Max(pool.TargetSize, count);
            SpawnToTargetSize(pool);
        }

        public IVfxSlot? Acquire(VfxType vfxType)
        {
            var pool = GetOrCreateSubPool(vfxType);
            var slot = AcquireInternal(pool);
            if (slot == null)
            {
                return null;
            }

            if (slot.GameObject != null)
            {
                slot.GameObject.SetActive(true);
            }

            slot.VfxType = vfxType;
            slot.IsManagedExternally = true;
            slot.PlayStartTime = Time.realtimeSinceStartup;

            return slot;
        }

        public void Release(IVfxSlot slot)
        {
            if (slot is not PooledSlot pooled || pooled.IsInPool)
            {
                return;
            }

            if (!_pools.TryGetValue(pooled.VfxType, out var pool))
            {
                return;
            }

            int idx = pool.Active.IndexOf(pooled);
            if (idx < 0)
            {
                return;
            }

            ReleaseInternal(pool, pooled, idx);
            _totalActiveVfxCount = Mathf.Max(0, _totalActiveVfxCount - 1);
        }

        private PooledSlot AcquireInternal(SubPool pool)
        {
            PooledSlot slot;
            _totalActiveVfxCount++;

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
            if (slot == null)
            {
                return;
            }

            slot.ResetVisual();

            if (slot.GameObject != null)
            {
                slot.GameObject.SetActive(false);
            }

            slot.IsManagedExternally = false;
            slot.IsInPool = true;
            pool.Active.RemoveAt(activeIndex);
            pool.Available.Enqueue(slot);
            pool.LastReleaseTime = Time.realtimeSinceStartup;
        }

        private void TeardownSubPool(SubPool pool)
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

        private void DestroyPooledSlot(PooledSlot slot)
        {
            if (_entityBatchRenderer != null && slot.BatchHandle != null)
            {
                _entityBatchRenderer.UnregisterSprite(slot.BatchHandle);
            }

            if (slot.GameObject != null)
            {
                Destroy(slot.GameObject);
            }
        }

        private PooledSlot CreatePooledSlot(SubPool pool)
        {
            GameObject go = _sceneObjects.Create($"PooledVfx_{pool.VfxType}", RuntimeOwner.VFX);
            go.SetActive(false);

            WorldEntityBatchRenderer.SpriteHandle? handle =
                _entityBatchRenderer?.RegisterSprite(go.transform, -500);

            return new PooledSlot
            {
                VfxType = pool.VfxType,
                GameObject = go,
                EntityBatchRenderer = _entityBatchRenderer!,
                BatchHandle = handle!,
                PlayStartTime = 0f,
                IsInPool = true,
            };
        }

        private void SpawnToTargetSize(SubPool pool)
        {
            if (_sceneObjects == null || _entityBatchRenderer == null)
            {
                return;
            }

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

        public sealed class PooledSlot : IVfxSlot
        {
            public VfxType VfxType;
            public GameObject? GameObject { get; set; }
            public WorldEntityBatchRenderer EntityBatchRenderer = null!;
            public WorldEntityBatchRenderer.SpriteHandle BatchHandle = null!;
            public float PlayStartTime;
            public bool IsInPool;
            public bool IsManagedExternally;

            public void SetSprite(Sprite? sprite)
            {
                EntityBatchRenderer.SetSprite(BatchHandle, sprite);
            }

            public void SetColor(Color color)
            {
                BatchHandle.SetColor(color);
            }

            public void SetEnabled(bool enabled)
            {
                BatchHandle.SetEnabled(enabled);
            }

            public void ResetVisual()
            {
                SetSprite(null);
                SetColor(Color.white);
                SetEnabled(false);
            }
        }

        private sealed class SubPool
        {
            public VfxType VfxType;
            public readonly Queue<PooledSlot> Available = new();
            public readonly List<PooledSlot> Active = new();
            public int TargetSize;
            public float LastReleaseTime;
            public int PeakActiveCount;
        }
    }
}
